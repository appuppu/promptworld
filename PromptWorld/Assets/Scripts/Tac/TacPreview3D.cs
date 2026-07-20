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
    // Layer 31 is reserved for preview geometry: ONLY the preview camera renders
    // it, and the geometry sits FAR from the play area — so it can never leak into
    // the main camera (which was causing the brief-screen flicker).
    const int PreviewLayer = 31;
    static readonly Vector3 FarOffset = new Vector3(0f, -5000f, 0f);
    static Camera cam;
    static RenderTexture rt;
    static Transform root;
    static Texture2D readback;
    static Light rigLight;

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
        cam.cullingMask = 1 << PreviewLayer;  // renders ONLY preview geometry
        rt = new RenderTexture(RW, RH, 16, RenderTextureFormat.ARGB32) { name = "TacPreviewRT" };
        cam.targetTexture = rt;
        // dedicated sun that lights ONLY the preview layer — thumbnails must not
        // inherit whatever mood the live scene is in (the home diorama runs a
        // dusk grade that was turning every card map near-black)
        var lightGo = new GameObject("TacPreviewSun");
        Object.DontDestroyOnLoad(lightGo);
        lightGo.hideFlags = HideFlags.HideAndDontSave;
        lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        rigLight = lightGo.AddComponent<Light>();
        rigLight.type = LightType.Directional;
        rigLight.intensity = 1.0f;
        rigLight.color = Color.white;
        rigLight.cullingMask = 1 << PreviewLayer;
        rigLight.enabled = false;            // on only during Render()
        var rootGo = new GameObject("TacPreviewWorld");
        Object.DontDestroyOnLoad(rootGo);
        rootGo.hideFlags = HideFlags.HideAndDontSave;
        rootGo.transform.position = FarOffset; // physically away from the play area
        root = rootGo.transform;
        readback = new Texture2D(RW, RH, TextureFormat.RGBA32, false);
    }

    // Recursively stamp the preview layer onto a spawned cube and its children.
    static void SetLayer(GameObject go)
    {
        go.layer = PreviewLayer;
        for (int i = 0; i < go.transform.childCount; i++) SetLayer(go.transform.GetChild(i).gameObject);
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
            // Clear previous stage geometry IMMEDIATELY. Object.Destroy is deferred
            // to end-of-frame, so with several cards rendering in the same frame the
            // old cubes would still be present at cam.Render() — every card would
            // then show the same accumulated scene. DestroyImmediate guarantees each
            // Render sees only its own stage.
            for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

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

            // stamp the preview layer on all geometry so ONLY the preview camera
            // sees it (never the main game camera).
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i).gameObject);

            // angled top-down camera looking at the arena center. Geometry lives
            // under `root` at FarOffset, so add FarOffset to the camera too.
            float dist = Mathf.Max(aw, ad) * 0.82f;
            cam.transform.position = FarOffset + new Vector3(cx, dist * 0.85f, cz - dist * 0.72f);
            cam.transform.LookAt(FarOffset + new Vector3(cx, 0f, cz));

            // neutral, scene-independent lighting for the render: fixed bright
            // ambient, fog OFF (dusk fog was blackening the far half of the map),
            // rig sun on. All restored right after — one frame, render-only.
            var savedAmbient = RenderSettings.ambientLight;
            bool savedFog = RenderSettings.fog;
            RenderSettings.ambientLight = new Color(0.52f, 0.54f, 0.58f);
            RenderSettings.fog = false;
            rigLight.enabled = true;
            cam.Render();
            rigLight.enabled = false;
            RenderSettings.ambientLight = savedAmbient;
            RenderSettings.fog = savedFog;
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
