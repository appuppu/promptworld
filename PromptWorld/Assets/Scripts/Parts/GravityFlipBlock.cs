using UnityEngine;

/// <summary>Inverts the player's gravity on touch. Cooldown prevents double-triggering while passing through.</summary>
[RequireComponent(typeof(Collider2D))]
public class GravityFlipBlock : MonoBehaviour
{
    public float cooldown = 0.7f;

    private float lastTriggerTime = -999f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (Time.time - lastTriggerTime < cooldown) return;

        var player = other.GetComponent<PlayerController>();
        if (player == null) return;

        lastTriggerTime = Time.time;
        player.FlipGravity();
    }
}
