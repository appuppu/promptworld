using UnityEngine;

/// <summary>
/// Fills a fan's wind zone with faint white streaks that flow in the wind
/// direction, so "air is being blown this way" reads at a glance. Purely
/// cosmetic — the push physics live in the sim (SimFan). The streaks are laid
/// out in the zone's LOCAL space (parent scaled to w x h), tinted low-alpha so
/// they read as moving air rather than solid bars, and wrapped so they loop
/// forever within the zone.
/// </summary>
public class FanAnimator : MonoBehaviour
{
    private Transform[] streaks;
    private Vector2 flow;     // local units/sec along the wind direction
    private bool vertical;

    public void Init(float zoneW, float zoneH, float dirX, float dirY, float power, Sprite sprite)
    {
        // Normalize the wind direction; default straight up if unset.
        float dx = dirX;
        float dy = dirY;
        if (dx == 0f && dy == 0f) dy = 1f;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 0.0001f) len = 1f;
        dx /= len;
        dy /= len;
        vertical = Mathf.Abs(dy) >= Mathf.Abs(dx);

        // Parent is scaled to (w,h); children live in local [-0.5,0.5]. Convert a
        // world speed to local space per axis so the flow speed looks even.
        float wsafe = Mathf.Max(0.01f, zoneW);
        float hsafe = Mathf.Max(0.01f, zoneH);
        float speed = Mathf.Clamp(power, 4f, 60f) * 0.06f; // world u/s -> gentle drift
        flow = new Vector2(dx * speed / wsafe, dy * speed / hsafe);

        // A grid of streaks across the cross-axis, several along the flow axis.
        int across = 3;
        int along = 6;
        streaks = new Transform[across * along];
        int idx = 0;
        for (int a = 0; a < across; a++)
        {
            float crossT = (across == 1) ? 0f : ((float)a / (across - 1) - 0.5f) * 0.7f;
            for (int b = 0; b < along; b++)
            {
                float alongT = ((float)b / along - 0.5f) * 1.0f;
                var s = new GameObject("Streak");
                s.transform.SetParent(transform, false);
                float lx = vertical ? crossT : alongT;
                float ly = vertical ? alongT : crossT;
                s.transform.localPosition = new Vector3(lx, ly, 0f);
                // Streak is a short dash oriented along the flow (thin cross-axis).
                float longS = 0.16f;
                float thinS = 0.05f;
                s.transform.localScale = vertical
                    ? new Vector3(thinS, longS, 1f)
                    : new Vector3(longS, thinS, 1f);
                var sr = s.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = new Color(1f, 1f, 1f, 0.5f);
                sr.sortingOrder = 1;
                streaks[idx++] = s.transform;
            }
        }
    }

    private void Update()
    {
        if (streaks == null) return;
        Vector2 d = flow * Time.deltaTime;
        foreach (Transform s in streaks)
        {
            Vector3 p = s.localPosition;
            p.x += d.x;
            p.y += d.y;
            // Wrap within the local zone [-0.5, 0.5] on the flow axis so streaks
            // recycle forever. A little fade near the edges would be nicer but a
            // hard wrap reads fine at this alpha.
            if (vertical)
            {
                while (p.y > 0.5f) p.y -= 1.0f;
                while (p.y < -0.5f) p.y += 1.0f;
            }
            else
            {
                while (p.x > 0.5f) p.x -= 1.0f;
                while (p.x < -0.5f) p.x += 1.0f;
            }
            s.localPosition = p;
        }
    }
}
