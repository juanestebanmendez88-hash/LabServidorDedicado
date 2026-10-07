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

    /// <summary>
    /// Permite lanzar el mismo ejecutable con roles distintos, sin tener que
    /// recompilar ni tocar el Inspector entre una instancia y otra:
    ///
    ///   juego.exe --player p1 --remote p2
    ///   juego.exe --player p2 --remote p1 --dt 50
    ///
    /// Los argumentos que no se pasen conservan el valor del Inspector.
    /// </summary>
    void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--player": localPlayerId = args[i + 1]; break;
                case "--remote": remotePlayerId = args[i + 1]; break;
                case "--game": gameId = args[i + 1]; break;
                case "--server": serverUrl = args[i + 1]; break;
                case "--dt":
                    if (int.TryParse(args[i + 1], out int dt)) deltaTimeMs = dt;
                    break;
            }
        }
    }

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
        // El tamano se deriva de la altura de la pantalla para que el texto sea
        // legible tanto en el editor como en un build a pantalla completa.
        int size = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.028f));
        float line = size * 1.5f;
        float margin = size * 0.8f;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        string estadoRemoto = RemoteSeen ? "CONECTADO" : "esperando primera posicion (404)";

        // Fondo oscuro semitransparente, para que el texto se lea sobre el cielo claro.
        var box = new Rect(0, 0, Screen.width, line * 3 + margin * 2);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float y = margin;
        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"Jugador: {localPlayerId}   |   remoto: {remotePlayerId}   |   dt: {deltaTimeMs} ms", style);
        y += line;
        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"Remoto: {estadoRemoto}   |   RTT: {LastRttMs:F1} ms", style);
        y += line;
        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"POST: {PublishCount}   GET: {PollCount}   errores: {ErrorCount}", style);
    }
}
