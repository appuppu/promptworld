// TAC UI kit — mirrors the WEB design language exactly (tac.html/tac-home.html):
// bg #0b0d10, panel #12151b, hairline #2a2f38 borders, teal #4dd2c3 accent,
// letter-spaced uppercase labels, flat bordered buttons, circular color-coded
// touch buttons. Helvetica Neue + Hiragino, no rounded-pill styling.
using UnityEngine;
using UnityEngine.UI;

public static class TacUi
{
    // web palette (:root of tac-home.html + tac.html)
    public static readonly Color Bg = Hex(0x0B0D10);
    public static readonly Color Panel = Hex(0x12151B);
    public static readonly Color Line = Hex(0x2A2F38);
    public static readonly Color Fg = Hex(0xF2F4F6);
    public static readonly Color Dim = Hex(0x9AA3AD);
    public static readonly Color Teal = Hex(0x4DD2C3);
    public static readonly Color Warn = Hex(0xFFD23E);
    public static readonly Color Alert = Hex(0xFF5A48);
    public static readonly Color DroneGreen = Hex(0x3DDC84);
    public static readonly Color ScopeCyan = Hex(0x59D9F0);
    public static readonly Color Promo = Hex(0xCFD6DE);
    public static readonly Color Overlay = new Color(6 / 255f, 8 / 255f, 10 / 255f, 0.85f);

    public static Color Hex(int v)
    {
        return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
    }

