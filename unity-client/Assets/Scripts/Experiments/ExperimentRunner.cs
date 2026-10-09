using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine;

public class ExperimentRunner : MonoBehaviour
{
    [Header("Referencias")]
    public PositionSyncClient client;
    public EventClient eventClient;

    [Header("Salida")]
    [Tooltip("Carpeta donde se guardan los CSV. Vacia significa 'resultados/' en la raiz " +
             "del repositorio, que es lo que se quiere al clonarlo en cualquier equipo.")]
    public string outputFolder = "";

    [Header("Parametros")]
    [Tooltip("Muestras de RTT del experimento A. La guia pide 100.")]
    public int samplesA = 100;

    [Tooltip("Valores distintos que emite el experimento B. La guia pide 20.")]
    public int valuesB = 20;

    [Tooltip("Eventos que emite el experimento C. La guia pide 20.")]
    public int valuesC = 20;

    [Tooltip("Milisegundos entre valores o eventos emitidos. La guia pide 20.")]
    public int emitIntervalMs = 20;

    [Tooltip("Tasa de fotogramas durante los experimentos, para evitar la cuantizacion por fotograma.")]
    public int experimentFrameRate = 500;

    [Tooltip("Segundos que la instancia observadora espera a que empiece la emision, " +
             "para dar tiempo de cambiar de ventana y pulsar F2.")]
    [FormerlySerializedAs("esperaInicioSegundos")]
    public float startWaitSeconds = 60f;

    [Header("Teclas")]
    public Key keyExperimentA = Key.F1;
    public Key keyEmitB = Key.F2;
    public Key keyObserveB = Key.F3;
    public Key keyEmitC = Key.F4;
    public Key keyObserveC = Key.F5;

    readonly List<double> _rttSamples = new List<double>();
    readonly List<int> _observed = new List<int>();
    readonly List<GameEvent> _receivedC = new List<GameEvent>();
    bool _collectingA;
    bool _observingB;
    bool _observingC;
    bool _busy;
    string _status = "Listo. F1 = exp. A   |   F2/F3 = emitir/observar B   |   F4/F5 = emitir/observar C";

    void OnEnable()
    {
        if (client == null) client = GetComponent<PositionSyncClient>();
        if (eventClient == null) eventClient = GetComponent<EventClient>();

        if (client != null)
        {
            client.OnPollCompleted += HandlePollCompleted;
            client.OnRemotePositionReceived += HandleRemotePosition;
        }
        if (eventClient != null) eventClient.OnEventReceived += HandleEvent;
    }

    void OnDisable()
    {
        if (client != null)
        {
            client.OnPollCompleted -= HandlePollCompleted;
            client.OnRemotePositionReceived -= HandleRemotePosition;
        }
        if (eventClient != null) eventClient.OnEventReceived -= HandleEvent;
    }

    void HandlePollCompleted(double rttMs)
    {
        if (_collectingA && _rttSamples.Count < samplesA) _rttSamples.Add(rttMs);
    }

    void HandleRemotePosition(Vector3 pos)
    {
        if (_observingB) _observed.Add(Mathf.RoundToInt(pos.x));
    }

    void HandleEvent(GameEvent gameEvent)
    {
        if (_observingC && gameEvent.type == EventTypeC) _receivedC.Add(gameEvent);
    }

