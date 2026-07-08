using System.Collections;
using UnityEngine;

/// <summary>
/// A solid that blinks briefly after the player touches it, then vanishes,
/// then respawns. Keeps runs moving — hesitation is punished.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class CrumbleBlock : MonoBehaviour
{
    public float crumbleDelay = 0.5f;
    public float respawnAfter = 2.5f;

    private SpriteRenderer spriteRenderer;
    private Collider2D blockCollider;
    private bool crumbling;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        blockCollider = GetComponent<Collider2D>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!crumbling && collision.gameObject.CompareTag("Player"))
        {
            StartCoroutine(Crumble());
        }
    }

    private IEnumerator Crumble()
    {
        crumbling = true;

        float blinkUntil = Time.time + crumbleDelay;
        bool visible = true;
        while (Time.time < blinkUntil)
        {
            visible = !visible;
            spriteRenderer.enabled = visible;
            yield return new WaitForSeconds(0.07f);
        }

        spriteRenderer.enabled = false;
        blockCollider.enabled = false;

        yield return new WaitForSeconds(respawnAfter);

        spriteRenderer.enabled = true;
        blockCollider.enabled = true;
        crumbling = false;
    }
}
