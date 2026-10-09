using System.Collections.Generic;
using System.Collections;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine;

public class ArenaEvents : MonoBehaviour
{
    public const string ProjectileFired = "ProjectileFired";
    public const string PlayerHit = "PlayerHit";
    public const string ShieldRaised = "ShieldRaised";

    [Header("Referencias")]
    public EventClient eventClient;
    public PositionSyncClient positionClient;

    [Header("Teclas")]
    [FormerlySerializedAs("teclaDisparo")]
    public Key fireKey = Key.Space;
    [FormerlySerializedAs("teclaEscudo")]
    public Key shieldKey = Key.E;

    [Header("Proyectil")]
    [FormerlySerializedAs("velocidadProyectil")]
    public float projectileSpeed = 12f;
    [FormerlySerializedAs("vidaProyectil")]
    public float projectileLifetime = 2f;

    [Tooltip("Distancia a la que el proyectil cuenta como impacto. Generoso a " +
             "proposito: se apunta a la ultima posicion conocida del otro, que " +
             "tiene hasta dt de retraso.")]
    [FormerlySerializedAs("radioImpacto")]
    public float hitRadius = 1.5f;

    [Header("Duraciones")]
    [FormerlySerializedAs("duracionEscudo")]
    public float shieldDuration = 2f;
    [FormerlySerializedAs("duracionGolpe")]
    public float hitDuration = 0.6f;

    string _lastEvent = "sin eventos todavia";

    readonly HashSet<Transform> _beingHit = new HashSet<Transform>();

    void Awake()
    {
        if (eventClient == null) eventClient = GetComponent<EventClient>();
        if (positionClient == null) positionClient = GetComponent<PositionSyncClient>();
    }

    void OnEnable()
    {
        if (eventClient != null) eventClient.OnEventReceived += PlayReaction;
    }

    void OnDisable()
    {
        if (eventClient != null) eventClient.OnEventReceived -= PlayReaction;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || eventClient == null) return;

        Transform self = positionClient != null ? positionClient.LocalPlayer : null;
        if (self == null) return;

        if (kb[fireKey].wasPressedThisFrame)
        {
            eventClient.Send(ProjectileFired, self.position, ShotDirection(self));
        }
        else if (kb[shieldKey].wasPressedThisFrame)
        {
            eventClient.Send(ShieldRaised, self.position, targetId: eventClient.MyId);
        }
    }

    Vector3 ShotDirection(Transform self)
    {
        if (positionClient != null && positionClient.RemotePlayer != null)
        {
            Vector3 d = positionClient.RemotePlayer.position - self.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) return d.normalized;
        }
        return self.forward;
    }

    void PlayReaction(GameEvent e)
    {
        if (positionClient == null) return;

        Vector3 origin = new Vector3(e.payload.x, e.payload.y, e.payload.z);

        string targetId = string.IsNullOrEmpty(e.payload.target) ? e.player_id : e.payload.target;
        Transform affected = positionClient.CapsuleOf(targetId);

        switch (e.type)
        {
            case ProjectileFired:
                bool isMine = eventClient != null && e.player_id == eventClient.MyId;
                StartCoroutine(ProjectileRoutine(origin,
                    new Vector3(e.payload.dx, e.payload.dy, e.payload.dz), isMine));
                break;

            case ShieldRaised:
                if (affected != null) StartCoroutine(ShieldRoutine(affected));
                break;

            case PlayerHit:
                if (affected != null && !_beingHit.Contains(affected))
                    StartCoroutine(HitRoutine(affected));
                break;

            case ExperimentRunner.EventTypeC:
                break;

            default:
                Debug.LogWarning($"Evento desconocido: {e.type}");
                break;
        }

        _lastEvent = $"seq {e.seq}  {e.type}  de {e.player_id}";
    }

    IEnumerator ProjectileRoutine(Vector3 since, Vector3 direction, bool authoritative)
    {
        if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
        direction.Normalize();

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Proyectil";
        ball.transform.localScale = Vector3.one * 0.35f;
        ball.transform.position = since + direction * 0.8f;
        Destroy(ball.GetComponent<Collider>());
        Paint(ball, new Color(1f, 0.85f, 0.2f), false);

        string victimId = positionClient.remotePlayerId;
        Transform victim = positionClient.RemotePlayer;

        float t = 0f;
        while (t < projectileLifetime && ball != null)
        {
            ball.transform.position += direction * (projectileSpeed * Time.deltaTime);

            if (authoritative && victim != null &&
                Vector3.Distance(ball.transform.position, victim.position) < hitRadius)
            {
                eventClient.Send(PlayerHit, victim.position, targetId: victimId);
                break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        if (ball != null) Destroy(ball);
    }

    IEnumerator ShieldRoutine(Transform subject)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Escudo";
        sphere.transform.localScale = Vector3.one * 2.4f;
        Destroy(sphere.GetComponent<Collider>());
        Paint(sphere, new Color(0.3f, 0.85f, 1f, 0.3f), true);

        float t = 0f;
        while (t < shieldDuration && sphere != null)
        {
            if (subject != null) sphere.transform.position = subject.position;
            sphere.transform.Rotate(Vector3.up, 90f * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }

        if (sphere != null) Destroy(sphere);
    }

    IEnumerator HitRoutine(Transform subject)
    {
        _beingHit.Add(subject);

        var render = subject.GetComponent<Renderer>();
        Color original = default;
        bool hasColor = render != null && render.material.HasProperty("_BaseColor");
        if (hasColor) original = render.material.GetColor("_BaseColor");

        Vector3 originalScale = subject.localScale;
        float t = 0f;

        while (t < hitDuration && subject != null)
        {
            float p = Mathf.PingPong(t * 10f, 1f);
            if (hasColor) render.material.SetColor("_BaseColor", Color.Lerp(original, Color.white, p));
            subject.localScale = originalScale * (1f - 0.25f * Mathf.Sin(t / hitDuration * Mathf.PI));
            t += Time.deltaTime;
            yield return null;
        }

        if (subject != null) subject.localScale = originalScale;
        if (hasColor) render.material.SetColor("_BaseColor", original);

        _beingHit.Remove(subject);
    }

    static void Paint(GameObject obj, Color color, bool translucent)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null) return;

        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (translucent)
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

    void OnGUI()
    {
        if (eventClient == null) return;

        int size = Hud.FontSize(0.022f, 13);
        float line = size * 1.4f;
        float panelHeight = line * 2 + size;
        float y = Screen.height - panelHeight - size * 4.5f;

        Hud.Backdrop(new Rect(0, y, Screen.width, panelHeight), 0.6f);

        var labelStyle = Hud.Label(size, new Color(0.7f, 1f, 0.7f));

        GUI.Label(new Rect(size, y + size * 0.3f, Screen.width, line),
            "ARENA   Espacio = disparar   E = escudo   " +
            "(el impacto del proyectil genera PlayerHit sobre el alcanzado)", labelStyle);
        GUI.Label(new Rect(size, y + size * 0.3f + line, Screen.width, line),
            $"enviados: {eventClient.SentCount}   recibidos: {eventClient.ReceivedCount}   " +
            $"last_seq: {eventClient.LastSeq}   |   ultimo: {_lastEvent}", labelStyle);
    }
}
