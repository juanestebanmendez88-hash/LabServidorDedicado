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
/// Las capsulas se declaran por numero de jugador y no por papel, de modo que
/// la capsula del jugador 1 es la misma en las dos ventanas. El papel (cual se
/// controla y cual se refleja) lo decide esta clase segun el rol que le toque a
/// la instancia. Si se declararan como "local" y "remota", su color y su
/// etiqueta significarian cosas distintas en cada ventana.
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

    [Header("Capsulas por numero de jugador")]
    [Tooltip("Capsula del jugador 1. Es la misma en las dos ventanas.")]
    public Transform jugador1;

    [Tooltip("Capsula del jugador 2. Es la misma en las dos ventanas.")]
    public Transform jugador2;

    [Header("Posicion inicial")]
    [Tooltip("Separa el punto de aparicion segun el rol, para que las dos " +
             "instancias no empiecen una encima de la otra.")]
    public bool separarAlAparecer = true;

    [Tooltip("Distancia entre los puntos de aparicion.")]
    public float separacion = 4f;

    // ---------- Papeles resueltos en Awake ----------

    /// <summary>Capsula que controla esta instancia y cuya posicion se publica.</summary>
    public Transform LocalPlayer { get; private set; }

    /// <summary>Capsula que refleja la posicion recibida del servicio.</summary>
    public Transform RemotePlayer { get; private set; }

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
    /// Reparte los roles entre instancias sin tener que editar el Inspector en
    /// cada ventana. Se resuelve en dos pasos, de menor a mayor prioridad:
    ///
    ///   1. Multiplayer Play Mode lanza cada editor con "-name PlayerN".
    ///      El editor principal no lo recibe, asi que conserva lo del Inspector.
    ///   2. Argumentos propios, para el juego compilado:
    ///        juego.exe --player p2 --remote p1 --dt 50
    ///
    /// Lo que no se pase conserva el valor del Inspector.
    /// </summary>
    void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();

        ApplyPlaymodeName(args);

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

        AsignarPapeles();
    }

    /// <summary>
    /// Traduce el "-name PlayerN" de Multiplayer Play Mode a un par de
    /// identificadores: Player1 queda como p1 mirando a p2, y Player2 como p2
    /// mirando a p1. Si el argumento no esta, no se toca nada.
    /// </summary>
    void ApplyPlaymodeName(string[] args)
    {
        const string prefix = "Player";

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-name") continue;

            string name = args[i + 1];
            if (!name.StartsWith(prefix)) continue;
            if (!int.TryParse(name.Substring(prefix.Length), out int n)) continue;

            localPlayerId = $"p{n}";
            remotePlayerId = n == 1 ? "p2" : "p1";
            return;
        }
    }

    /// <summary>Extrae el numero de un identificador con forma "pN".</summary>
    static int NumeroDe(string playerId)
    {
        if (playerId != null && playerId.Length > 1 &&
            int.TryParse(playerId.Substring(1), out int n))
            return n;
        return 1;
    }

    /// <summary>
    /// Decide cual capsula controla esta instancia y cual refleja a la otra, y
    /// deja activo el control por teclado solo en la propia. Asi la capsula del
    /// jugador 1 conserva su color y su etiqueta en las dos ventanas.
    /// </summary>
    void AsignarPapeles()
    {
        bool soyElDos = NumeroDe(localPlayerId) == 2;

        LocalPlayer = soyElDos ? jugador2 : jugador1;
        RemotePlayer = soyElDos ? jugador1 : jugador2;

        // El teclado solo mueve la capsula propia. La otra la mueve el servicio.
        ActivarControl(LocalPlayer, true);
        ActivarControl(RemotePlayer, false);

        // Las dos instancias cargan la misma escena, asi que sin esto ambas
        // capsulas apareceria en el mismo punto.
        if (separarAlAparecer && LocalPlayer != null)
        {
            Vector3 p = LocalPlayer.position;
            p.x = (NumeroDe(localPlayerId) - 1.5f) * separacion;
            LocalPlayer.position = p;
        }
    }

    /// <summary>
    /// Capsula que representa a un jugador, en cualquiera de las dos ventanas.
    /// La usa el cliente de eventos para saber sobre quien dibujar la reaccion.
    /// </summary>
    public Transform CapsulaDe(string playerId)
    {
        return NumeroDe(playerId) == 2 ? jugador2 : jugador1;
    }

    static void ActivarControl(Transform capsula, bool activo)
    {
        if (capsula == null) return;

        var control = capsula.GetComponent<PlayerController>();
        if (control != null) control.enabled = activo;
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
        if (LocalPlayer == null) yield break;

        Vector3 p = PositionOverride ?? LocalPlayer.position;
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

                if (RemotePlayer != null) RemotePlayer.position = pos;

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

    /// <summary>Color distintivo de cada instancia, para no confundir las ventanas.</summary>
    Color ColorDeJugador()
    {
        switch (NumeroDe(localPlayerId))
        {
            case 1: return new Color(1f, 0.45f, 0.35f);   // rojizo
            case 2: return new Color(0.45f, 0.7f, 1f);    // azul claro
            case 3: return new Color(0.6f, 1f, 0.5f);     // verde
            default: return Color.white;
        }
    }

    void OnGUI()
    {
        // Los tamanos se derivan de la altura de la pantalla para que el texto
        // sea legible tanto en el editor como a pantalla completa.
        int size = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.026f));
        int titleSize = size * 2;
        float line = size * 1.45f;
        float margin = size * 0.8f;

        var title = new GUIStyle(GUI.skin.label)
        {
            fontSize = titleSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ColorDeJugador() }
        };

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        string estadoRemoto = RemoteSeen ? "CONECTADO" : "esperando primera posicion (404)";

        float alto = titleSize * 1.4f + line * 3 + margin * 2;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, alto), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float y = margin;
        GUI.Label(new Rect(margin, y, Screen.width, titleSize * 1.4f),
            $"ESTA VENTANA CONTROLA A  {localPlayerId.ToUpper()}", title);
        y += titleSize * 1.4f;

        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"publica como {localPlayerId}   |   observa a {remotePlayerId}   |   dt: {deltaTimeMs} ms", style);
        y += line;
        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"Remoto: {estadoRemoto}   |   RTT: {LastRttMs:F1} ms", style);
        y += line;
        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"POST: {PublishCount}   GET: {PollCount}   errores: {ErrorCount}", style);
    }
}
