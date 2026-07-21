// TAC mobile client — the Unity twin of server/tac-client.js.
// Owns the shared deterministic sim (TacSim.cs, crosschecked bit-identical to
// tacsim.js), renders it with low-poly primitives, drives touch input at the
// same 50 Hz input contract, records the replay, and submits verified runs.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TacGame : MonoBehaviour
{
    enum Mode { List, Brief, Play, Result, Paused }
    Mode mode = Mode.List;

    TacWorld world;
    TacJson.JObj stageDoc;
    string stageJson, stageId, stageName;
    readonly List<TacInput> recs = new List<TacInput>();

    // camera / input state
    float camYaw, camPitch = -0.12f;
    // smoothed follow distance: indoors the camera pulls in so walls/ceilings never
    // sit between it and the player. Pull-in is instant (no clipping); easing back
    // out is slow so leaving a doorway doesn't pop the view.
    float camDistS = 5.2f;
    // scope magnification (render-side only — FOV never touches the sim/hit math).
    // Lower FOV = more zoom. Two-finger pinch drives it while scoped.
    const float ScopeFovMin = 7f;    // max zoom-in
    const float ScopeFovMax = 26f;   // zoomed-out
    const float ScopeFovDefault = 18f;
    float scopeFov = ScopeFovDefault;
    float pinchPrevDist = -1f;       // last-frame two-finger spread, -1 = not pinching
    Vector2 stickOrigin; int stickTouch = -1, lookTouch = -1;
    int heading = 255;
    bool fireHeld, sneakOn;
    bool jumpLatch, grenLatch, droneLatch, scopeLatch, bombAiming, bombCancelled;
    LineRenderer bombArc; GameObject bombLandRing;
    int autoLockT; long autoLockKey = -1; // ticks the lock-on has stayed on one target (auto-fire)
    Image grenIcon, scopeIcon, droneIcon;
    Text scopeCountText;
    RectTransform fireRt, jumpRt, bombRt, droneRt, scopeRt, sneakRt; // thumb-arc buttons (handedness mirror)
    double acc;

    // views
    Transform worldRoot;
    GameObject playerView;
    // Minecraft-style leg walk cycle (render-only, mirrors tac-client.js): per-view
    // phase + eased amplitude, advanced from ground speed measured between frames.
    float playerLegPhase, playerLegAmp; Vector3 playerLegPrevPos; bool playerLegInit;
    readonly List<float> enemyLegPhase = new List<float>();
    readonly List<float> enemyLegAmp = new List<float>();
    readonly List<Vector3> enemyLegPrevPos = new List<Vector3>();
    readonly List<GameObject> boxViews = new List<GameObject>();
    readonly List<GameObject> enemyViews = new List<GameObject>();
    readonly List<GameObject> bulletPool = new List<GameObject>();
    readonly List<GameObject> grenadePool = new List<GameObject>();
    readonly Dictionary<TacBomb, GameObject> bombViews = new Dictionary<TacBomb, GameObject>();
    readonly List<GameObject> veilViews = new List<GameObject>();
    readonly List<GameObject> medkitViews = new List<GameObject>();
    readonly List<GameObject> intelViews = new List<GameObject>();
    GameObject exitPad;
    readonly List<GameObject> switchViews = new List<GameObject>();
    readonly List<GameObject> mineViews = new List<GameObject>();
    readonly List<GameObject> barrelViews = new List<GameObject>();
    GameObject pilotView, lockMarker;
    Vector3 pilotPrevPos;      // render-side velocity source (sim has no pilot velocity)
    bool pilotPosInit;

    // ---- snapshot interpolation ----------------------------------------------
    // The sim advances in discrete 50Hz ticks; the display runs faster. Rather
    // than exponentially chase the latest tick (which yields fast-then-slow
    // motion every tick — the "カクカク" strafe judder), we keep the pose from
    // BEFORE and AFTER each tick and render at lerp(prev, cur, acc/TICK). That
    // is geometrically uniform at any refresh rate. Everything the eye tracks in
    // motion (player body, camera pivot, enemies, pilot drone) is driven from
    // the same interpolated positions so nothing desyncs. Sim math is untouched.
    struct MoBody { public Vector3 pos; public float yawDeg; public bool valid; }
    MoBody snapPrevP, snapCurP;                 // player
    MoBody snapPrevPilot, snapCurPilot;         // pilot drone
    MoBody[] snapPrevE = new MoBody[0], snapCurE = new MoBody[0]; // enemies
    bool snapReady;   // false until the first tick pair exists (else lerp has no prev)
    // The frame that starts a run does heavy one-off work (world build, HUD,
    // StartMusic), so the NEXT frame's Time.deltaTime spikes — up to the 0.333 s
    // maximumDeltaTime clamp, i.e. ~16 ticks of accumulator in one frame. With
    // the 5-ticks-per-frame cap that backlog takes several frames to drain, and
    // during the drain the sim replays PAST ticks (the run "freezes" until real
    // time catches up — the reported "wait a bit and it unfreezes"). We swallow
    // the first play frame's dt so the sim starts from a clean accumulator.
    bool primePlayFrame;
    readonly List<Transform> searchPivots = new List<Transform>();
    readonly List<Renderer> waterViews = new List<Renderer>();

    Light sunLight;
    bool nightLightingOn;
    LineRenderer sniperLaser;
    class Fx { public GameObject go; public float t, dur; public bool ring; }
    readonly List<Fx> fxs = new List<Fx>();
    class Deb { public GameObject go; public Vector3 vel, rotAxis; public float t, dur; }
    readonly List<Deb> debs = new List<Deb>();
    Image dmgFlash;
    RectTransform scopeCross;
    float dmgT, lastAlertT = -99f, briefCamT;

    // ---- combat juice (render-only, sim untouched) ----
    float shakeT, shakeDur, shakeAmp;            // camera-shake envelope
    GameObject muzzleFlash; float muzzleT;       // flash at the player's gun tip
    RectTransform hitMark; Image[] hitMarkBars;  // HUD hit-confirm ✕ at the impact point
    float hitMarkT, hitMarkDur;
    Image playVig, lowHpVig;                     // static cinematic vignette + low-HP pulse
    RectTransform hpBarRoot;                     // segmented HP bar (replaces the heart glyphs)
    readonly List<Image> hpSegs = new List<Image>();
    int hpSegMax = -1;

    // ---- attract mode: a SILENT training-ground firefight runs behind the home
    // list as a live diorama (the home backdrop is the game itself). The scout
    // wanders on autopilot and auto-fires like real play; all SFX/haptics/HUD
    // reactions are suppressed; the run resets a beat after it ends.
    bool attract;
    float attractOrbitYaw, attractResetT;
    float attractAimDeg, attractWantDeg;
    bool attractPause;
    int attractDirT;
    readonly System.Random attractRng = new System.Random(1234567);

    // ui
    Canvas canvas;
    RectTransform hudRoot, listRoot, briefRoot, resultRoot, pauseRoot, settingsRoot, createRoot, listContent;
    readonly Dictionary<string, string> stageCache = new Dictionary<string, string>();
    class StageMeta { public string id, name, creator, published; public double goods, bads, attempts, clears, parMs; }
    readonly List<StageMeta> metas = new List<StageMeta>();
    int sortMode; // 0 new, 1 good, 2 plays, 3 hard, 4 testbench
    string searchQuery = ""; // stage-name search (server-side ?q=)
    class BriefEntry { public string sid, name, creator, embedded; public StageMeta meta; }
    readonly List<BriefEntry> briefList = new List<BriefEntry>();
    int briefIndex;
    Text foesText, timeText, msgText;
    Text toastText; float toastUntil; // canvas-level toast (visible on any screen)
    RawImage minimapImg;
    Texture2D minimapTex;
    Color32[] minimapBase;
    Image stickBg, stickNub;
    Camera cam;
    TacAudioKit audioKit;
    float msgUntil;
    // Deep-link draft preview: set true while the stage on screen arrived via a
    // shared ?stage=&key= link AND is still status='draft'. Drives the PREVIEW
    // badge and keeps score/vote submission off an unpublished stage.
    bool previewDraft;

    const string TRAINING = "{\"name\":\"TRAINING GROUND\",\"timeLimit\":600,\"lives\":5,\"ammo\":0," +
        "\"arena\":{\"w\":40,\"d\":70},\"playerStart\":{\"x\":20,\"z\":5,\"yaw\":0}," +
        "\"parts\":[{\"type\":\"rock\",\"x\":14,\"z\":14,\"w\":4,\"d\":1.6,\"h\":1.4}," +
        "{\"type\":\"rock\",\"x\":27,\"z\":18,\"w\":4,\"d\":1.6,\"h\":1.4}," +
        "{\"type\":\"crackedWall\",\"x\":30,\"z\":30,\"w\":6,\"d\":1,\"h\":3}," +
        "{\"type\":\"mine\",\"x\":30,\"z\":28.6},{\"type\":\"barrel\",\"x\":10,\"z\":30}," +
        "{\"type\":\"trench\",\"x\":14,\"z\":40,\"w\":8,\"d\":3}," +
        "{\"type\":\"platform\",\"x\":32,\"z\":52,\"w\":8,\"d\":4,\"h\":2}," +
        "{\"type\":\"slope\",\"x\":32,\"z\":47,\"w\":4,\"d\":6,\"h\":2,\"dir\":0}," +
        "{\"type\":\"medkit\",\"x\":6,\"z\":22},{\"type\":\"switch\",\"x\":20,\"z\":60,\"r\":9}]," +
        "\"enemies\":[{\"type\":\"soldier\",\"x\":20,\"z\":24,\"yaw\":180,\"patrolX\":28,\"patrolZ\":24}," +
        "{\"type\":\"soldier\",\"x\":12,\"z\":40,\"yaw\":180}," +
        "{\"type\":\"bomber\",\"x\":34,\"z\":36,\"yaw\":200}," +
        "{\"type\":\"sniper\",\"x\":32,\"z\":53,\"yaw\":180}," +
        "{\"type\":\"drone\",\"x\":8,\"z\":48,\"yaw\":90,\"patrolX\":34,\"patrolZ\":48,\"group\":2}," +
        "{\"type\":\"operator\",\"x\":6,\"z\":64,\"yaw\":90,\"group\":2}]}";

    void Start()
    {
        try { StartInner(); }
        catch (System.Exception ex)
        {
            var cgo = new GameObject("ErrCanvas", typeof(Canvas));
            cgo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            TacUi.Label((RectTransform)cgo.transform, "BOOT ERR " + ex.GetType().Name + ": " + ex.Message, 16, Color.red, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Debug.LogException(ex);
        }
    }

    void StartInner()
    {
        Application.targetFrameRate = 60;
        // agent self-verification hooks (desktop only, opt-in via CLI args):
        //   -tacshot <dir>  save a screenshot every 2 s
        //   -tacauto        auto-drive: home -> training brief -> deploy
        var args = System.Environment.GetCommandLineArgs();
        for (int ai = 0; ai < args.Length; ai++)
        {
            if (args[ai] == "-tacshot" && ai + 1 < args.Length) StartCoroutine(ShotLoop(args[ai + 1]));
            // supersample factor for -tacshot (store screenshots need exact
            // device pixel sizes far larger than a Mac window)
            if (args[ai] == "-tacshotscale" && ai + 1 < args.Length) shotScale = int.Parse(args[ai + 1]);
            // App-Preview recorder: dump EVERY frame as PNG on a deterministic
            // 30fps clock (Time.captureFramerate) — ffmpeg turns them into the
            // store video. Wall-clock runs slower; game time stays correct.
            if (args[ai] == "-tacrec" && ai + 1 < args.Length) StartCoroutine(RecLoop(args[ai + 1]));
            if (args[ai] == "-tacauto") StartCoroutine(AutoPilot());
            if (args[ai] == "-taccreate") StartCoroutine(AutoCreate());
            // desktop-only: fake a phone safe area "top,bottom[,left,right]" (px)
            if (args[ai] == "-tacsafe" && ai + 1 < args.Length)
            {
                var p = args[ai + 1].Split(',');
                float T = p.Length > 0 ? float.Parse(p[0]) : 0;
                float B = p.Length > 1 ? float.Parse(p[1]) : 0;
                float L = p.Length > 2 ? float.Parse(p[2]) : 0;
                float R = p.Length > 3 ? float.Parse(p[3]) : 0;
                _fakeSafe = new Vector4(T, B, L, R);
            }
        }
        cam = Camera.main;
        cam.backgroundColor = new Color(0.66f, 0.71f, 0.78f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        var sunGo = GameObject.Find("Sun");
        if (sunGo != null) sunLight = sunGo.GetComponent<Light>();
        // layer 31 belongs to the offscreen thumbnail rig (TacPreview3D), which
        // brings its own light — keep the scene sun (day OR dusk) off of it
        if (sunLight != null) sunLight.cullingMask &= ~(1 << 31);
        cam.nearClipPlane = 0.25f;
        cam.farClipPlane = 260f;
        audioKit = gameObject.AddComponent<TacAudioKit>();
        BuildUi();
        // First launch: a one-time intro that says what the game is. Afterwards go
        // straight to the stage list.
        if (PlayerPrefs.GetInt("tac_onboarded", 0) == 0) ShowOnboarding();
        else ShowList();

        // Deep links: a shared ?stage=<id>&key=<editKey> URL. Cold start arrives
        // in Application.absoluteURL; a tap while running fires deepLinkActivated.
        // Both route to the SAME handler, which only previews status='draft'
        // stages (published courses must be reached through the in-app list).
        Application.deepLinkActivated += OnDeepLink;
        if (!string.IsNullOrEmpty(Application.absoluteURL)) OnDeepLink(Application.absoluteURL);
    }

    void OnDestroy()
    {
        Application.deepLinkActivated -= OnDeepLink;
    }

    // Parse ?stage=<id>[&key=<editKey>] out of a deep-link URL, then preview the
    // stage IF it is an unpublished draft. Anything else (no stage id, network
    // failure, or an already-published stage) is a silent no-op — the app just
    // stays on the home list.
    void OnDeepLink(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        string sid = QueryParam(url, "stage");
        if (string.IsNullOrEmpty(sid)) return;
        string key = QueryParam(url, "key");
        StartCoroutine(OpenDraftPreview(sid, key));
    }

    static string QueryParam(string url, string name)
    {
        int q = url.IndexOf('?');
        if (q < 0) return null;
        string query = url.Substring(q + 1);
        foreach (var pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (pair.Substring(0, eq) == name)
                return System.Uri.UnescapeDataString(pair.Substring(eq + 1));
        }
        return null;
    }

    IEnumerator OpenDraftPreview(string sid, string key)
    {
        // 1. Probe status WITHOUT the body — never preview a published stage.
        string metaJson = null;
        yield return TacNet.GetStageMeta(sid, (j) => metaJson = j);
        if (metaJson == null)
        {
            Toast(TacLoc.T("dl_notfound"), 5f);
            yield break;
        }
        string status = "";
        try { status = TacJson.Parse(metaJson).Str("status"); }
        catch (System.Exception) { }
        if (status != "draft")
        {
            // Published (or unverified testbench) — direct links are draft-only.
            Toast(TacLoc.T("dl_published"), 6f);
            yield break;
        }
        // 2. Fetch the full draft JSON (opGetStage returns drafts) and preview it.
        string full = null;
        yield return TacNet.GetJson("/api/stages/" + sid, (j) => full = j, null);
        if (full == null)
        {
            Toast(TacLoc.T("dl_notfound"), 5f);
            yield break;
        }
        stageCache[sid] = full;
        // Preview mode: solo brief entry, PREVIEW badge, no score/vote writes.
        previewDraft = true;
        briefList.Clear();
        briefList.Add(new BriefEntry { sid = sid, name = null, embedded = null, meta = null });
        StartStage(full, sid, null);
    }

    int shotScale = 1;

    IEnumerator RecLoop(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        Time.captureFramerate = 30;
        int n = 0;
        while (true)
        {
            yield return null;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "f" + n.ToString("00000") + ".png"), shotScale);
            n++;
        }
    }

    IEnumerator ShotLoop(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        int n = 0;
        while (true)
        {
            yield return new WaitForSeconds(2f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot-" + (n++).ToString("00") + ".png"), shotScale);
        }
    }

    IEnumerator AutoCreate()
    {
        yield return new WaitForSeconds(3f);
        ShowCreate();
    }

    IEnumerator AutoPilot()
    {
        yield return new WaitForSeconds(4f);
        StartStage(TRAINING, null, "TRAINING GROUND");
        yield return new WaitForSeconds(3f);
        BeginPlay();
        yield return new WaitForSeconds(6f);
        // walk forward + look around a bit so the shots show gameplay
        heading = 0;
        camYaw = 0.3f;
        yield return new WaitForSeconds(5f);
        heading = 255;
    }

    // ------------------------------------------------------------------ UI --
    void HoldButton(Button b, System.Action down, System.Action up)
    {
        var trig = b.gameObject.AddComponent<EventTrigger>();
        var d = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        d.callback.AddListener((_) => down());
        var u = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        u.callback.AddListener((_) => up());
        trig.triggers.Add(d);
        trig.triggers.Add(u);
    }

    // Thumb-arc placement: action buttons hug the RIGHT edge by default; the
    // handedness setting ("tac_hand_l") mirrors them to the LEFT edge and the
    // sneak toggle to the opposite corner, so either thumb reaches everything.
    static bool LeftHanded => PlayerPrefs.GetInt("tac_hand_l", 0) == 1;

    static void PlaceThumb(RectTransform rt, Vector2 center, float dia, bool mirrorToLeft)
    {
        if (rt == null) return;
        var a = mirrorToLeft ? new Vector2(0, 0) : new Vector2(1, 0);
        var c = mirrorToLeft ? new Vector2(-center.x, center.y) : center;
        rt.anchorMin = a; rt.anchorMax = a;
        rt.offsetMin = new Vector2(c.x - dia / 2, c.y - dia / 2);
        rt.offsetMax = new Vector2(c.x + dia / 2, c.y + dia / 2);
    }

    void ApplyHandLayout()
    {
        bool L = LeftHanded;
        PlaceThumb(fireRt, new Vector2(-64, 86), 96, L);
        PlaceThumb(jumpRt, new Vector2(-62, 84), 78, L);
        PlaceThumb(bombRt, new Vector2(-150, 96), 62, L);
        PlaceThumb(scopeRt, new Vector2(-56, 172), 58, L);
        PlaceThumb(droneRt, new Vector2(-148, 184), 62, L);
        PlaceThumb(sneakRt, new Vector2(64, 196), 60, !L); // opposite corner
    }

    // Real device safe area, with an optional editor/desktop override so the Mac
    // verification build can reproduce a phone's notch + home indicator (which a
    // windowed Mac build never has). Set via CLI: -tacsafe top,bottom (px at the
    // real screen height), e.g. -tacsafe 141,102 mimics a 3x iPhone.
    static Vector4 _fakeSafe = Vector4.zero; // x=top y=bottom z=left w=right (px)
    Rect SafeArea()
    {
        var sa = Screen.safeArea;
        if (_fakeSafe != Vector4.zero)
        {
            return new Rect(_fakeSafe.z, _fakeSafe.y,
                Screen.width - _fakeSafe.z - _fakeSafe.w,
                Screen.height - _fakeSafe.x - _fakeSafe.y);
        }
        return sa;
    }

    // Child rect stretched PAST its safe-area panel to the PHYSICAL screen edges
    // (same math as MakePanel's backdrop child). Full-screen overlays — vignette,
    // damage flash — must bleed under the notch/home bar or play looks letterboxed.
    RectTransform FullBleed(string name, RectTransform parent)
    {
        var rt = TacUi.Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var sa = SafeArea();
        float cw = 420f, chh = cw * Screen.height / Mathf.Max(1, Screen.width);
        float uxL = sa.xMin / Screen.width * cw;
        float uxR = (Screen.width - sa.xMax) / Screen.width * cw;
        float uyB = sa.yMin / Screen.height * chh;
        float uyT = (Screen.height - sa.yMax) / Screen.height * chh;
        rt.offsetMin = new Vector2(-uxL, -uyB);
        rt.offsetMax = new Vector2(uxR, uyT);
        return rt;
    }

    RectTransform MakePanel(string name, Color c)
    {
        // content root hugs the SAFE AREA (text must never sit under the notch
        // or the home indicator); the background child stretches back out to
        // cover the whole screen, so backdrops still bleed edge to edge.
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        var sa = SafeArea();
        rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
        rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        if (c.a > 0f)
        {
            var bgGo = new GameObject("panelbg", typeof(RectTransform));
            var bg = bgGo.GetComponent<RectTransform>();
            bg.SetParent(rt, false);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            // canvas units from the width-matched scaler (the canvas RECT can
            // still be zero this frame, so don't read it)
            float cw = 420f, chh = cw * Screen.height / Mathf.Max(1, Screen.width);
            float uxL = sa.xMin / Screen.width * cw;
            float uxR = (Screen.width - sa.xMax) / Screen.width * cw;
            float uyB = sa.yMin / Screen.height * chh;
            float uyT = (Screen.height - sa.yMax) / Screen.height * chh;
            bg.offsetMin = new Vector2(-uxL, -uyB);
            bg.offsetMax = new Vector2(uxR, uyT);
            var img = bgGo.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = c.a > 0.05f; // overlays still swallow taps behind them
        }
        return rt;
    }

    void BuildUi()
    {
        var cgo = new GameObject("TacCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // Portrait-only app (user decision 2026-07-20): width-match against a
        // 420-unit-wide reference so every screen lays out at a known width.
        sc.referenceResolution = new Vector2(420, 900);
        sc.matchWidthOrHeight = 0f;
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ---- stage list (mirrors tac-home.html, stacked for portrait) ----
        // Translucent backdrop: the attract-mode battle diorama (a live TRAINING
        // firefight) plays behind the list, so the home screen IS the game.
        listRoot = MakePanel("list", new Color(TacUi.Bg.r, TacUi.Bg.g, TacUi.Bg.b, 0.50f));
        // header sits snug against the safe-area top (user: trim the gap above PROMPT WORLD)
        var brand = TacUi.Label(listRoot, "", 22, TacUi.Fg, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -40), new Vector2(-24, -4), true);
        brand.supportRichText = true;
        brand.text = TacUi.Track("PROMPT") + " <color=#4dd2c3>" + TacUi.Track("WORLD") + "</color>";
        StartCoroutine(FadeRiseIn((RectTransform)brand.transform, 0f, 0.6f, 12f)); // logo entrance (mirrors web)
        var tagLbl = TacUi.Label(listRoot, TacLoc.T("tagline"), 10, TacUi.Dim, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -60), new Vector2(-24, -38));
        StartCoroutine(FadeIn((RectTransform)tagLbl.transform, 0.1f, 0.6f));
        var hline = TacUi.Rect("hline", listRoot, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -67), new Vector2(0, -66));
        TacUi.Fill(hline, TacUi.Line);

        // ---- HUD ---- (hidden immediately; only Play mode shows it)
        hudRoot = MakePanel("hud", new Color(0, 0, 0, 0));
        hudRoot.gameObject.SetActive(false);
        // cinematic edge vignette (always on in play) + low-HP red pulse above it.
        // FullBleed: these must reach the PHYSICAL screen edges, not stop at the
        // safe area — a vignette ring ending at the notch line reads as a shrunken,
        // letterboxed play view (user report 2026-07-20).
        var vigRt = FullBleed("vig", hudRoot);
        playVig = vigRt.gameObject.AddComponent<Image>();
        playVig.sprite = TacUi.Vignette();
        playVig.color = new Color(0f, 0f, 0f, 0.35f);
        playVig.raycastTarget = false;
        var lowRt = FullBleed("lowhp", hudRoot);
        lowHpVig = lowRt.gameObject.AddComponent<Image>();
        lowHpVig.sprite = TacUi.Vignette();
        lowHpVig.color = new Color(0.9f, 0.08f, 0.06f, 0f);
        lowHpVig.raycastTarget = false;
        var dmgRt = FullBleed("dmg", hudRoot);
        dmgFlash = TacUi.Fill(dmgRt, new Color(1f, 0.15f, 0.1f, 0f));
        dmgFlash.raycastTarget = false;

        // scope reticle (center of screen, shown only while scoped)
        scopeCross = TacUi.Rect("scope", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-90, -90), new Vector2(90, 90));
        var ringImg = scopeCross.gameObject.AddComponent<Image>();
        ringImg.sprite = TacUi.CircleLine();
        ringImg.color = new Color(0f, 0f, 0f, 0.55f);
        ringImg.raycastTarget = false;
        // four ticks + center dot from thin bars
        System.Action<Vector2, Vector2> bar = (mn, mx) =>
        {
            var b2 = TacUi.Rect("bar", scopeCross, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), mn, mx);
            var im = b2.gameObject.AddComponent<Image>();
            im.color = new Color(1f, 0.25f, 0.2f, 0.9f);
            im.raycastTarget = false;
        };
        bar(new Vector2(-1.2f, 8), new Vector2(1.2f, 42));    // up
        bar(new Vector2(-1.2f, -42), new Vector2(1.2f, -8));  // down
        bar(new Vector2(8, -1.2f), new Vector2(42, 1.2f));    // right
        bar(new Vector2(-42, -1.2f), new Vector2(-8, 1.2f));  // left
        bar(new Vector2(-2, -2), new Vector2(2, 2));          // center dot
        scopeCross.gameObject.SetActive(false);

        // hit-confirm marker: 4 diagonal ticks that pop at the impact point.
        // white = hit, red + bigger = kill. Repositioned per event (world→screen).
        hitMark = TacUi.Rect("hitmark", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30, -30), new Vector2(30, 30));
        hitMarkBars = new Image[4];
        for (int hb = 0; hb < 4; hb++)
        {
            var pivotRt = TacUi.Rect("hmp" + hb, hitMark, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            pivotRt.localEulerAngles = new Vector3(0, 0, 45 + 90 * hb);
            var barRt = TacUi.Rect("hmb", pivotRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-1.7f, 7), new Vector2(1.7f, 18));
            hitMarkBars[hb] = barRt.gameObject.AddComponent<Image>();
            hitMarkBars[hb].raycastTarget = false;
        }
        hitMark.gameObject.SetActive(false);

        // segmented HP bar (top-left) — replaces the old heart glyph string
        hpBarRoot = TacUi.Rect("hpbar", hudRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -38), new Vector2(196, -24));
        hpSegMax = -1;
        hpSegs.Clear();
        foesText = TacUi.Label(hudRoot, "", 13, TacUi.Dim, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -74), new Vector2(420, -46));
        timeText = TacUi.Label(hudRoot, "", 14, TacUi.Fg, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-80, -38), new Vector2(80, -10));
        msgText = TacUi.Label(hudRoot, "", 17, TacUi.Warn, TextAnchor.MiddleCenter, new Vector2(0.2f, 0.62f), new Vector2(0.8f, 0.76f), Vector2.zero, Vector2.zero);

        var mmGo = new GameObject("minimap", typeof(RectTransform));
        var mmRt = mmGo.GetComponent<RectTransform>();
        mmRt.SetParent(hudRoot, false);
        mmRt.anchorMin = new Vector2(1, 1); mmRt.anchorMax = new Vector2(1, 1);
        mmRt.offsetMin = new Vector2(-158, -158); mmRt.offsetMax = new Vector2(-8, -8);
        minimapImg = mmGo.AddComponent<RawImage>();
        minimapImg.color = new Color(1, 1, 1, 0.92f);
        var mmFrame = TacUi.Box(hudRoot, new Color(0.03f, 0.035f, 0.05f, 0.6f), TacUi.Line, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-162, -162), new Vector2(-4, -4));
        mmFrame.SetAsFirstSibling();

        Text tmp;
        Image tmpi;
        // Portrait thumb arc, mirrored by the handedness setting (ApplyHandLayout).
        // FIRE only appears for the manual-trigger states (scope / drone pilot);
        // normal play auto-fires once the lock-on settles.
        var br = new Vector2(1, 0);
        var fireB = TacUi.RoundIconBtn(hudRoot, "fire", TacIcons.Fire(), new Color(1f, 1f, 1f, 0.95f), br, new Vector2(-64, 86), 96, null, out tmpi);
        HoldButton(fireB, () => fireHeld = true, () => fireHeld = false);
        fireRt = (RectTransform)fireB.transform;
        fireRt.gameObject.SetActive(false);
        var jumpB = TacUi.RoundIconBtn(hudRoot, "jump", TacIcons.Jump(), new Color(1, 1, 1, 0.75f), br, new Vector2(-62, 84), 78, () => jumpLatch = true, out tmpi);
        jumpRt = (RectTransform)jumpB.transform;
        var bombBtn = TacUi.RoundIconBtn(hudRoot, "bomb", TacIcons.Bomb(), TacUi.Warn, br, new Vector2(-150, 96), 62, null, out grenIcon);
        bombRt = (RectTransform)bombBtn.transform;
        // hold = aim (trajectory shows); slide OFF the button = cancel (the
        // trajectory disappears — releasing then throws nothing); slide back
        // on = re-arm; release on the button = throw
        var bombTrig = bombBtn.gameObject.AddComponent<EventTrigger>();
        void BombEntry(EventTriggerType et, System.Action act)
        {
            var en2 = new EventTrigger.Entry { eventID = et };
            en2.callback.AddListener((_) => act());
            bombTrig.triggers.Add(en2);
        }
        BombEntry(EventTriggerType.PointerDown, () => { bombAiming = true; bombCancelled = false; });
        BombEntry(EventTriggerType.PointerExit, () => { if (bombAiming) bombCancelled = true; });
        BombEntry(EventTriggerType.PointerEnter, () => { if (bombAiming) bombCancelled = false; });
        BombEntry(EventTriggerType.PointerUp, () => { if (bombAiming && !bombCancelled) grenLatch = true; bombAiming = false; bombCancelled = false; });
        var droneB = TacUi.RoundIconBtn(hudRoot, "drone", TacIcons.Drone(), TacUi.DroneGreen, br, new Vector2(-148, 184), 62, () => droneLatch = true, out droneIcon);
        droneRt = (RectTransform)droneB.transform;
        var scopeBtn = TacUi.RoundIconBtn(hudRoot, "scope", TacIcons.Scope(), TacUi.ScopeCyan, br, new Vector2(-56, 172), 58, () => scopeLatch = true, out scopeIcon);
        scopeRt = (RectTransform)scopeBtn.transform;
        scopeCountText = TacUi.Label((RectTransform)scopeBtn.transform, "", 11, TacUi.ScopeCyan, TextAnchor.LowerCenter, Vector2.zero, Vector2.one, new Vector2(0, 2), new Vector2(0, 0));
        var sneakB = TacUi.RoundIconBtn(hudRoot, "sneak", TacIcons.Sneak(), TacUi.Dim, new Vector2(0, 0), new Vector2(64, 196), 60, null, out tmpi);
        sneakRt = (RectTransform)sneakB.transform;
        var sneakIcon = tmpi;
        var sneakRing = sneakB.transform.Find("ring").GetComponent<Image>();
        sneakB.onClick.AddListener(() =>
        {
            sneakOn = !sneakOn;
            var c = sneakOn ? TacUi.Teal : TacUi.Dim;
            sneakIcon.color = c;
            sneakRing.color = new Color(c.r, c.g, c.b, 0.75f);
        });
        // pause sits under the minimap (top-right); centered it would collide
        // with the clock on the 420-wide portrait canvas
        TacUi.Btn(hudRoot, "II", 14, new Color(1, 1, 1, 0.6f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-52, -212), new Vector2(-8, -170), () => { if (mode == Mode.Play) ShowPause(); }, out tmp);

        var sbGo = new GameObject("stickbg", typeof(RectTransform));
        var sbRt = sbGo.GetComponent<RectTransform>();
        sbRt.SetParent(hudRoot, false);
        sbRt.sizeDelta = new Vector2(120, 120);
        stickBg = sbGo.AddComponent<Image>();
        stickBg.sprite = TacUi.CircleLine();
        stickBg.color = new Color(1, 1, 1, 0.35f);
        stickBg.raycastTarget = false;
        var snGo = new GameObject("sticknub", typeof(RectTransform));
        var snRt = snGo.GetComponent<RectTransform>();
        snRt.SetParent(sbRt, false);
        snRt.sizeDelta = new Vector2(48, 48);
        stickNub = snGo.AddComponent<Image>();
        stickNub.sprite = TacUi.Circle();
        stickNub.color = new Color(1, 1, 1, 0.4f);
        stickNub.raycastTarget = false;
        stickBg.gameObject.SetActive(false);
        ApplyHandLayout();

        // ---- brief / result / pause ----
        settingsRoot = MakePanel("settings", TacUi.Overlay);
        settingsRoot.gameObject.SetActive(false);
        createRoot = MakePanel("create", TacUi.Overlay);
        createRoot.gameObject.SetActive(false);
        briefRoot = MakePanel("brief", new Color(6 / 255f, 8 / 255f, 10 / 255f, 0.42f));
        resultRoot = MakePanel("result", TacUi.Overlay);
        pauseRoot = MakePanel("pause", TacUi.Overlay);
        hudRoot.gameObject.SetActive(false);
        briefRoot.gameObject.SetActive(false);
        resultRoot.gameObject.SetActive(false);
        pauseRoot.gameObject.SetActive(false);
        // Canvas-level toast: a deep-link that resolves to nothing (bad link, or
        // an already-published stage) shows feedback here — msgText lives on the
        // HUD and is invisible on the home list where deep links land.
        toastText = TacUi.Label((RectTransform)canvas.transform, "", 14, TacUi.Warn, TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.44f), new Vector2(0.92f, 0.52f), Vector2.zero, Vector2.zero);
        toastText.gameObject.SetActive(false);
        uiScreenW = Screen.width; uiScreenH = Screen.height; uiSafe = SafeArea();
    }

    // External-link gate: every jump out of the app asks first. Makes the app's
    // web access explicitly user-confirmed (App Review clarity) and stops an
    // accidental tap from yanking the player out to Safari mid-session.
    void ConfirmOpenUrl(string url)
    {
        var ov = MakePanel("extlink", TacUi.Overlay);
        ov.SetAsLastSibling();
        var box = TacUi.Box(ov, TacUi.Panel, TacUi.Line, new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.62f), Vector2.zero, Vector2.zero);
        TacUi.Label(box, TacUi.Track(TacLoc.T("extTitle")), 15, TacUi.Fg, TextAnchor.MiddleCenter,
            new Vector2(0, 0.68f), new Vector2(1, 0.95f), Vector2.zero, Vector2.zero, true);
        var body = TacUi.Label(box, TacLoc.T("extBody"), 12, TacUi.Dim, TextAnchor.UpperCenter,
            new Vector2(0.06f, 0.38f), new Vector2(0.94f, 0.66f), Vector2.zero, Vector2.zero);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        Text tmp;
        TacUi.Btn(box, TacLoc.T("extCancel"), 12, TacUi.Line, new Vector2(0.06f, 0.07f), new Vector2(0.48f, 0.32f),
            Vector2.zero, Vector2.zero, () => Destroy(ov.gameObject), out tmp);
        tmp.color = TacUi.Dim;
        TacUi.Btn(box, TacLoc.T("extOpen"), 12, TacUi.Teal, new Vector2(0.52f, 0.07f), new Vector2(0.94f, 0.32f),
            Vector2.zero, Vector2.zero, () =>
            {
                Haptic.Tap();
                Destroy(ov.gameObject);
                Application.OpenURL(url);
            }, out tmp);
        StartCoroutine(StampIn(box, 0f));
    }

    void Toast(string msg, float secs)
    {
        if (toastText == null) return;
        toastText.text = msg;
        toastText.gameObject.SetActive(true);
        toastText.transform.SetAsLastSibling();
        toastUntil = Time.time + secs;
    }

    void ClearChildren(RectTransform rt, int keep = 0)
    {
        for (int i = rt.childCount - 1; i >= keep; i--)
        {
            var ch = rt.GetChild(i);
            if (ch.name == "panelbg") continue;
            Destroy(ch.gameObject);
        }
    }

    // ------------------------------------------------------- onboarding (1x) --
    // First-launch intro: three lines that say what the game is + a START button.
    // Shown once (PlayerPrefs 'tac_onboarded'), then never again.
    void ShowOnboarding()
    {
        var ob = MakePanel("onboard", TacUi.Bg);
        ob.gameObject.SetActive(true);
        var title = TacUi.Label(ob, "", 26, TacUi.Fg, TextAnchor.MiddleCenter, new Vector2(0, 0.78f), new Vector2(1, 0.86f), Vector2.zero, Vector2.zero, true);
        title.supportRichText = true;
        title.text = TacUi.Track("PROMPT") + " <color=#4dd2c3>" + TacUi.Track("WORLD") + "</color>";
        StartCoroutine(FadeRiseIn((RectTransform)title.transform, 0f, 0.6f, 14f));

        string[] keys = { "obL1", "obL2", "obL3" };
        for (int i = 0; i < keys.Length; i++)
        {
            float top = 0.64f - i * 0.13f;
            var l = TacUi.Label(ob, TacLoc.T(keys[i]), 13, TacUi.Promo, TextAnchor.UpperCenter,
                new Vector2(0.1f, top), new Vector2(0.9f, top + 0.12f), Vector2.zero, Vector2.zero);
            l.horizontalOverflow = HorizontalWrapMode.Wrap;
            StartCoroutine(FadeIn((RectTransform)l.transform, 0.15f + i * 0.12f, 0.6f));
        }

        Text bt;
        var start = TacUi.Btn(ob, TacLoc.T("obStart"), 15, TacUi.Teal,
            new Vector2(0.2f, 0.12f), new Vector2(0.8f, 0.19f), Vector2.zero, Vector2.zero, () =>
            {
                Haptic.Tap();
                PlayerPrefs.SetInt("tac_onboarded", 1); PlayerPrefs.Save();
                Object.Destroy(ob.gameObject);
                ShowList();
            }, out bt);
        StartCoroutine(FadeIn((RectTransform)start.transform, 0.5f, 0.6f));
    }

    // ----------------------------------------------------------- stage list --
    void ShowList()
    {
        mode = Mode.List;
        loadSeq++; // back on the list: any in-flight stage fetch is obsolete
        // Leaving any deep-link preview: back to normal published-course flow.
        previewDraft = false;
        if (uiDirty) { uiDirty = false; BuildUiRefresh(); return; }
        listRoot.gameObject.SetActive(true);
        hudRoot.gameObject.SetActive(false);
        briefRoot.gameObject.SetActive(false);
        resultRoot.gameObject.SetActive(false);
        pauseRoot.gameObject.SetActive(false);
        settingsRoot.gameObject.SetActive(false);
        // Home diorama: keep a live attract battle running behind the list. A
        // sort/search refresh must NOT restart the fight — only (re)build when
        // arriving without one (first boot, back from a stage, etc).
        if (!attract || world == null) StartAttract();
        ClearChildren(listRoot, 4); // panelbg + the 3 persistent header children

        // sort pills (one row of five, 68 wide each fits the 420 canvas)
        string[] sortKeys = { "sortNew", "sortGood", "sortPlays", "sortHard", "sortTb" };
        for (int i = 0; i < 5; i++)
        {
            int si = i;
            bool cur = sortMode == i;
            Text st;
            TacUi.Btn(listRoot, TacLoc.T(sortKeys[i]), 9, cur ? TacUi.Teal : TacUi.Line,
                new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(24 + i * 76, -104), new Vector2(24 + i * 76 + 68, -76),
                () => { if (sortMode != si) { sortMode = si; metas.Clear(); } ShowList(); }, out st);
            if (!cur) st.color = TacUi.Dim;
        }
        // create / settings on their own row under the pills
        Text tmpS;
        TacUi.Btn(listRoot, TacLoc.T("create"), 10, TacUi.Teal, new Vector2(0, 1), new Vector2(0.5f, 1),
            new Vector2(24, -144), new Vector2(-6, -112), ShowCreate, out tmpS);
        TacUi.Btn(listRoot, TacLoc.T("settings"), 10, TacUi.Line, new Vector2(0.5f, 1), new Vector2(1, 1),
            new Vector2(6, -144), new Vector2(-24, -112), ShowSettings, out tmpS);
        tmpS.color = TacUi.Dim;

        // stage-name search — re-queries the server (works on the published list
        // AND the testbench shelf) when the field is committed.
        var searchField = TacUi.Search(listRoot, TacLoc.T("search"), 11, new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(24, -184), new Vector2(-24, -152), null);
        searchField.text = searchQuery;
        searchField.onEndEdit.AddListener(v =>
        {
            var q = (v ?? "").Trim();
            if (q == searchQuery) return;
            searchQuery = q; metas.Clear(); ShowList();
        });

        // scroll shell — the list must reach the PHYSICAL screen bottom (past the
        // safe-area inset), or a strip below the last card stays empty even with a
        // full list. listRoot hugs the safe area, so extend the scroll down by the
        // bottom inset (in canvas units) to fill the home-indicator strip too.
        var saB = SafeArea();
        float chhB = 420f * Screen.height / Mathf.Max(1, Screen.width);
        float bottomInsetUnits = saB.yMin / Screen.height * chhB;
        var scrollRt = TacUi.Rect("scroll", listRoot, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, -bottomInsetUnits), new Vector2(0, -192));
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 18f;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        var viewport = TacUi.Rect("viewport", scrollRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0.001f);
        listContent = TacUi.Rect("content", viewport, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        listContent.pivot = new Vector2(0.5f, 1);
        scroll.viewport = viewport;
        scroll.content = listContent;

        if (metas.Count > 0)
        {
            RenderCards();
            return;
        }
        var status = TacUi.Label(listRoot, TacLoc.T("loading"), 12, TacUi.Dim, TextAnchor.MiddleRight, new Vector2(0.5f, 0), new Vector2(1, 0.08f), Vector2.zero, new Vector2(-24, 0));
        string sortParam = sortMode == 1 ? "&sort=top" : (sortMode == 3 ? "&sort=hard" : (sortMode == 4 ? "&sort=testbench" : "&sort=new"));
        // playerId lets the server drop stages this device has hidden; q= is the
        // stage-name search (works on both the published list and the testbench).
        string extra = "&playerId=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(TacNet.PlayerId);
        if (!string.IsNullOrEmpty(searchQuery))
            extra += "&q=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(searchQuery);
        StartCoroutine(TacNet.GetJson("/api/stages?game=tac" + sortParam + extra, (json) =>
        {
            try
            {
                var arr = TacJson.Parse(json).Arr("stages");
                status.text = "";
                metas.Clear();
                for (int i = 0; i < arr.Count && i < 40; i++)
                {
                    var st = arr.Obj(i);
                    metas.Add(new StageMeta
                    {
                        id = st.Str("id"),
                        name = st.Has("name") ? st.Str("name") : st.Str("id"),
                        creator = st.Has("creator") ? st.Str("creator") : "",
                        published = st.Has("published_at") ? st.Str("published_at") : "",
                        goods = st.Num("goods", 0),
                        bads = st.Num("bads", 0),
                        attempts = st.Num("attempts", 0),
                        clears = st.Num("clears", 0),
                        parMs = st.Num("clear_time_ms", 0)
                    });
                }
                RenderCards();
            }
            catch (System.Exception ex) { status.text = TacLoc.T("offline") + " [" + ex.GetType().Name + "]"; }
        }, () => { status.text = TacLoc.T("offline"); }));
        SizeListContent(1);
    }

    void RenderCards()
    {
        ClearChildren(listContent);
        var sorted = new List<StageMeta>(metas);
        // NEW/GOOD/HARD arrive already ranked by the server; PLAYS has no server
        // sort key, so order it client-side by attempts.
        if (sortMode == 2) sorted.Sort((a2, b2) => b2.attempts.CompareTo(a2.attempts));
        int idx = 0;
        briefList.Clear();
        // training stage removed from the public list (still used by offline fallback + autopilot)
        foreach (var m in sorted)
        {
            briefList.Add(new BriefEntry { sid = m.id, name = m.name, creator = m.creator, embedded = null, meta = m });
            AddStageCard(idx++, m.id, m.name, m.creator, null, m);
        }
        SizeListContent(idx);
    }

    void ShowSettings()
    {
        // portrait flow via a top-anchored vertical FlowCol: every row is placed
        // by its own height in canvas units and the column tracks the running Y,
        // so the safe-area-shrunk panel on a real device never overlaps rows the
        // way the old fixed -offset math did. The footer is bottom-pinned.
        settingsRoot.gameObject.SetActive(true);
        ClearChildren(settingsRoot);
        Text tmp;
        var col = new FlowCol(settingsRoot, topPad: 14f, sidePad: 24f);

        col.Header(TacUi.Track(TacLoc.T("settings")), 36);
        col.Divider();
        col.Gap(6);

        // language
        col.SectionLabel(TacLoc.T("language"));
        col.Row(34, (row, w) =>
        {
            int n = TacLoc.Langs.Length;
            float bw = (w - (n - 1) * 8f) / n;
            for (int i = 0; i < n; i++)
            {
                var l = TacLoc.Langs[i];
                bool cur = l == TacLoc.Lang;
                TacUi.Btn(row, l.ToUpper(), 11, cur ? TacUi.Teal : TacUi.Line,
                    new Vector2(0, 0), new Vector2(0, 1),
                    new Vector2(i * (bw + 8), 0), new Vector2(i * (bw + 8) + bw, 0),
                    () => { TacLoc.SetLang(l); BuildUiRefresh(); ShowSettingsAfterRefresh(); }, out var lt);
                if (!cur) lt.color = TacUi.Dim;
            }
        });
        col.Gap(10);

        // volumes (label left, slider fills the rest of the row)
        col.Row(30, (row, w) =>
        {
            TacUi.Label(row, TacLoc.T("bgm"), 12, TacUi.Teal, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(86, 0));
            TacUi.SliderRow(row, TacAudioKit.BgmVol, new Vector2(0, 0), new Vector2(1, 1), new Vector2(96, 0), new Vector2(-4, 0), (v) => { TacAudioKit.BgmVol = v; });
        });
        col.Gap(12);
        col.Row(30, (row, w) =>
        {
            TacUi.Label(row, TacLoc.T("sfx"), 12, TacUi.Teal, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(86, 0));
            TacUi.SliderRow(row, TacAudioKit.SfxVol, new Vector2(0, 0), new Vector2(1, 1), new Vector2(96, 0), new Vector2(-4, 0), (v) => { TacAudioKit.SfxVol = v; audioKit.Play("shot", 0.8f); });
        });
        col.Gap(14);

        // operative skins
        col.SectionLabel(TacLoc.T("skin"));
        col.Row(56, (row, w) =>
        {
            int n = TacSkins.All.Length;
            float bw = (w - (n - 1) * 8f) / n;
            for (int i = 0; i < n; i++)
            {
                int si = i;
                bool cur = TacSkins.Index == i;
                var sw = TacUi.Box(row, TacSkins.All[i].body, cur ? TacUi.Teal : TacUi.Line,
                    new Vector2(0, 0), new Vector2(0, 1),
                    new Vector2(si * (bw + 8), 0), new Vector2(si * (bw + 8) + bw, 0));
                var swBtn = sw.gameObject.AddComponent<Button>();
                swBtn.onClick.AddListener(() => { TacSkins.Index = si; ShowSettings(); });
                if (cur) TacUi.Label(sw, "✓", 18, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
        });
        col.Gap(14);

        // control handedness: which edge the thumb-arc buttons hug in play
        col.SectionLabel(TacLoc.T("handSide"));
        bool leftNow = LeftHanded;
        col.Row(34, (row, w) =>
        {
            TacUi.Btn(row, TacLoc.T("handR"), 11, leftNow ? TacUi.Line : TacUi.Teal,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(130, 0),
                () => { PlayerPrefs.SetInt("tac_hand_l", 0); ApplyHandLayout(); ShowSettings(); }, out var t0);
            if (leftNow) t0.color = TacUi.Dim;
            TacUi.Btn(row, TacLoc.T("handL"), 11, leftNow ? TacUi.Teal : TacUi.Line,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(140, 0), new Vector2(270, 0),
                () => { PlayerPrefs.SetInt("tac_hand_l", 1); ApplyHandLayout(); ShowSettings(); }, out var t1);
            if (!leftNow) t1.color = TacUi.Dim;
        });

        // footer: legal links (App Store / Play policy: reachable in-app) + back
        TacUi.Btn(settingsRoot, TacLoc.T("privacy"), 10, TacUi.Line,
            new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(24, 68), new Vector2(-6, 98),
            () => ConfirmOpenUrl(TacNet.Origin + "/privacy.html"), out tmp);
        tmp.color = TacUi.Dim;
        TacUi.Btn(settingsRoot, TacLoc.T("terms"), 10, TacUi.Line,
            new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(6, 68), new Vector2(-24, 98),
            () => ConfirmOpenUrl(TacNet.Origin + "/terms.html"), out tmp);
        tmp.color = TacUi.Dim;

        TacUi.Btn(settingsRoot, TacLoc.T("back"), 12, new Color(1, 1, 1, 0.85f), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-90, 18), new Vector2(90, 56), () =>
        {
            PlayerPrefs.Save();
            settingsRoot.gameObject.SetActive(false);
        }, out tmp);
    }

    bool reopenSettings;
    void ShowSettingsAfterRefresh() { }

    const string McpCmd = "claude mcp add --transport http promptworld https://promptworldgame.org/mcp";

    // in-app creator onboarding (local screen first; web guide only on request)
    void ShowCreate()
    {
        // portrait flow via FlowCol (see ShowSettings) — safe-area-robust.
        createRoot.gameObject.SetActive(true);
        ClearChildren(createRoot);
        var col = new FlowCol(createRoot, topPad: 14f, sidePad: 24f);
        col.Header(TacUi.Track(TacLoc.T("create")), 36);
        col.Divider();
        col.Para(TacLoc.T("createLead"), 60, 12, TacUi.Promo);
        col.Gap(8);

        string[] steps = { "createS1", "createS2", "createS3" };
        for (int i = 0; i < 3; i++)
        {
            int si = i;
            col.Row(74, (row, w) =>
            {
                var box = TacUi.Box(row, TacUi.Panel, TacUi.Line, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                TacUi.Label(box, (si + 1).ToString(), 22, TacUi.Teal, TextAnchor.MiddleLeft, new Vector2(0, 0), new Vector2(0, 1), new Vector2(14, 0), new Vector2(44, 0));
                var st = TacUi.Label(box, TacLoc.T(steps[si]), 11, TacUi.Dim, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(48, 6), new Vector2(-10, -6));
                st.horizontalOverflow = HorizontalWrapMode.Wrap;
            });
            col.Gap(10);
        }

        // MCP command box + copy
        col.Row(48, (row, w) =>
        {
            var cmdBox = TacUi.Box(row, TacUi.Hex(0x07090C), TacUi.Line, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var cmdT = TacUi.Label(cmdBox, McpCmd, 9, TacUi.Teal, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(10, 2), new Vector2(-8, -2));
            cmdT.horizontalOverflow = HorizontalWrapMode.Wrap;
        });
        col.Gap(10);
        col.Row(32, (row, w) =>
        {
            Text copyTxt;
            var copyBtn = TacUi.Btn(row, TacLoc.T("copyCmd"), 10, TacUi.Teal, new Vector2(0, 0), new Vector2(1, 1), new Vector2(w * 0.25f, 0), new Vector2(-w * 0.25f, 0), () =>
            {
                GUIUtility.systemCopyBuffer = McpCmd;
            }, out copyTxt);
            var copyBtnText = copyTxt;
            copyBtn.onClick.AddListener(() => { copyBtnText.text = TacLoc.T("copied"); });
        });

        Text tmp;
        // footer pinned to the bottom edge
        TacUi.Btn(createRoot, TacLoc.T("openGuide"), 11, new Color(1, 1, 1, 0.85f), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(24, 20), new Vector2(-6, 58), () =>
        {
            ConfirmOpenUrl("https://promptworldgame.org/create?lang=" + TacLoc.Lang);
        }, out tmp);
        TacUi.Btn(createRoot, TacLoc.T("back"), 11, TacUi.Dim, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(6, 20), new Vector2(-24, 58), () =>
        {
            createRoot.gameObject.SetActive(false);
        }, out tmp);
        tmp.color = TacUi.Dim;
    }

    const float CardH = 118f, CardGap = 10f, CardTop = 12f;

    void SizeListContent(int count)
    {
        listContent.sizeDelta = new Vector2(0, CardTop + count * (CardH + CardGap) + 16);
    }

    // --- lightweight UI entrance animations (mirror the web home) --------------
    // Fade a rect in from `rise` px below its resting position, after `delay` s.
    IEnumerator FadeRiseIn(RectTransform rt, float delay, float dur, float rise)
    {
        if (rt == null) yield break;
        var cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
        Vector2 rest = rt.anchoredPosition;
        cg.alpha = 0f;
        rt.anchoredPosition = rest - new Vector2(0, rise);
        float t = 0f;
        while (delay > 0f) { delay -= Time.unscaledDeltaTime; yield return null; if (rt == null) yield break; }
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            cg.alpha = k;
            rt.anchoredPosition = Vector2.LerpUnclamped(rest - new Vector2(0, rise), rest, k);
            yield return null;
        }
        cg.alpha = 1f;
        rt.anchoredPosition = rest;
    }

    // Stage thumbnail: prefer the angled 3D preview, fall back to the flat map.
    static Texture2D PreviewTex(TacJson.JObj st, int w, int h)
    {
        var tex = TacPreview3D.Render(st);
        if (tex != null) return tex;
        return TacThumb.Draw(st, w, h);
    }

    // Thumbnail texture cache. A 3D preview render = geometry rebuild + offscreen
    // render + GPU readback (~ms each); every ShowList used to re-render EVERY
    // card in one frame. A published stage's layout never changes, so cache by id
    // and reuse for the session.
    readonly Dictionary<string, Texture2D> thumbCache = new Dictionary<string, Texture2D>();
    int lastThumbFrame = -1;

    Texture2D ThumbFor(string key, TacJson.JObj st, int w, int h)
    {
        if (key == null) return PreviewTex(st, w, h);
        string k = key + ":" + w + "x" + h;
        Texture2D t;
        if (thumbCache.TryGetValue(k, out t) && t != null) return t;
        t = PreviewTex(st, w, h);
        if (t != null)
        {
            if (thumbCache.Count >= 100) // pressure valve; realistic use stays far below
            {
                foreach (var old in thumbCache.Values) if (old != null) Destroy(old);
                thumbCache.Clear();
            }
            thumbCache[k] = t;
        }
        return t;
    }

    // Fade a rect in (no movement) — for header title/buttons.
    IEnumerator FadeIn(RectTransform rt, float delay, float dur)
    {
        if (rt == null) yield break;
        var cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        while (delay > 0f) { delay -= Time.unscaledDeltaTime; yield return null; if (rt == null) yield break; }
        float t = 0f;
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Clamp01(t / dur);
            yield return null;
        }
        cg.alpha = 1f;
    }

    // Stamp-in: big → rest with an ease-out, for verdict/mission titles.
    IEnumerator StampIn(RectTransform rt, float delay = 0f)
    {
        if (rt == null) yield break;
        var cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        rt.localScale = Vector3.one * 2.1f;
        while (delay > 0f) { delay -= Time.unscaledDeltaTime; yield return null; if (rt == null) yield break; }
        float t = 0f;
        const float dur = 0.22f;
        while (t < dur)
        {
            if (rt == null) yield break;
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = 1f - (1f - k) * (1f - k); // ease-out
            cg.alpha = k;
            rt.localScale = Vector3.one * Mathf.Lerp(2.1f, 1f, k);
            yield return null;
        }
        rt.localScale = Vector3.one;
        cg.alpha = 1f;
    }

    void AddStageCard(int idx, string sid, string nm, string creator, string embeddedJson, StageMeta meta)
    {
        // single column: portrait width fits exactly one card. The card panel is
        // slightly translucent so the attract diorama glints through the list.
        float y0 = -CardTop - idx * (CardH + CardGap);
        var card = TacUi.Box(listContent, new Color(TacUi.Panel.r, TacUi.Panel.g, TacUi.Panel.b, 0.90f), TacUi.Line,
            new Vector2(0.03f, 1), new Vector2(0.97f, 1),
            new Vector2(0, y0 - CardH), new Vector2(0, y0));
        // Staggered entrance: fade + rise, mirroring the web home's cardIn. Only
        // the first dozen stagger; later cards just fade so long lists don't lag.
        float delay = idx < 12 ? idx * 0.045f : 0f;
        StartCoroutine(FadeRiseIn(card, delay, 0.5f, 14f));
        // the panel-fill Image is the raycast target (one Graphic per GO)
        var btn = card.gameObject.AddComponent<Button>();
        // Tap feedback (mobile has no hover): the card panel tints brighter while
        // pressed, so a touch feels responsive. targetGraphic = the fill Image.
        btn.targetGraphic = card.GetComponent<Image>();
        btn.transition = Selectable.Transition.ColorTint;
        var cbtn = btn.colors;
        cbtn.normalColor = Color.white;
        cbtn.highlightedColor = Color.white;
        cbtn.pressedColor = new Color(1.35f, 1.35f, 1.35f, 1f); // brighten the panel on press
        cbtn.selectedColor = Color.white;
        cbtn.fadeDuration = 0.08f;
        btn.colors = cbtn;
        btn.onClick.AddListener(() =>
        {
            Haptic.Tap();
            if (embeddedJson != null) StartStage(embeddedJson, null, nm);
            else if (stageCache.ContainsKey(sid)) StartStage(stageCache[sid], sid, nm);
            else StartCoroutine(LoadAndStart(sid, nm));
        });
        // thumbnail (left) — lazy loaded from the full stage JSON.
        // RawImage lives on its own child: the box already carries the fill Image.
        var thumbBox = TacUi.Box(card, TacUi.Bg, TacUi.Line, new Vector2(0, 0), new Vector2(0, 1), new Vector2(8, 8), new Vector2(128, -8));
        var rawRt = TacUi.Rect("thumb", thumbBox, Vector2.zero, Vector2.one, new Vector2(1, 1), new Vector2(-1, -1));
        rawRt.SetSiblingIndex(1);
        var raw = rawRt.gameObject.AddComponent<RawImage>();
        raw.color = Color.white;
        var nameLbl = TacUi.Label(card, nm, 15, TacUi.Fg, TextAnchor.UpperLeft, new Vector2(0, 0), new Vector2(1, 1), new Vector2(140, 8), new Vector2(-10, -12));
        // course description under the stats line (localized desc from the stage JSON)
        var descLbl = TacUi.Label(card, "", 10, TacUi.Dim, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(140, -92), new Vector2(-10, -56));
        descLbl.horizontalOverflow = HorizontalWrapMode.Wrap;
        descLbl.verticalOverflow = VerticalWrapMode.Truncate;
        if (embeddedJson != null)
        {
            raw.texture = ThumbFor("emb:" + nm, TacJson.Parse(embeddedJson), 120, 80);
            FillCardDetail(embeddedJson, nameLbl, descLbl);
        }
        else StartCoroutine(LoadThumb(sid, raw, nameLbl, descLbl));
        string sub = creator;
        if (meta != null)
        {
            sub = creator + "   ▶" + (int)meta.attempts + "  ♥" + (int)meta.goods;
        }
        TacUi.Label(card, sub, 11, TacUi.Dim, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(140, -56), new Vector2(-10, -34));
        int bestTicks = PlayerPrefs.GetInt("tac_clear_" + (sid ?? "training"), 0);
        if (bestTicks > 0)
        {
            TacUi.Label(card, "✓ " + FmtTime(bestTicks), 11, TacUi.Teal, TextAnchor.UpperRight, new Vector2(0, 0), new Vector2(1, 1), new Vector2(140, 8), new Vector2(-12, -12));
            var edge = card.Find("edge");
            if (edge != null) edge.GetComponent<Image>().color = new Color(TacUi.Teal.r, TacUi.Teal.g, TacUi.Teal.b, 0.55f);
        }
        TacUi.Label(card, TacUi.Track("PLAY ▸"), 11, TacUi.Teal, TextAnchor.LowerLeft, new Vector2(0, 0), new Vector2(1, 1), new Vector2(140, 10), new Vector2(-10, -8));

        // Report / hide — bottom-right of the card, only for real (server) stages.
        // Tapping once hides the card for THIS device and reports it (the stage
        // stays visible to everyone else; admins review the report count).
        if (!string.IsNullOrEmpty(sid))
        {
            Text hideTxt;
            var hideBtn = TacUi.Btn(card, TacLoc.T("hide"), 9, TacUi.Line,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-118, 8), new Vector2(-10, 30), null, out hideTxt);
            hideTxt.color = TacUi.Dim;
            bool reported = false;
            hideBtn.onClick.AddListener(() =>
            {
                if (reported) return;
                reported = true;
                hideTxt.text = TacLoc.T("hidden");
                hideTxt.color = TacUi.Alert;
                StartCoroutine(TacNet.Hide(sid, () => { if (card != null) card.gameObject.SetActive(false); }));
            });
        }
    }

    IEnumerator LoadThumb(string sid, RawImage raw, Text nameLbl = null, Text descLbl = null)
    {
        // stage JSON is immutable once published — reuse the session cache
        // instead of refetching on every list rebuild (sort switch, back-nav)
        string got;
        if (!stageCache.TryGetValue(sid, out got))
        {
            got = null;
            yield return TacNet.GetJson("/api/stages/" + sid, (j) => got = j, null);
            if (got == null || raw == null) yield break;
            stageCache[sid] = got;
        }
        // uncached 3D renders are spread one-per-frame: several fetches can land
        // in the same frame, and each render carries a GPU-readback hitch
        if (!thumbCache.ContainsKey(sid + ":120x80"))
        {
            while (lastThumbFrame == Time.frameCount) yield return null;
            lastThumbFrame = Time.frameCount;
        }
        if (raw == null) yield break;
        try
        {
            raw.texture = ThumbFor(sid, TacJson.Parse(got), 120, 80);
            FillCardDetail(got, nameLbl, descLbl);
        }
        catch (System.Exception) { }
    }

    // localized name + description pulled out of the full stage JSON
    void FillCardDetail(string json, Text nameLbl, Text descLbl)
    {
        try
        {
            var st = TacJson.Parse(json);
            string lang = TacLoc.Lang;
            if (nameLbl != null && st.Has("nameLoc"))
            {
                var nl = st.Obj("nameLoc");
                if (nl != null && nl.Has(lang)) nameLbl.text = nl.Str(lang);
            }
            if (descLbl != null && st.Has("desc"))
            {
                var dc = st.Obj("desc");
                if (dc != null)
                {
                    string d = dc.Has(lang) ? dc.Str(lang) : (dc.Has("en") ? dc.Str("en") : "");
                    descLbl.text = d;
                }
            }
        }
        catch (System.Exception) { }
    }

    void BuildUiRefresh()
    {
        Destroy(canvas.gameObject);
        BuildUi();
        ShowList();
    }

    // Selection token: every navigation (card tap, prev/next, play, back-to-list)
    // bumps it, and an in-flight stage fetch only opens its briefing if it is
    // still the LATEST selection. Without this, rapid taps raced: whichever
    // response arrived last would open ITS briefing — a different course than
    // the one the player last picked — or even yank a running game back to a
    // briefing when a slow response landed mid-play.
    int loadSeq;

    IEnumerator LoadAndStart(string sid, string nm)
    {
        int my = ++loadSeq;
        Toast(TacLoc.T("loading"), 15f);
        string got = null;
        yield return TacNet.GetJson("/api/stages/" + sid, (j) => got = j, null);
        if (my != loadSeq) yield break; // stale: the player moved on
        if (got != null) { stageCache[sid] = got; StartStage(got, sid, nm); }
        else
        {
            Toast(TacLoc.T("offline"), 3f); // a silent dead tap reads as a freeze
            // prev/next advanced briefIndex optimistically; the fetch failed, so
            // point it back at the course still on screen or ▶ would skip one
            if (mode == Mode.Brief)
            {
                int cur = briefList.FindIndex(e => e.sid == stageId);
                if (cur >= 0) briefIndex = cur;
            }
        }
    }

    // ------------------------------------------------------------- lifecycle --
    void StartStage(string json, string sid, string nm)
    {
        try { StartStageInner(json, sid, nm); }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            // msgText lives under the HUD, which is hidden on the list screen —
            // an error there was invisible and left a half-built briefing with
            // no buttons (the "app froze" report). Toast renders on any screen;
            // then restore a working list so the player is never stranded.
            Toast("ERR " + ex.GetType().Name + ": " + ex.Message, 8f);
            try { ShowList(); } catch (System.Exception) { }
        }
    }

    void StartStageInner(string json, string sid, string nm)
    {
        loadSeq++;       // this briefing is now the truth — drop in-flight fetches
        toastUntil = 0f; // dismiss the LOADING toast
        attract = false; // leaving the home diorama: sounds/HUD reactions resume
        stageJson = json;
        stageId = sid;
        stageDoc = TacJson.Parse(json);
        stageName = TacLoc.Pick(stageDoc.Has("nameLoc") ? stageDoc.Obj("nameLoc") : null, nm);
        listRoot.gameObject.SetActive(false);
        briefRoot.gameObject.SetActive(true);
        ClearChildren(briefRoot);
        // 3D vista: build the (pristine) world now so the briefing shows the
        // actual arena from the start position behind a translucent overlay
        BuildWorldView();
        briefCamT = 0f;
        var missionLbl = TacUi.Label(briefRoot, TacUi.Track(previewDraft ? TacLoc.T("preview") : "MISSION"), 11,
            previewDraft ? TacUi.Warn : TacUi.Teal, TextAnchor.MiddleCenter,
            new Vector2(0, 0.90f), new Vector2(1, 0.94f), Vector2.zero, Vector2.zero);
        StartCoroutine(FadeIn((RectTransform)missionLbl.transform, 0.05f, 0.35f));
        var stageLbl = TacUi.Label(briefRoot, TacUi.Track(stageName), 22, TacUi.Fg, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.83f), new Vector2(0.96f, 0.90f), Vector2.zero, Vector2.zero, true);
        StartCoroutine(StampIn((RectTransform)stageLbl.transform, 0.1f));
        TacUi.Divider(briefRoot, 0.82f);
        // map inset bottom-left, stats + desc to its right — the vertical
        // CENTER stays clear so the 3D vista of the arena (rendered behind
        // this translucent overlay) is the star of the briefing
        var mapBox = TacUi.Box(briefRoot, TacUi.Hex(0x07090C), TacUi.Line, new Vector2(0.05f, 0.26f), new Vector2(0.38f, 0.46f), Vector2.zero, Vector2.zero);
        var mraw = TacUi.Rect("map", mapBox, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4)).gameObject.AddComponent<RawImage>();
        try { mraw.texture = ThumbFor(stageId ?? ("emb:" + stageName), stageDoc, 120, 150); } catch (System.Exception) { }
        string desc = TacLoc.Pick(stageDoc.Has("desc") ? stageDoc.Obj("desc") : null, "");
        var dt = TacUi.Label(briefRoot, desc, 11, TacUi.Promo, TextAnchor.UpperLeft, new Vector2(0.42f, 0.26f), new Vector2(0.95f, 0.395f), Vector2.zero, Vector2.zero);
        dt.horizontalOverflow = HorizontalWrapMode.Wrap;
        dt.verticalOverflow = VerticalWrapMode.Truncate;
        // stats: difficulty stars (clear-rate), plays, rating — same data as home
        briefIndex = briefList.FindIndex(e => (stageId == null && e.sid == null) || e.sid == stageId);
        if (briefIndex < 0) briefIndex = 0;
        var bm = briefIndex >= 0 && briefIndex < briefList.Count ? briefList[briefIndex].meta : null;
        string statsLine;
        if (bm != null)
        {
            double rate = (bm.clears + 1) / (bm.attempts + 2);
            string diff = rate < 0.2 ? "★★★" : (rate < 0.45 ? "★★☆" : "★☆☆");
            statsLine = diff + "   ▶ " + (int)bm.attempts + "   ♥ " + (int)bm.goods;
        }
        else statsLine = "TUTORIAL";
        TacUi.Label(briefRoot, statsLine, 14, TacUi.Warn, TextAnchor.MiddleLeft, new Vector2(0.42f, 0.40f), new Vector2(0.95f, 0.46f), Vector2.zero, Vector2.zero);

        Text tmp;
        TacUi.Btn(briefRoot, TacLoc.T("start"), 14, new Color(1, 1, 1, 0.9f), new Vector2(0.24f, 0.13f), new Vector2(0.76f, 0.21f), Vector2.zero, Vector2.zero, BeginPlay, out tmp);
        TacUi.Btn(briefRoot, TacLoc.T("back"), 11, TacUi.Dim, new Vector2(0.03f, 0.925f), new Vector2(0.24f, 0.97f), Vector2.zero, Vector2.zero, ShowList, out tmp);
        // prev / next course flank the start button (also swipe on the backdrop)
        if (briefList.Count > 1)
        {
            TacUi.Btn(briefRoot, "◀", 20, TacUi.Dim, new Vector2(0.04f, 0.13f), new Vector2(0.19f, 0.21f), Vector2.zero, Vector2.zero, () => SwitchBrief(-1), out tmp);
            TacUi.Btn(briefRoot, "▶", 20, TacUi.Dim, new Vector2(0.81f, 0.13f), new Vector2(0.96f, 0.21f), Vector2.zero, Vector2.zero, () => SwitchBrief(1), out tmp);
            TacUi.Label(briefRoot, (briefIndex + 1) + " / " + briefList.Count, 11, TacUi.Dim, TextAnchor.MiddleCenter, new Vector2(0.3f, 0.05f), new Vector2(0.7f, 0.10f), Vector2.zero, Vector2.zero);
        }
        mode = Mode.Brief;
    }

    void SwitchBrief(int dir)
    {
        if (briefList.Count == 0) return;
        briefIndex = ((briefIndex + dir) % briefList.Count + briefList.Count) % briefList.Count;
        var e = briefList[briefIndex];
        if (e.embedded != null) StartStage(e.embedded, null, e.name);
        else if (stageCache.ContainsKey(e.sid)) StartStage(stageCache[e.sid], e.sid, e.name);
        else StartCoroutine(LoadAndStart(e.sid, e.name));
    }

    // swipe left/right anywhere on the brief screen switches course
    Vector2 briefSwipeStart; bool briefSwiping;
    void ReadBriefSwipe()
    {
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began) { briefSwipeStart = t.position; briefSwiping = true; }
            else if (briefSwiping && (t.phase == TouchPhase.Ended))
            {
                float dx = t.position.x - briefSwipeStart.x;
                briefSwiping = false;
                if (Mathf.Abs(dx) > Screen.width * 0.12f && Mathf.Abs(dx) > Mathf.Abs(t.position.y - briefSwipeStart.y))
                    SwitchBrief(dx < 0 ? 1 : -1);
            }
        }
        else if (Application.isEditor)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow)) SwitchBrief(1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) SwitchBrief(-1);
        }
    }

    void BeginPlay()
    {
        try { BeginPlayInner(); }
        catch (System.Exception ex)
        {
            var err = TacUi.Label(briefRoot, "ERR " + ex.GetType().Name + ": " + ex.Message, 13, TacUi.Alert, TextAnchor.LowerCenter, new Vector2(0, 0), new Vector2(1, 0.1f), Vector2.zero, Vector2.zero);
            Debug.LogException(ex);
        }
    }

    void BeginPlayInner()
    {
        loadSeq++; // a late stage fetch must never yank a running game to a briefing
        // the brief screen may have prebuilt the pristine (never-stepped) world
        // for its 3D vista — reuse it; otherwise build fresh
        if (world == null || world.tick != 0) BuildWorldView();
        recs.Clear();
        acc = 0;
        primePlayFrame = true; // swallow the next frame's dt spike (see field comment)
        // seed the snapshot with the spawn pose; snapReady flips true after the
        // first real tick, so until then views render straight at the current pose
        snapReady = false; snapPrevP = default(MoBody); snapCurP = default(MoBody);
        snapPrevPilot = default(MoBody); snapCurPilot = default(MoBody);
        snapCurE = new MoBody[0]; snapPrevE = new MoBody[0];
        CaptureSnapshot();
        camYaw = (float)(world.yawQ * System.Math.PI * 2.0 / 65536.0);
        camPitch = -0.12f;
        heading = 255;
        fireHeld = false; sneakOn = false; bombAiming = false;
        // Drop any touch ownership from the previous run. If a finger was down
        // when the run ended, its id stays claimed forever (the end event fires
        // while input reading is frozen) and the move stick "freezes" on retry.
        stickTouch = -1; lookTouch = -1; pinchPrevDist = -1f;
        if (stickBg != null) stickBg.gameObject.SetActive(false);
        autoLockT = 0; autoLockKey = -1;
        jumpLatch = grenLatch = droneLatch = scopeLatch = false;
        bool nightStage = stageDoc.Has("night") && stageDoc.Num("night") != 0.0;
        audioKit.StartMusic(stageDoc.Has("music") ? stageDoc.Obj("music") : null, nightStage);

        briefRoot.gameObject.SetActive(false);
        hudRoot.gameObject.SetActive(true);
        mode = Mode.Play;
        // Draft preview stays fully off the server's stat counters.
        if (stageId != null && !previewDraft) StartCoroutine(TacNet.ReportPlay(stageId, false));
    }

    // ------------------------------------------------------- attract diorama --
    // (Re)build the silent TRAINING battle that plays behind the home list.
    void StartAttract()
    {
        attract = true;
        previewDraft = false;
        stageJson = TRAINING;
        stageId = null;
        stageDoc = TacJson.Parse(TRAINING);
        BuildWorldView();
        // Dusk grade (render-only): the day-lit arena is too bright to sit
        // behind the dark home UI, so the diorama gets a moody blue-hour look.
        // nightLightingOn=true makes the next ApplyNightLighting(false) — any
        // real stage build — fully restore the day values instead of skipping.
        if (!world.night)
        {
            if (sunLight != null) { sunLight.intensity = 0.30f; sunLight.color = new Color(0.72f, 0.80f, 1f); }
            RenderSettings.ambientLight = new Color(0.17f, 0.19f, 0.25f);
            RenderSettings.fogColor = new Color(0.05f, 0.065f, 0.09f);
            RenderSettings.fogStartDistance = 28f;
            RenderSettings.fogEndDistance = 95f;
            cam.backgroundColor = new Color(0.05f, 0.065f, 0.09f);
            nightLightingOn = true;
        }
        acc = 0;
        snapReady = false; snapPrevP = default(MoBody); snapCurP = default(MoBody);
        snapPrevPilot = default(MoBody); snapCurPilot = default(MoBody);
        snapCurE = new MoBody[0]; snapPrevE = new MoBody[0];
        CaptureSnapshot();
        autoLockT = 0; autoLockKey = -1;
        attractResetT = 0f;
        attractDirT = 0;
        attractPause = false;
        attractAimDeg = attractWantDeg = (float)(world.yawQ * 360.0 / 65536.0);
        attractOrbitYaw = (float)(world.yawQ * System.Math.PI * 2.0 / 65536.0) + 2.6f;
    }

    // Autopilot for the diorama scout: wander toward the arena interior with a
    // fresh waypoint every 1–3 s, and auto-fire with the same settled-lock rule
    // real play uses — so genuine firefights break out behind the menu.
    TacInput AttractInput()
    {
        if (attractDirT-- <= 0)
        {
            attractDirT = 60 + attractRng.Next(110);
            attractPause = attractRng.Next(5) == 0;
            double toC = System.Math.Atan2(world.arenaW / 2.0 - world.px, world.arenaD / 2.0 - world.pz);
            attractWantDeg = (float)(toC * Mathf.Rad2Deg + (attractRng.NextDouble() - 0.5) * 200.0);
        }
        attractAimDeg = Mathf.MoveTowardsAngle(attractAimDeg, attractWantDeg, 170f * (float)TAC.TICK);
        int b = 0;
        long lk = world.lockTarget >= 0 ? (long)world.lockKind * 100000 + world.lockTarget : -1;
        if (lk >= 0 && lk == autoLockKey) autoLockT++; else autoLockT = 0;
        autoLockKey = lk;
        if (lk >= 0 && autoLockT >= 10) b |= 2;
        int yawQ = ((int)System.Math.Round(attractAimDeg / 360.0 * 65536.0) % 65536 + 65536) % 65536;
        return new TacInput { b = b, m = attractPause ? 255 : 0, yawQ = yawQ, pitchQ = 0 };
    }

    // Slow cinematic orbit around the scout, framing whatever fight breaks out.
    void AttractCamera()
    {
        attractOrbitYaw += Time.deltaTime * 0.11f;
        float a = SnapAlpha();
        Vector3 p = snapReady ? LerpPos(snapPrevP, snapCurP, a)
                              : new Vector3((float)world.px, (float)world.py, (float)world.pz);
        var pivot = p + Vector3.up * 1.2f;
        var dir = new Vector3(Mathf.Sin(attractOrbitYaw), 0f, Mathf.Cos(attractOrbitYaw));
        var eye = pivot - dir * 11.5f + Vector3.up * 4.6f;
        float minY = (float)world.GroundY(eye.x, eye.z, 1000.0, 0.2) + 0.4f;
        if (eye.y < minY) eye.y = minY;
        cam.transform.position = Vector3.Lerp(cam.transform.position, eye, SmAlpha(0.45f));
        // aim BELOW the scout so the action rides the upper third of the screen —
        // that's the strip the (translucent) home list leaves fully open
        var look = Quaternion.LookRotation(pivot - Vector3.up * 5f - cam.transform.position, Vector3.up);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, look, SmAlpha(0.35f));
        cam.fieldOfView = Screen.height > Screen.width ? 66f : 50f;
    }

    void BuildWorldView()
    {
        TearDownWorld();
        world = new TacWorld(stageDoc);

        worldRoot = new GameObject("TacWorldView").transform;
        TacRenderKit.BuildStatic(worldRoot, world, boxViews);
        foreach (var wr in worldRoot.GetComponentsInChildren<Renderer>())
            if (wr.gameObject.name == "waterSurf") waterViews.Add(wr);
        playerView = TacRenderKit.BuildPlayerView(worldRoot);
        // muzzle flash rides the gun tip (gun barrel: local (0.28, 1.05, 0.5..1.0))
        // and blinks on for ~2 frames per shot with a random roll per flash
        muzzleFlash = new GameObject("muzzle");
        muzzleFlash.transform.SetParent(playerView.transform, false);
        muzzleFlash.transform.localPosition = new Vector3(0.28f, 1.05f, 1.12f);
        var mfCore = TacRenderKit.Sphere(muzzleFlash.transform, Vector3.zero, 0.30f, new Color(1f, 0.92f, 0.5f));
        mfCore.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(new Color(1f, 0.92f, 0.5f));
        var mfSpikeH = TacRenderKit.Cube(muzzleFlash.transform, Vector3.zero, new Vector3(0.62f, 0.07f, 0.07f), Color.white);
        mfSpikeH.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(Color.white);
        var mfSpikeV = TacRenderKit.Cube(muzzleFlash.transform, Vector3.zero, new Vector3(0.07f, 0.62f, 0.07f), Color.white);
        mfSpikeV.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(Color.white);
        muzzleFlash.SetActive(false);
        foreach (var en in world.enemies) enemyViews.Add(TacRenderKit.BuildEnemyView(worldRoot, en));

        foreach (var sw in world.switches)
        {
            var console = TacRenderKit.Cube(worldRoot, new Vector3((float)sw.x, (float)sw.y + 0.6f, (float)sw.z), new Vector3(0.7f, 1.2f, 0.7f), new Color(0.5f, 0.4f, 0.85f));
            switchViews.Add(console);
            var dome = TacRenderKit.Sphere(worldRoot, new Vector3((float)sw.x, (float)sw.y, (float)sw.z), (float)sw.r * 2f, TacRenderKit.VeilCol, true);
            veilViews.Add(dome);
        }
        foreach (var mk in world.medkits) medkitViews.Add(TacRenderKit.BuildMedkitView(worldRoot, mk));
        foreach (var it in world.intels)
        {
            var g = TacRenderKit.Cube(worldRoot, new Vector3((float)it.x, (float)it.y + 0.18f, (float)it.z), new Vector3(0.5f, 0.36f, 0.5f), new Color(0.16f, 0.2f, 0.24f));
            var core = TacRenderKit.Cube(g.transform, new Vector3(0, 0.75f, 0), new Vector3(0.42f, 0.6f, 0.42f), TacUi.Teal);
            core.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(TacUi.Teal);
            core.name = "core";
            intelViews.Add(g);
        }
        if (world.exitZone != null)
        {
            var xz = world.exitZone;
            exitPad = TacRenderKit.Cube(worldRoot,
                new Vector3((float)((xz.x0 + xz.x1) / 2), 0.05f, (float)((xz.z0 + xz.z1) / 2)),
                new Vector3((float)(xz.x1 - xz.x0), 0.08f, (float)(xz.z1 - xz.z0)), TacUi.Teal);
            exitPad.GetComponent<Renderer>().sharedMaterial = TacRenderKit.TransMat(new Color(TacUi.Teal.r, TacUi.Teal.g, TacUi.Teal.b, 0.2f));
        }
        ApplyNightLighting(world.night);
        if (!world.night && world.palette != null && world.palette.Has("sky") &&
            ColorUtility.TryParseHtmlString(world.palette.Str("sky"), out var skyC))
        {
            cam.backgroundColor = skyC;
            RenderSettings.fogColor = skyC;
        }
        if (world.night)
        {
            var warm = new Color(1f, 0.85f, 0.45f);
            foreach (var lm in world.lamps)
            {
                var basePos = new Vector3((float)lm.x, 0f, (float)lm.z);
                TacRenderKit.Cube(worldRoot, basePos + new Vector3(0, 1.3f, 0), new Vector3(0.16f, 2.6f, 0.16f), new Color(0.14f, 0.15f, 0.18f));
                var head = TacRenderKit.Cube(worldRoot, basePos + new Vector3(0, 2.68f, 0), new Vector3(0.55f, 0.22f, 0.55f), warm);
                head.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(warm);
                var pool = TacRenderKit.Cyl(worldRoot, basePos + new Vector3(0, 0.03f, 0), (float)lm.r * 2f, 0.02f, warm);
                pool.GetComponent<Renderer>().sharedMaterial = TacRenderKit.TransMat(new Color(warm.r, warm.g, warm.b, 0.18f));
                var lgo = new GameObject("lampLight");
                lgo.transform.SetParent(worldRoot, false);
                lgo.transform.position = basePos + new Vector3(0, 2.4f, 0);
                var pl = lgo.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.range = (float)lm.r * 1.8f;
                pl.intensity = 1.5f;
                pl.color = new Color(1f, 0.88f, 0.6f);
            }
            foreach (var sl in world.lights)
            {
                var basePos = new Vector3((float)sl.x, 0f, (float)sl.z);
                TacRenderKit.Cube(worldRoot, basePos + new Vector3(0, 1.5f, 0), new Vector3(0.3f, 3.0f, 0.3f), new Color(0.13f, 0.14f, 0.17f));
                var pivotGo = new GameObject("searchPivot");
                pivotGo.transform.SetParent(worldRoot, false);
                pivotGo.transform.position = basePos;
                var head = TacRenderKit.Cube(pivotGo.transform, new Vector3(0, 3.1f, 0), new Vector3(0.5f, 0.42f, 0.72f), warm);
                head.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(warm);
                float reach = (float)sl.r;
                float wHalf = reach * Mathf.Tan(7.2f * Mathf.Deg2Rad);
                var beam = TacRenderKit.Cube(pivotGo.transform, new Vector3(0, 0.07f, reach * 0.5f), new Vector3(wHalf * 2f, 0.05f, reach), warm);
                beam.GetComponent<Renderer>().sharedMaterial = TacRenderKit.TransMat(new Color(warm.r, warm.g, warm.b, 0.22f));
                var sgo = new GameObject("searchLight");
                sgo.transform.SetParent(pivotGo.transform, false);
                sgo.transform.localPosition = new Vector3(0, 3.1f, 0);
                sgo.transform.localRotation = Quaternion.Euler(72f, 0f, 0f);
                var spl = sgo.AddComponent<Light>();
                spl.type = LightType.Spot;
                spl.range = reach * 1.6f;
                spl.spotAngle = 34f;
                spl.intensity = 2.6f;
                spl.color = new Color(1f, 0.92f, 0.62f);
                searchPivots.Add(pivotGo.transform);
            }
        }
        foreach (var mi in world.mines)
        {
            var mv = TacRenderKit.Cyl(worldRoot, new Vector3((float)mi.x, (float)mi.y + 0.09f, (float)mi.z), 0.7f, 0.18f, new Color(0.3f, 0.32f, 0.3f));
            TacRenderKit.Sphere(mv.transform, new Vector3(0, 1.1f, 0), 0.5f, new Color(1f, 0.25f, 0.2f));
            mineViews.Add(mv);
        }
        foreach (var ba in world.barrels)
        {
            barrelViews.Add(TacRenderKit.Cyl(worldRoot, new Vector3((float)ba.x, (float)ba.y + 0.5f, (float)ba.z), 1.0f, 1.0f, new Color(0.75f, 0.32f, 0.2f)));
        }
        for (int i = 0; i < 72; i++)
        {
            var b = TacRenderKit.Cube(worldRoot, Vector3.zero, new Vector3(0.15f, 0.15f, 1.15f), new Color(1f, 0.9f, 0.4f));
            b.SetActive(false);
            bulletPool.Add(b);
        }
        for (int i = 0; i < 8; i++)
        {
            var g = TacRenderKit.Sphere(worldRoot, Vector3.zero, 0.3f, new Color(0.2f, 0.2f, 0.22f));
            g.SetActive(false);
            grenadePool.Add(g);
        }
        lockMarker = TacRenderKit.Cube(worldRoot, Vector3.zero, new Vector3(0.34f, 0.34f, 0.34f), new Color(1f, 0.45f, 0.1f));
        lockMarker.SetActive(false);
        var laserGo = new GameObject("laser");
        laserGo.transform.SetParent(worldRoot, false);
        sniperLaser = laserGo.AddComponent<LineRenderer>();
        sniperLaser.material = TacRenderKit.UnlitMat(new Color(1f, 1f, 1f));
        sniperLaser.startWidth = 0.05f; sniperLaser.endWidth = 0.05f;
        sniperLaser.positionCount = 2;
        sniperLaser.enabled = false;
        var arcGo = new GameObject("bombArc");
        arcGo.transform.SetParent(worldRoot, false);
        bombArc = arcGo.AddComponent<LineRenderer>();
        bombArc.material = TacRenderKit.UnlitMat(new Color(1f, 0.82f, 0.2f));
        bombArc.startWidth = 0.12f; bombArc.endWidth = 0.12f;
        bombArc.enabled = false;
        bombLandRing = TacRenderKit.Cyl(worldRoot, Vector3.zero, (float)TAC.GRENADE_BLAST_R * 2f, 0.03f, new Color(1f, 0.3f, 0.15f));
        bombLandRing.GetComponent<Renderer>().sharedMaterial = TacRenderKit.TransMat(new Color(1f, 0.3f, 0.15f, 0.6f));
        bombLandRing.SetActive(false);

        BuildMinimapBase();
    }

    // scene lighting is baked bright by TacSceneBuilder; night stages override it
    // at runtime and TearDownWorld restores the day look for the menu/next stage
    void ApplyNightLighting(bool on)
    {
        if (on == nightLightingOn) { if (!on) return; }
        nightLightingOn = on;
        if (on)
        {
            if (sunLight != null) { sunLight.intensity = 0.14f; sunLight.color = new Color(0.65f, 0.72f, 1f); }
            RenderSettings.ambientLight = new Color(0.10f, 0.12f, 0.18f);
            RenderSettings.fogColor = new Color(0.03f, 0.045f, 0.075f);
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 110f;
            cam.backgroundColor = new Color(0.03f, 0.045f, 0.075f);
        }
        else
        {
            if (sunLight != null) { sunLight.intensity = 0.88f; sunLight.color = new Color(1f, 0.98f, 0.92f); }
            RenderSettings.ambientLight = new Color(0.46f, 0.48f, 0.52f);
            RenderSettings.fogColor = new Color(0.66f, 0.71f, 0.78f);
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 180f;
            cam.backgroundColor = new Color(0.66f, 0.71f, 0.78f);
        }
    }

    void TearDownWorld()
    {
        if (worldRoot != null) Destroy(worldRoot.gameObject);
        worldRoot = null;
        boxViews.Clear(); enemyViews.Clear(); bulletPool.Clear(); grenadePool.Clear();
        enemyLegPhase.Clear(); enemyLegAmp.Clear(); enemyLegPrevPos.Clear();
        playerLegInit = false; playerLegPhase = 0f; playerLegAmp = 0f;
        bombViews.Clear(); veilViews.Clear(); switchViews.Clear(); mineViews.Clear(); barrelViews.Clear(); medkitViews.Clear(); intelViews.Clear();
        searchPivots.Clear(); waterViews.Clear();
        ApplyNightLighting(false);
        exitPad = null;
        fxs.Clear();
        foreach (var m in timed) if (m.go != null) Destroy(m.go);
        timed.Clear();
        pilotView = null; lockMarker = null; sniperLaser = null; bombArc = null; bombLandRing = null; bombAiming = false;
        muzzleFlash = null; muzzleT = 0f;
        shakeT = 0f; shakeDur = 0f; shakeAmp = 0f;
        audioKit.StopMusic();
        world = null;
    }

    // ------------------------------------------------------------------ input --
    // Left half: dynamic move stick. Right half: DRAG = camera, TAP = fire one
    // shot, press-and-hold (without dragging) = continuous fire. Touches that
    // begin on a UI button never reach the game.
    static bool OverUi(int fingerId)
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    void ReadTouches()
    {
        // mouse fallback for the editor
        if (Input.touchCount == 0 && Application.isEditor)
        {
            if (Input.GetMouseButton(1))
            {
                camYaw += Input.GetAxis("Mouse X") * 0.03f;
                camPitch = Mathf.Clamp(camPitch + Input.GetAxis("Mouse Y") * 0.02f, -1.35f, 1.35f);
            }
            float mx = Input.GetAxisRaw("Horizontal");
            float mz = Input.GetAxisRaw("Vertical");
            if (mx != 0 || mz != 0)
            {
                float a = Mathf.Atan2(mx, mz);
                heading = ((int)System.Math.Round(a * 65536.0 / (System.Math.PI * 2.0) / 512.0)) & 127;
            }
            else heading = 255;
            if (Input.GetKey(KeyCode.Space)) jumpLatch = true;
            fireHeld = Input.GetKey(KeyCode.F) || (Input.GetMouseButton(0) && !OverUi(-1));
            return;
        }

        bool stickActive = false;
        // Self-heal stale touch ownership: if a claimed finger id no longer
        // exists in the touch list (its end event fired while input reading was
        // frozen — pause, result, scope hand-off), release it, or that half of
        // the controls stays dead ("stick frozen after retry").
        if (stickTouch >= 0 || lookTouch >= 0)
        {
            bool stickSeen = false, lookSeen = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                int fid = Input.GetTouch(i).fingerId;
                if (fid == stickTouch) stickSeen = true;
                if (fid == lookTouch) lookSeen = true;
            }
            if (stickTouch >= 0 && !stickSeen) { stickTouch = -1; stickBg.gameObject.SetActive(false); }
            if (lookTouch >= 0 && !lookSeen) lookTouch = -1;
        }
        // Split-screen controls: one half is the move stick (dynamic-origin
        // swipe), the other half drags the camera. Handedness ("tac_hand_l")
        // picks which half moves — right-handed = move LEFT / look RIGHT, so
        // the action buttons (which hug the right edge by default) stay under
        // the same thumb that also aims the camera. Left-handed mirrors it.
        float splitX = Screen.width * 0.5f;
        bool moveOnLeft = !LeftHanded; // right-handed → move stick is the LEFT half

        // Two-finger pinch = scope zoom (standard iOS zoom-in/out). Only while
        // scoped; the two fingers drive magnification instead of steering, so we
        // skip the per-finger look/steer handling below when a pinch is active.
        bool pinching = false;
        if (world.scoped && Input.touchCount == 2)
        {
            var a0 = Input.GetTouch(0);
            var a1 = Input.GetTouch(1);
            if (!OverUi(a0.fingerId) && !OverUi(a1.fingerId))
            {
                float dist = (a0.position - a1.position).magnitude;
                if (pinchPrevDist > 0f && dist > 0f)
                {
                    // scale FOV by the inverse spread ratio: fingers apart → zoom in
                    scopeFov = Mathf.Clamp(scopeFov * (pinchPrevDist / dist), ScopeFovMin, ScopeFovMax);
                }
                pinchPrevDist = dist;
                pinching = true;
                // release any camera-look finger so it doesn't jerk when the pinch ends
                lookTouch = -1;
            }
        }
        if (!pinching) pinchPrevDist = -1f;
        if (pinching)
        {
            // pinch owns both fingers this frame: drop the move stick + reticle steer
            if (stickTouch >= 0) { stickTouch = -1; stickBg.gameObject.SetActive(false); }
            heading = 255;
            return;
        }

        // Scoped: drag ANYWHERE on screen to aim (standard FPS sniper feel). Any
        // single non-UI finger becomes the look-drag — no left/right split, no
        // move stick — and the drag feeds camYaw/camPitch → input.yawQ/pitchQ,
        // which the sim maps straight onto the reticle. Stays deterministic.
        if (world.scoped)
        {
            if (stickTouch >= 0) { stickTouch = -1; stickBg.gameObject.SetActive(false); }
            heading = 255;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began && lookTouch < 0 && !OverUi(t.fingerId))
                    lookTouch = t.fingerId;
                if (t.fingerId == lookTouch)
                {
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) lookTouch = -1;
                    else if (t.phase == TouchPhase.Moved)
                    {
                        // INVERTED drag (telescope/"grab the world" feel): pulling the
                        // finger right sweeps the view right, so the reticle tracks
                        // opposite the finger. Slower gain than hip-fire for fine aim.
                        camYaw -= t.deltaPosition.x * 0.0022f;
                        camPitch = Mathf.Clamp(camPitch - t.deltaPosition.y * 0.0018f, -1.35f, 1.35f);
                    }
                }
            }
            return;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began && !OverUi(t.fingerId))
            {
                bool onMoveSide = (t.position.x < splitX) == moveOnLeft;
                if (onMoveSide && stickTouch < 0)
                {
                    stickTouch = t.fingerId;
                    stickOrigin = t.position;
                    stickBg.gameObject.SetActive(true);
                    stickBg.rectTransform.position = t.position;
                }
                else if (!onMoveSide && lookTouch < 0)
                {
                    lookTouch = t.fingerId;
                }
            }
            if (t.fingerId == stickTouch)
            {
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    stickTouch = -1;
                    stickBg.gameObject.SetActive(false);
                }
                else
                {
                    var d = t.position - stickOrigin;
                    stickActive = d.magnitude > 14f;
                    if (stickActive)
                    {
                        float a = Mathf.Atan2(d.x, d.y);
                        heading = ((int)System.Math.Round(((a * 65536.0 / (System.Math.PI * 2.0)) % 65536.0 + 65536.0) % 65536.0 / 512.0)) & 127;
                    }
                    stickNub.rectTransform.position = stickOrigin + Vector2.ClampMagnitude(d, 50f);
                }
            }
            else if (t.fingerId == lookTouch)
            {
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    lookTouch = -1;
                }
                else if (t.phase == TouchPhase.Moved)
                {
                    camYaw += t.deltaPosition.x * 0.0042f;
                    camPitch = Mathf.Clamp(camPitch + t.deltaPosition.y * 0.0032f, -1.35f, 1.35f);
                }
            }
        }
        if (stickTouch < 0 || !stickActive) heading = 255;
    }

    TacInput CollectInput()
    {
        int b = 0;
        if (jumpLatch) b |= 1;
        if (fireHeld) b |= 2;
        // Auto-fire: once the lock-on has stayed settled on the SAME target for
        // ~0.24 s, hold the trigger (the sim's FIRE_CD paces the actual shots).
        // Scoped sniping and drone piloting stay manual via the FIRE button.
        if (world != null && !world.scoped && world.pilot == null)
        {
            long lk = world.lockTarget >= 0 ? (long)world.lockKind * 100000 + world.lockTarget : -1;
            if (lk >= 0 && lk == autoLockKey) autoLockT++; else autoLockT = 0;
            autoLockKey = lk;
            if (lk >= 0 && autoLockT >= 12) b |= 2;
        }
        if (sneakOn) b |= 4;
        if (droneLatch) b |= 8;
        if (grenLatch) b |= 16;
        if (scopeLatch) b |= 32;
        jumpLatch = grenLatch = droneLatch = scopeLatch = false;
        int yawQ = ((int)System.Math.Round(camYaw * 65536.0 / (System.Math.PI * 2.0)) % 65536 + 65536) % 65536;
        int pitchQ = (int)System.Math.Round(camPitch * 65536.0 / (System.Math.PI * 2.0));
        if (pitchQ > TAC.PITCH_MAX) pitchQ = TAC.PITCH_MAX;
        if (pitchQ < TAC.PITCH_MIN) pitchQ = TAC.PITCH_MIN;
        return new TacInput { b = b, m = heading, yawQ = yawQ, pitchQ = pitchQ };
    }

    // ------------------------------------------------------------------ loop --
    // responsive: the safe-area anchors and flow layouts are baked at build
    // time, so a screen/safe-area change (iPad multitasking, Mac window
    // resize; orientation itself is locked) must rebuild the canvas. Menus
    // rebuild immediately; during play/brief the rebuild waits for the list.
    int uiScreenW, uiScreenH;
    Rect uiSafe;
    bool uiDirty;

    void WatchScreenSize()
    {
        if (Screen.width == uiScreenW && Screen.height == uiScreenH && SafeArea() == uiSafe) return;
        uiScreenW = Screen.width; uiScreenH = Screen.height; uiSafe = SafeArea();
        if (mode == Mode.List) BuildUiRefresh();
        else uiDirty = true;
    }

    void Update()
    {
        WatchScreenSize();
        if (toastText != null && toastText.gameObject.activeSelf && Time.time > toastUntil)
            toastText.gameObject.SetActive(false);
        UpdateDebris();
        UpdateTimed();
        if (dmgFlash != null && dmgT > 0f)
        {
            dmgT -= Time.deltaTime;
            var c = dmgFlash.color;
            c.a = Mathf.Clamp01(dmgT) * 0.5f;
            dmgFlash.color = c;
        }
        // hit-confirm marker: quick pop (1.45→1 scale) then fade out
        if (hitMark != null && hitMark.gameObject.activeSelf)
        {
            hitMarkT -= Time.deltaTime;
            if (hitMarkT <= 0f) hitMark.gameObject.SetActive(false);
            else
            {
                float p = 1f - hitMarkT / hitMarkDur;
                hitMark.localScale = Vector3.one * (1.45f - 0.45f * Mathf.Min(1f, p * 3f));
                float al = p < 0.6f ? 1f : 1f - (p - 0.6f) / 0.4f;
                foreach (var b in hitMarkBars)
                {
                    var bc = b.color; bc.a = al; b.color = bc;
                }
            }
        }
        // attract mode: silently advance the home-screen battle + orbit camera
        if (mode == Mode.List && attract && world != null)
        {
            if (attractResetT > 0f)
            {
                attractResetT -= Time.deltaTime;
                if (attractResetT <= 0f) { StartAttract(); }
            }
            else
            {
                acc += Time.deltaTime;
                int asteps = 0;
                while (acc >= TAC.TICK && asteps < 4)
                {
                    var ev = world.Step(AttractInput());
                    HandleEvents(ev);
                    CaptureSnapshot();
                    acc -= TAC.TICK;
                    asteps++;
                    if (world.dead || world.clearedFlag || world.timedOutFlag)
                    {
                        attractResetT = 1.6f; // linger on the death burst, then next wave
                        break;
                    }
                }
            }
            if (world != null)
            {
                UpdateViews();
                AttractCamera();
            }
            return;
        }
        if (mode == Mode.Brief && world != null)
        {
            ReadBriefSwipe();
            // slow orbital vista over the arena from behind the spawn point
            briefCamT += Time.deltaTime;
            float byaw = (float)(world.yawQ * System.Math.PI * 2.0 / 65536.0) + Mathf.Sin(briefCamT * 0.12f) * 0.35f;
            var spawn = new Vector3((float)world.px, (float)world.py, (float)world.pz);
            var bdir = new Vector3(Mathf.Sin(byaw), 0, Mathf.Cos(byaw));
            var beye = spawn - bdir * 10f + Vector3.up * 6.5f;
            float bMinY = (float)world.GroundY(beye.x, beye.z, 1000.0, 0.2) + 0.5f;
            if (beye.y < bMinY) beye.y = bMinY;
            cam.transform.position = Vector3.Lerp(cam.transform.position, beye, 0.06f);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation,
                Quaternion.LookRotation((spawn + bdir * 14f + Vector3.up * 1f) - beye, Vector3.up), 0.06f);
            cam.fieldOfView = Screen.height > Screen.width ? 78f : 58f;
        }
        if (mode != Mode.Play || world == null) return;
        ReadTouches();
        // First frame of the run: its dt covers the heavy start work, not real
        // play time — starting the sim from it would backlog the accumulator and
        // stall input. Consume the spike once and begin cleanly next frame.
        if (primePlayFrame) { primePlayFrame = false; UpdateViews(); UpdateCamera(); UpdateHud(); return; }
        acc += Time.deltaTime;
        int steps = 0;
        while (acc >= TAC.TICK && steps < 5)
        {
            var inp = CollectInput();
            recs.Add(inp);
            var ev = world.Step(inp);
            HandleEvents(ev);
            CaptureSnapshot();   // record the post-tick pose for snapshot interpolation
            acc -= TAC.TICK;
            steps++;
            if (world.clearedFlag || world.dead || world.timedOutFlag)
            {
                StartCoroutine(EndRunCo());
                return;
            }
        }
        // Render EVERY frame — not just on frames where a sim step ran. The sim is
        // a fixed 50Hz; a display running faster used to freeze the whole scene on
        // the last tick's pose and jump at the next tick (the "カクカク" judder).
        // Smoothing is now frame-rate-independent (see Sm), so at any refresh rate
        // the views ease continuously toward the latest sim pose.
        UpdateViews();
        UpdateCamera();
        UpdateHud();
    }

    string ClearKey() { return "tac_clear_" + (stageId ?? "training"); }

    IEnumerator EndRunCo()
    {
        mode = Mode.Result; // freeze input immediately; views keep their last frame
        audioKit.StopMusic(); // BGM cuts the moment the run ends (user request);
                              // retry restarts it via BeginPlay → StartMusic
        UpdateViews();
        yield return new WaitForSeconds(world.dead ? 1.1f : 0.5f);
        EndRun();
    }

    void EndRun()
    {
        string kind = world.clearedFlag ? "cleared" : (world.timedOutFlag ? "timeout" : "dead");
        if (world.clearedFlag)
        {
            int best = PlayerPrefs.GetInt(ClearKey(), 0);
            if (best == 0 || world.tick < best) PlayerPrefs.SetInt(ClearKey(), world.tick);
            PlayerPrefs.Save();
        }
        audioKit.Play(world.clearedFlag ? "clear" : "dead");
        // Game over (death or timeout) shows an interstitial; clears never do.
        // The 45s frequency cap lives inside Ads.ShowInterstitial().
        if (!world.clearedFlag) Ads.ShowInterstitial();
        mode = Mode.Result;
        resultRoot.gameObject.SetActive(true);
        ClearChildren(resultRoot);
        var title = TacUi.Label(resultRoot, TacUi.Track(TacLoc.T(kind)), 30, world.clearedFlag ? TacUi.Teal : TacUi.Alert, TextAnchor.MiddleCenter, new Vector2(0, 0.68f), new Vector2(1, 0.80f), Vector2.zero, Vector2.zero, true);
        StartCoroutine(StampIn((RectTransform)title.transform, 0.05f));
        TacUi.Divider(resultRoot, 0.66f);
        if (world.clearedFlag)
        {
            int best = PlayerPrefs.GetInt(ClearKey(), 0);
            string bestNote = (best == world.tick) ? "   ★ BEST" : (best > 0 ? "   (BEST " + FmtTime(best) + ")" : "");
            var timeLbl = TacUi.Label(resultRoot, TacLoc.T("time") + "  " + FmtTime(world.tick) + bestNote, 16, TacUi.Fg, TextAnchor.MiddleCenter, new Vector2(0, 0.59f), new Vector2(1, 0.65f), Vector2.zero, Vector2.zero);
            StartCoroutine(FadeRiseIn((RectTransform)timeLbl.transform, 0.22f, 0.4f, 10f));
        }
        Text tmp;
        var retryB = TacUi.Btn(resultRoot, TacLoc.T("retry"), 14, new Color(1, 1, 1, 0.9f), new Vector2(0.08f, 0.14f), new Vector2(0.48f, 0.22f), Vector2.zero, Vector2.zero, () =>
        {
            resultRoot.gameObject.SetActive(false);
            BeginPlay();
        }, out tmp);
        StartCoroutine(FadeRiseIn((RectTransform)retryB.transform, 0.3f, 0.4f, 12f));
        var backB = TacUi.Btn(resultRoot, TacLoc.T("back"), 14, TacUi.Dim, new Vector2(0.52f, 0.14f), new Vector2(0.92f, 0.22f), Vector2.zero, Vector2.zero, ShowList, out tmp);
        StartCoroutine(FadeRiseIn((RectTransform)backB.transform, 0.38f, 0.4f, 12f));

        // LIKE / MEH rating — only for real published stages (not training, not
        // a deep-link draft preview). Each button pairs a glyph with a text
        // label; the heart stays an empty outline (♡) until the player votes.
        if (stageId != null && !previewDraft)
        {
            TacUi.Label(resultRoot, TacLoc.T("rateStage"), 12, TacUi.Dim, TextAnchor.MiddleCenter, new Vector2(0, 0.49f), new Vector2(1, 0.535f), Vector2.zero, Vector2.zero);
            Text goodT, badT;
            var goodBtn = TacUi.Btn(resultRoot, "", 22, TacUi.Line, new Vector2(0.26f, 0.40f), new Vector2(0.48f, 0.475f), Vector2.zero, Vector2.zero, null, out goodT);
            var goodLbl = TacUi.Label(resultRoot, TacLoc.T("voteGood"), 10, TacUi.Dim, TextAnchor.MiddleCenter, new Vector2(0.26f, 0.365f), new Vector2(0.48f, 0.40f), Vector2.zero, Vector2.zero);
            var badBtn = TacUi.Btn(resultRoot, "", 22, TacUi.Line, new Vector2(0.52f, 0.40f), new Vector2(0.74f, 0.475f), Vector2.zero, Vector2.zero, null, out badT);
            var badLbl = TacUi.Label(resultRoot, TacLoc.T("voteBad"), 10, TacUi.Dim, TextAnchor.MiddleCenter, new Vector2(0.52f, 0.365f), new Vector2(0.74f, 0.40f), Vector2.zero, Vector2.zero);
            var rateLbl = TacUi.Label(resultRoot, "", 11, TacUi.Teal, TextAnchor.MiddleCenter, new Vector2(0, 0.325f), new Vector2(1, 0.36f), Vector2.zero, Vector2.zero);
            // reflect current selection: filled heart + teal for LIKE, red ✕ for MEH,
            // and an empty outline heart (unfilled) whenever LIKE isn't the choice.
            System.Action<int> paint = (vote) =>
            {
                goodT.text = vote == 1 ? "♥" : "♡";
                goodT.color = vote == 1 ? TacUi.Teal : TacUi.Dim;
                if (goodLbl != null) goodLbl.color = vote == 1 ? TacUi.Teal : TacUi.Dim;
                // U+00D7 — the fancy ✕ (U+2715) is missing from iOS Helvetica
                // and rendered as a blank button on device
                badT.text = "×";
                badT.color = vote == 2 ? TacUi.Alert : TacUi.Dim;
                if (badLbl != null) badLbl.color = vote == 2 ? TacUi.Alert : TacUi.Dim;
            };
            System.Action<bool> cast = (g) =>
            {
                StartCoroutine(TacNet.Vote(stageId, g, () => { if (rateLbl != null) rateLbl.text = TacLoc.T("voteThanks"); }));
                PlayerPrefs.SetInt("tac_voted_" + stageId, g ? 1 : 2);
                paint(g ? 1 : 2);
            };
            goodBtn.onClick.AddListener(() => cast(true));
            badBtn.onClick.AddListener(() => cast(false));
            paint(PlayerPrefs.GetInt("tac_voted_" + stageId, 0));
        }

        // Deep-link draft preview never touches the server's play stats, ghost
        // or leaderboard — the stage isn't published, and its verified clear
        // must come from the creator's own browser run, not an app preview.
        if (stageId != null && !previewDraft)
        {
            StartCoroutine(TacNet.ReportPlay(stageId, world.clearedFlag, world.tick * 20));
            if (world.clearedFlag)
            {
                string data = TacReplay.EncodeTrace(recs);
                var note = TacUi.Label(resultRoot, "", 12, TacUi.Teal, TextAnchor.MiddleCenter, new Vector2(0, 0.27f), new Vector2(1, 0.315f), Vector2.zero, Vector2.zero);
                StartCoroutine(TacNet.SubmitScore(stageId, recs.Count, data, (okSent, firstClear) =>
                {
                    if (!okSent) return;
                    if (firstClear)
                    {
                        // World-first clear = this run just published the stage. Celebrate.
                        Haptic.Success();
                        var banner = TacUi.Label(resultRoot, TacLoc.T("firstClear"), 22, TacUi.Warn, TextAnchor.MiddleCenter,
                            new Vector2(0, 0.80f), new Vector2(1, 0.90f), Vector2.zero, Vector2.zero, true);
                        TacUi.Label(resultRoot, TacLoc.T("firstClearSub"), 12, TacUi.Fg, TextAnchor.MiddleCenter,
                            new Vector2(0, 0.755f), new Vector2(1, 0.795f), Vector2.zero, Vector2.zero);
                        if (note != null) note.text = "";
                    }
                    else if (note != null) note.text = TacLoc.T("scoreSent");
                }));
            }
        }
        else if (previewDraft && world.clearedFlag)
        {
            TacUi.Label(resultRoot, TacLoc.T("preview_clear"), 12, TacUi.Warn, TextAnchor.MiddleCenter,
                new Vector2(0, 0.27f), new Vector2(1, 0.315f), Vector2.zero, Vector2.zero);
        }
    }

    void ShowPause()
    {
        mode = Mode.Paused;
        pauseRoot.gameObject.SetActive(true);
        ClearChildren(pauseRoot);
        TacUi.Label(pauseRoot, TacUi.Track(TacLoc.T("paused")), 26, TacUi.Fg, TextAnchor.MiddleCenter, new Vector2(0, 0.62f), new Vector2(1, 0.74f), Vector2.zero, Vector2.zero);
        TacUi.Divider(pauseRoot, 0.60f);
        Text tmp;
        // full-width stacked buttons: easy thumb targets in portrait
        TacUi.Btn(pauseRoot, TacLoc.T("resume"), 14, new Color(1, 1, 1, 0.9f), new Vector2(0.15f, 0.42f), new Vector2(0.85f, 0.50f), Vector2.zero, Vector2.zero, () =>
        {
            pauseRoot.gameObject.SetActive(false);
            mode = Mode.Play;
        }, out tmp);
        TacUi.Btn(pauseRoot, TacLoc.T("back"), 14, TacUi.Dim, new Vector2(0.15f, 0.30f), new Vector2(0.85f, 0.38f), Vector2.zero, Vector2.zero, ShowList, out tmp);
    }

    static string FmtTime(int ticks)
    {
        int ms = ticks * 20;
        int s = ms / 1000;
        return (s / 60) + ":" + (s % 60).ToString("00") + "." + ((ms % 1000) / 10).ToString("00");
    }

    // ------------------------------------------------------------------ views --
    // Frame-rate-independent exponential smoothing. A fixed per-frame Lerp would
    // chase the target ~twice as fast at 120Hz as at 60Hz — and only advanced on
    // sim-step frames, so the scene stuttered. Here the alpha is derived from the
    // real frame time so the ease rate is identical at any refresh rate, and it's
    // fast enough (~40ms half-life) that the view tracks the 50Hz sim tightly.
    static float SmAlpha(float halfLife)
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return 1f;
        return 1f - Mathf.Pow(2f, -dt / halfLife);
    }

    // ---- snapshot interpolation helpers ---------------------------------------
    // Capture the current sim pose. Called once per sim tick (after Step). The
    // previous "cur" slides into "prev" so we always hold the pair bracketing the
    // in-between render frames.
    void CaptureSnapshot()
    {
        var w = world;
        snapPrevP = snapCurP;
        snapCurP = new MoBody {
            pos = new Vector3((float)w.px, (float)w.py, (float)w.pz),
            yawDeg = (float)(w.faceQ * 360.0 / 65536.0), valid = true };

        snapPrevPilot = snapCurPilot;
        snapCurPilot = w.pilot != null
            ? new MoBody { pos = new Vector3((float)w.pilot.x, (float)w.pilot.y, (float)w.pilot.z),
                           yawDeg = (float)(w.pilot.yawQ * 360.0 / 65536.0), valid = true }
            : default(MoBody);

        int n = w.enemies.Count;
        if (snapCurE.Length != n) { snapCurE = new MoBody[n]; snapPrevE = new MoBody[n]; snapReady = false; }
        var tmp = snapPrevE; snapPrevE = snapCurE; snapCurE = tmp;   // reuse arrays: cur→prev
        for (int i = 0; i < n; i++)
        {
            var en = w.enemies[i];
            snapCurE[i] = new MoBody {
                pos = new Vector3((float)en.x, (float)en.y, (float)en.z),
                yawDeg = (float)(en.yawQ * 360.0 / 65536.0), valid = en.alive };
        }

        // snapPrevP.valid becomes true on the 2nd capture — that's when a real
        // prev→cur pair exists and interpolation can begin.
        if (snapPrevP.valid) snapReady = true;
    }

    // fraction of the way through the current sim tick (0..1), for the lerp
    float SnapAlpha()
    {
        if (!snapReady) return 1f;
        return Mathf.Clamp01((float)(acc / TAC.TICK));
    }

    static Vector3 LerpPos(MoBody a, MoBody b, float t) { return Vector3.LerpUnclamped(a.pos, b.pos, t); }
    static float LerpYaw(float a, float b, float t) { return a + Mathf.DeltaAngle(a, b) * t; } // shortest arc

    // Slide a view's legL/legR feet fore/aft (local +Z) in counter-phase to fake
    // a stiff Minecraft walk. amp 0 = feet level. Render-only; matches tac-client.js.
    static void SwingLegs(GameObject view, float phase, float amp)
    {
        if (view == null) return;
        var lt = view.transform.Find("legL");
        var rt = view.transform.Find("legR");
        if (lt == null || rt == null) return;
        float sw = Mathf.Sin(phase) * amp;
        var lp = lt.localPosition; lp.z = sw; lt.localPosition = lp;
        var rp = rt.localPosition; rp.z = -sw; rt.localPosition = rp;
    }

    // advance a walk phase/amplitude from a view's ground speed between frames
    static void StepGait(Vector3 cur, ref Vector3 prev, ref bool init, ref float phase, ref float amp, float y)
    {
        if (!init) { prev = cur; init = true; }
        float dx = cur.x - prev.x, dz = cur.z - prev.z; prev = cur;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float spd = Mathf.Sqrt(dx * dx + dz * dz) / dt;
        bool moving = spd > 0.4f && y < 0.2f;
        phase += spd * 2.2f * dt;
        // target foot travel ~0.22 m fore/aft, matching tac-client.js drawLegs amp
        amp += ((moving ? 0.22f : 0f) - amp) * Mathf.Min(1f, dt * 12f);
        if (!moving && amp < 0.02f) phase = 0f;
    }

    void UpdateViews()
    {
        var w = world;
        float a = SnapAlpha();
        // player — snapshot-interpolated between the two bracketing sim ticks for
        // perfectly uniform motion (no exponential-chase strafe judder)
        Vector3 pRender; float pYaw;
        if (snapReady)
        {
            pRender = LerpPos(snapPrevP, snapCurP, a);
            pYaw = LerpYaw(snapPrevP.yawDeg, snapCurP.yawDeg, a);
        }
        else
        {
            pRender = new Vector3((float)w.px, (float)w.py, (float)w.pz);
            pYaw = (float)(w.faceQ * 360.0 / 65536.0);
        }
        playerView.transform.position = pRender;
        playerView.transform.rotation = Quaternion.Euler(0, pYaw, 0);
        playerView.transform.localScale = new Vector3(1, w.crouched ? 0.55f : 1f, 1);
        playerView.SetActive(w.pilot == null && !w.dead);
        StepGait(pRender, ref playerLegPrevPos, ref playerLegInit, ref playerLegPhase, ref playerLegAmp, pRender.y);
        SwingLegs(playerView, playerLegPhase, playerLegAmp);

        // muzzle flash: a ~2-frame blink per shot, rolled randomly so bursts shimmer
        if (muzzleFlash != null)
        {
            muzzleT -= Time.deltaTime;
            bool mOn = muzzleT > 0f && playerView.activeSelf && !w.scoped;
            if (muzzleFlash.activeSelf != mOn) muzzleFlash.SetActive(mOn);
            if (mOn) muzzleFlash.transform.localRotation = Quaternion.Euler(0, 0, (Time.time * 991f) % 360f);
        }

        // enemies
        for (int i = 0; i < w.enemies.Count; i++)
        {
            var en = w.enemies[i];
            var v = enemyViews[i];
            if (!en.alive)
            {
                if (v.activeSelf) v.SetActive(false);
                continue;
            }
            // interpolate between the bracketing ticks, but only when the prev
            // snapshot for THIS enemy is valid (alive last tick too) — otherwise a
            // just-spawned/teleported enemy would streak in from its old slot
            if (snapReady && i < snapCurE.Length && i < snapPrevE.Length
                && snapCurE[i].valid && snapPrevE[i].valid)
            {
                v.transform.position = LerpPos(snapPrevE[i], snapCurE[i], a);
                v.transform.rotation = Quaternion.Euler(0, LerpYaw(snapPrevE[i].yawDeg, snapCurE[i].yawDeg, a), 0);
            }
            else
            {
                v.transform.position = new Vector3((float)en.x, (float)en.y, (float)en.z);
                v.transform.rotation = Quaternion.Euler(0, (float)(en.yawQ * 360.0 / 65536.0), 0);
            }
            v.transform.localScale = new Vector3(1, en.crouched ? 0.5f : 1f, 1);
            // stiff-leg walk cycle (render-only). gatling(1)/drone(3) have no legs;
            // SwingLegs no-ops on them (no legL/legR children).
            while (enemyLegPhase.Count <= i) { enemyLegPhase.Add(0f); enemyLegAmp.Add(0f); enemyLegPrevPos.Add(v.transform.position); }
            float legPh = enemyLegPhase[i], legAm = enemyLegAmp[i]; Vector3 legPrev = enemyLegPrevPos[i]; bool legInit = true;
            StepGait(v.transform.position, ref legPrev, ref legInit, ref legPh, ref legAm, v.transform.position.y);
            enemyLegPhase[i] = legPh; enemyLegAmp[i] = legAm; enemyLegPrevPos[i] = legPrev;
            SwingLegs(v, legPh, legAm);
            if (en.type == 6)
            {
                var plate = v.transform.Find("plate");
                if (plate != null)
                {
                    bool up = w.ShieldUp(en);
                    var wantP = up ? new Vector3(0, 0.95f, 0.55f) : new Vector3(0.5f, en.shieldStagT > 0 ? 0.3f : 0.5f, 0.45f);
                    var wantR = up ? Quaternion.identity : Quaternion.Euler(0, 28f, 0);
                    plate.localPosition = Vector3.Lerp(plate.localPosition, wantP, Time.deltaTime * 8f);
                    plate.localRotation = Quaternion.Slerp(plate.localRotation, wantR, Time.deltaTime * 8f);
                    var wantS = up ? new Vector3(2.0f, 1.9f, 0.1f) : new Vector3(2.0f, 0.9f, 0.1f);
                    plate.localScale = Vector3.Lerp(plate.localScale, wantS, Time.deltaTime * 8f);
                }
            }
            if (en.type == 3)
            {
                var rt = v.transform.Find("rotors");
                if (rt != null) rt.localRotation = Quaternion.Euler(0, Time.time * 1400f % 360f, 0);
            }
            else if (en.type == 1)
            {
                var gs = v.transform.Find("gatspin");
                if (gs != null) gs.localRotation = Quaternion.Euler(0, 0, Time.time * (en.state == 2 ? 900f : 140f) % 360f);
            }
        }

        // bullets
        int bp = 0;
        for (int i = 0; i < w.bullets.Count && bp < bulletPool.Count; i++)
        {
            var bu = w.bullets[i];
            if (!bu.alive) continue;
            var v = bulletPool[bp++];
            v.SetActive(true);
            v.transform.position = new Vector3((float)bu.x, (float)bu.y, (float)bu.z);
            v.transform.rotation = Quaternion.LookRotation(new Vector3((float)bu.vx, (float)bu.vy, (float)bu.vz));
            v.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(bu.fromPlayer ? new Color(1f, 0.93f, 0.5f) : new Color(1f, 0.42f, 0.3f));
        }
        for (; bp < bulletPool.Count; bp++) bulletPool[bp].SetActive(false);

        // grenades
        int gp = 0;
        for (int i = 0; i < w.grenades.Count && gp < grenadePool.Count; i++)
        {
            var g = w.grenades[i];
            if (!g.alive) continue;
            var v = grenadePool[gp++];
            v.SetActive(true);
            v.transform.position = new Vector3((float)g.x, (float)g.y, (float)g.z);
        }
        for (; gp < grenadePool.Count; gp++) grenadePool[gp].SetActive(false);

        // bombs: lob arc then armed blinker + danger ring
        foreach (var bo in w.bombs)
        {
            if (!bombViews.ContainsKey(bo))
            {
                var root = new GameObject("bomb");
                root.transform.SetParent(worldRoot, false);
                TacRenderKit.Sphere(root.transform, Vector3.zero, 0.42f, new Color(0.15f, 0.12f, 0.1f));
                var blink = TacRenderKit.Sphere(root.transform, new Vector3(0, 0.32f, 0), 0.16f, new Color(1f, 0.15f, 0.1f));
                blink.name = "blink";
                var ring = TacRenderKit.Cyl(root.transform, new Vector3(0, 0.06f, 0), (float)TAC.BOMB_BLAST_R * 2f, 0.03f, new Color(1f, 0.25f, 0.15f));
                ring.GetComponent<Renderer>().sharedMaterial = TacRenderKit.TransMat(new Color(1f, 0.25f, 0.15f, 0.3f));
                ring.name = "ring";
                bombViews[bo] = root;
            }
            var view = bombViews[bo];
            if (bo.state == 2) { view.SetActive(false); continue; }
            view.SetActive(true);
            if (bo.state == 0)
            {
                double bpf = (double)bo.t / TAC.BOMB_FLIGHT;
                double bx = bo.sx + (bo.x - bo.sx) * bpf;
                double bz = bo.sz + (bo.z - bo.sz) * bpf;
                double by = bo.sy + (bo.y - bo.sy) * bpf + 5.0 * 4.0 * bpf * (1.0 - bpf);
                view.transform.position = new Vector3((float)bx, (float)by, (float)bz);
                view.transform.Find("ring").gameObject.SetActive(false);
            }
            else
            {
                view.transform.position = new Vector3((float)bo.x, (float)bo.y + 0.2f, (float)bo.z);
                view.transform.Find("ring").gameObject.SetActive(true);
                int period = bo.fuse > 125 ? 24 : (bo.fuse > 50 ? 12 : 5);
                view.transform.Find("blink").gameObject.SetActive((world.tick % period) < (period >> 1));
            }
        }

        // veils / switches / mines / barrels
        for (int i = 0; i < w.switches.Count; i++)
        {
            bool alive = w.switches[i].alive;
            if (veilViews[i].activeSelf != alive) veilViews[i].SetActive(alive);
            if (switchViews[i].activeSelf != alive) switchViews[i].SetActive(alive);
            if (alive)
            {
                var vr = veilViews[i].GetComponent<Renderer>();
                var vc = vr.material.color;
                vc.a = 0.12f + 0.05f * Mathf.Sin(Time.time * 1.7f + i);
                vr.material.color = vc;
                veilViews[i].transform.localRotation = Quaternion.Euler(0, Time.time * 9f % 360f, 0);
            }
        }
        for (int i = 0; i < waterViews.Count; i++)
        {
            var wc2 = waterViews[i].material.color;
            wc2.a = 0.55f + 0.08f * Mathf.Sin(Time.time * 2.2f + i);
            waterViews[i].material.color = wc2;
        }
        for (int i = 0; i < w.intels.Count; i++)
        {
            var it = w.intels[i];
            if (!it.alive) { if (intelViews[i].activeSelf) intelViews[i].SetActive(false); continue; }
            var core2 = intelViews[i].transform.Find("core");
            if (core2 != null)
            {
                core2.localRotation = Quaternion.Euler(0, Time.time * 90f, 0);
                var cp2 = core2.localPosition; cp2.y = 0.75f + 0.12f * Mathf.Sin(Time.time * 2.4f + i * 2f);
                core2.localPosition = cp2;
            }
        }
        for (int i = 0; i < searchPivots.Count && i < w.lights.Count; i++)
        {
            var li = w.lights[i];
            searchPivots[i].localRotation = Quaternion.Euler(0f, (li.angQ & 65535) * 360f / 65536f, 0f);
        }
        if (exitPad != null && w.intelLeft <= 0)
        {
            var er = exitPad.GetComponent<Renderer>();
            var ec = er.material.color;
            ec.a = 0.4f + 0.25f * Mathf.Sin(Time.time * 5f);
            er.material.color = ec;
        }
        for (int i = 0; i < w.medkits.Count; i++)
        {
            var mk = w.medkits[i];
            if (!mk.alive) { if (medkitViews[i].activeSelf) { medkitViews[i].SetActive(false); Sfx("heard", 0.7f); } continue; }
            var mp = medkitViews[i].transform.position;
            mp.y = (float)mk.y + 0.35f + 0.1f * Mathf.Sin(Time.time * 3f + i);
            medkitViews[i].transform.position = mp;
            medkitViews[i].transform.rotation = Quaternion.Euler(0, Time.time * 60f, 0);
        }
        for (int i = 0; i < w.mines.Count; i++)
        {
            var mi = w.mines[i];
            if (!mi.alive) { mineViews[i].SetActive(false); continue; }
            var top = mineViews[i].transform.GetChild(0);
            top.gameObject.SetActive(mi.fuse >= 0 ? (world.tick % 6 < 3) : (world.tick % 50 < 25));
        }
        for (int i = 0; i < w.barrels.Count; i++)
        {
            var ba = w.barrels[i];
            if (!ba.alive) { barrelViews[i].SetActive(false); continue; }
            barrelViews[i].transform.position = new Vector3((float)ba.x, (float)ba.y + 0.5f, (float)ba.z);
        }
        // cracked walls / glass that got razed, and auto doors that slid open
        for (int i = 0; i < w.boxes.Count; i++)
        {
            var bx2 = w.boxes[i];
            bool hidden = !bx2.alive || (bx2.kind == 6 && bx2.open); // open door: slid away
            if (boxViews[i].activeSelf == hidden) boxViews[i].SetActive(!hidden);
        }

        // pilot drone — this is the HERO unit, so it gets the same live detail the
        // enemy drones have (spinning rotors) plus player-side flourishes: a teal
        // sensor eye instead of a hostile red one, smoothed motion and a bank/tilt
        // toward its travel direction.
        if (w.pilot != null)
        {
            if (pilotView == null)
            {
                pilotView = TacRenderKit.BuildEnemyView(worldRoot, new TacEnemy { type = 3, h = TAC.DRONE_H });
                foreach (var r in pilotView.GetComponentsInChildren<Renderer>()) r.sharedMaterial = TacRenderKit.Mat(TacRenderKit.PlayerCol);
                // recolor the sensor "eye": red = enemy, teal = yours
                var eye = pilotView.transform.Find("eye");
                if (eye != null) eye.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(TacUi.Teal);
                // NOTE: the glow sphere + halo ring were dropped — the Legacy
                // transparent shader falls back to opaque on iOS device builds, so
                // they rendered as a solid sphere + cylinder stuck to the drone
                // instead of soft translucent flourishes. Ship the clean drone.
                pilotPosInit = false;
            }
            pilotView.SetActive(true);

            var pilotTarget = new Vector3((float)w.pilot.x, (float)w.pilot.y, (float)w.pilot.z);
            if (!pilotPosInit) { pilotView.transform.position = pilotTarget; pilotPrevPos = pilotTarget; pilotPosInit = true; }
            // snapshot-interpolate when a valid prev pilot pose exists, else snap
            var smoothed = (snapReady && snapPrevPilot.valid && snapCurPilot.valid)
                ? LerpPos(snapPrevPilot, snapCurPilot, a)
                : pilotTarget;
            pilotView.transform.position = smoothed;

            // derive velocity render-side (sim keeps no pilot velocity) and bank into it
            var vel = smoothed - pilotPrevPos;
            pilotPrevPos = smoothed;
            float yawDeg = (float)(w.pilot.yawQ * 360.0 / 65536.0);
            var yawRot = Quaternion.Euler(0, yawDeg, 0);
            var localVel = Quaternion.Inverse(yawRot) * vel;
            float bankZ = Mathf.Clamp(-localVel.x * 90f, -22f, 22f);   // roll into turns
            float pitchX = Mathf.Clamp(localVel.z * 90f, -22f, 22f);   // nose down when advancing
            var wantRot = yawRot * Quaternion.Euler(pitchX, 0, bankZ);
            pilotView.transform.rotation = Quaternion.Slerp(pilotView.transform.rotation, wantRot, SmAlpha(0.06f));

            // spin the rotors like the enemy drones do
            var prt = pilotView.transform.Find("rotors");
            if (prt != null) prt.localRotation = Quaternion.Euler(0, Time.time * 1600f % 360f, 0);
            // pulse the sensor eye so the drone feels alive; dim as the battery drains
            float batt = Mathf.Clamp01(w.pilot.battery / (float)TAC.PILOT_BATTERY);
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 3.4f));
            var peye = pilotView.transform.Find("eye");
            if (peye != null) peye.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(new Color(TacUi.Teal.r, TacUi.Teal.g, TacUi.Teal.b, 1f) * Mathf.Lerp(0.5f, 1f, batt * pulse));
        }
        else if (pilotView != null) { pilotView.SetActive(false); pilotPosInit = false; }

        // lock marker
        Vector3? lockPos = null;
        // while piloting, w.lockTarget is the homing-dive target — show the
        // reticle over it too (that's where the drone will strike)
        bool pilotLock = w.pilot != null && w.lockTarget >= 0 && w.lockTarget < w.enemies.Count;
        if (pilotLock)
        {
            var lt = w.enemies[w.lockTarget];
            if (lt.alive) lockPos = new Vector3((float)lt.x, (float)(lt.y + lt.h + 0.5), (float)lt.z);
        }
        else if (w.lockTarget >= 0 && w.pilot == null && !w.scoped)
        {
            if (w.lockKind == 0 && w.lockTarget < w.enemies.Count)
            {
                var lt = w.enemies[w.lockTarget];
                if (lt.alive) lockPos = new Vector3((float)lt.x, (float)(lt.y + lt.h + 0.5), (float)lt.z);
            }
            else if (w.lockKind == 1 && w.lockTarget < w.barrels.Count)
                lockPos = new Vector3((float)w.barrels[w.lockTarget].x, (float)w.barrels[w.lockTarget].y + 1.6f, (float)w.barrels[w.lockTarget].z);
            else if (w.lockKind == 2 && w.lockTarget < w.mines.Count)
                lockPos = new Vector3((float)w.mines[w.lockTarget].x, (float)w.mines[w.lockTarget].y + 1.0f, (float)w.mines[w.lockTarget].z);
            else if (w.lockKind == 4 && w.lockTarget < w.switches.Count)
                lockPos = new Vector3((float)w.switches[w.lockTarget].x, (float)w.switches[w.lockTarget].y + 1.8f, (float)w.switches[w.lockTarget].z);
        }
        if (lockPos.HasValue)
        {
            lockMarker.SetActive(true);
            lockMarker.transform.position = lockPos.Value;
            lockMarker.transform.rotation = Quaternion.Euler(45, Time.time * 120f, 45);
        }
        else lockMarker.SetActive(false);

        // sniper laser: the aiming sniper paints the player
        bool laser = false;
        foreach (var en in w.enemies)
        {
            if (en.alive && en.type == 2 && en.warnT > 0)
            {
                sniperLaser.SetPosition(0, new Vector3((float)en.x, (float)(en.y + en.h - 0.3), (float)en.z));
                sniperLaser.SetPosition(1, new Vector3((float)w.px, (float)w.py + 1.1f, (float)w.pz));
                laser = true;
                break;
            }
        }
        sniperLaser.enabled = laser;

        // grenade trajectory preview while the BOMB button is held
        bool showArc = bombAiming && !bombCancelled && w.grenadeCd == 0 && !w.scoped && w.pilot == null;
        bombArc.enabled = showArc;
        bombLandRing.SetActive(showArc);
        if (showArc)
        {
            double gfx = TacMath.SinQ(w.faceQ), gfz = TacMath.CosQ(w.faceQ);
            double gx = w.px + gfx * 0.5, gy = w.py + TAC.CHEST_H, gz = w.pz + gfz * 0.5;
            double gvx = gfx * TAC.GRENADE_SPEED_H, gvy = TAC.GRENADE_SPEED_V, gvz = gfz * TAC.GRENADE_SPEED_H;
            var pts = new List<Vector3>();
            pts.Add(new Vector3((float)gx, (float)gy, (float)gz));
            double lx = gx, ly = gy, lz = gz;
            for (int gi = 0; gi < 90; gi++)
            {
                gvy -= TAC.GRAVITY * TAC.TICK;
                double nx = gx + gvx * TAC.TICK, ny = gy + gvy * TAC.TICK, nz = gz + gvz * TAC.TICK;
                if (nx < 0 || nx > w.arenaW || nz < 0 || nz > w.arenaD || w.SegBlocked(gx, gy, gz, nx, ny, nz)) { lx = gx; ly = gy; lz = gz; break; }
                double ggy = w.GroundY(nx, nz, gy, TAC.GRENADE_R);
                if (ny <= ggy + 0.05) { lx = nx; ly = ggy + 0.05; lz = nz; break; }
                pts.Add(new Vector3((float)nx, (float)ny, (float)nz));
                gx = nx; gy = ny; gz = nz; lx = nx; ly = ny; lz = nz;
            }
            bombArc.positionCount = pts.Count;
            bombArc.SetPositions(pts.ToArray());
            float lgy = (float)w.GroundY(lx, lz, 1000.0, 0.3) + 0.06f;
            bombLandRing.transform.position = new Vector3((float)lx, lgy, (float)lz);
        }

        // fx decay
        for (int i = fxs.Count - 1; i >= 0; i--)
        {
            var f = fxs[i];
            f.t += Time.deltaTime;
            float pr = f.t / f.dur;
            if (pr >= 1f) { Destroy(f.go); fxs.RemoveAt(i); continue; }
            float s0 = f.go.transform.localScale.x;
            f.go.transform.localScale = Vector3.one * Mathf.Lerp(s0, f.ring ? s0 + 3f * Time.deltaTime : s0, 1f);
            var rend = f.go.GetComponent<Renderer>();
            var c = rend.material.color;
            c.a = 0.7f * (1f - pr);
            rend.material.color = c;
        }
    }

    static readonly Color[] TypeCols = { TacRenderKit.SoldierCol, TacRenderKit.GatlingCol, TacRenderKit.SniperCol, TacRenderKit.DroneCol, TacRenderKit.OperatorCol, TacRenderKit.BomberCol };

    void Burst(Vector3 pos, Color c, int n)
    {
        if (worldRoot == null) return;
        for (int i = 0; i < n; i++)
        {
            float sz = Random.Range(0.14f, 0.3f);
            var go = TacRenderKit.Cube(worldRoot, pos + Random.insideUnitSphere * 0.4f, new Vector3(sz, sz, sz), c);
            debs.Add(new Deb
            {
                go = go,
                vel = Random.insideUnitSphere * 3.2f + Vector3.up * Random.Range(2f, 4.5f),
                rotAxis = Random.onUnitSphere,
                t = 0,
                dur = Random.Range(0.7f, 1.2f)
            });
        }
    }

    void UpdateDebris()
    {
        float dt = Time.deltaTime;
        for (int i = debs.Count - 1; i >= 0; i--)
        {
            var d = debs[i];
            d.t += dt;
            if (d.t >= d.dur) { Destroy(d.go); debs.RemoveAt(i); continue; }
            d.vel.y -= 12f * dt;
            var pos = d.go.transform.position + d.vel * dt;
            if (pos.y < 0.08f) { pos.y = 0.08f; d.vel.y = -d.vel.y * 0.35f; d.vel.x *= 0.7f; d.vel.z *= 0.7f; }
            d.go.transform.position = pos;
            d.go.transform.Rotate(d.rotAxis, 420f * dt, Space.World);
            float pr = d.t / d.dur;
            if (pr > 0.7f) d.go.transform.localScale = d.go.transform.localScale * (1f - dt * 3f);
        }
    }

    // one-frame hitscan tracer (sniper shots)
    void TracerFlash(Vector3 a, Vector3 b)
    {
        var mid = (a + b) / 2f;
        var go = TacRenderKit.Cube(worldRoot, mid, new Vector3(0.08f, 0.08f, (b - a).magnitude), Color.white);
        go.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(Color.white);
        go.transform.rotation = Quaternion.LookRotation(b - a);
        fxs.Add(new Fx { go = go, t = 0, dur = 0.14f });
    }

    class Timed { public GameObject go; public float t, dur; public int kind; public Vector3 baseScale; } // kind 0 flash,1 fire,2 shock,3 smoke
    readonly List<Timed> timed = new List<Timed>();

    GameObject Puff(Vector3 pos, float d, Color c)
    {
        var go = TacRenderKit.Sphere(worldRoot, pos, d, c, true);
        go.GetComponent<Renderer>().material = new Material(TacRenderKit.TransMat(c));
        return go;
    }

    void HitSpark(Vector3 pos)
    {
        if (worldRoot == null) return;
        var flash = Puff(pos, 0.7f, new Color(1f, 1f, 0.6f, 0.9f));
        timed.Add(new Timed { go = flash, t = 0, dur = 0.16f, kind = 0, baseScale = Vector3.one * 0.7f });
        for (int i = 0; i < 6; i++)
        {
            var e = TacRenderKit.Cube(worldRoot, pos, new Vector3(0.09f, 0.09f, 0.09f), new Color(1f, 0.85f, 0.3f));
            e.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(new Color(1f, 0.85f, 0.3f));
            debs.Add(new Deb { go = e, vel = Random.insideUnitSphere * 3.5f, rotAxis = Random.onUnitSphere, t = 0, dur = 0.3f });
        }
    }

    void Boom(Vector3 pos, float r)
    {
        if (worldRoot == null) return;
        // white flash
        timed.Add(new Timed { go = Puff(pos, r * 1.4f, new Color(1f, 1f, 0.9f, 0.95f)), t = 0, dur = 0.12f, kind = 0, baseScale = Vector3.one * r * 1.4f });
        // expanding fireball
        timed.Add(new Timed { go = Puff(pos, r * 1.2f, new Color(1f, 0.55f, 0.12f, 0.9f)), t = 0, dur = 0.42f, kind = 1, baseScale = Vector3.one * r * 2.6f });
        // ground shockwave ring
        var ring = TacRenderKit.Cyl(worldRoot, new Vector3(pos.x, 0.06f, pos.z), r * 0.5f, 0.04f, new Color(1f, 0.8f, 0.4f));
        ring.GetComponent<Renderer>().sharedMaterial = TacRenderKit.TransMat(new Color(1f, 0.8f, 0.4f, 0.6f));
        timed.Add(new Timed { go = ring, t = 0, dur = 0.38f, kind = 2, baseScale = new Vector3(r * 4f, 0.04f, r * 4f) });
        // lingering smoke
        timed.Add(new Timed { go = Puff(pos + Vector3.up * 0.3f, r * 0.8f, new Color(0.3f, 0.3f, 0.3f, 0.5f)), t = 0, dur = 1.1f, kind = 3, baseScale = Vector3.one * r * 2f });
        // hot embers
        for (int i = 0; i < 14; i++)
        {
            float sz = Random.Range(0.1f, 0.22f);
            var e = TacRenderKit.Cube(worldRoot, pos + Vector3.up * 0.2f, new Vector3(sz, sz, sz), new Color(1f, 0.6f, 0.12f));
            e.GetComponent<Renderer>().sharedMaterial = TacRenderKit.UnlitMat(new Color(1f, 0.6f, 0.12f));
            debs.Add(new Deb { go = e, vel = Random.insideUnitSphere * 4.5f + Vector3.up * Random.Range(3f, 6f), rotAxis = Random.onUnitSphere, t = 0, dur = Random.Range(0.5f, 0.9f) });
        }
    }

    void UpdateTimed()
    {
        float dt = Time.deltaTime;
        for (int i = timed.Count - 1; i >= 0; i--)
        {
            var m = timed[i];
            m.t += dt;
            float pr = m.t / m.dur;
            if (pr >= 1f || m.go == null) { if (m.go != null) Destroy(m.go); timed.RemoveAt(i); continue; }
            var rend = m.go.GetComponent<Renderer>();
            var c = rend.material.color;
            if (m.kind == 0) { m.go.transform.localScale = m.baseScale * (0.7f + pr * 1.2f); c.a = (1f - pr) * 0.95f; }
            else if (m.kind == 1) { m.go.transform.localScale = m.baseScale * (0.35f + pr * 0.9f); c.a = (1f - pr) * 0.9f; c.g = 0.4f + 0.4f * (1f - pr); }
            else if (m.kind == 2) { var sc = m.baseScale; sc.x *= (0.2f + pr * 1.4f); sc.z *= (0.2f + pr * 1.4f); m.go.transform.localScale = sc; c.a = (1f - pr) * 0.6f; }
            else { m.go.transform.localScale = m.baseScale * (0.6f + pr * 1.1f); m.go.transform.position += Vector3.up * dt * 1.2f; float g = 0.32f - pr * 0.14f; c = new Color(g, g, g, (1f - pr) * 0.5f); }
            rend.material.color = c;
        }
    }

    // SFX gate: the attract diorama keeps every VISUAL effect but stays silent
    void Sfx(string name, float vol = 1f) { if (!attract) audioKit.Play(name, vol); }

    void HandleEvents(TacEvents ev)
    {
        if (ev.shot) { Sfx("shot"); muzzleT = 0.055f; }
        if (ev.rifleShot && ev.eshots != null)
        {
            // gatling fire is SILENT; only aimed rifle/sniper rounds make a
            // sound, one per tick, from the nearest non-gatling source
            double nearest = 1e9;
            foreach (var es in ev.eshots)
            {
                if (es.gat) continue;
                double dx = es.x - world.px, dz = es.z - world.pz;
                double d2 = dx * dx + dz * dz;
                if (d2 < nearest) nearest = d2;
            }
            if (nearest < 1e8)
            {
                float dist = Mathf.Sqrt((float)nearest);
                Sfx("eshot", Mathf.Clamp01(1f - dist / 45f) * 0.8f);
            }
        }
        if (ev.kills != null)
        {
            Sfx("kill");
            bool first = true;
            foreach (var k in ev.kills)
            {
                Color kc = k.type >= 0 && k.type < TypeCols.Length ? TypeCols[k.type] : TacRenderKit.SoldierCol;
                var kp = new Vector3((float)k.x, (float)k.y + 0.8f, (float)k.z);
                Burst(kp, kc, 14);
                if (!attract && first)
                {
                    ShowHitMark(kp, true); // red kill-confirm ✕ + a light haptic tick
                    Haptic.Tap();
                    Shake(0.10f, 0.18f);
                    first = false;
                }
            }
        }
        if (ev.enemyHit && ev.hits != null)
        {
            Sfx("kill", 0.35f);
            bool first = true;
            foreach (var h in ev.hits)
            {
                var hp2 = new Vector3((float)h.x, (float)h.y, (float)h.z);
                HitSpark(hp2);
                if (!attract && first) { ShowHitMark(hp2, false); first = false; }
            }
        }
        if (ev.playerHit)
        {
            Sfx("hurt");
            if (!attract) { dmgT = 0.55f; Shake(0.22f, 0.3f); }
        }
        if (ev.playerDead)
        {
            Burst(new Vector3((float)world.px, (float)world.py + 0.9f, (float)world.pz), TacRenderKit.PlayerCol, 20);
            if (!attract) Shake(0.35f, 0.5f);
        }
        if (ev.spotted) { if (!attract && Time.time - lastAlertT > 3f) { audioKit.Play("alert", 0.6f); lastAlertT = Time.time; } }
        if (ev.heard) Sfx("heard");
        if (ev.radio) Sfx("heard", 0.35f);
        if (ev.corpseFound) Sfx("alert", 0.3f);
        if (ev.shieldBlock) Sfx("blip", 0.5f);
        if (ev.mineArmed) Sfx("beep");
        if (ev.scopeOn)
        {
            scopeFov = ScopeFovDefault;   // each aim-in starts at default zoom
            // sync the drag angle to the current facing so direct-drag aiming
            // continues seamlessly from where the player was already looking
            // (the sim seeds aimYaw=faceQ on this same tick; from the next tick
            // the reticle follows input.yawQ/pitchQ 1:1)
            camYaw = (float)(world.faceQ * System.Math.PI * 2.0 / 65536.0);
        }
        if (ev.scopeShot) { Sfx("shot"); muzzleT = 0.055f; }
        if (ev.sniperShot) Sfx("eshot");
        if (ev.bomberThrow != null) Sfx("whoosh");
        if (ev.intelPick != null)
        {
            Sfx("clear", 0.5f);
            if (!attract)
            {
                msgText.text = ev.intelPick.left > 0
                    ? "INTEL " + (world.intels.Count - ev.intelPick.left) + "/" + world.intels.Count
                    : "ALL INTEL — GO TO EXIT!";
                msgUntil = Time.time + 2.5f;
            }
        }
        if (ev.switchDown) Sfx("zap");
        if (ev.jamZap != null)
        {
            Sfx("zap");
            var z = TacRenderKit.Sphere(worldRoot, new Vector3((float)ev.jamZap.x, (float)ev.jamZap.y, (float)ev.jamZap.z), 1.2f, new Color(0.7f, 0.55f, 1f, 0.6f), true);
            z.GetComponent<Renderer>().material = new Material(TacRenderKit.TransMat(new Color(0.7f, 0.55f, 1f, 0.6f)));
            fxs.Add(new Fx { go = z, t = 0, dur = 0.35f });
        }
        if (ev.explosions != null)
        {
            Sfx("boom");
            foreach (var ex in ev.explosions) Boom(new Vector3((float)ex.x, (float)ex.y, (float)ex.z), (float)ex.r);
            if (!attract) Shake(0.5f, 0.55f);
        }
        if (ev.wallBreaks != null)
        {
            Sfx("crash");
            if (!attract) Shake(0.28f, 0.4f);
        }
        if (ev.glassBreaks != null)
        {
            Sfx("crash", 0.7f); // brighter/lighter than a wall; no heavy shake
        }
        bool anyAlert = false;
        foreach (var en in world.enemies) if (en.alive && en.state == 2) { anyAlert = true; break; }
        if (!attract) audioKit.SetCombat(anyAlert);
    }

    // ---------------------------------------------------------- juice helpers --
    // Camera shake: envelopes stack by taking the max, decay is quadratic.
    void Shake(float amp, float dur)
    {
        shakeAmp = Mathf.Max(shakeAmp, amp);
        shakeDur = Mathf.Max(shakeDur, dur);
        shakeT = shakeDur;
    }

    // Applied AFTER the camera settles each frame; Perlin-driven so it wobbles
    // instead of jittering. Scoped view keeps a much smaller kick.
    void ApplyShake()
    {
        if (shakeT <= 0f) return;
        shakeT -= Time.deltaTime;
        float k = Mathf.Clamp01(shakeT / Mathf.Max(shakeDur, 1e-4f));
        float amp = shakeAmp * k * k * (world != null && world.scoped ? 0.25f : 1f);
        float t = Time.time * 34f;
        var off = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.6f, t) - 0.5f, 0f) * 2f * amp;
        cam.transform.position += cam.transform.rotation * off;
        cam.transform.rotation *= Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, 7.7f) - 0.5f) * amp * 5f);
        if (shakeT <= 0f) { shakeAmp = 0f; shakeDur = 0f; }
    }

    // Pop the hit-confirm ✕ at the impact point (world→HUD projection).
    void ShowHitMark(Vector3 worldPos, bool kill)
    {
        if (hitMark == null || cam == null || hudRoot == null) return;
        var sp = cam.WorldToScreenPoint(worldPos);
        if (sp.z <= 0f) return; // behind the camera — nothing meaningful to mark
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRoot, sp, null, out local)) return;
        hitMark.anchoredPosition = local;
        hitMarkDur = kill ? 0.42f : 0.22f;
        hitMarkT = hitMarkDur;
        var c = kill ? TacUi.Alert : Color.white;
        foreach (var b in hitMarkBars) b.color = c;
        hitMark.localScale = Vector3.one * (kill ? 1.75f : 1.45f);
        hitMark.gameObject.SetActive(true);
        hitMark.SetAsLastSibling();
    }

    // ------------------------------------------------------------------ camera --
    void UpdateCamera()
    {
        var w = world;
        float a = SnapAlpha();
        // interpolated player position — shared by scoped + third-person so the
        // camera never snaps to raw 50Hz steps (the horizontal judder source)
        Vector3 pInterp = snapReady ? LerpPos(snapPrevP, snapCurP, a)
                                    : new Vector3((float)w.px, (float)w.py, (float)w.pz);
        if (w.scoped)
        {
            float yaw = (float)(w.aimYawQ * System.Math.PI * 2.0 / 65536.0);
            float pitch = (float)(w.aimPitchQ * System.Math.PI * 2.0 / 65536.0);
            float pivY = pInterp.y + (float)(w.crouched ? 1.05 : 1.5);
            var fwd = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            cam.transform.position = new Vector3(pInterp.x, pivY, pInterp.z);
            cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            cam.fieldOfView = scopeFov;
            ApplyShake();
            return;
        }
        // Portrait's narrow horizontal FOV would blind the flanks — widen the
        // vertical FOV so the horizontal one lands near the landscape feel.
        cam.fieldOfView = Screen.height > Screen.width ? 88f : 62f;
        Vector3 pivot;
        float dist;
        if (w.pilot != null)
        {
            // pilot pivot from the interpolated pilot pose when available
            Vector3 pil = (snapReady && snapPrevPilot.valid && snapCurPilot.valid)
                ? LerpPos(snapPrevPilot, snapCurPilot, a)
                : new Vector3((float)w.pilot.x, (float)w.pilot.y, (float)w.pilot.z);
            pivot = pil + new Vector3(0, 0.6f, 0);
            dist = 6.5f;
        }
        else
        {
            pivot = pInterp + new Vector3(0, 1.6f, 0);
            dist = 5.2f;
        }
        var dir = new Vector3(Mathf.Sin(camYaw) * Mathf.Cos(camPitch), Mathf.Sin(camPitch), Mathf.Cos(camYaw) * Mathf.Cos(camPitch));
        // camera collision: cast pivot → desired eye against the solid boxes and stop
        // short of the first wall/ceiling, so indoor stages keep the player in view
        var dEye = pivot - dir * dist;
        double camT = 2.0;
        for (int cbi = 0; cbi < w.boxes.Count; cbi++)
        {
            var cb = w.boxes[cbi];
            if (!cb.alive) continue;
            double t = TacWorld.SegBoxT(pivot.x, pivot.y, pivot.z, dEye.x, dEye.y, dEye.z, cb);
            if (t < camT) camT = t;
        }
        float wantDist = camT <= 1.0 ? Mathf.Max(0.5f, dist * (float)camT - 0.35f) : dist;
        if (wantDist < camDistS) camDistS = wantDist;
        else camDistS += (wantDist - camDistS) * Mathf.Min(1f, Time.deltaTime * 4f);
        var eye = pivot - dir * camDistS;
        // floor clamp referenced to the PLAYER's height, not the sky: under a roof,
        // refY=1000 made the roof count as "ground" and warped the camera on top of it
        float minY = (float)world.GroundY(eye.x, eye.z, pivot.y, 0.2) + 0.3f;
        if (eye.y < minY) eye.y = minY;
        cam.transform.position = Vector3.Lerp(cam.transform.position, eye, SmAlpha(0.05f));
        cam.transform.rotation = Quaternion.LookRotation(pivot + new Vector3(0, 0.0f, 0) - cam.transform.position, Vector3.up);
        ApplyShake();
    }

    // ------------------------------------------------------------------ HUD --
    // build/rebuild the segmented HP bar for a given max (segment count changes
    // only between stages, so this runs rarely)
    void BuildHpBar(int max)
    {
        hpSegMax = max;
        for (int i = hpBarRoot.childCount - 1; i >= 0; i--) Destroy(hpBarRoot.GetChild(i).gameObject);
        hpSegs.Clear();
        if (max <= 0) return;
        float wAll = 178f, gap = 4f;
        float sw = (wAll - gap * (max - 1)) / max;
        for (int i = 0; i < max; i++)
        {
            float x = i * (sw + gap);
            var bgR = TacUi.Rect("seg" + i, hpBarRoot, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 0), new Vector2(x + sw, 0));
            var bg = bgR.gameObject.AddComponent<Image>();
            bg.sprite = TacUi.RoundFill();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.13f);
            bg.raycastTarget = false;
            var fR = TacUi.Rect("fill", bgR, Vector2.zero, Vector2.one, new Vector2(1.5f, 1.5f), new Vector2(-1.5f, -1.5f));
            var f = fR.gameObject.AddComponent<Image>();
            f.sprite = TacUi.RoundFill();
            f.type = Image.Type.Sliced;
            f.raycastTarget = false;
            hpSegs.Add(f);
        }
    }

    void UpdateHud()
    {
        var w = world;
        // HP: segmented bar, teal → amber → red as it drains; low HP pulses the
        // red vignette (a modern-shooter "hurt" frame instead of heart glyphs)
        if (hpSegMax != w.maxHp) BuildHpBar(w.maxHp);
        float hpFrac = w.maxHp > 0 ? (float)w.hp / w.maxHp : 1f;
        var hpc = hpFrac <= 0.34f ? TacUi.Alert : (hpFrac <= 0.6f ? TacUi.Warn : TacUi.Teal);
        if (hpFrac <= 0.34f && w.hp > 0)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);
            hpc = new Color(hpc.r * pulse + (1f - pulse), hpc.g * pulse, hpc.b * pulse);
        }
        for (int i = 0; i < hpSegs.Count; i++)
        {
            bool on = i < w.hp;
            hpSegs[i].enabled = on;
            if (on) hpSegs[i].color = hpc;
        }
        if (lowHpVig != null)
        {
            float want = (w.hp > 0 && hpFrac <= 0.34f) ? 0.28f + 0.12f * Mathf.Sin(Time.time * 5.2f) : 0f;
            var lc = lowHpVig.color;
            lc.a = Mathf.MoveTowards(lc.a, want, Time.deltaTime * 1.6f);
            lowHpVig.color = lc;
        }
        string objLine = TacLoc.T("hostiles") + " " + w.enemiesLeft;
        if (w.goalType == 1)
        {
            objLine = w.intelLeft > 0
                ? "INTEL " + (w.intels.Count - w.intelLeft) + "/" + w.intels.Count
                : "▶ EXIT!";
        }
        foesText.text = objLine + (w.droneUses > 0 ? "   " + TacLoc.T("drone") + " x" + w.droneUses : "");
        timeText.text = FmtTime(w.tick);
        var gc = TacUi.Warn;
        grenIcon.color = w.grenadeCd > 0 ? new Color(gc.r, gc.g, gc.b, 0.25f) : gc;
        var scc = TacUi.ScopeCyan;
        bool scDry = w.scopeShots <= 0;
        scopeIcon.color = scDry ? new Color(scc.r, scc.g, scc.b, 0.18f) : (w.scoped ? Color.white : scc);
        if (scopeCountText != null) scopeCountText.text = w.scopeShots + "/" + TAC.SCOPE_MAX;
        var dc = TacUi.DroneGreen;
        droneIcon.color = w.droneUses > 0 || w.pilot != null ? dc : new Color(dc.r, dc.g, dc.b, 0.25f);
        // Manual-trigger states swap the thumb corner: FIRE replaces JUMP/BOMB
        // (which the sim ignores while scoped or piloting); DRONE hides while
        // scoped and SCOPE hides while piloting for the same reason.
        bool manualFire = w.scoped || w.pilot != null;
        if (fireRt != null && fireRt.gameObject.activeSelf != manualFire) fireRt.gameObject.SetActive(manualFire);
        if (jumpRt != null && jumpRt.gameObject.activeSelf == manualFire) jumpRt.gameObject.SetActive(!manualFire);
        if (bombRt != null && bombRt.gameObject.activeSelf == manualFire) bombRt.gameObject.SetActive(!manualFire);
        if (droneRt != null && droneRt.gameObject.activeSelf == w.scoped) droneRt.gameObject.SetActive(!w.scoped);
        if (scopeRt != null && scopeRt.gameObject.activeSelf == (w.pilot != null)) scopeRt.gameObject.SetActive(w.pilot == null);
        if (Time.time > msgUntil) msgText.text = "";
        if (scopeCross != null && scopeCross.gameObject.activeSelf != w.scoped) scopeCross.gameObject.SetActive(w.scoped);
        if ((Time.frameCount % 6) == 0) RepaintMinimap();
    }

    void BuildMinimapBase()
    {
        int W = 128, H = 128;
        minimapTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        minimapTex.filterMode = FilterMode.Point;
        minimapBase = new Color32[W * H];
        var ground = new Color32(52, 58, 66, 235);
        var boxc = new Color32(90, 98, 110, 255);
        var pitc = new Color32(22, 24, 28, 255);
        var crackc = new Color32(110, 98, 85, 255);
        for (int i = 0; i < minimapBase.Length; i++) minimapBase[i] = ground;
        System.Action<double, double, double, double, Color32> rect = (x0, z0, x1, z1, c) =>
        {
            int px0 = (int)(x0 / world.arenaW * W), px1 = (int)(x1 / world.arenaW * W);
            int pz0 = (int)(z0 / world.arenaD * H), pz1 = (int)(z1 / world.arenaD * H);
            for (int z = Mathf.Max(0, pz0); z < Mathf.Min(H, pz1); z++)
                for (int x = Mathf.Max(0, px0); x < Mathf.Min(W, px1); x++)
                    minimapBase[z * W + x] = c;
        };
        foreach (var p in world.pits) rect(p.x0, p.z0, p.x1, p.z1, pitc);
        foreach (var b in world.boxes) rect(b.x0, b.z0, b.x1, b.z1, b.kind == 3 ? crackc : boxc);
        foreach (var s in world.slopes) rect(s.x0, s.z0, s.x1, s.z1, new Color32(105, 112, 122, 255));
        minimapImg.texture = minimapTex;
        RepaintMinimap();
    }

    void RepaintMinimap()
    {
        if (minimapTex == null || world == null) return;
        int W = 128, H = 128;
        var px = (Color32[])minimapBase.Clone();
        System.Action<double, double, int, Color32> dot = (x, z, r, c) =>
        {
            int cx = (int)(x / world.arenaW * W), cz = (int)(z / world.arenaD * H);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int ix = cx + dx, iz = cz + dz;
                    if (ix >= 0 && ix < W && iz >= 0 && iz < H) px[iz * W + ix] = c;
                }
        };
        bool jammed = world.playerJammed;
        if (!jammed)
        {
            foreach (var en in world.enemies)
            {
                if (!en.alive) continue;
                dot(en.x, en.z, en.type == 3 ? 1 : 2, en.state == 2 ? new Color32(255, 60, 50, 255) : new Color32(200, 160, 60, 255));
            }
            foreach (var bo in world.bombs)
            {
                if (bo.state == 1 && (world.tick % 20) < 10) dot(bo.x, bo.z, 2, new Color32(255, 60, 40, 255));
            }
        }
        else
        {
            // EMP static: scramble
            for (int i = 0; i < 500; i++)
            {
                int r = Random.Range(0, px.Length);
                px[r] = new Color32((byte)Random.Range(40, 180), (byte)Random.Range(40, 180), (byte)Random.Range(60, 200), 255);
            }
        }
        foreach (var it in world.intels)
        {
            if (it.alive) dot(it.x, it.z, 2, new Color32(77, 210, 195, 255));
        }
        foreach (var lm in world.lamps) dot(lm.x, lm.z, 2, new Color32(255, 210, 62, 255));
        foreach (var sl in world.lights)
        {
            dot(sl.x, sl.z, 2, new Color32(255, 210, 62, 255));
            double ba = (sl.angQ & 65535) * System.Math.PI * 2.0 / 65536.0;
            double bs = System.Math.Sin(ba), bc = System.Math.Cos(ba);
            for (double d = 1.0; d < sl.r; d += 1.0) dot(sl.x + bs * d, sl.z + bc * d, 0, new Color32(255, 210, 62, 200));
        }
        if (world.exitZone != null)
        {
            var mez = world.exitZone;
            dot((mez.x0 + mez.x1) / 2, (mez.z0 + mez.z1) / 2, 3, new Color32(40, 140, 128, 255));
        }
        dot(world.px, world.pz, 2, new Color32(60, 150, 255, 255));
        if (world.pilot != null) dot(world.pilot.x, world.pilot.z, 2, new Color32(120, 220, 255, 255));
        minimapTex.SetPixels32(px);
        minimapTex.Apply(false);
    }
}
