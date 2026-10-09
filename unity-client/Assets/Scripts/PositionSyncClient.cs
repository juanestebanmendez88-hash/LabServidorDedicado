using Debug = UnityEngine.Debug;
using System.Collections;
using System.Diagnostics;
using System.Text;
using System;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using UnityEngine;

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
    [FormerlySerializedAs("jugador1")]
    public Transform player1;

    [Tooltip("Capsula del jugador 2. Es la misma en las dos ventanas.")]
    [FormerlySerializedAs("jugador2")]
    public Transform player2;

    [Header("Posicion inicial")]
    [Tooltip("Separa el punto de aparicion segun el rol, para que las dos " +
             "instancias no empiecen una encima de la otra.")]
    [FormerlySerializedAs("separarAlAparecer")]
    public bool separateOnSpawn = true;

    [Tooltip("Distancia entre los puntos de aparicion.")]
    [FormerlySerializedAs("separacion")]
    public float separation = 4f;

    public Transform LocalPlayer { get; private set; }

    public Transform RemotePlayer { get; private set; }

    public bool RemoteSeen { get; private set; }

    public double LastRttMs { get; private set; }

    public int PublishCount { get; private set; }
    public int PollCount { get; private set; }
    public int ErrorCount { get; private set; }

    public event Action<double> OnPollCompleted;

    public event Action<Vector3> OnRemotePositionReceived;

    public Vector3? PositionOverride { get; set; }

    float DeltaSeconds => deltaTimeMs / 1000f;

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

        AssignRoles();
    }

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

    static int PlayerNumberOf(string playerId)
    {
        if (playerId != null && playerId.Length > 1 &&
            int.TryParse(playerId.Substring(1), out int n))
            return n;
        return 1;
    }

    void AssignRoles()
    {
        bool isPlayerTwo = PlayerNumberOf(localPlayerId) == 2;

        LocalPlayer = isPlayerTwo ? player2 : player1;
        RemotePlayer = isPlayerTwo ? player1 : player2;

        SetControlEnabled(LocalPlayer, true);
        SetControlEnabled(RemotePlayer, false);

        if (separateOnSpawn && LocalPlayer != null)
        {
            Vector3 p = LocalPlayer.position;
            p.x = (PlayerNumberOf(localPlayerId) - 1.5f) * separation;
            LocalPlayer.position = p;
        }
    }

    public Transform CapsuleOf(string playerId)
    {
        return PlayerNumberOf(playerId) == 2 ? player2 : player1;
    }

    static void SetControlEnabled(Transform capsule, bool active)
    {
        if (capsule == null) return;

        var control = capsule.GetComponent<PlayerController>();
        if (control != null) control.enabled = active;
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

    IEnumerator PublishLoop()
    {
        while (true)
        {
            yield return PublishOnce();

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

            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            PublishCount++;

            if (req.result != UnityWebRequest.Result.Success)
            {
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
                RemoteSeen = false;
            }
            else
            {
                ErrorCount++;
                Debug.LogWarning($"[GET {remotePlayerId}] fallo ({req.responseCode}): {req.error}");
            }
        }
    }

    Color PlayerColor()
    {
        switch (PlayerNumberOf(localPlayerId))
        {
            case 1: return new Color(1f, 0.45f, 0.35f);
            case 2: return new Color(0.45f, 0.7f, 1f);
            case 3: return new Color(0.6f, 1f, 0.5f);
            default: return Color.white;
        }
    }

    void OnGUI()
    {
        int size = Hud.FontSize(0.026f, 14);
        int titleSize = size * 2;
        float line = size * 1.45f;
        float margin = size * 0.8f;

        var title = Hud.Label(titleSize, PlayerColor(), bold: true);
        var style = Hud.Label(size, Color.white, bold: true);

        string remoteStatus = RemoteSeen ? "CONECTADO" : "esperando primera posicion (404)";

        float panelHeight = titleSize * 1.4f + line * 3 + margin * 2;
        Hud.Backdrop(new Rect(0, 0, Screen.width, panelHeight), 0.65f);

        float y = margin;
        GUI.Label(new Rect(margin, y, Screen.width, titleSize * 1.4f),
            $"ESTA VENTANA CONTROLA A  {localPlayerId.ToUpper()}", title);
        y += titleSize * 1.4f;

        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"publica como {localPlayerId}   |   observa a {remotePlayerId}   |   dt: {deltaTimeMs} ms", style);
        y += line;
        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"Remoto: {remoteStatus}   |   RTT: {LastRttMs:F1} ms", style);
        y += line;
        GUI.Label(new Rect(margin, y, Screen.width, line),
            $"POST: {PublishCount}   GET: {PollCount}   errores: {ErrorCount}", style);
    }
}
