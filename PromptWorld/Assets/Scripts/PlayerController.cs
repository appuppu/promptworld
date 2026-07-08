using UnityEngine;

/// <summary>
/// Physics-based player movement: horizontal run + single jump.
/// All motion goes through Rigidbody2D so stage parts (jump pads, boosts,
/// gravity flips) act on the same physics body. Gravity direction is a
/// first-class concept: jumping and ground checks follow the current sign.
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
    private float baseGravityScale = 3f;
    private float gravityDirection = 1f; // 1 = normal, -1 = inverted
    private float moveInput;
    private bool jumpQueued;
    private bool frozen;
    private float controlLockTimer; // while > 0, boosts own the velocity
    private Vector2 spawnPoint;

    public Rigidbody2D Body => body;
    public float GravityDirection => gravityDirection;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();
        body.freezeRotation = true;
        if (body.gravityScale > 0f) baseGravityScale = body.gravityScale;
        spawnPoint = transform.position;
    }

    /// <summary>Called by StageLoader right after runtime creation.</summary>
    public void Init(LayerMask ground, Vector2 spawn)
    {
        groundLayer = ground;
        spawnPoint = spawn;
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

        if (controlLockTimer > 0f)
        {
            controlLockTimer -= Time.fixedDeltaTime;
        }
        else
        {
            body.linearVelocity = new Vector2(moveInput * moveSpeed, body.linearVelocity.y);
        }

        if (jumpQueued)
        {
            jumpQueued = false;
            body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);
            body.AddForce(Vector2.up * gravityDirection * jumpForce, ForceMode2D.Impulse);
        }
    }

    private bool IsGrounded()
    {
        Bounds b = box.bounds;
        Vector2 towardGround = Vector2.down * gravityDirection;
        return Physics2D.BoxCast(b.center, b.size, 0f, towardGround, groundCheckDistance, groundLayer);
    }

    /// <summary>Jump pad: vertical relaunch, player keeps horizontal control.</summary>
    public void Bounce(float speed)
    {
        body.linearVelocity = new Vector2(body.linearVelocity.x, speed * gravityDirection);
    }

    /// <summary>Boost/knockback: set velocity and lock control briefly so the launch isn't cancelled by input.</summary>
    public void Launch(Vector2 velocity, float controlLockDuration)
    {
        body.linearVelocity = velocity;
        controlLockTimer = controlLockDuration;
    }

    public void FlipGravity()
    {
        gravityDirection = -gravityDirection;
        body.gravityScale = baseGravityScale * gravityDirection;
    }

    /// <summary>Back to the start point (hazards, falling out of the world). Timer keeps running.</summary>
    public void Respawn()
    {
        if (gravityDirection < 0f) FlipGravity();
        transform.position = spawnPoint;
        body.linearVelocity = Vector2.zero;
        controlLockTimer = 0f;
    }

    /// <summary>Called by GameManager when the run ends (clear or game over).</summary>
    public void Freeze()
    {
        frozen = true;
        body.linearVelocity = Vector2.zero;
        body.simulated = false;
    }
}
