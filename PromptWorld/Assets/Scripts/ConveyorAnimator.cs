using UnityEngine;

/// <summary>
/// Scrolls a row of black stripes across a conveyor belt so its direction and
/// motion read at a glance. Purely cosmetic — the physics live in the sim.
/// </summary>
public class ConveyorAnimator : MonoBehaviour
{
    private Transform[] stripes;
    private float speed;   // local units/sec, signed by direction
    private float span;    // belt width in local units
    private float spacing;

    public void Init(float beltWidth, float beltHeight, float dir, float beltSpeed, Sprite sprite)
    {
        // beltSpeed is world units/sec; convert to local (object is scaled by width)
        span = 1f;                 // local space spans -0.5..0.5 regardless of scale
        spacing = 0.28f;
        int count = Mathf.CeilToInt(span / spacing) + 2;
        speed = (dir >= 0f ? 1f : -1f) * (beltSpeed / Mathf.Max(0.01f, beltWidth));

        stripes = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            var s = new GameObject("Stripe");
            s.transform.SetParent(transform, false);
            s.transform.localPosition = new Vector3(-0.5f + i * spacing, 0f, 0f);
            s.transform.localScale = new Vector3(0.12f, 0.6f, 1f);
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
        foreach (Transform s in stripes)
        {
            Vector3 p = s.localPosition;
            p.x += dx;
            // wrap within the belt
            if (p.x > 0.5f + spacing) p.x -= (span + spacing);
            else if (p.x < -0.5f - spacing) p.x += (span + spacing);
            s.localPosition = p;
        }
    }
}
