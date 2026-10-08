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
/// Ejecuta los experimentos A, B y C y guarda los resultados en CSV.
///
///   F1  Experimento A: registra 100 RTT del GET con el dt configurado.
///   F2  Experimento B, emisor: publica posX = 1..20, uno cada 20 ms.
///   F3  Experimento B, observador: cuenta cuantos valores distintos ve.
///   F4  Experimento C, emisor: envia 20 eventos, uno cada 20 ms.
///   F5  Experimento C, observador: cuenta eventos, orden y repeticiones.
///
/// Los experimentos B y C necesitan las dos instancias: en una se pulsa la
/// tecla de observar y enseguida en la otra la de emitir. B mide el servicio
/// de posiciones y C el de eventos, con el mismo dt, para que la comparacion
/// entre ambos sea valida.
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
    public EventClient eventClient;

    [Header("Salida")]
    [Tooltip("Carpeta donde se guardan los CSV. Si se deja vacia se usa la del proyecto.")]
    public string outputFolder = @"C:\Users\usuario\Documents\Unity\LabServidorDedicado\resultados";

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
    public float esperaInicioSegundos = 60f;

    [Header("Teclas")]
    public Key keyExperimentA = Key.F1;
    public Key keyEmitB = Key.F2;
    public Key keyObserveB = Key.F3;
    public Key keyEmitC = Key.F4;
    public Key keyObserveC = Key.F5;

    // Estado de la recoleccion
    readonly List<double> _rttSamples = new List<double>();
    readonly List<int> _observed = new List<int>();
    readonly List<GameEvent> _recibidosC = new List<GameEvent>();
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
        // El emisor publica valores enteros en posX, asi que se redondea para
        // identificarlos sin que el error de coma flotante los multiplique.
        if (_observingB) _observed.Add(Mathf.RoundToInt(pos.x));
    }

    void HandleEvent(GameEvent e)
    {
        // Solo cuentan los eventos de la secuencia del experimento, marcados
        // con el tipo ExpC. Los de Arena que se disparen por teclado no
        // forman parte de la medicion.
        if (_observingC && e.type == TipoEventoC) _recibidosC.Add(e);
    }

    void Update()
    {
        if (_busy) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[keyExperimentA].wasPressedThisFrame) StartCoroutine(RunExperimentA());
        else if (kb[keyEmitB].wasPressedThisFrame) StartCoroutine(RunExperimentBEmitter());
        else if (kb[keyObserveB].wasPressedThisFrame) StartCoroutine(RunExperimentBObserver());
        else if (kb[keyEmitC].wasPressedThisFrame) StartCoroutine(RunExperimentCEmitter());
        else if (kb[keyObserveC].wasPressedThisFrame) StartCoroutine(RunExperimentCObserver());
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

        // Fase 1: esperar a que la otra instancia empiece a emitir. La emision
        // dura menos de medio segundo, asi que una ventana fija obligaria a
        // cambiar de ventana y pulsar F2 en ese tiempo. En vez de eso se espera
        // sin prisa a ver el primer valor de la secuencia.
        float limite = Time.realtimeSinceStartup + esperaInicioSegundos;
        while (Time.realtimeSinceStartup < limite && ValoresDeLaSecuencia().Count == 0)
        {
            _status = $"Experimento B (dt = {dt} ms): esperando la emision. " +
                      $"Pulsa F2 en la OTRA ventana. Quedan {limite - Time.realtimeSinceStartup:F0} s";
            yield return null;
        }

        if (ValoresDeLaSecuencia().Count == 0)
        {
            _observingB = false;
            _status = "Experimento B cancelado: no llego ningun valor. Pulsa F3 aqui " +
                      "y enseguida F2 en la otra ventana.";
            Debug.LogWarning(_status);
            RestoreSettings();
            _busy = false;
            yield break;
        }

        // Fase 2: seguir observando mientras sigan llegando valores nuevos. Se
        // corta cuando pasa un rato sin novedad, con margen suficiente para no
        // cortar entre dos consultas cuando dt es grande.
        float silencio = Mathf.Max(3f, dt / 1000f * 4f);
        float ultimoCambio = Time.realtimeSinceStartup;
        int vistos = ValoresDeLaSecuencia().Distinct().Count();

        while (Time.realtimeSinceStartup - ultimoCambio < silencio)
        {
            int ahora = ValoresDeLaSecuencia().Distinct().Count();
            if (ahora != vistos)
            {
                vistos = ahora;
                ultimoCambio = Time.realtimeSinceStartup;
            }
            _status = $"Experimento B (dt = {dt} ms): {vistos} valores distintos de {valuesB}...";
            yield return null;
        }

        _observingB = false;

        var deLaSecuencia = ValoresDeLaSecuencia();
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

    // ---------- Experimento C ----------

    /// <summary>Tipo con el que se marcan los eventos de la medicion.</summary>
    public const string TipoEventoC = "ExpC";

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

        float intervalo = emitIntervalMs / 1000f;
        for (int i = 1; i <= valuesC; i++)
        {
            eventClient.Enviar(TipoEventoC, Vector3.zero, Vector3.zero, i);
            yield return new WaitForSeconds(intervalo);
        }

        // Los envios salen de la cola de uno en uno, asi que se espera a que
        // se vacie antes de dar por terminada la emision.
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

        // El dt que se reporta es el que el cliente de eventos esta usando de
        // verdad, no el del cliente de posiciones: si fueran distintos, el CSV
        // quedaria etiquetado con un intervalo que no es el que se midio.
        int dt = eventClient.DeltaEfectivoMs;
        _recibidosC.Clear();
        _observingC = true;

        // Igual que en B: se espera sin prisa a que empiece la emision, para
        // dar tiempo de cambiar de ventana.
        float limite = Time.realtimeSinceStartup + esperaInicioSegundos;
        while (Time.realtimeSinceStartup < limite && _recibidosC.Count == 0)
        {
            _status = $"Experimento C (dt = {dt} ms): esperando los eventos. " +
                      $"Pulsa F4 en la OTRA ventana. Quedan {limite - Time.realtimeSinceStartup:F0} s";
            yield return null;
        }

        if (_recibidosC.Count == 0)
        {
            _observingC = false;
            _status = "Experimento C cancelado: no llego ningun evento. Pulsa F5 aqui " +
                      "y enseguida F4 en la otra ventana.";
            Debug.LogWarning(_status);
            RestoreSettings();
            _busy = false;
            yield break;
        }

        float silencio = Mathf.Max(3f, dt / 1000f * 4f);
        float ultimoCambio = Time.realtimeSinceStartup;
        int vistos = _recibidosC.Count;

        while (Time.realtimeSinceStartup - ultimoCambio < silencio)
        {
            if (_recibidosC.Count != vistos)
            {
                vistos = _recibidosC.Count;
                ultimoCambio = Time.realtimeSinceStartup;
            }
            _status = $"Experimento C (dt = {dt} ms): {vistos} eventos de {valuesC}...";
            yield return null;
        }

        _observingC = false;

        // Las tres propiedades que pide la guia: cuantos llegaron, si venian en
        // orden y si alguno se repitio.
        var seqs = _recibidosC.Select(e => e.seq).ToList();
        var indices = _recibidosC.Select(e => e.payload.n).ToList();
        bool enOrden = seqs.SequenceEqual(seqs.OrderBy(s => s));
        int repetidos = seqs.Count - seqs.Distinct().Count();

        var sb = new StringBuilder();
        sb.AppendLine("# Experimento C: propagacion de eventos");
        sb.AppendLine($"# jugador={eventClient.MiId} dt_ms={dt} emitidos={valuesC} intervalo_emision_ms={emitIntervalMs}");
        sb.AppendLine($"# recibidos={seqs.Count} en_orden={enOrden} repetidos={repetidos}");
        sb.AppendLine($"# indices_recibidos={string.Join(" ", indices)}");
        sb.AppendLine("orden_llegada,seq,indice,player_id,timestamp");
        for (int i = 0; i < _recibidosC.Count; i++)
        {
            var e = _recibidosC[i];
            sb.AppendLine($"{i + 1},{e.seq},{e.payload.n},{e.player_id},{e.timestamp}");
        }

        string file = Write($"expC_dt{dt}_{eventClient.MiId}.csv", sb.ToString());

        _status = $"Experimento C terminado (dt = {dt} ms): {seqs.Count} de {valuesC} eventos, " +
                  $"en orden = {enOrden}, repetidos = {repetidos}. Guardado en {file}";
        Debug.Log(_status);

        RestoreSettings();
        _busy = false;
    }

    // ---------- Utilidades ----------

    /// <summary>
    /// Lecturas que pertenecen a la secuencia emitida (1..valuesB). Las demas
    /// corresponden a la posicion real del jugador antes de que empezara la
    /// emision y no forman parte del experimento.
    /// </summary>
    List<int> ValoresDeLaSecuencia()
    {
        return _observed.Where(v => v >= 1 && v <= valuesB).ToList();
    }

    void ApplyMeasurementSettings()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = experimentFrameRate;

        // Durante el experimento B hay que hacer clic en la otra ventana para
        // lanzar la emision, con lo que esta pierde el foco. Sin esto, Unity
        // la congelaria y dejaria de sondear justo mientras mide.
        Application.runInBackground = true;
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
        int size = Mathf.Max(13, Mathf.RoundToInt(Screen.height * 0.024f));
        float h = size * 3f;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            wordWrap = true,
            normal = { textColor = Color.yellow }
        };

        var box = new Rect(0, Screen.height - h, Screen.width, h);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(size * 0.8f, Screen.height - h + size * 0.4f,
                           Screen.width - size * 1.6f, h), _status, style);
    }
}
