// TAC world rendering: builds the static arena (ground with real pit holes,
// boxes, stairs, water) and per-entity views from Unity primitives, mirroring
// tac-client.js's low-poly look. Built-in render pipeline (Standard shader).
using System.Collections.Generic;
using UnityEngine;

public static class TacRenderKit
{
    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    static readonly Dictionary<Color, Material> transMats = new Dictionary<Color, Material>();

    public static Material Mat(Color c)
    {
        if (mats.ContainsKey(c)) return mats[c];
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", 0.08f);
        mats[c] = m;
        return m;
    }

    static readonly Dictionary<Color, Material> unlitMats = new Dictionary<Color, Material>();
    public static Material UnlitMat(Color c)
    {
        if (unlitMats.ContainsKey(c)) return unlitMats[c];
        var m = new Material(Shader.Find("Unlit/Color"));
        m.color = c;
        unlitMats[c] = m;
        return m;
    }

    public static Material TransMat(Color c)
    {
        if (transMats.ContainsKey(c)) return transMats[c];
        // Legacy transparent shader: no keyword variants to get stripped from
        // device builds (runtime-enabled Standard-transparent falls back to
        // opaque there — the veil rendered as a solid purple dome)
        var m = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
        m.color = c;
        transMats[c] = m;
        return m;
    }

    public static GameObject Cube(Transform parent, Vector3 pos, Vector3 size, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = Mat(c);
        return go;
    }

    public static GameObject Sphere(Transform parent, Vector3 pos, float d, Color c, bool trans = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(d, d, d);
        go.GetComponent<Renderer>().sharedMaterial = trans ? TransMat(c) : Mat(c);
        return go;
    }

    public static GameObject Cyl(Transform parent, Vector3 pos, float d, float h, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(d, h / 2f, d);
        go.GetComponent<Renderer>().sharedMaterial = Mat(c);
        return go;
    }

    // world-space box from sim AABB (x0..x1, 0..h, z0..z1)
    static void BoxAabb(Transform parent, double x0, double z0, double x1, double z1, double y0, double y1, Color c)
    {
        Cube(parent,
            new Vector3((float)((x0 + x1) / 2), (float)((y0 + y1) / 2), (float)((z0 + z1) / 2)),
            new Vector3((float)(x1 - x0), (float)(y1 - y0), (float)(z1 - z0)), c);
    }

    public static readonly Color GroundCol = new Color(0.58f, 0.61f, 0.65f);
    public static readonly Color RockCol = new Color(0.44f, 0.47f, 0.51f);
    public static readonly Color WallCol = new Color(0.33f, 0.36f, 0.41f);
    public static readonly Color PlatformCol = new Color(0.55f, 0.58f, 0.62f);
    public static readonly Color CrackedCol = new Color(0.52f, 0.48f, 0.43f);
    public static readonly Color StairCol = new Color(0.58f, 0.61f, 0.66f);
    public static readonly Color PitCol = new Color(0.30f, 0.28f, 0.26f);
    public static readonly Color WaterCol = new Color(0.27f, 0.51f, 0.78f, 0.75f);
    public static readonly Color SoldierCol = new Color(0.36f, 0.46f, 0.34f);
    public static readonly Color GatlingCol = new Color(0.45f, 0.36f, 0.30f);
    public static readonly Color SniperCol = new Color(0.34f, 0.38f, 0.50f);
    public static readonly Color DroneCol = new Color(0.55f, 0.58f, 0.64f);
    public static readonly Color OperatorCol = new Color(0.55f, 0.48f, 0.34f);
    public static readonly Color BomberCol = new Color(0.52f, 0.32f, 0.26f);
    public static readonly Color PlayerCol = new Color(0.20f, 0.55f, 0.85f);
    public static readonly Color VeilCol = new Color(0.62f, 0.48f, 1.0f, 0.16f);

