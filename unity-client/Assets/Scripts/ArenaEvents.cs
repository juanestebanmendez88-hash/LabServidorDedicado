using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Escenario 1 del laboratorio, Arena. Traduce las teclas en eventos y los
/// eventos recibidos en una reaccion visible.
///
///   Espacio  ProjectileFired  dispara una esfera hacia el otro jugador
///   E        ShieldRaised     envuelve a quien lo lanza en un escudo
///   Q        PlayerHit        golpe directo al otro jugador, sin proyectil
///
/// PlayerHit tambien se emite solo cuando un proyectil alcanza a alguien, y en
/// ambos casos recae sobre el jugador alcanzado y no sobre quien lo envia.
/// Solo la instancia que disparo decide si hubo impacto: si las dos lo
/// decidieran, el mismo golpe se registraria dos veces.
///
/// Las reacciones se disparan desde el evento recibido y no desde la tecla,
/// incluso para los eventos propios. Asi las dos ventanas reproducen lo mismo
/// a partir de la misma fuente, y "una sola vez" queda garantizado por el
/// filtro 'since' del servicio y no por logica del cliente.
///
/// Las formas se crean en tiempo de ejecucion para que la escena no necesite
/// prefabs ni montaje adicional.
/// </summary>
public class ArenaEvents : MonoBehaviour
{
    public const string ProjectileFired = "ProjectileFired";
    public const string PlayerHit = "PlayerHit";
    public const string ShieldRaised = "ShieldRaised";

    [Header("Referencias")]
    public EventClient eventClient;
    public PositionSyncClient positionClient;

    [Header("Teclas")]
    public Key teclaDisparo = Key.Space;
    public Key teclaEscudo = Key.E;
    public Key teclaGolpe = Key.Q;

    [Header("Proyectil")]
    public float velocidadProyectil = 12f;
    public float vidaProyectil = 2f;

    [Tooltip("Distancia a la que el proyectil cuenta como impacto.")]
    public float radioImpacto = 1.2f;

    [Header("Duraciones")]
    public float duracionEscudo = 2f;
    public float duracionGolpe = 0.6f;

    string _ultimo = "sin eventos todavia";

    /// <summary>Capsulas con un golpe en curso, para no superponer dos.</summary>
    readonly HashSet<Transform> _golpeados = new HashSet<Transform>();

    void Awake()
    {
        if (eventClient == null) eventClient = GetComponent<EventClient>();
        if (positionClient == null) positionClient = GetComponent<PositionSyncClient>();
    }

    void OnEnable()
    {
        if (eventClient != null) eventClient.OnEventReceived += Reproducir;
    }

    void OnDisable()
    {
        if (eventClient != null) eventClient.OnEventReceived -= Reproducir;
    }

