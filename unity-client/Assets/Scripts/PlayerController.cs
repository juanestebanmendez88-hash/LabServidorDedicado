using UnityEngine.InputSystem;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Tooltip("Unidades por segundo.")]
    public float speed = 5f;

    [Tooltip("Camara que define las direcciones. Si se deja vacio se usa la principal.")]
    public Camera referenceCamera;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

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

    Vector3 Direction(Vector3 input)
    {
        Camera cam = referenceCamera != null ? referenceCamera : Camera.main;
        if (cam == null) return input;

        Transform t = cam.transform;

        Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(t.up, Vector3.up);

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        return forward * input.z + right * input.x;
    }
}