    static Font _font;
    public static Font Fnt()
    {
        if (_font == null)
        {
            string[] names = { "Helvetica Neue", "Hiragino Kaku Gothic ProN", "Hiragino Sans", "PingFang SC", "Apple SD Gothic Neo", "Arial" };
            try { _font = Font.CreateDynamicFontFromOSFont(names, 32); } catch (System.Exception) { }
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }

    // letter-spacing like the web's .2em tracking (latin only; CJK left as-is)
    public static string Track(string s)
    {
        bool latin = true;
        foreach (var ch in s) if (ch > 0x2FF) { latin = false; break; }
        if (!latin) return s;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i < s.Length - 1) sb.Append(' ');
        }
        return sb.ToString();
    }

    // 1px hairline border sprite (transparent center), sliced
    static Sprite _border;
    public static Sprite Border()
    {
        if (_border != null) return _border;
        int S = 8;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                px[y * S + x] = (x == 0 || y == 0 || x == S - 1 || y == S - 1) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
        tex.SetPixels32(px);
        tex.Apply(false);
        _border = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
        return _border;
    }

    // ---- rounded corners (subtle 6px radius, user decision 2026-07-20) ------
    // The border sprite is sliced with a 100 px/unit factor: a corner region of
    // `Rad` texels reproduces a ~Rad-canvas-unit radius on screen (canvas is the
    // 420-wide reference). We bake sprites at 2x that radius so the rounded
    // corner has enough resolution, then 9-slice with the corner region = the
    // baked radius. AA via the same distance-field trick as the circle.
    public const int Rad = 6;

    // rounded-rect FILL sprite (opaque interior, rounded corners), sliced
    static Sprite _roundFill;
    public static Sprite RoundFill()
    {
        if (_roundFill == null) _roundFill = MakeRound(false);
        return _roundFill;
    }
    // rounded-rect BORDER sprite (1px stroke, transparent center), sliced
    static Sprite _roundBorder;
    public static Sprite RoundBorder()
    {
        if (_roundBorder == null) _roundBorder = MakeRound(true);
        return _roundBorder;
    }
    static Sprite MakeRound(bool stroke)
    {
        int r = Rad * 2;            // baked corner radius in texels
        int S = r * 2 + 2;          // full sprite: two corners + 2px center
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // distance to the nearest rounded corner's inner circle center
                float cx = x < S / 2 ? r : S - 1 - r;
                float cy = y < S / 2 ? r : S - 1 - r;
                bool inCorner = (x < r && (y < r || y >= S - r)) || (x >= S - r && (y < r || y >= S - r));
                float dEdge; // signed distance from the shape edge (positive = inside)
                if (inCorner)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    dEdge = r - d;
                }
                else
                {
                    // straight edges: distance to the closest of the 4 sides
                    dEdge = Mathf.Min(Mathf.Min(x, S - 1 - x), Mathf.Min(y, S - 1 - y)) + 0.5f;
                }
                float a;
                if (stroke)
                {
                    // 1.4px ring hugging the rounded edge
                    a = Mathf.Clamp01(1.4f - Mathf.Abs(dEdge - 0.7f));
                }
                else
                {
                    a = Mathf.Clamp01(dEdge + 0.5f);
                }
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        // 9-slice border = baked radius r; pixelsPerUnit 100 → r texels ≈ Rad canvas units
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }

    // filled circle + circle outline (for the web's round touch buttons)
    static Sprite _circle, _circleLine;
    public static Sprite Circle()
    {
        if (_circle == null) _circle = MakeCircle(false);
        return _circle;
    }
    public static Sprite CircleLine()
    {
        if (_circleLine == null) _circleLine = MakeCircle(true);
        return _circleLine;
    }
    static Sprite MakeCircle(bool outline)
    {
        int S = 96;
        float r = S / 2f - 1.5f, cx = (S - 1) / 2f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cx) * (y - cx));
                float a = outline ? Mathf.Clamp01(1.6f - Mathf.Abs(r - d)) : Mathf.Clamp01(r - d + 0.5f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
    }

    public static RectTransform Rect(string name, RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
        return rt;
    }

    public static Image Fill(RectTransform rt, Color c)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        return img;
    }

    // panel with fill + hairline border, like every web card (subtle rounded corners)
    public static RectTransform Box(RectTransform parent, Color fill, Color line, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var rt = Rect("box", parent, aMin, aMax, offMin, offMax);
        if (fill.a > 0)
        {
            var fimg = rt.gameObject.AddComponent<Image>();
            fimg.sprite = RoundFill();
            fimg.type = Image.Type.Sliced;
            fimg.color = fill;
        }
        var edge = Rect("edge", rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var eimg = edge.gameObject.AddComponent<Image>();
        eimg.sprite = RoundBorder();
        eimg.type = Image.Type.Sliced;
        eimg.color = line;
        eimg.raycastTarget = false;
        return rt;
    }

    public static Text Label(RectTransform parent, string txt, int size, Color color, TextAnchor anchor, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, bool bold = false)
    {
        var rt = Rect("txt", parent, aMin, aMax, offMin, offMax);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Fnt();
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.fontSize = size;
        t.text = txt;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var sh = rt.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.45f);
        sh.effectDistance = new Vector2(1f, -1f);
        return t;
    }

    // web .btn: transparent, 1px border, uppercase tracked label.
    // NOTE: one Graphic per GameObject — the fill Image doubles as the
    // raycast target; the border is a child.
    public static Button Btn(RectTransform parent, string label, int size, Color edge, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, System.Action onTap, out Text txt)
    {
        var rt = Rect("btn-" + label, parent, aMin, aMax, offMin, offMax);
        var fillImg = rt.gameObject.AddComponent<Image>();
        fillImg.sprite = RoundFill();
        fillImg.type = Image.Type.Sliced;
        fillImg.color = new Color(0, 0, 0, 0.25f);
        fillImg.raycastTarget = true;
        var edgeRt = Rect("edge", rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var eimg = edgeRt.gameObject.AddComponent<Image>();
        eimg.sprite = RoundBorder();
        eimg.type = Image.Type.Sliced;
        eimg.color = edge;
        eimg.raycastTarget = false;
        var b = rt.gameObject.AddComponent<Button>();
        if (onTap != null) b.onClick.AddListener(() => onTap());
        txt = Label(rt, Track(label.ToUpper()), size, edge, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return b;
    }

    // icon variant of the circular touch button
    public static Button RoundIconBtn(RectTransform parent, string name, Sprite icon, Color ring, Vector2 anchor, Vector2 center, float dia, System.Action onTap, out Image iconImg)
    {
        Text _;
        var b = RoundBtn(parent, "", 1, ring, anchor, center, dia, onTap, out _);
        b.gameObject.name = "tbtn-" + name;
        var irt = Rect("icon", (RectTransform)b.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-dia * 0.28f, -dia * 0.28f), new Vector2(dia * 0.28f, dia * 0.28f));
        iconImg = irt.gameObject.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.color = ring;
        iconImg.raycastTarget = false;
        return b;
    }

    // web .tbtn: circular translucent touch button, color-coded 1px ring
    public static Button RoundBtn(RectTransform parent, string label, int size, Color ring, Vector2 anchor, Vector2 center, float dia, System.Action onTap, out Text txt)
    {
        var rt = Rect("tbtn-" + label, parent, anchor, anchor, new Vector2(center.x - dia / 2, center.y - dia / 2), new Vector2(center.x + dia / 2, center.y + dia / 2));
        var bgImg = rt.gameObject.AddComponent<Image>();
        bgImg.sprite = Circle();
        bgImg.color = new Color(10 / 255f, 12 / 255f, 14 / 255f, 0.35f);
        var ringRt = Rect("ring", rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var ringImg = ringRt.gameObject.AddComponent<Image>();
        ringImg.sprite = CircleLine();
        ringImg.color = new Color(ring.r, ring.g, ring.b, 0.75f);
        ringImg.raycastTarget = false;
        var b = rt.gameObject.AddComponent<Button>();
        if (onTap != null) b.onClick.AddListener(() => onTap());
        txt = Label(rt, Track(label.ToUpper()), size, ring, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return b;
    }

    // horizontal slider built from scratch (bar + fill + round handle)
    public static Slider SliderRow(RectTransform parent, float value, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, System.Action<float> onChange)
    {
        var rt = Rect("slider", parent, aMin, aMax, offMin, offMax);
        var bg = Rect("bg", rt, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -3), new Vector2(0, 3));
        Fill(bg, Line);
        var fillArea = Rect("fillArea", rt, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -3), new Vector2(0, 3));
        var fillRt = Rect("fill", fillArea, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Fill(fillRt, Teal);
        var handleArea = Rect("handleArea", rt, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
        var handleRt = Rect("handle", handleArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-11, -11), new Vector2(11, 11));
        var himg = handleRt.gameObject.AddComponent<Image>();
        himg.sprite = Circle();
        himg.color = Fg;
        var sl = rt.gameObject.AddComponent<Slider>();
        sl.fillRect = fillRt;
        sl.handleRect = handleRt;
        sl.targetGraphic = himg;
        sl.minValue = 0f;
        sl.maxValue = 1f;
        sl.value = value;
        if (onChange != null) sl.onValueChanged.AddListener((v) => onChange(v));
        return sl;
    }

    // thin teal divider (web .divider)
    public static void Divider(RectTransform parent, float yAnchor)
    {
        var rt = Rect("divider", parent, new Vector2(0.5f, yAnchor), new Vector2(0.5f, yAnchor), new Vector2(-28, 0), new Vector2(28, 1.4f));
        var img = Fill(rt, Teal);
        img.color = new Color(Teal.r, Teal.g, Teal.b, 0.6f);
        img.raycastTarget = false;
    }

    // divider pinned by an offset from the panel TOP (portrait flow layouts)
    public static void DividerAt(RectTransform parent, float yTop)
    {
        var rt = Rect("divider", parent, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-28, yTop - 1.4f), new Vector2(28, yTop));
        var img = Fill(rt, Teal);
        img.color = new Color(Teal.r, Teal.g, Teal.b, 0.6f);
        img.raycastTarget = false;
    }
}

