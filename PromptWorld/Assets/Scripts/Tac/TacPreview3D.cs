using UnityEngine;

/// <summary>
/// Angled-top-down 3D stage thumbnails for the stage list (mirrors the web
/// home's Preview3D). One shared offscreen camera + RenderTexture + world root is
/// reused for every card: build the stage's geometry cheaply (floor + raised
/// blocks + enemy/player markers), render one frame, and bake it into a
/// Texture2D the card's RawImage displays. Far more "real game shot" than the flat
/// TacThumb map, while staying light — a single camera, one render per card, no
/// per-frame updates. Falls back to TacThumb.Draw when anything goes wrong.
/// </summary>
public static class TacPreview3D
{
    const int RW = 320, RH = 200;
    static Camera cam;
    static RenderTexture rt;
    static Transform root;
    static Texture2D readback;

    static void EnsureRig()
    {
        if (cam != null) return;
        var camGo = new GameObject("TacPreviewCam");
        Object.DontDestroyOnLoad(camGo);
        camGo.hideFlags = HideFlags.HideAndDontSave;
        cam = camGo.AddComponent<Camera>();
        cam.enabled = false;                 // we drive it manually with Render()
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.055f, 0.07f, 0.09f, 1f);
        cam.fieldOfView = 52f;
        cam.nearClipPlane = 0.5f; cam.farClipPlane = 4000f;
        rt = new RenderTexture(RW, RH, 16, RenderTextureFormat.ARGB32) { name = "TacPreviewRT" };
        cam.targetTexture = rt;
        var rootGo = new GameObject("TacPreviewWorld");
        Object.DontDestroyOnLoad(rootGo);
        rootGo.hideFlags = HideFlags.HideAndDontSave;
        root = rootGo.transform;
        readback = new Texture2D(RW, RH, TextureFormat.RGBA32, false);
    }

    static readonly Color PlayerCol = new Color(0.30f, 0.82f, 0.76f);
    static Color EnemyCol(string t)
    {
        switch (t)
        {
            case "gatling": return new Color(0.75f, 0.34f, 0.30f);
            case "sniper": return new Color(0.63f, 0.43f, 0.76f);
            case "drone": return new Color(0.65f, 0.68f, 0.73f);
            case "operator": return new Color(0.76f, 0.63f, 0.37f);
            case "shield": return new Color(0.37f, 0.54f, 0.76f);
            case "bomber": return new Color(0.85f, 0.54f, 0.23f);
            case "apc": return new Color(0.56f, 0.23f, 0.21f);
            default: return new Color(0.49f, 0.60f, 0.43f); // soldier
        }
    }
    static Color PartCol(string t, string tint)
    {
        if (t == "block" && !string.IsNullOrEmpty(tint) && tint.Length == 7 && tint[0] == '#')
        {
            try
            {
                return new Color(
                    System.Convert.ToInt32(tint.Substring(1, 2), 16) / 255f,
                    System.Convert.ToInt32(tint.Substring(3, 2), 16) / 255f,
                    System.Convert.ToInt32(tint.Substring(5, 2), 16) / 255f);
            }
            catch { }
        }
        switch (t)
        {
            case "rock": return new Color(0.34f, 0.37f, 0.42f);
            case "wall": return new Color(0.27f, 0.30f, 0.35f);
            case "platform": return new Color(0.44f, 0.47f, 0.52f);
            case "slope": return new Color(0.39f, 0.42f, 0.47f);
            case "block": return new Color(0.42f, 0.46f, 0.53f);
            default: return Color.clear; // skip non-structural parts
        }
    }

    /// <summary>Render `st` into a fresh Texture2D, or null on failure (caller
    /// falls back to TacThumb).</summary>
    public static Texture2D Render(TacJson.JObj st)
    {
        try
        {
            EnsureRig();
            // clear previous stage geometry
            for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);

            var arena = st.Obj("arena");
            float aw = (float)arena.Num("w"), ad = (float)arena.Num("d");
            float cx = aw / 2f, cz = ad / 2f;

            TacRenderKit.Cube(root, new Vector3(cx, -0.1f, cz), new Vector3(aw, 0.2f, ad), new Color(0.17f, 0.19f, 0.23f));

            var parts = st.Arr("parts");
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts.Obj(i);
                var col = PartCol(p.Str("type"), p.Str("tint"));
                if (col.a == 0f) continue;
                float h = (float)p.Num("h", 1.4);
                TacRenderKit.Cube(root, new Vector3((float)p.Num("x"), h / 2f, (float)p.Num("z")),
                    new Vector3((float)p.Num("w", 1), h, (float)p.Num("d", 1)), col);
            }
            var enemies = st.Arr("enemies");
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies.Obj(i);
                TacRenderKit.Cube(root, new Vector3((float)e.Num("x"), 1.3f, (float)e.Num("z")),
                    new Vector3(1.8f, 2.6f, 1.8f), EnemyCol(e.Str("type")));
            }
            var ps = st.Obj("playerStart");
            if (ps != null)
                TacRenderKit.Cube(root, new Vector3((float)ps.Num("x"), 1.3f, (float)ps.Num("z")),
                    new Vector3(1.8f, 2.6f, 1.8f), PlayerCol);

            // angled top-down camera looking at arena center
            float dist = Mathf.Max(aw, ad) * 0.82f;
            cam.transform.position = new Vector3(cx, dist * 0.85f, cz - dist * 0.72f);
            cam.transform.LookAt(new Vector3(cx, 0f, cz));

            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, RW, RH), 0, 0);
            readback.Apply();
            RenderTexture.active = prev;

            // copy into a fresh texture so each card owns its own image
            var outTex = new Texture2D(RW, RH, TextureFormat.RGBA32, false);
            outTex.SetPixels32(readback.GetPixels32());
            outTex.Apply();
            return outTex;
        }
        catch (System.Exception)
        {
            return null;
        }
    }
}
