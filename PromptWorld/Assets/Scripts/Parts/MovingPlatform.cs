using UnityEngine;

/// <summary>
/// Kinematic platform oscillating smoothly between its origin and
/// origin + delta. Moving via MovePosition lets contact friction carry
/// the player naturally.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform : MonoBehaviour
{
    public Vector2 delta = new Vector2(4f, 0f);
    public float period = 4f;

    private Rigidbody2D body;
    private Vector2 origin;
    private float elapsed;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        origin = transform.position;
    }

    private void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        float k = 0.5f - 0.5f * Mathf.Cos(elapsed / period * 2f * Mathf.PI);
        body.MovePosition(origin + delta * k);
    }
}
