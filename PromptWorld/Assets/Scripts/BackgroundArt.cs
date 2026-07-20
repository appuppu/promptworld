using System;
using UnityEngine;

/// <summary>
/// Decodes a stage's optional 1-bit backdrop (BgData) into a texture and lays it
/// behind the play field. Pure decoration — the deterministic sim never sees it.
///
/// Data format: base64 of a byte stream = [startColour, run, run, run, ...] where
/// each run is a LEB128 varint and colours alternate (startColour, !start, ...).
/// A "1" pixel is white (paper), "0" is black (ink). w x h pixels, row-major.
/// </summary>
public static class BackgroundArt
{
    // How far (as a fraction of camera travel) the backdrop drifts — 0 = pinned
    // to the screen, 1 = moves with the world. 0.1 = distant parallax depth.
    private const float ParallaxFactor = 0.1f;

    /// <summary>Builds the backdrop object under `parent` (the camera), framed to
    /// fill the camera's view. `worldHalfSpan` is the max distance the camera can
    /// travel from the stage centre on each axis — used to over-scale the image so
    /// parallax drift never reveals the black edge. Returns null if no valid bg.</summary>
    public static GameObject Build(Transform parent, BgData bg, float cameraViewSize, float worldHalfSpanX, float worldHalfSpanY)
    {
        if (bg == null || string.IsNullOrEmpty(bg.data) || bg.w <= 0 || bg.h <= 0) return null;
        if ((long)bg.w * bg.h > 4_000_000) return null; // sanity cap (~2000x2000)

        byte[] stream;
        try { stream = Convert.FromBase64String(bg.data); }
        catch { return null; }
        if (stream.Length < 2) return null;

        int total = bg.w * bg.h;
        var pixels = new Color32[total];
        // GREY the backdrop down so it sits BEHIND the pure-white/black gameplay
        // sprites: ink -> dark grey, paper -> mid grey. The foreground's true
        // black (#000) and white (#fff) then read clearly against it, keeping the
        // monochrome look. invert swaps the two.
        Color32 ink = new Color32(30, 32, 38, 255);      // background "black"
        Color32 paper = new Color32(120, 124, 132, 255); // background "white"
        Color32 black = bg.invert ? paper : ink;
        Color32 white = bg.invert ? ink : paper;

        int p = 0;                 // pixel cursor
        int cur = stream[0] & 1;   // starting colour
        int i = 1;
        while (i < stream.Length && p < total)
        {
            // read a LEB128 varint run length
            int run = 0, shift = 0;
            while (i < stream.Length)
            {
                byte bb = stream[i++];
                run |= (bb & 0x7f) << shift;
                if ((bb & 0x80) == 0) break;
                shift += 7;
            }
            Color32 c = cur == 1 ? white : black;
            int endP = p + run;
            if (endP > total) endP = total;
            // Texture rows are bottom-up; our data is top-down. Flip Y per pixel.
            for (; p < endP; p++)
            {
                int x = p % bg.w;
                int y = p / bg.w;
                int ty = bg.h - 1 - y;
                pixels[ty * bg.w + x] = c;
            }
            cur ^= 1;
        }

        var tex = new Texture2D(bg.w, bg.h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;   // crisp 1-bit pixels, no smoothing
        tex.SetPixels32(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, bg.w, bg.h),
            new Vector2(0.5f, 0.5f), 100f);

        var go = new GameObject("Background");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = -100;               // behind every gameplay sprite

        // COVER-fit the actual camera view (no gaps at any window aspect). The
        // orthographic view is viewH tall and viewH*screenAspect wide; scale the
        // image up uniformly until it covers BOTH, cropping the overflow.
        float viewH = cameraViewSize * 2f;
        float screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : (16f / 9f);
        float viewW = viewH * screenAspect;
        // World size the sprite would occupy at scale 1 (Sprite built at 100 px/unit).
        float baseW = bg.w / 100f;
        float baseH = bg.h / 100f;

        // PARALLAX OVERSCAN: with parallax the backdrop drifts across the screen by
        // (camera travel * (1 - ParallaxFactor)). Max drift from the start is
        // worldHalfSpan * (1 - ParallaxFactor); the image must still cover the view
        // at the far end of that drift or the black edge shows. Cover a FULL view
        // plus twice the drift, and add a generous margin (extra half-view + 20%)
        // to absorb camera smooth-follow overshoot and any zoom slop — a visible
        // black edge is far worse than a slightly softer backdrop.
        float drift = 1f - ParallaxFactor;
        float coverW = viewW + worldHalfSpanX * drift * 2f + viewW; // +1 full view of slack
        float coverH = viewH + worldHalfSpanY * drift * 2f + viewH;
        float sx = coverW / baseW;
        float sy = coverH / baseH;
        float s = Mathf.Max(sx, sy) * 1.2f; // + 20% safety overscan
        go.transform.localScale = new Vector3(s, s, 1f);

        // Parallax mover: the bg is a child of the camera, so by default it moves
        // 1:1 with the camera (pinned to screen). This counter-shifts it by the
        // camera's travel * (1 - ParallaxFactor) so it ends up moving only
        // ParallaxFactor as fast — distant-background depth. Pure visual.
        var px = go.AddComponent<BackgroundParallax>();
        px.Init(parent, ParallaxFactor);

        return go;
    }
}

/// <summary>Makes a camera-child backdrop drift slower than the camera (parallax).
/// Records the camera's start position and, each late frame, offsets the backdrop
/// by -(cameraTravel * (1 - factor)) so it visually moves at `factor` speed.</summary>
public class BackgroundParallax : MonoBehaviour
{
    private Transform cam;
    private float factor;
    private Vector3 camStart;

    public void Init(Transform camera, float parallaxFactor)
    {
        cam = camera;
        factor = parallaxFactor;
        if (cam != null) camStart = cam.position;
    }

    private void LateUpdate()
    {
        if (cam == null) return;
        // Camera travel since start, on X/Y only (keep the bg's own depth Z).
        float dx = (cam.position.x - camStart.x) * (1f - factor);
        float dy = (cam.position.y - camStart.y) * (1f - factor);
        // As a child of the camera the bg would sit at local 0; counter-shift it.
        transform.localPosition = new Vector3(-dx, -dy, transform.localPosition.z);
    }
}
