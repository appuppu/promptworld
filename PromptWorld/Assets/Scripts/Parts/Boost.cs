using UnityEngine;

/// <summary>
/// Launches the player horizontally (the "blown away at 50 km/h" family of
/// rules). Control is locked briefly so player input can't cancel the launch.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Boost : MonoBehaviour
{
    public float directionX = 1f;
    public float power = 10f;
    public float controlLockDuration = 0.6f;
    public float verticalKick = 4f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        var player = other.GetComponent<PlayerController>();
        if (player == null) return;

        var velocity = new Vector2(directionX * power, verticalKick * player.GravityDirection);
        player.Launch(velocity, controlLockDuration);
        Sfx.Play(SfxId.Boost);
    }
}
