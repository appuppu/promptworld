// Programmatic icon sprites for the touch buttons (no image assets shipped).
using UnityEngine;

public static class TacIcons
{
    const int S = 96;

    static Sprite FromPainter(System.Action<Color32[]> paint)
    {
        var px = new Color32[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
        paint(px);
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
    }

    static void Dot(Color32[] px, float cx, float cy, float r)
    {
        for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
            for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
            {
                if (x < 0 || y < 0 || x >= S || y >= S) continue;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a = Mathf.Clamp01(r - d + 0.5f);
                if (a > 0) Blend(px, x, y, a);
            }
    }

    static void Ring(Color32[] px, float cx, float cy, float r, float w)
    {
        for (int y = (int)(cy - r - w); y <= cy + r + w; y++)
            for (int x = (int)(cx - r - w); x <= cx + r + w; x++)
            {
                if (x < 0 || y < 0 || x >= S || y >= S) continue;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a = Mathf.Clamp01(w / 2f - Mathf.Abs(d - r) + 0.5f);
                if (a > 0) Blend(px, x, y, a);
            }
    }

    static void Line(Color32[] px, float x0, float y0, float x1, float y1, float w)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        int n = Mathf.CeilToInt(len * 2f) + 1;
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            Dot(px, x0 + dx * t, y0 + dy * t, w / 2f);
        }
    }

    static void Blend(Color32[] px, int x, int y, float a)
    {
        int i = y * S + x;
        byte na = (byte)Mathf.Max(px[i].a, (byte)(a * 255));
        px[i] = new Color32(255, 255, 255, na);
    }

    static Sprite _jump, _bomb, _drone, _scope, _sneak, _fire;

    // muzzle burst: center dot + rays
    public static Sprite Fire()
    {
        if (_fire != null) return _fire;
        _fire = FromPainter(px =>
        {
            Dot(px, 48, 48, 10);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                Line(px, 48 + c * 18, 48 + sn * 18, 48 + c * (i % 2 == 0 ? 34 : 27), 48 + sn * (i % 2 == 0 ? 34 : 27), 6);
            }
        });
        return _fire;
    }

    // upward arrow
    public static Sprite Jump()
    {
        if (_jump != null) return _jump;
        _jump = FromPainter(px =>
        {
            Line(px, 48, 22, 48, 74, 9);
            Line(px, 48, 76, 26, 52, 9);
            Line(px, 48, 76, 70, 52, 9);
        });
        return _jump;
    }

    // round bomb + fuse + spark
    public static Sprite Bomb()
    {
        if (_bomb != null) return _bomb;
        _bomb = FromPainter(px =>
        {
            Dot(px, 48, 40, 24);
            Line(px, 48, 62, 48, 74, 7);
            Line(px, 48, 74, 58, 80, 6);
            Dot(px, 64, 84, 5);
        });
        return _bomb;
    }

    // quadcopter: body + 4 arms + rotor rings
    public static Sprite Drone()
    {
        if (_drone != null) return _drone;
        _drone = FromPainter(px =>
        {
            Dot(px, 48, 48, 9);
            Line(px, 48, 48, 26, 26, 5);
            Line(px, 48, 48, 70, 26, 5);
            Line(px, 48, 48, 26, 70, 5);
            Line(px, 48, 48, 70, 70, 5);
            Ring(px, 24, 24, 10, 4);
            Ring(px, 72, 24, 10, 4);
            Ring(px, 24, 72, 10, 4);
            Ring(px, 72, 72, 10, 4);
        });
        return _drone;
    }

    // scope crosshair
    public static Sprite Scope()
    {
        if (_scope != null) return _scope;
        _scope = FromPainter(px =>
        {
            Ring(px, 48, 48, 26, 5);
            Line(px, 48, 8, 48, 30, 5);
            Line(px, 48, 66, 48, 88, 5);
            Line(px, 8, 48, 30, 48, 5);
            Line(px, 66, 48, 88, 48, 5);
            Dot(px, 48, 48, 4);
        });
        return _scope;
    }

    // crouch: figure squatting low (head + bent body line)
    public static Sprite Sneak()
    {
        if (_sneak != null) return _sneak;
        // double down-chevron: "get low"
        _sneak = FromPainter(px =>
        {
            Line(px, 26, 66, 48, 48, 8);
            Line(px, 48, 48, 70, 66, 8);
            Line(px, 26, 44, 48, 26, 8);
            Line(px, 48, 26, 70, 44, 8);
        });
        return _sneak;
    }
}
