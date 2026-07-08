using UnityEngine;

/// <summary>
/// Stage-clear trigger. Attach to a white goal object whose Collider2D
/// has "Is Trigger" enabled.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Goal : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            GameManager.Instance.StageClear();
        }
    }
}