    // ---------- Entrada ----------

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || eventClient == null) return;

        Transform yo = positionClient != null ? positionClient.LocalPlayer : null;
        if (yo == null) return;

        if (kb[teclaDisparo].wasPressedThisFrame)
        {
            eventClient.Enviar(ProjectileFired, yo.position, DireccionDeDisparo(yo));
        }
        else if (kb[teclaEscudo].wasPressedThisFrame)
        {
            // El escudo recae sobre quien lo levanta.
            eventClient.Enviar(ShieldRaised, yo.position, objetivo: eventClient.playerId);
        }
        else if (kb[teclaGolpe].wasPressedThisFrame)
        {
            // Golpe directo: lo anuncia quien pega, pero recae sobre el otro.
            Transform otro = positionClient.RemotePlayer;
            if (otro != null)
                eventClient.Enviar(PlayerHit, otro.position,
                                   objetivo: positionClient.remotePlayerId);
        }
    }

    /// <summary>
    /// El proyectil sale hacia el otro jugador si se sabe donde esta; si no,
    /// hacia donde mira la capsula.
    /// </summary>
    Vector3 DireccionDeDisparo(Transform yo)
    {
        if (positionClient != null && positionClient.RemotePlayer != null)
        {
            Vector3 d = positionClient.RemotePlayer.position - yo.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) return d.normalized;
        }
        return yo.forward;
    }

    // ---------- Reacciones ----------

    void Reproducir(GameEvent e)
    {
        if (positionClient == null) return;

        Vector3 donde = new Vector3(e.payload.x, e.payload.y, e.payload.z);

        // Sobre quien recae el evento. Si no viene indicado, sobre quien lo envia.
        string objetivo = string.IsNullOrEmpty(e.payload.target) ? e.player_id : e.payload.target;
        Transform sobreQuien = positionClient.CapsulaDe(objetivo);

        switch (e.type)
        {
            case ProjectileFired:
                // Solo el proyectil de quien disparo decide si hubo impacto.
                bool esMio = eventClient != null && e.player_id == eventClient.playerId;
                StartCoroutine(Proyectil(donde,
                    new Vector3(e.payload.dx, e.payload.dy, e.payload.dz), esMio));
                break;

            case ShieldRaised:
                if (sobreQuien != null) StartCoroutine(Escudo(sobreQuien));
                break;

            case PlayerHit:
                // Si ya hay un golpe en curso sobre esa capsula no se lanza
                // otro: el segundo tomaria como color original el que dejo el
                // primero a medias y la capsula se quedaria blanca.
                if (sobreQuien != null && !_golpeados.Contains(sobreQuien))
                    StartCoroutine(Golpe(sobreQuien));
                break;

            case ExperimentRunner.TipoEventoC:
                // Evento de medicion del experimento C: no tiene reaccion.
                break;

            default:
                Debug.LogWarning($"Evento desconocido: {e.type}");
                break;
        }

        _ultimo = $"seq {e.seq}  {e.type}  de {e.player_id}";
    }

    /// <param name="autoritativo">
    /// Cierto solo en la instancia que disparo. Esa es la unica que decide si
    /// hubo impacto y emite el PlayerHit; en la otra el proyectil es decorado.
    /// Si ambas lo decidieran, el mismo golpe se registraria dos veces.
    /// </param>
    IEnumerator Proyectil(Vector3 desde, Vector3 direccion, bool autoritativo)
    {
        if (direccion.sqrMagnitude < 0.01f) direccion = Vector3.forward;
        direccion.Normalize();

        var bola = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bola.name = "Proyectil";
        bola.transform.localScale = Vector3.one * 0.35f;
        bola.transform.position = desde + direccion * 0.8f;
        Destroy(bola.GetComponent<Collider>());
        Pintar(bola, new Color(1f, 0.85f, 0.2f), false);

        string victimaId = positionClient.remotePlayerId;
        Transform victima = positionClient.RemotePlayer;

        float t = 0f;
        while (t < vidaProyectil && bola != null)
        {
            bola.transform.position += direccion * (velocidadProyectil * Time.deltaTime);

            if (autoritativo && victima != null &&
                Vector3.Distance(bola.transform.position, victima.position) < radioImpacto)
            {
                eventClient.Enviar(PlayerHit, victima.position, objetivo: victimaId);
                break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        if (bola != null) Destroy(bola);
    }

    IEnumerator Escudo(Transform quien)
    {
        var esfera = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        esfera.name = "Escudo";
        esfera.transform.localScale = Vector3.one * 2.4f;
        Destroy(esfera.GetComponent<Collider>());
        Pintar(esfera, new Color(0.3f, 0.85f, 1f, 0.3f), true);

        float t = 0f;
        while (t < duracionEscudo && esfera != null)
        {
            // Sigue a la capsula aunque el jugador se mueva mientras dura.
            if (quien != null) esfera.transform.position = quien.position;
            esfera.transform.Rotate(Vector3.up, 90f * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }

        if (esfera != null) Destroy(esfera);
    }

    IEnumerator Golpe(Transform quien)
    {
        _golpeados.Add(quien);

        var render = quien.GetComponent<Renderer>();
        Color original = default;
        bool tieneColor = render != null && render.material.HasProperty("_BaseColor");
        if (tieneColor) original = render.material.GetColor("_BaseColor");

        Vector3 escalaOriginal = quien.localScale;
        float t = 0f;

        while (t < duracionGolpe && quien != null)
        {
            // Parpadeo rapido entre el color propio y el blanco, y un encogido
            // que vuelve solo. Se nota aunque la capsula este lejos.
            float p = Mathf.PingPong(t * 10f, 1f);
            if (tieneColor) render.material.SetColor("_BaseColor", Color.Lerp(original, Color.white, p));
            quien.localScale = escalaOriginal * (1f - 0.25f * Mathf.Sin(t / duracionGolpe * Mathf.PI));
            t += Time.deltaTime;
            yield return null;
        }

        if (quien != null) quien.localScale = escalaOriginal;
        if (tieneColor) render.material.SetColor("_BaseColor", original);

        _golpeados.Remove(quien);
    }

    /// <summary>
    /// Crea un material para URP. La transparencia necesita configurarse a
    /// mano, porque el modo translucido no es el de por defecto del shader.
    /// </summary>
    static void Pintar(GameObject obj, Color color, bool translucido)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null) return;

        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (translucido)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        obj.GetComponent<Renderer>().material = mat;
    }

    // ---------- Indicador en pantalla ----------

    void OnGUI()
    {
        if (eventClient == null) return;

        int size = Mathf.Max(13, Mathf.RoundToInt(Screen.height * 0.022f));
        float line = size * 1.4f;
        float alto = line * 2 + size;
        float y = Screen.height - alto - size * 4.5f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0, y, Screen.width, alto), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var estilo = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            normal = { textColor = new Color(0.7f, 1f, 0.7f) }
        };

        GUI.Label(new Rect(size, y + size * 0.3f, Screen.width, line),
            "ARENA   Espacio = disparar (el impacto genera PlayerHit)   " +
            "E = escudo propio   Q = golpe directo al otro", estilo);
        GUI.Label(new Rect(size, y + size * 0.3f + line, Screen.width, line),
            $"enviados: {eventClient.SentCount}   recibidos: {eventClient.ReceivedCount}   " +
            $"last_seq: {eventClient.LastSeq}   |   ultimo: {_ultimo}", estilo);
    }
}
