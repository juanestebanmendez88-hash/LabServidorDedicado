using UnityEngine;

/// <summary>
/// Movimiento del jugador local con WASD o flechas.
///
/// Usa el Input Manager antiguo, asi que Project Settings - Player -
/// Active Input Handling debe estar en "Both" o en "Input Manager (Old)".
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Tooltip("Unidades por segundo.")]
    public float speed = 5f;

    void Update()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 dir = new Vector3(h, 0f, v);
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        transform.position += dir * (speed * Time.deltaTime);
    }
}