// Vertical flow layout for portrait panels. Each row is anchored to the panel
// TOP and placed by a running Y cursor (all in canvas units — the width-matched
// scaler fixes canvas width at 420, so heights are consistent across devices).
// Because rows anchor to the top edge of the (safe-area-shrunk) panel, the whole
// stack slides down with the notch on a real device instead of overlapping a
// bottom-pinned footer the way absolute -offsets did.
public class FlowCol
{
    readonly RectTransform parent;
    readonly float sidePad;
    float y; // running cursor, negative-going from the top edge

    public FlowCol(RectTransform parent, float topPad = 14f, float sidePad = 24f)
    {
        this.parent = parent;
        this.sidePad = sidePad;
        this.y = -topPad;
    }

    public float Cursor => y;

    public void Gap(float h) { y -= h; }

    // a full-width row of the given height; the callback fills it, receiving the
    // row RectTransform and its usable width (canvas 420 minus both side pads)
    public RectTransform Row(float h, System.Action<RectTransform, float> fill = null)
    {
        var rt = TacUi.Rect("row", parent, new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(sidePad, y - h), new Vector2(-sidePad, y));
        if (fill != null) fill(rt, 420f - sidePad * 2f);
        y -= h;
        return rt;
    }

    public void Header(string txt, float h)
    {
        TacUi.Label(parent, txt, 20, TacUi.Fg, TextAnchor.MiddleCenter,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, y - h), new Vector2(0, y));
        y -= h;
    }

    public void SectionLabel(string txt)
    {
        TacUi.Label(parent, txt, 12, TacUi.Teal, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(sidePad, y - 20), new Vector2(-sidePad, y));
        y -= 22;
    }

    public void Divider()
    {
        TacUi.DividerAt(parent, y);
        y -= 12;
    }

    // a wrapping paragraph of the given height
    public Text Para(string txt, float h, int size, Color c)
    {
        var t = TacUi.Label(parent, txt, size, c, TextAnchor.UpperLeft,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(sidePad, y - h), new Vector2(-sidePad, y));
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        y -= h;
        return t;
    }
}

