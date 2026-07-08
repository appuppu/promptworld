using UnityEngine;

/// <summary>Relaunches the player upward (relative to current gravity) on touch.</summary>
[RequireComponent(typeof(Collider2D))]
public class JumpPad : MonoBehaviour
{
    public float power = 22f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        var player = other.GetComponent<PlayerController>();
        if (player == null) return;
        player.Bounce(power);
        Sfx.Play(SfxId.Pad);
    }
}
