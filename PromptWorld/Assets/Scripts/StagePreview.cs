using UnityEngine;

/// <summary>
/// Renders a stage's "shape" as a tiny white-on-black thumbnail texture —
/// the menu shows what a stage feels like before you tap it.
/// </summary>
public static class StagePreview
{
    public static Texture2D Render(StageData data, int texW = 192, int texH = 96)
    {
        float minX = data.playerStart.x, maxX = data.playerStart.x;
        float minY = data.playerStart.y, maxY = data.playerStart.y;

        void Grow(float x, float y, float w, float h)
        {
            minX = Mathf.Min(minX, x - w / 2f);
            maxX = Mathf.Max(maxX, x + w / 2f);
            minY = Mathf.Min(minY, y - h / 2f);
            maxY = Mathf.Max(maxY, y + h / 2f);
        }

        foreach (PartData p in data.parts) Grow(p.x, p.y, p.w, p.h);
        Grow(data.goal.x, data.goal.y, data.goal.w, data.goal.h);

        // Padding around the stage bounds so the geometry never touches the
        // thumbnail edge (which reads as "cut off"). Extra on X because stages
        // are usually wide and would otherwise hug the left/right border.
        minX -= 4f; maxX += 4f; minY -= 2.5f; maxY += 2.5f;
        float scale = Mathf.Min(texW / (maxX - minX), texH / (maxY - minY));
        float offX = (texW - (maxX - minX) * scale) / 2f;
        float offY = (texH - (maxY - minY) * scale) / 2f;

        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        var pixels = new Color32[texW * texH];
        var black = new Color32(0, 0, 0, 255);
        var white = new Color32(255, 255, 255, 255);

        // Backdrop: if the stage has a 1-bit bg image, paint it (dimmed) behind
        // the geometry so the thumbnail carries the course's atmosphere; else a
        // plain black field. The gameplay shapes are drawn on top in pure white,
        // so they stay legible against the darkened backdrop.
        int[] bgBits = DecodeBg(data.bg);
        if (bgBits != null)
        {
            int bw = data.bg.w, bh = data.bg.h;
            // COVER-fit the bg into the thumbnail (crop overflow, no distortion).
            float bgAspect = (float)bw / bh, thumbAspect = (float)texW / texH;
            float uScale, vScale, uOff, vOff;
            if (bgAspect > thumbAspect) { vScale = 1f; uScale = thumbAspect / bgAspect; }
            else { uScale = 1f; vScale = bgAspect / thumbAspect; }
            uOff = (1f - uScale) / 2f; vOff = (1f - vScale) / 2f;
            // Dimmed backdrop tones — darker than in-game so white shapes pop.
            var bgInk = new Color32(16, 17, 20, 255);
            var bgPaper = new Color32(70, 73, 80, 255);
            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float u = uOff + (x + 0.5f) / texW * uScale;
                    float v = vOff + (y + 0.5f) / texH * vScale;
                    int sx = Mathf.Clamp((int)(u * bw), 0, bw - 1);
                    // bg data is top-down; thumbnail is bottom-up. Flip Y.
                    int sy = Mathf.Clamp((int)((1f - v) * bh), 0, bh - 1);
                    int bit = bgBits[sy * bw + sx];
                    if (data.bg.invert) bit ^= 1;
                    pixels[y * texW + x] = bit == 1 ? bgPaper : bgInk;
                }
            }
        }
        else
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = black;
        }

        void Fill(float cx, float cy, float w, float h)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((cx - w / 2f - minX) * scale + offX), 0, texW - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((cx + w / 2f - minX) * scale + offX), 0, texW - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((cy - h / 2f - minY) * scale + offY), 0, texH - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((cy + h / 2f - minY) * scale + offY), 0, texH - 1);
            if (x1 - x0 < 1) x1 = Mathf.Min(x0 + 1, texW - 1);
            if (y1 - y0 < 1) y1 = Mathf.Min(y0 + 1, texH - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    pixels[y * texW + x] = white;
        }

        void Frame(float cx, float cy, float w, float h)
        {
            Fill(cx, cy + h / 2f, w, 0f);
            Fill(cx, cy - h / 2f, w, 0f);
            Fill(cx - w / 2f, cy, 0f, h);
            Fill(cx + w / 2f, cy, 0f, h);
        }

        foreach (PartData p in data.parts)
        {
            switch (p.type)
            {
                case "gravityFlip": Frame(p.x, p.y, p.w, p.h); break;
                case "timedGate": Frame(p.x, p.y, p.w, p.h); break;
                case "hazard": Fill(p.x, p.y, p.w * 0.6f, p.h * 0.6f); break;
                case "key": Fill(p.x, p.y, p.w * 0.5f, p.h * 0.5f); break;
                case "cannon": Fill(p.x, p.y, p.w, p.h); break;
                default: Fill(p.x, p.y, p.w, p.h); break;
            }
        }
        Frame(data.goal.x, data.goal.y, data.goal.w, data.goal.h);
        Fill(data.playerStart.x, data.playerStart.y, 1f, 1f);

        tex.SetPixels32(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        return tex;
    }

    /// <summary>Decode a BgData's RLE-varint stream into a row-major 0/1 bit grid
    /// (top-down, same orientation as BackgroundArt). Returns null if absent or
    /// malformed — the caller then falls back to a plain black backdrop.</summary>
    private static int[] DecodeBg(BgData bg)
    {
        if (bg == null || string.IsNullOrEmpty(bg.data) || bg.w <= 0 || bg.h <= 0) return null;
        if ((long)bg.w * bg.h > 4_000_000) return null;
        byte[] stream;
        try { stream = System.Convert.FromBase64String(bg.data); }
        catch { return null; }
        if (stream.Length < 2) return null;

        int total = bg.w * bg.h;
        var bits = new int[total];
        int p = 0;
        int cur = stream[0] & 1;
        int i = 1;
        while (i < stream.Length && p < total)
        {
            int run = 0, shift = 0;
            while (i < stream.Length)
            {
                byte bb = stream[i++];
                run |= (bb & 0x7f) << shift;
                if ((bb & 0x80) == 0) break;
                shift += 7;
            }
            int endP = p + run;
            if (endP > total) endP = total;
            for (; p < endP; p++) bits[p] = cur;
            cur ^= 1;
        }
        return bits;
    }
}
