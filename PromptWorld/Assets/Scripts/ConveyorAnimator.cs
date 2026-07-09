using UnityEngine;

/// <summary>
/// Scrolls a row of black stripes across a conveyor belt so its direction and
/// motion read at a glance. Purely cosmetic — the physics live in the sim.
/// Stripes are masked to the belt so they never spill onto adjacent blocks.
/// </summary>
public class ConveyorAnimator : MonoBehaviour
{
    private Transform[] stripes;
    private float speed;   // local units/sec, signed by direction
    private float spacing;

    private const float HalfBelt = 0.44f; // keep stripes inside the belt face

    public void Init(float beltWidth, float beltHeight, float dir, float beltSpeed, Sprite sprite)
    {
        spacing = 0.26f;
        int count = Mathf.CeilToInt((HalfBelt * 2f) / spacing) + 2;
        speed = (dir >= 0f ? 1f : -1f) * (beltSpeed / Mathf.Max(0.01f, beltWidth));

        stripes = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            var s = new GameObject("Stripe");
            s.transform.SetParent(transform, false);
            s.transform.localPosition = new Vector3(-HalfBelt + i * spacing, 0f, 0f);
            s.transform.localScale = new Vector3(0.1f, 0.55f, 1f);
            var sr = s.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = Color.black;
            sr.sortingOrder = 1;
            stripes[i] = s.transform;
        }
    }

    private void Update()
    {
        if (stripes == null) return;
        float dx = speed * Time.deltaTime;
        float range = HalfBelt * 2f;
        foreach (Transform s in stripes)
        {
            Vector3 p = s.localPosition;
            p.x += dx;
            // wrap strictly within the belt face so nothing spills over neighbors
            while (p.x > HalfBelt) p.x -= range;
            while (p.x < -HalfBelt) p.x += range;
            s.localPosition = p;
        }
    }
}
