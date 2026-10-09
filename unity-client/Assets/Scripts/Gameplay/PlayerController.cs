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
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float horizontal = 0f, vertical = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) vertical -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) vertical += 1f;

        Vector3 input = new Vector3(horizontal, 0f, vertical);
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