    void Update()
    {
        if (_busy) return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[keyExperimentA].wasPressedThisFrame) StartCoroutine(RunExperimentA());
        else if (keyboard[keyEmitB].wasPressedThisFrame) StartCoroutine(RunExperimentBEmitter());
        else if (keyboard[keyObserveB].wasPressedThisFrame) StartCoroutine(RunExperimentBObserver());
        else if (keyboard[keyEmitC].wasPressedThisFrame) StartCoroutine(RunExperimentCEmitter());
        else if (keyboard[keyObserveC].wasPressedThisFrame) StartCoroutine(RunExperimentCObserver());
    }

    IEnumerator RunExperimentA()
    {
        _busy = true;
        ApplyMeasurementSettings();

        int dt = client.deltaTimeMs;
        _rttSamples.Clear();

        int publishStart = client.PublishCount;
        int pollStart = client.PollCount;
        float tStart = Time.realtimeSinceStartup;

        _collectingA = true;
        _status = $"Experimento A en curso (dt = {dt} ms)...";

        while (_rttSamples.Count < samplesA)
        {
            _status = $"Experimento A: {_rttSamples.Count}/{samplesA} muestras (dt = {dt} ms)";
            yield return null;
        }

        _collectingA = false;

        float elapsed = Time.realtimeSinceStartup - tStart;
        int requests = (client.PublishCount - publishStart) + (client.PollCount - pollStart);
        double instanceRate = requests / elapsed;

        double avg = _rttSamples.Average();
        double min = _rttSamples.Min();
        double max = _rttSamples.Max();

        var csv = new StringBuilder();
        csv.AppendLine("# Experimento A: latencia y carga");
        csv.AppendLine($"# jugador={client.localPlayerId} dt_ms={dt} muestras={_rttSamples.Count}");
        csv.AppendLine($"# rtt_promedio_ms={F(avg)} rtt_min_ms={F(min)} rtt_max_ms={F(max)}");
        csv.AppendLine($"# duracion_s={F(elapsed)} peticiones_esta_instancia={requests} peticiones_por_s_esta_instancia={F(instanceRate)}");
        csv.AppendLine("# El total del sistema es la suma de las dos instancias.");
        csv.AppendLine("muestra,rtt_ms");
        for (int i = 0; i < _rttSamples.Count; i++)
            csv.AppendLine($"{i + 1},{F(_rttSamples[i])}");

        string file = Write($"expA_dt{dt}_{client.localPlayerId}.csv", csv.ToString());

        _status = $"Experimento A terminado (dt = {dt} ms): RTT promedio {avg:F2} ms, " +
                  $"{instanceRate:F1} pet/s en esta instancia. Guardado en {file}";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    IEnumerator RunExperimentBEmitter()
    {
        _busy = true;
        ApplyMeasurementSettings();

        _status = $"Experimento B: emitiendo {valuesB} valores, uno cada {emitIntervalMs} ms...";
        Debug.Log(_status);

        float interval = emitIntervalMs / 1000f;
        var emitted = new List<int>();

        for (int posValue = 1; posValue <= valuesB; posValue++)
        {
            client.PositionOverride = new Vector3(posValue, 1f, 0f);
            emitted.Add(posValue);
            yield return new WaitForSeconds(interval);
        }

        yield return new WaitForSeconds(2f);
        client.PositionOverride = null;

        _status = $"Experimento B: emision terminada ({emitted.Count} valores). " +
                  "Los resultados se leen en la instancia observadora.";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    IEnumerator RunExperimentBObserver()
    {
        _busy = true;
        ApplyMeasurementSettings();

        int dt = client.deltaTimeMs;
        _observed.Clear();
        _observingB = true;

        float deadline = Time.realtimeSinceStartup + startWaitSeconds;
        while (Time.realtimeSinceStartup < deadline && SequenceValues().Count == 0)
        {
            _status = $"Experimento B (dt = {dt} ms): esperando la emision. " +
                      $"Pulsa F2 en la OTRA ventana. Quedan {deadline - Time.realtimeSinceStartup:F0} s";
            yield return null;
        }

        if (SequenceValues().Count == 0)
        {
            _observingB = false;
            _status = "Experimento B cancelado: no llego ningun valor. Pulsa F3 aqui " +
                      "y enseguida F2 en la otra ventana.";
            Debug.LogWarning(_status);
            RestoreSettings();
            _busy = false;
            yield break;
        }

        float silenceWindow = Mathf.Max(3f, dt / 1000f * 4f);
        float lastChange = Time.realtimeSinceStartup;
        int seen = SequenceValues().Distinct().Count();

        while (Time.realtimeSinceStartup - lastChange < silenceWindow)
        {
            int now = SequenceValues().Distinct().Count();
            if (now != seen)
            {
                seen = now;
                lastChange = Time.realtimeSinceStartup;
            }
            _status = $"Experimento B (dt = {dt} ms): {seen} valores distintos de {valuesB}...";
            yield return null;
        }

        _observingB = false;

        var sequence = SequenceValues();
        var distinctValues = sequence.Distinct().OrderBy(posValue => posValue).ToList();

        var csv = new StringBuilder();
        csv.AppendLine("# Experimento B: muestreo de posiciones");
        csv.AppendLine($"# jugador={client.localPlayerId} dt_ms={dt} emitidos={valuesB} intervalo_emision_ms={emitIntervalMs}");
        csv.AppendLine($"# lecturas_totales={sequence.Count} valores_distintos={distinctValues.Count}");
        csv.AppendLine($"# valores_vistos={string.Join(" ", distinctValues)}");
        csv.AppendLine("lectura,posX");
        for (int i = 0; i < sequence.Count; i++)
            csv.AppendLine($"{i + 1},{sequence[i]}");

        string file = Write($"expB_dt{dt}_{client.localPlayerId}.csv", csv.ToString());

        _status = $"Experimento B terminado (dt = {dt} ms): {distinctValues.Count} de {valuesB} " +
                  $"valores distintos observados. Guardado en {file}";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    public const string EventTypeC = "ExpC";

    IEnumerator RunExperimentCEmitter()
    {
        if (eventClient == null)
        {
            _status = "Experimento C: falta asignar el Event Client en el Inspector.";
            Debug.LogError(_status);
            yield break;
        }

        _busy = true;
        ApplyMeasurementSettings();

        _status = $"Experimento C: enviando {valuesC} eventos, uno cada {emitIntervalMs} ms...";
        Debug.Log(_status);

        float waitInterval = emitIntervalMs / 1000f;
        for (int i = 1; i <= valuesC; i++)
        {
            eventClient.Send(EventTypeC, Vector3.zero, Vector3.zero, i);
            yield return new WaitForSeconds(waitInterval);
        }

        while (eventClient.PendingSends > 0) yield return null;

        _status = $"Experimento C: {valuesC} eventos enviados. " +
                  "Los resultados se leen en la instancia observadora.";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    IEnumerator RunExperimentCObserver()
    {
        if (eventClient == null)
        {
            _status = "Experimento C: falta asignar el Event Client en el Inspector.";
            Debug.LogError(_status);
            yield break;
        }

        _busy = true;
        ApplyMeasurementSettings();

        int dt = eventClient.EffectiveDeltaMs;
        _receivedC.Clear();
        _observingC = true;

        float deadline = Time.realtimeSinceStartup + startWaitSeconds;
        while (Time.realtimeSinceStartup < deadline && _receivedC.Count == 0)
        {
            _status = $"Experimento C (dt = {dt} ms): esperando los eventos. " +
                      $"Pulsa F4 en la OTRA ventana. Quedan {deadline - Time.realtimeSinceStartup:F0} s";
            yield return null;
        }

        if (_receivedC.Count == 0)
        {
            _observingC = false;
            _status = "Experimento C cancelado: no llego ningun evento. Pulsa F5 aqui " +
                      "y enseguida F4 en la otra ventana.";
            Debug.LogWarning(_status);
            RestoreSettings();
            _busy = false;
            yield break;
        }

        float silenceWindow = Mathf.Max(3f, dt / 1000f * 4f);
        float lastChange = Time.realtimeSinceStartup;
        int seen = _receivedC.Count;

        while (Time.realtimeSinceStartup - lastChange < silenceWindow)
        {
            if (_receivedC.Count != seen)
            {
                seen = _receivedC.Count;
                lastChange = Time.realtimeSinceStartup;
            }
            _status = $"Experimento C (dt = {dt} ms): {seen} eventos de {valuesC}...";
            yield return null;
        }

        _observingC = false;

        var seqs = _receivedC.Select(gameEvent => gameEvent.seq).ToList();
        var indexes = _receivedC.Select(gameEvent => gameEvent.payload.sequenceIndex).ToList();
        bool inOrder = seqs.SequenceEqual(seqs.OrderBy(s => s));
        int duplicates = seqs.Count - seqs.Distinct().Count();

        var csv = new StringBuilder();
        csv.AppendLine("# Experimento C: propagacion de eventos");
        csv.AppendLine($"# jugador={eventClient.MyId} dt_ms={dt} emitidos={valuesC} intervalo_emision_ms={emitIntervalMs}");
        csv.AppendLine($"# recibidos={seqs.Count} en_orden={inOrder} repetidos={duplicates}");
        csv.AppendLine($"# indices_recibidos={string.Join(" ", indexes)}");
        csv.AppendLine("orden_llegada,seq,indice,player_id,timestamp");
        for (int i = 0; i < _receivedC.Count; i++)
        {
            var gameEvent = _receivedC[i];
            csv.AppendLine($"{i + 1},{gameEvent.seq},{gameEvent.payload.sequenceIndex},{gameEvent.player_id},{gameEvent.timestamp}");
        }

        string file = Write($"expC_dt{dt}_{eventClient.MyId}.csv", csv.ToString());

        _status = $"Experimento C terminado (dt = {dt} ms): {seqs.Count} de {valuesC} eventos, " +
                  $"en orden = {inOrder}, repetidos = {duplicates}. Guardado en {file}";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    List<int> SequenceValues()
    {
        return _observed.Where(posValue => posValue >= 1 && posValue <= valuesB).ToList();
    }

    void ApplyMeasurementSettings()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = experimentFrameRate;

        Application.runInBackground = true;
    }

    void RestoreSettings()
    {
        Application.targetFrameRate = -1;
    }

    static string F(double posValue) => posValue.ToString("F3", CultureInfo.InvariantCulture);

    string Write(string fileName, string content)
    {
        string folder = string.IsNullOrWhiteSpace(outputFolder)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "resultados"))
            : outputFolder;

        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }
        catch (Exception gameEvent)
        {
            Debug.LogError($"No se pudo escribir {fileName} en {folder}: {gameEvent.Message}");
            return "(no se pudo guardar)";
        }
    }

    void OnGUI()
    {
        int size = Hud.FontSize(0.024f, 13);
        float panelHeight = size * 3f;

        var style = Hud.Label(size, Color.yellow, wordWrap: true);

        var box = new Rect(0, Screen.height - panelHeight, Screen.width, panelHeight);
        Hud.Backdrop(box, 0.6f);

        GUI.Label(new Rect(size * 0.8f, Screen.height - panelHeight + size * 0.4f,
                           Screen.width - size * 1.6f, panelHeight), _status, style);
    }
}
