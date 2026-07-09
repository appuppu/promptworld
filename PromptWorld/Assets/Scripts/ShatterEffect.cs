using UnityEngine;

/// <summary>
/// A one-shot burst of small white shards flying outward, then fading — the
/// player square "breaking" when it's crushed, shot or spiked. Purely visual;
/// the sim already handled the respawn.
/// </summary>
public class ShatterEffect : MonoBehaviour
{
    private static Sprite shardSprite;

    public static void Burst(Vector3 pos, Sprite white)
    {
        shardSprite = white;
        var go = new GameObject("Shatter");
        go.transform.position = pos;
        go.AddComponent<ShatterEffect>().Spawn();
    }

    private Transform[] shards;
    private Vector2[] velocities;
    private SpriteRenderer[] renderers;
    private float life;

    private void Spawn()
    {
        const int count = 7;
        shards = new Transform[count];
        velocities = new Vector2[count];
        renderers = new SpriteRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var s = new GameObject("Shard");
            s.transform.SetParent(transform, false);
            float size = 0.18f + (i % 3) * 0.06f;
            s.transform.localScale = new Vector3(size, size, 1f);
            var sr = s.AddComponent<SpriteRenderer>();
            sr.sprite = shardSprite;
            sr.color = Color.white;
            sr.sortingOrder = 5;
            renderers[i] = sr;
            shards[i] = s.transform;

            // fan out deterministically-ish (visual only; not in the sim)
            float ang = (i / (float)count) * Mathf.PI * 2f;
            float speed = 4f + (i % 4);
            velocities[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) + 0.4f) * speed;
        }
    }

    private void Update()
    {
        if (shards == null) return;
        life += Time.deltaTime;
        float t = life / 0.55f;
        for (int i = 0; i < shards.Length; i++)
        {
            velocities[i] += Vector2.down * 14f * Time.deltaTime; // gravity
            shards[i].position += (Vector3)(velocities[i] * Time.deltaTime);
            shards[i].Rotate(0f, 0f, 360f * Time.deltaTime);
            var c = renderers[i].color;
            c.a = Mathf.Clamp01(1f - t);
            renderers[i].color = c;
        }
        if (t >= 1f) Destroy(gameObject);
    }
}
