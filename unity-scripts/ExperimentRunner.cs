using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ejecuta los experimentos A y B de la Parte 1 y guarda los resultados en CSV.
///
///   F1  Experimento A: registra 100 RTT del GET con el dt configurado.
///   F2  Experimento B, emisor: publica posX = 1..20, uno cada 20 ms.
///   F3  Experimento B, observador: cuenta cuantos valores distintos ve.
///
/// El experimento B necesita las dos instancias: en una se pulsa F3 (observador)
/// y enseguida en la otra F2 (emisor).
///
/// Sobre la medicion: las corrutinas de Unity se reanudan una vez por fotograma,
/// asi que cualquier espera se redondea al alza hasta el siguiente fotograma. A
/// 60 FPS eso son ~16,7 ms, lo que haria imposible emitir cada 20 ms y
/// distorsionaria un dt de 50 ms. Por eso, al arrancar un experimento se
/// desactiva el VSync y se eleva la tasa de fotogramas objetivo.
/// </summary>
public class ExperimentRunner : MonoBehaviour
{
    [Header("Referencias")]
    public PositionSyncClient client;

    [Header("Salida")]
    [Tooltip("Carpeta donde se guardan los CSV. Si se deja vacia se usa la del proyecto.")]
    public string outputFolder = @"C:\Users\usuario\Documents\Unity\LabServidorDedicado\resultados";

    [Header("Parametros")]
    [Tooltip("Muestras de RTT del experimento A. La guia pide 100.")]
    public int samplesA = 100;

    [Tooltip("Valores distintos que emite el experimento B. La guia pide 20.")]
    public int valuesB = 20;

    [Tooltip("Milisegundos entre valores del experimento B. La guia pide 20.")]
    public int emitIntervalMs = 20;

    [Tooltip("Tasa de fotogramas durante los experimentos, para evitar la cuantizacion por fotograma.")]
    public int experimentFrameRate = 500;

    [Header("Teclas")]
    public Key keyExperimentA = Key.F1;
    public Key keyEmitB = Key.F2;
    public Key keyObserveB = Key.F3;

    // Estado de la recoleccion
    readonly List<double> _rttSamples = new List<double>();
    readonly List<int> _observed = new List<int>();
    bool _collectingA;
    bool _observingB;
    bool _busy;
    string _status = "Listo. F1 = experimento A, F2 = emitir B, F3 = observar B.";

    void OnEnable()
    {
        if (client == null) client = GetComponent<PositionSyncClient>();
        if (client != null)
        {
            client.OnPollCompleted += HandlePollCompleted;
            client.OnRemotePositionReceived += HandleRemotePosition;
        }
    }

    void OnDisable()
    {
        if (client != null)
        {
            client.OnPollCompleted -= HandlePollCompleted;
            client.OnRemotePositionReceived -= HandleRemotePosition;
        }
    }

    void HandlePollCompleted(double rttMs)
    {
        if (_collectingA && _rttSamples.Count < samplesA) _rttSamples.Add(rttMs);
    }

    void HandleRemotePosition(Vector3 pos)
    {
        // El emisor publica valores enteros en posX, asi que se redondea para
        // identificarlos sin que el error de coma flotante los multiplique.
        if (_observingB) _observed.Add(Mathf.RoundToInt(pos.x));
    }

