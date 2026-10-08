using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Cliente de la Parte 2: sincronizacion por propagacion de eventos.
///
/// A diferencia de la Parte 1, aqui no se puede perder nada. El servicio
/// guarda todos los eventos en orden y esta clase pide unicamente los que
/// todavia no ha recibido, enviando como 'since' el ultimo numero de orden
/// conocido. Asi cada evento llega exactamente una vez por instancia, por
/// grande que sea dt.
///
/// Los envios pasan por una cola y salen de uno en uno. Si se lanzaran en
/// paralelo, el servicio les asignaria el numero de orden segun el momento de
/// llegada, que no tiene por que coincidir con el de salida, y los eventos
/// podrian quedar registrados desordenados.
/// </summary>
public class EventClient : MonoBehaviour
{
    [Header("Servicio")]
    [Tooltip("URL del servicio de eventos de la Parte 2.")]
    public string serverUrl = "http://localhost:5006";

    [Header("Identificadores")]
    [Tooltip("Partida. Debe ser el mismo en las dos instancias.")]
    public string gameId = "g1";

    [Tooltip("Se toma del PositionSyncClient si se deja vacio.")]
    public string playerId = "";

    /// <summary>
    /// Identificador que se usa de verdad. Se resuelve en cada consulta y no
    /// una vez en Awake, porque el orden de los Awake entre componentes del
    /// mismo objeto no esta garantizado: si este corriera antes que el de
    /// PositionSyncClient, se leeria el valor del Inspector en lugar del rol
    /// ya resuelto, y las dos ventanas enviarian eventos como el mismo jugador.
    /// </summary>
    public string MiId =>
        !string.IsNullOrEmpty(playerId) ? playerId
        : (positionClient != null ? positionClient.localPlayerId : "p1");

    /// <summary>Partida efectiva, resuelta igual que el identificador.</summary>
    public string MiPartida =>
        positionClient != null ? positionClient.gameId : gameId;

    [Header("Sondeo")]
    [Tooltip("Usa el mismo dt que el cliente de posiciones. Conviene dejarlo " +
             "activado: los experimentos B y C solo son comparables si ambos " +
             "sondean al mismo ritmo, y asi hay una sola casilla que cambiar.")]
    public bool heredarDelta = true;

    [Tooltip("Intervalo de consulta en milisegundos. Se ignora si se hereda.")]
    [Range(10, 2000)]
    public int deltaTimeMs = 200;

    [Header("Referencias")]
    [Tooltip("De donde se toma el identificador de jugador. Opcional.")]
    public PositionSyncClient positionClient;

    /// <summary>Ultimo numero de orden recibido. Es lo que se envia como 'since'.</summary>
    public int LastSeq { get; private set; }

    public int SentCount { get; private set; }
    public int ReceivedCount { get; private set; }
    public int PollCount { get; private set; }
    public int ErrorCount { get; private set; }
    public int PendingSends => _cola.Count;

    /// <summary>Se dispara una vez por cada evento recibido, en orden de seq.</summary>
    public event Action<GameEvent> OnEventReceived;

    readonly Queue<EventPost> _cola = new Queue<EventPost>();

    /// <summary>
    /// Intervalo que se esta usando de verdad. Se lee en cada vuelta, de modo
    /// que un cambio de dt en el Inspector tiene efecto en caliente tambien
    /// cuando se hereda.
    /// </summary>
    public int DeltaEfectivoMs =>
        (heredarDelta && positionClient != null) ? positionClient.deltaTimeMs : deltaTimeMs;

    float DeltaSeconds => DeltaEfectivoMs / 1000f;

    void Awake()
    {
        // GetComponent es seguro en Awake sea cual sea el orden. Lo que no se
        // puede es leer ya los valores que el otro componente resuelve en el
        // suyo: para eso estan MiId y MiPartida.
        if (positionClient == null) positionClient = GetComponent<PositionSyncClient>();
    }

    void OnEnable()
    {
        StartCoroutine(SendLoop());
        StartCoroutine(PollLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    // ---------- Envio ----------

    /// <summary>
    /// Encola un evento. Devuelve de inmediato: el envio real ocurre en la
    /// corrutina, de modo que pulsar una tecla nunca congela el juego.
    /// </summary>
    public void Enviar(string tipo, Vector3 donde, Vector3 direccion = default,
                       int indice = 0, string objetivo = null)
    {
        _cola.Enqueue(new EventPost
        {
            player_id = MiId,
            type = tipo,
            payload = new EventPayload
            {
                x = donde.x, y = donde.y, z = donde.z,
                dx = direccion.x, dy = direccion.y, dz = direccion.z,
                n = indice,
                target = objetivo ?? ""
            },
            // Un identificador por evento, para que un reenvio no lo duplique.
            event_id = Guid.NewGuid().ToString()
        });
    }

    IEnumerator SendLoop()
    {
        while (true)
        {
            if (_cola.Count == 0)
            {
                yield return null;
                continue;
            }

            yield return EnviarUno(_cola.Dequeue());
        }
    }

    IEnumerator EnviarUno(EventPost evento)
    {
        string url = $"{serverUrl}/games/{MiPartida}/events";
        string json = JsonUtility.ToJson(evento);

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                SentCount++;
            }
            else
            {
                ErrorCount++;
                Debug.LogWarning($"[POST evento {evento.type}] fallo ({req.responseCode}): {req.error}");
            }
        }
    }

    // ---------- Recepcion ----------

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return ConsultarUna();
            yield return new WaitForSeconds(DeltaSeconds);
        }
    }

    IEnumerator ConsultarUna()
    {
        string url = $"{serverUrl}/games/{MiPartida}/events?since={LastSeq}";

        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            PollCount++;

            if (req.result != UnityWebRequest.Result.Success)
            {
                // Un fallo de red no pierde nada: como 'since' no avanza, la
                // siguiente consulta vuelve a pedir los mismos eventos.
                ErrorCount++;
                Debug.LogWarning($"[GET eventos] fallo ({req.responseCode}): {req.error}");
                yield break;
            }

            var resp = JsonUtility.FromJson<EventsResponse>(req.downloadHandler.text);
            if (resp?.events == null) yield break;

            foreach (var e in resp.events)
            {
                // Se avanza el contador antes de reaccionar: si la reaccion
                // fallara, el evento no se repetiria en la siguiente consulta.
                LastSeq = e.seq;
                ReceivedCount++;

                try
                {
                    OnEventReceived?.Invoke(e);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[evento seq={e.seq} {e.type}] la reaccion fallo: {ex.Message}");
                }
            }
        }
    }
}