// stage map thumbnails, matching tac-home.html drawThumb colors
public static class TacThumb
{
    public static Texture2D Draw(TacJson.JObj st, int W, int H)
    {
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var px = new Color32[W * H];
        var bg = new Color32(11, 13, 16, 255);
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        var arena = st.Obj("arena");
        double aw = arena.Num("w"), ad = arena.Num("d");
        double sc = System.Math.Min(W / aw, H / ad);
        int ox = (int)((W - aw * sc) / 2), oz = (int)((H - ad * sc) / 2);
        System.Action<double, double, double, double, Color32> rect = (x, z, w2, d2, c) =>
        {
            int x0 = ox + (int)((x - w2 / 2) * sc), x1 = ox + (int)((x + w2 / 2) * sc);
            int z0 = oz + (int)((z - d2 / 2) * sc), z1 = oz + (int)((z + d2 / 2) * sc);
            if (x1 <= x0) x1 = x0 + 1;
            if (z1 <= z0) z1 = z0 + 1;
            for (int zz = Mathf.Max(0, z0); zz < Mathf.Min(H, z1); zz++)
                for (int xx = Mathf.Max(0, x0); xx < Mathf.Min(W, x1); xx++)
                    px[zz * W + xx] = c;
        };
        var parts = st.Arr("parts");
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts.Obj(i);
            string ty = p.Str("type");
            double w2 = p.Num("w", 1), d2 = p.Num("d", 1);
            if (ty == "block")
            {
                var bc = new Color32(90, 98, 112, 255);
                if (p.Has("tint") && ColorUtility.TryParseHtmlString(p.Str("tint"), out var btc)) bc = btc;
                rect(p.Num("x"), p.Num("z"), w2, d2, bc);
            }
            else if (ty == "pit") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(10, 12, 15, 255));
            else if (ty == "wall") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(69, 76, 88, 255));
            else if (ty == "platform") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(111, 118, 131, 255));
            else if (ty == "rock") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(87, 94, 106, 255));
            else if (ty == "slope") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(99, 106, 118, 255));
            else if (ty == "crackedWall") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(110, 98, 85, 255));
            else if (ty == "river") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(70, 130, 200, 190));
            else if (ty == "trench") rect(p.Num("x"), p.Num("z"), w2, d2, new Color32(15, 17, 20, 255));
            else if (ty == "switch") rect(p.Num("x"), p.Num("z"), 2, 2, new Color32(138, 123, 255, 255));
            else if (ty == "barrel") rect(p.Num("x"), p.Num("z"), 1.4, 1.4, new Color32(192, 82, 51, 255));
            else if (ty == "mine") rect(p.Num("x"), p.Num("z"), 1.2, 1.2, new Color32(255, 92, 74, 255));
            else if (ty == "medkit") rect(p.Num("x"), p.Num("z"), 1.4, 1.4, new Color32(236, 240, 244, 255));
        }
        var ens = st.Arr("enemies");
        for (int i = 0; i < ens.Count; i++)
        {
            var e = ens.Obj(i);
            rect(e.Num("x"), e.Num("z"), 1.6, 1.6, new Color32(255, 90, 72, 255));
        }
        var ps = st.Obj("playerStart");
        rect(ps.Num("x"), ps.Num("z"), 2, 2, new Color32(77, 210, 195, 255));
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}
