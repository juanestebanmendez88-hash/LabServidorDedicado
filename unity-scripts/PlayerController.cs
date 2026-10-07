using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento del jugador local con WASD o flechas.
///
/// Usa el Input System nuevo (UnityEngine.InputSystem), que es el que trae
/// configurado la plantilla Universal 3D. Asi no hace falta cambiar
/// Active Input Handling ni reiniciar el editor.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Tooltip("Unidades por segundo.")]
    public float speed = 5f;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;   // sin teclado conectado

        float h = 0f, v = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;

        Vector3 dir = new Vector3(h, 0f, v);
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        transform.position += dir * (speed * Time.deltaTime);
    }
}
