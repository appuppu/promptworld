using UnityEngine;

/// <summary>Touching a hazard sends the player back to the start (timer keeps running).</summary>
[RequireComponent(typeof(Collider2D))]
public class Hazard : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            GameManager.Instance.RespawnPlayer();
        }
    }
}