    // vision cone: unit fan mesh (+z forward, radius 1) with metric UVs so the
    // hatch-grid texture stays a fixed world size no matter the range scale
    public static Mesh FanMesh(float halfDeg, int n, float uvScale)
    {
        var verts = new Vector3[n + 2];
        var uvs = new Vector2[n + 2];
        var tris = new int[n * 3];
        verts[0] = Vector3.zero;
        uvs[0] = Vector2.zero;
        float half = halfDeg * Mathf.Deg2Rad;
        for (int i = 0; i <= n; i++)
        {
            float ang = -half + (i / (float)n) * half * 2f;
            verts[i + 1] = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang));
            uvs[i + 1] = new Vector2(verts[i + 1].x * uvScale, verts[i + 1].z * uvScale);
        }
        for (int k = 0; k < n; k++) { tris[k * 3] = 0; tris[k * 3 + 1] = k + 1; tris[k * 3 + 2] = k + 2; }
        var m = new Mesh();
        m.vertices = verts; m.uv = uvs; m.triangles = tris;
        m.RecalculateNormals();
        return m;
    }

    static Texture2D hatchTex;
    public static Material ConeMat(Color c)
    {
        if (hatchTex == null)
        {
            hatchTex = new Texture2D(16, 16, TextureFormat.RGBA32, true);
            hatchTex.filterMode = FilterMode.Trilinear;
            hatchTex.anisoLevel = 9; // grazing-angle lines stay lines, not smeared bands
            hatchTex.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    hatchTex.SetPixel(x, y, (x < 1 || y < 1) ? Color.white : new Color(1, 1, 1, 0));
            hatchTex.Apply(true);
        }
        var mat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
        mat.mainTexture = hatchTex;
        mat.color = c;
        return mat;
    }

    static Color PalColor(TacWorld w, string key, Color fallback)
    {
        if (w.palette != null && w.palette.Has(key) && ColorUtility.TryParseHtmlString(w.palette.Str(key), out var c)) return c;
        return fallback;
    }

    // ground plane decomposed around pit rects so trenches/rivers are real holes
    public static void BuildStatic(Transform root, TacWorld w, List<GameObject> boxViews)
    {
        // ground: subtract each pit footprint from the arena rect
        var rects = new List<double[]> { new double[] { 0, 0, w.arenaW, w.arenaD } };
        foreach (var pit in w.pits)
        {
            var next = new List<double[]>();
            foreach (var r in rects)
            {
                double rx0 = r[0], rz0 = r[1], rx1 = r[2], rz1 = r[3];
                double ix0 = System.Math.Max(rx0, pit.x0), iz0 = System.Math.Max(rz0, pit.z0);
                double ix1 = System.Math.Min(rx1, pit.x1), iz1 = System.Math.Min(rz1, pit.z1);
                if (ix0 >= ix1 || iz0 >= iz1) { next.Add(r); continue; }
                if (rz0 < iz0) next.Add(new double[] { rx0, rz0, rx1, iz0 });
                if (iz1 < rz1) next.Add(new double[] { rx0, iz1, rx1, rz1 });
                if (rx0 < ix0) next.Add(new double[] { rx0, iz0, ix0, iz1 });
                if (ix1 < rx1) next.Add(new double[] { ix1, iz0, rx1, iz1 });
            }
            rects = next;
        }
        var groundC = PalColor(w, "ground", GroundCol);
        foreach (var r in rects)
            BoxAabb(root, r[0], r[1], r[2], r[3], -0.12, 0.0, groundC);

        // pit interiors: floor slab + four inner walls
        foreach (var pit in w.pits)
        {
            double d = pit.depth;
            BoxAabb(root, pit.x0, pit.z0, pit.x1, pit.z1, -d - 0.1, -d, PitCol);
            BoxAabb(root, pit.x0 - 0.08, pit.z0 - 0.08, pit.x0, pit.z1 + 0.08, -d, 0, PitCol);
            BoxAabb(root, pit.x1, pit.z0 - 0.08, pit.x1 + 0.08, pit.z1 + 0.08, -d, 0, PitCol);
            BoxAabb(root, pit.x0, pit.z0 - 0.08, pit.x1, pit.z0, -d, 0, PitCol);
            BoxAabb(root, pit.x0, pit.z1, pit.x1, pit.z1 + 0.08, -d, 0, PitCol);
        }
        // water surface in river pits
        foreach (var rv in w.rivers)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3((float)((rv.x0 + rv.x1) / 2), (float)(-TAC.RIVER_DEPTH + 0.42), (float)((rv.z0 + rv.z1) / 2));
            go.transform.localScale = new Vector3((float)(rv.x1 - rv.x0), 0.04f, (float)(rv.z1 - rv.z0));
            go.name = "waterSurf";
            var wc = PalColor(w, "water", WaterCol);
            wc.a = WaterCol.a;
            go.GetComponent<Renderer>().sharedMaterial = TransMat(wc);
        }

        // boxes (rock/wall/platform/crackedWall), views kept for break-hiding
        foreach (var b in w.boxes)
        {
            Color c = b.kind == 0 ? RockCol : (b.kind == 1 ? WallCol : (b.kind == 3 ? CrackedCol : PlatformCol));
            if (b.tint != null && ColorUtility.TryParseHtmlString(b.tint, out var tc)) c = tc;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3((float)((b.x0 + b.x1) / 2), (float)((b.yb + b.h) / 2), (float)((b.z0 + b.z1) / 2));
            go.transform.localScale = new Vector3((float)(b.x1 - b.x0), (float)(b.h - b.yb), (float)(b.z1 - b.z0));
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
            if (b.kind == 3)
            {
                for (int ck = 0; ck < 3; ck++)
                {
                    int idx = boxViews.Count * 7 + ck * 13;
                    float ch = (idx % 10) / 10f;
                    var crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.Destroy(crack.GetComponent<Collider>());
                    crack.transform.SetParent(go.transform, false);
                    crack.transform.localPosition = new Vector3((ch - 0.5f) * 0.6f, ((ck * 29 + idx) % 10) / 10f * 0.4f - 0.1f, 0);
                    crack.transform.localScale = new Vector3(0.04f, 0.3f + 0.25f * ch, 1.04f);
                    crack.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.28f, 0.24f, 0.2f));
                }
            }
            boxViews.Add(go);
        }
        // ground tone patches: sparse darker blotches break up the flat plane
        for (int gp = 0; gp < 14; gp++)
        {
            long gh = (gp * 2654435761L) % 4096;
            float gx = ((gh & 63) / 63f) * (float)w.arenaW;
            float gz = (((gh >> 6) & 63) / 63f) * (float)w.arenaD;
            float gr = 2.2f + ((gh >> 3) % 5) * 0.9f;
            var patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(patch.GetComponent<Collider>());
            patch.transform.SetParent(root, false);
            patch.transform.localPosition = new Vector3(gx, 0.015f, gz);
            patch.transform.localScale = new Vector3(gr, 0.01f, gr);
            patch.GetComponent<Renderer>().sharedMaterial = TransMat(new Color(0f, 0f, 0f, 0.05f));
        }

        // stairs: terraced treads
        foreach (var s in w.slopes)
        {
            for (int st = 0; st < s.steps; st++)
            {
                double t0 = (double)st / s.steps;
                double sx0 = s.x0, sx1 = s.x1, sz0 = s.z0, sz1 = s.z1;
                if (s.dir == 0) sz0 = s.z0 + (s.z1 - s.z0) * t0;
                else if (s.dir == 2) sz1 = s.z1 - (s.z1 - s.z0) * t0;
                else if (s.dir == 1) sx0 = s.x0 + (s.x1 - s.x0) * t0;
                else sx1 = s.x1 - (s.x1 - s.x0) * t0;
                var stc = StairCol;
                if (s.tint != null && ColorUtility.TryParseHtmlString(s.tint, out var stt)) stc = stt;
                // raised staircase (y0 > 0): each tread runs from y0 up to its
                // tread line, so the flight sits on a lower floor and reads solid.
                BoxAabb(root, sx0, sz0, sx1, sz1, s.y0, s.y0 + s.rise * (st + 1), stc);
            }
        }

    }

    // pickup view (dynamic: hidden once consumed; TacGame keeps the list)
    public static GameObject BuildMedkitView(Transform parent, TacMedkit mk)
    {
        var g = Cube(parent, new Vector3((float)mk.x, (float)mk.y + 0.35f, (float)mk.z), new Vector3(0.5f, 0.34f, 0.5f), Color.white);
        Cube(g.transform, new Vector3(0, 0.55f, 0), new Vector3(0.66f, 0.12f, 0.24f), new Color(0.9f, 0.2f, 0.18f));
        Cube(g.transform, new Vector3(0, 0.55f, 0), new Vector3(0.24f, 0.12f, 0.66f), new Color(0.9f, 0.2f, 0.18f));
        g.name = "medkit";
        return g;
    }

    // Minecraft-style stiff legs: two vertical leg cubes named legL/legR under
    // the body. They do not bend; the walk cycle slides each foot fore/aft in
    // counter-phase (driven every frame in TacGame.UpdateViews). legLen is the
    // full leg height (cube center sits at legLen/2 so the foot touches y=0).
    // The caller shortens its torso by legLen so total height is unchanged.
    public static void AddLegs(Transform root, float legLen, float legW, float spread, Color c)
    {
        var l = Cube(root, new Vector3(-spread, legLen * 0.5f, 0), new Vector3(legW, legLen, legW), c);
        l.name = "legL";
        var r = Cube(root, new Vector3(spread, legLen * 0.5f, 0), new Vector3(legW, legLen, legW), c);
        r.name = "legR";
    }

    // low-poly enemy figure per type; root pivot at feet
    public static GameObject BuildEnemyView(Transform parent, TacEnemy en)
    {
        var root = new GameObject("enemy");
        root.transform.SetParent(parent, false);
        if (en.type == 1)
        {
            Cube(root.transform, new Vector3(0, 0.5f, 0), new Vector3(1.1f, 1.0f, 1.1f), GatlingCol);
            Cube(root.transform, new Vector3(0, 1.15f, 0.5f), new Vector3(0.22f, 0.22f, 1.2f), new Color(0.2f, 0.2f, 0.22f));
            var gatSpin = new GameObject("gatspin");
            gatSpin.transform.SetParent(root.transform, false);
            gatSpin.transform.localPosition = new Vector3(0, 1.15f, 1.15f);
            Cube(gatSpin.transform, Vector3.zero, new Vector3(0.34f, 0.34f, 0.14f), new Color(0.26f, 0.27f, 0.31f));
            Cube(gatSpin.transform, new Vector3(0, 0.14f, 0.02f), new Vector3(0.1f, 0.1f, 0.18f), new Color(0.16f, 0.17f, 0.2f));
            Cube(gatSpin.transform, new Vector3(0, -0.14f, 0.02f), new Vector3(0.1f, 0.1f, 0.18f), new Color(0.16f, 0.17f, 0.2f));
            Cube(root.transform, new Vector3(0, 1.35f, 0), new Vector3(0.5f, 0.4f, 0.5f), GatlingCol * 1.25f);
        }
        else if (en.type == 3)
        {
            Cube(root.transform, new Vector3(0, 0.25f, 0), new Vector3(0.7f, 0.22f, 0.7f), DroneCol);
            Cube(root.transform, new Vector3(0.4f, 0.32f, 0.4f), new Vector3(0.3f, 0.05f, 0.3f), DroneCol * 1.2f);
            Cube(root.transform, new Vector3(-0.4f, 0.32f, 0.4f), new Vector3(0.3f, 0.05f, 0.3f), DroneCol * 1.2f);
            Cube(root.transform, new Vector3(0.4f, 0.32f, -0.4f), new Vector3(0.3f, 0.05f, 0.3f), DroneCol * 1.2f);
            Cube(root.transform, new Vector3(-0.4f, 0.32f, -0.4f), new Vector3(0.3f, 0.05f, 0.3f), DroneCol * 1.2f);
            var rotors = new GameObject("rotors");
            rotors.transform.SetParent(root.transform, false);
            rotors.transform.localPosition = new Vector3(0, 0.42f, 0);
            Cube(rotors.transform, Vector3.zero, new Vector3(1.3f, 0.02f, 0.08f), DroneCol * 1.35f);
            Cube(rotors.transform, Vector3.zero, new Vector3(0.08f, 0.02f, 1.3f), DroneCol * 1.35f);
            var eye = Sphere(root.transform, new Vector3(0, 0.16f, 0.28f), 0.16f, Color.red);
            eye.name = "eye";
            eye.GetComponent<Renderer>().sharedMaterial = UnlitMat(Color.red);
        }
        else if (en.type == 7)
        {
            // APC: low armored hull + nose plate + turret + wheels (the sim's
            // newest enemy — without this branch it rendered as a foot soldier)
            var hull = new Color(0.56f, 0.23f, 0.21f);
            Cube(root.transform, new Vector3(0, 0.55f, 0), new Vector3(1.6f, 1.1f, 2.6f), hull);
            Cube(root.transform, new Vector3(0, 0.5f, 1.25f), new Vector3(1.4f, 0.7f, 0.5f), hull * 0.8f);
            Cube(root.transform, new Vector3(0, 1.3f, 0), new Vector3(1.0f, 0.5f, 1.0f), hull * 1.18f);
            Cube(root.transform, new Vector3(0, 1.35f, 0.95f), new Vector3(0.14f, 0.14f, 1.5f), new Color(0.14f, 0.15f, 0.18f));
            for (int wi = -1; wi <= 1; wi++)
            {
                Cube(root.transform, new Vector3(-0.85f, 0.26f, wi * 0.85f), new Vector3(0.5f, 0.52f, 0.5f), new Color(0.1f, 0.1f, 0.12f));
                Cube(root.transform, new Vector3(0.85f, 0.26f, wi * 0.85f), new Vector3(0.5f, 0.52f, 0.5f), new Color(0.1f, 0.1f, 0.12f));
            }
        }
        else if (en.type == 6)
        {
            AddLegs(root.transform, 0.5f, 0.24f, 0.16f, new Color(0.24f, 0.26f, 0.3f));
            Cube(root.transform, new Vector3(0, 1.025f, 0), new Vector3(0.6f, 1.05f, 0.5f), new Color(0.34f, 0.36f, 0.4f));
            var shHead = Cube(root.transform, new Vector3(0, 1.7f, 0), new Vector3(0.36f, 0.3f, 0.36f), new Color(0.4f, 0.42f, 0.47f));
            var plate = Cube(root.transform, new Vector3(0, 0.95f, 0.55f), new Vector3(2.0f, 1.9f, 0.1f), new Color(0.5f, 0.53f, 0.58f));
            plate.name = "plate";
            Cube(plate.transform, new Vector3(0, 0.26f, 0.55f), new Vector3(0.25f, 0.045f, 0.6f), new Color(0.12f, 0.13f, 0.15f)); // vision slit
        }
        else
        {
            Color c = en.type == 2 ? SniperCol : (en.type == 4 ? OperatorCol : (en.type == 5 ? BomberCol : SoldierCol));
            float bh = (float)en.h;
            // stiff legs take the bottom 0.4*bh; torso is shortened and raised to sit on them
            float legLen = bh * 0.4f;
            AddLegs(root.transform, legLen, 0.2f, 0.14f, c * 0.7f);
            Cube(root.transform, new Vector3(0, legLen + bh * 0.225f, 0), new Vector3(0.55f, bh * 0.45f, 0.42f), c);
            var head = Cube(root.transform, new Vector3(0, bh * 0.95f, 0), new Vector3(0.34f, 0.3f, 0.34f), c * 1.35f);
            // simple face: two eyes + mouth
            Cube(head.transform, new Vector3(-0.09f, 0.05f, 0.51f), new Vector3(0.16f, 0.16f, 0.06f), Color.black);
            Cube(head.transform, new Vector3(0.09f, 0.05f, 0.51f), new Vector3(0.16f, 0.16f, 0.06f), Color.black);
            Cube(head.transform, new Vector3(0, -0.22f, 0.51f), new Vector3(0.3f, 0.1f, 0.06f), Color.black);
            if (en.type == 5)
            {
                // bomb pack raised to ride on the shortened torso (clear of the legs)
                Cube(root.transform, new Vector3(0, legLen + bh * 0.2f, -0.4f), new Vector3(0.5f, 0.6f, 0.3f), new Color(0.15f, 0.12f, 0.1f));
                Sphere(root.transform, new Vector3(0, legLen + bh * 0.45f, -0.4f), 0.16f, new Color(1f, 0.3f, 0.15f));
            }
            else if (en.type == 2)
            {
                Cube(root.transform, new Vector3(0.12f, bh * 0.68f, 0.55f), new Vector3(0.09f, 0.09f, 1.3f), new Color(0.15f, 0.16f, 0.2f));
            }
            else if (en.type == 4)
            {
                Cube(root.transform, new Vector3(0, legLen + bh * 0.15f, -0.35f), new Vector3(0.44f, 0.5f, 0.2f), new Color(0.2f, 0.22f, 0.26f));
                Cube(root.transform, new Vector3(0.1f, bh * 1.15f, -0.35f), new Vector3(0.04f, 0.7f, 0.04f), new Color(0.7f, 0.75f, 0.85f));
            }
            else if (en.type == 0)
            {
                Cube(root.transform, new Vector3(0.28f, bh * 0.62f, 0.5f), new Vector3(0.09f, 0.09f, 1.0f), new Color(0.13f, 0.14f, 0.16f));
                Cube(head.transform, new Vector3(0, 0.55f, 0), new Vector3(1.25f, 0.45f, 1.25f), c * 0.75f);
                Cube(head.transform, new Vector3(0, 0.34f, 0.55f), new Vector3(1.15f, 0.16f, 0.55f), c * 0.75f);
            }
        }
        return root;
    }

    public static GameObject BuildPlayerView(Transform parent)
    {
        var body = TacSkins.Current.body;
        var root = new GameObject("player");
        root.transform.SetParent(parent, false);
        // stiff Minecraft-style legs; torso shortened and raised to sit on them
        AddLegs(root.transform, 0.42f, 0.24f, 0.15f, body * 0.8f);
        Cube(root.transform, new Vector3(0, 0.935f, 0), new Vector3(0.55f, 1.03f, 0.42f), body);
        var head = Cube(root.transform, new Vector3(0, 1.62f, 0), new Vector3(0.34f, 0.3f, 0.34f), body * 1.35f);
        // hero's eyes sit wider apart than the rank-and-file
        Cube(head.transform, new Vector3(-0.13f, 0.05f, 0.51f), new Vector3(0.16f, 0.16f, 0.06f), Color.black);
        Cube(head.transform, new Vector3(0.13f, 0.05f, 0.51f), new Vector3(0.16f, 0.16f, 0.06f), Color.black);
        Cube(head.transform, new Vector3(0, -0.2f, 0.51f), new Vector3(0.26f, 0.09f, 0.06f), Color.black);
        Cube(root.transform, new Vector3(0.28f, 1.05f, 0.5f), new Vector3(0.09f, 0.09f, 1.0f), new Color(0.1f, 0.11f, 0.13f));
        return root;
    }
}
