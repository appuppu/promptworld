using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prompt World's UI design system. One place for every color, spacing, font
/// size, and component factory so the whole app reads as one thing and nothing
/// is hand-placed by pixel. Built on Unity auto-layout (LayoutGroup +
/// LayoutElement) so it never overlaps across aspect ratios; pair with a
/// SafeAreaFitter root so it never sits under a notch.
///
/// Visual language: pure black ground, pure white foreground, thin white
/// "surfaces", no rounded corners (honor the world's straight geometry),
/// 8px spacing grid, uppercase tracked labels.
/// </summary>
public static class UI
{
    // ---- color tokens -------------------------------------------------------
    public static readonly Color Fg = Color.white;
    public static readonly Color Bg = Color.black;
    public static readonly Color Surface1 = new Color(1f, 1f, 1f, 0.06f);
    public static readonly Color Surface2 = new Color(1f, 1f, 1f, 0.10f);
    public static readonly Color Surface3 = new Color(1f, 1f, 1f, 0.16f);
    public static readonly Color Line = new Color(1f, 1f, 1f, 0.14f);
    public static readonly Color Dim = new Color(1f, 1f, 1f, 0.5f);
    public static readonly Color Dim2 = new Color(1f, 1f, 1f, 0.32f);
    public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

    // ---- spacing / type scale (8px grid) -----------------------------------
    public const float S1 = 8f, S2 = 16f, S3 = 24f, S4 = 32f;
    public const float FontLabel = 22f, FontBody = 26f, FontTitle = 58f, FontHuge = 84f;

    // ---- primitives ---------------------------------------------------------

    /// <summary>A full-rect child that stretches to its parent with padding.</summary>
    public static RectTransform Stretch(Transform parent, string name, Vector4 padding = default)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(padding.x, padding.w);   // left, bottom
        r.offsetMax = new Vector2(-padding.z, -padding.y); // -right, -top
        return r;
    }

    /// <summary>A solid/surface rectangle (an Image) as a layout child.</summary>
    public static RectTransform Box(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go.GetComponent<RectTransform>();
    }

    /// <summary>Adds a vertical auto-layout to a RectTransform.</summary>
    public static VerticalLayoutGroup VStack(RectTransform rect, float spacing = S1,
        TextAnchor align = TextAnchor.UpperLeft, RectOffset pad = null)
    {
        var v = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childAlignment = align;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        if (pad != null) v.padding = pad;
        return v;
    }

    public static HorizontalLayoutGroup HStack(RectTransform rect, float spacing = S1,
        TextAnchor align = TextAnchor.MiddleLeft, RectOffset pad = null)
    {
        var h = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childAlignment = align;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = true; h.childForceExpandHeight = false;
        if (pad != null) h.padding = pad;
        return h;
    }

    public static LayoutElement Sized(GameObject go, float minH = -1, float minW = -1,
        float prefH = -1, float prefW = -1, float flexW = -1, float flexH = -1)
    {
        var e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (minH >= 0) e.minHeight = minH;
        if (minW >= 0) e.minWidth = minW;
        if (prefH >= 0) e.preferredHeight = prefH;
        if (prefW >= 0) e.preferredWidth = prefW;
        if (flexW >= 0) e.flexibleWidth = flexW;
        if (flexH >= 0) e.flexibleHeight = flexH;
        return e;
    }

    // ---- components ---------------------------------------------------------

    public static TMP_Text Text(Transform parent, string name, string text, float size,
        Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, float tracking = 0f,
        bool bold = false, bool autoSize = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.characterSpacing = tracking;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        // autoSize: shrink to fit (down to ~55% of size) so long/localized labels
        // don't clip on narrow screens. Ellipsis stays as the final fallback.
        if (autoSize)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.55f;
        }
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    /// <summary>An uppercase tracked label in the dim color.</summary>
    public static TMP_Text Label(Transform parent, string text, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        return Text(parent, "Label", text, FontLabel * 0.7f, Dim, align, 3f);
    }

    /// <summary>A surface button with a centered label. Returns the Button.</summary>
    public static Button Button(Transform parent, string name, string label, float fontSize = 24f,
        bool ghost = false, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = ghost ? Clear : Surface1;

        var outline = ghost ? Clear : Line;
        AddBorder(go.transform, outline);

        var t = Text(go.transform, "Text", label, fontSize, ghost ? Dim : Fg, align, 2f, true, autoSize: true);
        var tr = (RectTransform)t.transform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(align == TextAlignmentOptions.Left ? 14f : 6f, 0f);
        tr.offsetMax = new Vector2(-6f, 0f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None; // hover handled by HoverTint (works with alpha surfaces)
        var hover = go.AddComponent<HoverTint>();
        hover.Init(img, ghost ? Clear : Surface1, ghost ? Surface1 : Surface3);
        return btn;
    }

    /// <summary>A surface text input with a placeholder. Returns the TMP_InputField.</summary>
    public static TMP_InputField Input(Transform parent, string name, string placeholder, float fontSize = 26f)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Surface1;
        AddBorder(go.transform, Line);

        var input = go.AddComponent<TMP_InputField>();

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var ar = area.GetComponent<RectTransform>();
        ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one;
        ar.offsetMin = new Vector2(16f, 4f); ar.offsetMax = new Vector2(-16f, -4f);

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(area.transform, false);
        var tt = textGo.AddComponent<TextMeshProUGUI>();
        tt.fontSize = fontSize; tt.color = Fg; tt.alignment = TextAlignmentOptions.MidlineLeft;
        StretchInto(textGo);

        var phGo = new GameObject("Placeholder", typeof(RectTransform));
        phGo.transform.SetParent(area.transform, false);
        var ph = phGo.AddComponent<TextMeshProUGUI>();
        ph.text = placeholder; ph.fontSize = fontSize; ph.color = Dim2;
        ph.alignment = TextAlignmentOptions.MidlineLeft; ph.characterSpacing = 1f;
        StretchInto(phGo);

        input.textViewport = ar;
        input.textComponent = tt;
        input.placeholder = ph;
        input.caretColor = Fg;
        input.customCaretColor = true;
        input.selectionColor = new Color(1f, 1f, 1f, 0.3f);
        return input;
    }

    private static void StretchInto(GameObject go)
    {
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    /// <summary>Four thin edges forming a hairline border on a rect.</summary>
    public static void AddBorder(Transform parent, Color color)
    {
        if (color.a <= 0f) return;
        Edge(parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(0f, 0f), color); // top
        Edge(parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), color);  // bottom
        Edge(parent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), color);  // left
        Edge(parent, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(0f, 0f), color); // right
    }

    private static void Edge(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color color)
    {
        var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        go.GetComponent<Image>().raycastTarget = false;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax;
    }
}

/// <summary>Simple hover/press tint for buttons (Image color swap).</summary>
public class HoverTint : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler,
    UnityEngine.EventSystems.IPointerDownHandler,
    UnityEngine.EventSystems.IPointerUpHandler
{
    private Image img;
    private Color normal, hi;

    public void Init(Image image, Color normalColor, Color hiColor)
    {
        img = image; normal = normalColor; hi = hiColor;
        if (img != null) img.color = normal;
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { if (img) img.color = hi; }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { if (img) img.color = normal; }
    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { if (img) img.color = hi; }
    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) { if (img) img.color = normal; }
}
