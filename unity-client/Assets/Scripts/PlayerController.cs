using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento del jugador local con WASD o flechas.
///
/// El desplazamiento se calcula en los ejes de la camara y no en los del
/// mundo, de modo que W siempre aleja y D siempre va hacia la derecha de lo
/// que se ve. Si se usaran los ejes del mundo, girar la camara dejaria los
/// controles cruzados.
///
/// Usa el Input System nuevo, que es el que trae la plantilla Universal 3D.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Tooltip("Unidades por segundo.")]
    public float speed = 5f;

    [Tooltip("Camara que define las direcciones. Si se deja vacio se usa la principal.")]
    public Camera referenceCamera;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;   // sin teclado conectado

        float h = 0f, v = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;

        Vector3 input = new Vector3(h, 0f, v);
        if (input.sqrMagnitude > 1f) input.Normalize();
        if (input.sqrMagnitude < 0.0001f) return;

        transform.position += Direction(input) * (speed * Time.deltaTime);
    }

    /// <summary>Convierte la entrada del teclado en una direccion del mundo.</summary>
    Vector3 Direction(Vector3 input)
    {
        Camera cam = referenceCamera != null ? referenceCamera : Camera.main;
        if (cam == null) return input;   // sin camara, se usan los ejes del mundo

        Transform t = cam.transform;

        // Se aplana sobre el plano horizontal para que el jugador no suba ni
        // baje cuando la camara mira en diagonal hacia el piso.
        Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up);

        // Con la camara mirando casi en vertical, su 'forward' aplanado queda
        // en cero. En ese caso sirve su 'up', que apunta hacia el fondo de la
        // imagen.
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(t.up, Vector3.up);

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        return forward * input.z + right * input.x;
    }
}
