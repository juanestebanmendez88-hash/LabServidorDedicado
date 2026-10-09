using System.Collections.Generic;
using System.Collections;
using System.Text;
using System;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using UnityEngine;

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

    public string MyId =>
        !string.IsNullOrEmpty(playerId) ? playerId
        : (positionClient != null ? positionClient.localPlayerId : "p1");

    public string MyGame =>
        positionClient != null ? positionClient.gameId : gameId;

    [Header("Sondeo")]
    [Tooltip("Usa el mismo dt que el cliente de posiciones. Conviene dejarlo " +
             "activado: los experimentos B y C solo son comparables si ambos " +
             "sondean al mismo ritmo, y asi hay una sola casilla que cambiar.")]
    [FormerlySerializedAs("heredarDelta")]
    public bool inheritDelta = true;

    [Tooltip("Intervalo de consulta en milisegundos. Se ignora si se hereda.")]
    [Range(10, 2000)]
    public int deltaTimeMs = 200;

    [Header("Referencias")]
    [Tooltip("De donde se toma el identificador de jugador. Opcional.")]
    public PositionSyncClient positionClient;

    public int LastSeq { get; private set; }

    public int SentCount { get; private set; }
    public int ReceivedCount { get; private set; }
    public int PollCount { get; private set; }
    public int ErrorCount { get; private set; }
    public int PendingSends => _queue.Count;

    public event Action<GameEvent> OnEventReceived;

    readonly Queue<EventPost> _queue = new Queue<EventPost>();

    public int EffectiveDeltaMs =>
        (inheritDelta && positionClient != null) ? positionClient.deltaTimeMs : deltaTimeMs;

    float DeltaSeconds => EffectiveDeltaMs / 1000f;

    void Awake()
    {
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

    public void Send(string eventType, Vector3 origin, Vector3 direction = default,
                       int index = 0, string targetId = null)
    {
        _queue.Enqueue(new EventPost
        {
            player_id = MyId,
            type = eventType,
            payload = new EventPayload
            {
                x = origin.x, y = origin.y, z = origin.z,
                dx = direction.x, dy = direction.y, dz = direction.z,
                sequenceIndex = index,
                target = targetId ?? ""
            },
            event_id = Guid.NewGuid().ToString()
        });
    }

    IEnumerator SendLoop()
    {
        while (true)
        {
            if (_queue.Count == 0)
            {
                yield return null;
                continue;
            }

            yield return SendOne(_queue.Dequeue());
        }
    }

    IEnumerator SendOne(EventPost gameEvent)
    {
        string url = $"{serverUrl}/games/{MyGame}/events";
        string json = JsonUtility.ToJson(gameEvent);

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
                Debug.LogWarning($"[POST evento {gameEvent.type}] fallo ({req.responseCode}): {req.error}");
            }
        }
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return FetchOnce();
            yield return new WaitForSeconds(DeltaSeconds);
        }
    }

    IEnumerator FetchOnce()
    {
        string url = $"{serverUrl}/games/{MyGame}/events?since={LastSeq}";

        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            PollCount++;

            if (req.result != UnityWebRequest.Result.Success)
            {
                ErrorCount++;
                Debug.LogWarning($"[GET eventos] fallo ({req.responseCode}): {req.error}");
                yield break;
            }

            var resp = JsonUtility.FromJson<EventsResponse>(req.downloadHandler.text);
            if (resp?.events == null) yield break;

            foreach (var e in resp.events)
            {
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