    void Update()
    {
        if (_busy) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[keyExperimentA].wasPressedThisFrame) StartCoroutine(RunExperimentA());
        else if (kb[keyEmitB].wasPressedThisFrame) StartCoroutine(RunExperimentBEmitter());
        else if (kb[keyObserveB].wasPressedThisFrame) StartCoroutine(RunExperimentBObserver());
    }

    // ---------- Experimento A ----------

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

        var sb = new StringBuilder();
        sb.AppendLine("# Experimento A: latencia y carga");
        sb.AppendLine($"# jugador={client.localPlayerId} dt_ms={dt} muestras={_rttSamples.Count}");
        sb.AppendLine($"# rtt_promedio_ms={F(avg)} rtt_min_ms={F(min)} rtt_max_ms={F(max)}");
        sb.AppendLine($"# duracion_s={F(elapsed)} peticiones_esta_instancia={requests} peticiones_por_s_esta_instancia={F(instanceRate)}");
        sb.AppendLine("# El total del sistema es la suma de las dos instancias.");
        sb.AppendLine("muestra,rtt_ms");
        for (int i = 0; i < _rttSamples.Count; i++)
            sb.AppendLine($"{i + 1},{F(_rttSamples[i])}");

        string file = Write($"expA_dt{dt}_{client.localPlayerId}.csv", sb.ToString());

        _status = $"Experimento A terminado (dt = {dt} ms): RTT promedio {avg:F2} ms, " +
                  $"{instanceRate:F1} pet/s en esta instancia. Guardado en {file}";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    // ---------- Experimento B ----------

    IEnumerator RunExperimentBEmitter()
    {
        _busy = true;
        ApplyMeasurementSettings();

        _status = $"Experimento B: emitiendo {valuesB} valores, uno cada {emitIntervalMs} ms...";
        Debug.Log(_status);

        float interval = emitIntervalMs / 1000f;
        var emitted = new List<int>();

        for (int v = 1; v <= valuesB; v++)
        {
            // Se fuerza el valor publicado: posX recorre 1..20 mientras el
            // resto de la posicion se mantiene fijo.
            client.PositionOverride = new Vector3(v, 1f, 0f);
            emitted.Add(v);
            yield return new WaitForSeconds(interval);
        }

        // Se deja un margen para que el observador alcance a consultar lo ultimo.
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

        // Ventana de observacion: lo que tarda la emision mas un margen.
        float window = (valuesB * emitIntervalMs / 1000f) + 3f;
        float tEnd = Time.realtimeSinceStartup + window;

        _status = $"Experimento B observando durante {window:F1} s (dt = {dt} ms). " +
                  "Pulsa F2 en la otra instancia ahora.";
        Debug.Log(_status);

        while (Time.realtimeSinceStartup < tEnd)
        {
            _status = $"Experimento B observando... {_observed.Count} lecturas, " +
                      $"{_observed.Distinct().Count()} valores distintos (dt = {dt} ms)";
            yield return null;
        }

        _observingB = false;

        // Solo cuentan los valores de la secuencia emitida (1..valuesB): las
        // lecturas previas a la emision corresponden a la posicion real del
        // jugador y no forman parte del experimento.
        var deLaSecuencia = _observed.Where(v => v >= 1 && v <= valuesB).ToList();
        var distintos = deLaSecuencia.Distinct().OrderBy(v => v).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Experimento B: muestreo de posiciones");
        sb.AppendLine($"# jugador={client.localPlayerId} dt_ms={dt} emitidos={valuesB} intervalo_emision_ms={emitIntervalMs}");
        sb.AppendLine($"# lecturas_totales={deLaSecuencia.Count} valores_distintos={distintos.Count}");
        sb.AppendLine($"# valores_vistos={string.Join(" ", distintos)}");
        sb.AppendLine("lectura,posX");
        for (int i = 0; i < deLaSecuencia.Count; i++)
            sb.AppendLine($"{i + 1},{deLaSecuencia[i]}");

        string file = Write($"expB_dt{dt}_{client.localPlayerId}.csv", sb.ToString());

        _status = $"Experimento B terminado (dt = {dt} ms): {distintos.Count} de {valuesB} " +
                  $"valores distintos observados. Guardado en {file}";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    // ---------- Utilidades ----------

    void ApplyMeasurementSettings()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = experimentFrameRate;
    }

    void RestoreSettings()
    {
        Application.targetFrameRate = -1;
    }

    /// <summary>Formatea con punto decimal, para que el CSV no dependa de la cultura del sistema.</summary>
    static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    string Write(string fileName, string content)
    {
        string folder = string.IsNullOrWhiteSpace(outputFolder)
            ? Application.persistentDataPath
            : outputFolder;

        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }
        catch (Exception e)
        {
            Debug.LogError($"No se pudo escribir {fileName} en {folder}: {e.Message}");
            return "(no se pudo guardar)";
        }
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        GUI.Label(new Rect(10, Screen.height - 60, Screen.width - 20, 50), _status, style);
    }
}
