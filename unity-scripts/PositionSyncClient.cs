using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

/// <summary>
/// Cliente de la Parte 1: sincronizacion por replicacion de estado.
///
/// Mantiene dos ciclos independientes, cada uno con su propia corrutina:
///   - PublishLoop: envia la posicion del jugador local cada dt.
///   - PollLoop:    consulta la posicion del jugador remoto cada dt.
///
/// Requisitos del laboratorio que resuelve esta clase:
///   - Ninguna peticion bloquea el hilo principal (todas son corrutinas).
///   - Como maximo una peticion en curso por tipo, sin solapamiento.
///   - El 404 inicial y los errores de red no detienen el ciclo de sondeo.
///   - dt es configurable desde el Inspector.
/// </summary>
public class PositionSyncClient : MonoBehaviour
{
    [Header("Servicio")]
    [Tooltip("URL del servicio de referencia de la Parte 1.")]
    public string serverUrl = "http://localhost:5005";

    [Header("Identificadores")]
    [Tooltip("Partida. Debe ser el mismo en las dos instancias.")]
    public string gameId = "g1";

    [Tooltip("Jugador que controla esta instancia.")]
    public string localPlayerId = "p1";

    [Tooltip("Jugador que representa la otra instancia. Debe ser el cruzado.")]
    public string remotePlayerId = "p2";

    [Header("Sondeo")]
    [Tooltip("Intervalo de sondeo en milisegundos. Los experimentos usan 50, 200 y 1000.")]
    [Range(10, 2000)]
    public int deltaTimeMs = 200;

    [Header("Objetos de la escena")]
    [Tooltip("Capsula que controla el jugador de esta instancia.")]
    public Transform localPlayer;

    [Tooltip("Capsula que representa al jugador de la otra instancia.")]
    public Transform remotePlayer;

    // ---------- Estado observable, para la UI y los experimentos ----------

    /// <summary>Falso mientras el jugador remoto no haya publicado nada (404).</summary>
    public bool RemoteSeen { get; private set; }

    /// <summary>Tiempo de ida y vuelta del ultimo GET, en milisegundos.</summary>
    public double LastRttMs { get; private set; }

    public int PublishCount { get; private set; }
    public int PollCount { get; private set; }
    public int ErrorCount { get; private set; }

    /// <summary>Se dispara al terminar cada GET, con su RTT en ms. Lo usa el experimento A.</summary>
    public event Action<double> OnPollCompleted;

    /// <summary>Se dispara con cada posicion remota recibida. Lo usa el experimento B.</summary>
    public event Action<Vector3> OnRemotePositionReceived;

    /// <summary>
    /// Si tiene valor, se publica este vector en vez de la posicion real del
    /// jugador. El experimento B lo usa para emitir una secuencia controlada.
    /// </summary>
    public Vector3? PositionOverride { get; set; }

    float DeltaSeconds => deltaTimeMs / 1000f;

    void OnEnable()
    {
        StartCoroutine(PublishLoop());
        StartCoroutine(PollLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    // ---------- Ciclos de sondeo ----------

    IEnumerator PublishLoop()
    {
        while (true)
        {
            // 'yield return PublishOnce()' espera a que la peticion anterior
            // termine antes de continuar, de modo que nunca hay dos POST en
            // curso al mismo tiempo. El no solapamiento es estructural: no
            // hace falta ninguna bandera auxiliar.
            yield return PublishOnce();

            // WaitForSeconds se crea en cada vuelta y no se cachea, para que
            // un cambio de dt desde el Inspector tenga efecto en caliente.
            yield return new WaitForSeconds(DeltaSeconds);
        }
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return PollOnce();
            yield return new WaitForSeconds(DeltaSeconds);
        }
    }

    // ---------- Peticiones ----------

    IEnumerator PublishOnce()
    {
        if (localPlayer == null) yield break;

        Vector3 p = PositionOverride ?? localPlayer.position;
        var body = new PositionData { posX = p.x, posY = p.y, posZ = p.z };
        string json = JsonUtility.ToJson(body);
        string url = $"{serverUrl}/server/{gameId}/{localPlayerId}";

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();

            // Sin esta cabecera el servicio responde 422: interpreta el cuerpo
            // como formulario y no encuentra ninguno de los tres campos.
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            PublishCount++;

            if (req.result != UnityWebRequest.Result.Success)
            {
                // Un error de red se registra pero no interrumpe el ciclo:
                // la siguiente vuelta lo reintenta.
                ErrorCount++;
                Debug.LogWarning($"[POST {localPlayerId}] fallo ({req.responseCode}): {req.error}");
            }
        }
    }

    IEnumerator PollOnce()
    {
        string url = $"{serverUrl}/server/{gameId}/{remotePlayerId}";

        using (var req = UnityWebRequest.Get(url))
        {
            // Stopwatch y no Time.time: Time.time se actualiza una sola vez por
            // fotograma, asi que mediria cero en cualquier operacion que empiece
            // y termine dentro del mismo fotograma.
            var watch = Stopwatch.StartNew();
            yield return req.SendWebRequest();
            watch.Stop();

            LastRttMs = watch.Elapsed.TotalMilliseconds;
            PollCount++;
            OnPollCompleted?.Invoke(LastRttMs);

            if (req.result == UnityWebRequest.Result.Success)
            {
                var data = JsonUtility.FromJson<PositionData>(req.downloadHandler.text);
                var pos = new Vector3(data.posX, data.posY, data.posZ);

                if (remotePlayer != null) remotePlayer.position = pos;

                RemoteSeen = true;
                OnRemotePositionReceived?.Invoke(pos);
            }
            else if (req.responseCode == 404)
            {
                // El jugador remoto todavia no ha publicado su posicion. No es
                // un error sino el estado inicial normal de la partida: se
                // sigue sondeando sin interrumpir nada.
                RemoteSeen = false;
            }
            else
            {
                ErrorCount++;
                Debug.LogWarning($"[GET {remotePlayerId}] fallo ({req.responseCode}): {req.error}");
            }
        }
    }

    // ---------- Indicador en pantalla ----------

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        string estadoRemoto = RemoteSeen ? "conectado" : "esperando primera posicion (404)";

        GUI.Label(new Rect(10, 10, 600, 24),
            $"Jugador: {localPlayerId}   |   remoto: {remotePlayerId}   |   dt: {deltaTimeMs} ms", style);
        GUI.Label(new Rect(10, 34, 600, 24),
            $"Remoto: {estadoRemoto}   |   RTT: {LastRttMs:F1} ms", style);
        GUI.Label(new Rect(10, 58, 600, 24),
            $"POST: {PublishCount}   GET: {PollCount}   errores: {ErrorCount}", style);
    }
}
