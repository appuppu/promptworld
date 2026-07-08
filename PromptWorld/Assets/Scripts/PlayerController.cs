using UnityEngine;

/// <summary>
/// Physics-based player movement: horizontal run + single jump.
/// All motion goes through Rigidbody2D so future user-defined rules
/// (knockback, gravity flips, etc.) can act on the same physics body.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float jumpForce = 14f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckDistance = 0.05f;

    private Rigidbody2D body;
    private BoxCollider2D box;
    private float moveInput;
    private bool jumpQueued;
    private bool frozen;

    /// <summary>Exposed for future rule modules (Day 2+) to apply custom physics.</summary>
    public Rigidbody2D Body => body;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();
        body.freezeRotation = true;
    }

    private void Update()
    {
        if (frozen) return;

        moveInput = Input.GetAxisRaw("Horizontal");

        if (Input.GetKeyDown(KeyCode.Space) && IsGrounded())
        {
            jumpQueued = true;
        }
    }

    private void FixedUpdate()
    {
        if (frozen) return;

        body.linearVelocity = new Vector2(moveInput * moveSpeed, body.linearVelocity.y);

        if (jumpQueued)
        {
            jumpQueued = false;
            body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);
            body.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        }
    }

    private bool IsGrounded()
    {
        Bounds b = box.bounds;
        return Physics2D.BoxCast(b.center, b.size, 0f, Vector2.down, groundCheckDistance, groundLayer);
    }

    /// <summary>Called by GameManager when the run ends (clear or game over).</summary>
    public void Freeze()
    {
        frozen = true;
        body.linearVelocity = Vector2.zero;
        body.simulated = false;
    }
}
