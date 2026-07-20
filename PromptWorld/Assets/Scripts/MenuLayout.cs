using UnityEngine;

/// <summary>
/// Reflows the menu between a two-column landscape layout and a stacked portrait
/// layout based on the aspect ratio, every time the screen size changes. Three
/// blocks: a controls column, the stage list (a fixed frame that scrolls inside
/// itself), and a CREATE bar that stays visible below the list. No hand-placed
/// pixels — each block just gets its anchors set so it fills its share of the
/// safe area.
/// </summary>
public class MenuLayout : MonoBehaviour
{
    [SerializeField] private RectTransform controls;  // title / name / search / sort / settings
    [SerializeField] private RectTransform list;       // stage list (fixed frame, inner scroll)
    [SerializeField] private RectTransform createBar;  // CREATE button — always visible

    private const float CreateBarPx = 64f; // fixed height reserved for the CREATE bar

    private int lastW, lastH;

    private void Start() { StartCoroutine(ApplyWhenReady()); }

    // Apply once now, then again after a frame — the controls' content height is
    // only measurable after the first layout pass settles, so the second pass
    // removes any gap between the sort row and the stage list.
    private System.Collections.IEnumerator ApplyWhenReady()
    {
        Apply();
        yield return null;
        Apply();
        yield return new WaitForEndOfFrame();
        Apply();
    }

    private void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH) Apply();
    }

    private void Apply()
    {
        lastW = Screen.width; lastH = Screen.height;
        if (controls == null || list == null) return;

        float aspect = lastH > 0 ? (float)lastW / lastH : 1.6f;
        bool wide = aspect >= 1.15f; // landscape-ish => two columns

        if (wide)
        {
            // Left column ~40%; CREATE pinned to the bottom of that column; the
            // list fills the right side full-height.
            SetAnchors(controls, new Vector2(0f, 0f), new Vector2(0.40f, 1f),
                new Vector2(0f, CreateBarPx + 8f), new Vector2(0f, 0f));
            if (createBar != null)
                SetAnchors(createBar, new Vector2(0f, 0f), new Vector2(0.40f, 0f),
                    new Vector2(0f, 0f), new Vector2(0f, CreateBarPx));
            SetAnchors(list, new Vector2(0.42f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
        }
        else
        {
            // Portrait: controls occupy exactly their CONTENT height at the top
            // (measured at runtime), so the stage list starts right below the
            // sort row with no dead gap. Falls back to a fraction if the height
            // can't be measured yet. CREATE stays pinned to the bottom.
            controls.anchorMin = new Vector2(0f, 1f); controls.anchorMax = new Vector2(1f, 1f);
            controls.pivot = new Vector2(0.5f, 1f);

            float contentH = MeasureControlsHeight();
            if (contentH <= 1f) contentH = (lastH * 0.32f); // fallback before layout settles
            controls.offsetMin = new Vector2(0f, -contentH);
            controls.offsetMax = new Vector2(0f, 0f);

            if (createBar != null)
                SetAnchors(createBar, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 0f), new Vector2(0f, CreateBarPx));

            // List fills between the CREATE bar and the bottom of the controls.
            SetAnchors(list, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, CreateBarPx + 8f), new Vector2(0f, -(contentH + 8f)));
        }
    }

    // The controls column's real content height (title + name + search + sort),
    // via the VerticalLayoutGroup's preferred size. Returns 0 if not ready.
    private float MeasureControlsHeight()
    {
        if (controls == null) return 0f;
        var vlg = controls.GetComponentInChildren<UnityEngine.UI.VerticalLayoutGroup>();
        if (vlg == null) return 0f;
        var rt = vlg.transform as RectTransform;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        float h = UnityEngine.UI.LayoutUtility.GetPreferredHeight(rt);
        return h;
    }

    private static void SetAnchors(RectTransform r, Vector2 min, Vector2 max, Vector2 oMin, Vector2 oMax)
    {
        r.anchorMin = min; r.anchorMax = max; r.offsetMin = oMin; r.offsetMax = oMax;
    }
}
