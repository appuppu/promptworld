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

        minX -= 1.5f; maxX += 1.5f; minY -= 1.5f; maxY += 1.5f;
        float scale = Mathf.Min(texW / (maxX - minX), texH / (maxY - minY));
        float offX = (texW - (maxX - minX) * scale) / 2f;
        float offY = (texH - (maxY - minY) * scale) / 2f;

        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        var pixels = new Color32[texW * texH];
        var black = new Color32(0, 0, 0, 255);
        var white = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = black;

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
}
