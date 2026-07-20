using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The heart of Prompt World: reads a stage JSON (schema v0.3) and builds a
/// playable world out of it at runtime. Gameplay runs inside the
/// deterministic PromptSim (see Sim/PromptSim.cs); Unity objects here are
/// pure white-on-black visuals synced by SimDriver. The same sim verifies
/// replay certificates server-side.
/// </summary>
public class StageLoader : MonoBehaviour
{
    [SerializeField] private string stageFile = "stage-002.json";
    [SerializeField] private GameManager gameManager;
    [SerializeField] private CameraFollow cameraFollow;

    private static Sprite whiteSprite;

    private IEnumerator Start()
    {
        // Server stages (?stage=<id>) take priority over built-in files.
        string url;
        if (!string.IsNullOrEmpty(GameSession.RemoteStageId))
        {
            url = $"{GameSession.ApiOrigin}/api/stages/{GameSession.RemoteStageId}";
            // Reflect the stage in the browser URL so it's shareable/reload-safe.
            // Keep the creator's editKey in the URL too, or a reload would drop it
            // and test-clears would stop being recorded.
            WebBridge.SetUrlStage(GameSession.RemoteStageId, GameSession.EditKey);
        }
        else
        {
            string file = string.IsNullOrEmpty(GameSession.SelectedStageFile)
                ? stageFile
                : GameSession.SelectedStageFile;
            url = System.IO.Path.Combine(Application.streamingAssetsPath, "Stages", file);
            if (!url.Contains("://")) url = "file://" + url;
        }

        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[PromptWorld] Failed to load stage from '{url}': {request.error}");
            gameManager.ConfigureSim("STAGE NOT FOUND", 60f);
            yield break;
        }

        BuildFromJson(request.downloadHandler.text);
    }

    private void BuildFromJson(string json)
    {
        StageData data = JsonUtility.FromJson<StageData>(json);

        var errors = StageValidator.Validate(data);
        if (errors.Count > 0)
        {
            foreach (string error in errors) Debug.LogError($"[PromptWorld] Invalid stage: {error}");
            gameManager.ConfigureSim("INVALID STAGE (see Console)", 60f);
            return;
        }

        Debug.Log($"[PromptWorld] Loading stage '{data.name}' ({data.id}), {data.parts.Length} parts, {data.timeLimit}s");

        var stageRoot = new GameObject("Stage").transform;
        var moverViews = new List<Transform>();
        var crumbleViews = new List<SpriteRenderer>();
        var fallerViews = new List<Transform>();
        var gateViews = new List<GameObject>();
        var keyViews = new List<GameObject>();
        var doorViews = new List<GameObject>();
        var gravSetViews = new List<GravitySetIndicator>();
        var rotorHeadViews = new List<Transform>();
        var waveViews = new List<Transform>();
        var switchGateViews = new List<GameObject>();
        var switchViews = new List<GameObject>();
        var enemyViews = new List<GameObject>();
        var bossDoorViews = new List<GameObject>();

        // Visual order MUST mirror SimWorld.FromStage's iteration (parts order).
        foreach (PartData part in data.parts)
        {
            BuildPartVisual(part, stageRoot, moverViews, crumbleViews, fallerViews, gateViews, keyViews, doorViews, gravSetViews, rotorHeadViews, switchGateViews, switchViews, enemyViews, bossDoorViews, waveViews);
        }
        BuildGoal(stageRoot, data.goal.x, data.goal.y, data.goal.w, data.goal.h);

        GameObject player = CreateRectObject("Player", stageRoot,
            new Vector3(data.playerStart.x, data.playerStart.y, 0f), new Vector3(1f, 1f, 1f));
        // Two black-dot eyes so the square has a "face" and its facing reads at
        // a glance. Grouped under one pivot the driver slides left/right.
        var eyes = new GameObject("Eyes");
        eyes.transform.SetParent(player.transform, false);
        CreateChildRect(eyes.transform, new Vector2(-0.13f, 0.14f), new Vector2(0.16f, 0.16f), Color.black);
        CreateChildRect(eyes.transform, new Vector2(0.13f, 0.14f), new Vector2(0.16f, 0.16f), Color.black);

        // Motion trail: a short white tail that reads as speed, so movement is
        // obvious even along a flat straightaway. Pure visual — never touches sim.
        AddPlayerTrail(player.transform);

        SimWorld world = SimWorld.FromStage(data);
        var driver = gameObject.AddComponent<SimDriver>();
        driver.Init(world, player.transform, moverViews.ToArray(), crumbleViews.ToArray(), gameManager);
        driver.SetEyes(eyes.transform);
        driver.SetPartViews(fallerViews.ToArray(), gateViews.ToArray(), keyViews.ToArray(), doorViews.ToArray(), GetWhiteSprite());
        driver.SetGravitySetViews(gravSetViews.ToArray());
        driver.SetGimmickViews(rotorHeadViews.ToArray(), switchGateViews.ToArray(), switchViews.ToArray());
        driver.SetWaveViews(waveViews.ToArray());
        driver.SetEnemyViews(enemyViews.ToArray(), bossDoorViews.ToArray());

        bool hasBoss = false;
        foreach (SimEnemy en in world.Enemies)
        {
            if (en.IsBoss) { hasBoss = true; break; }
        }
        gameManager.ConfigureSim(data.name, data.timeLimit, hasBoss, data.music);
        gameManager.UpdateLives(world.LivesLeft, world.MaxLives);
        cameraFollow.SetZoom(data.zoom); // per-stage fixed view (0 = default)
        cameraFollow.SetTarget(player.transform);

        // Optional decorative backdrop. Parent it to the camera so it stays framed
        // behind the view as the player moves; it parallax-drifts slower than the
        // camera for depth. Pure visuals — sim is untouched. We pass the stage's
        // half-extent so the backdrop is over-scaled enough that parallax drift
        // never uncovers the black edge.
        float minX = data.playerStart.x, maxX = data.playerStart.x;
        float minY = data.playerStart.y, maxY = data.playerStart.y;
        void GrowBounds(float x, float y, float w, float h)
        {
            if (x - w / 2f < minX) minX = x - w / 2f;
            if (x + w / 2f > maxX) maxX = x + w / 2f;
            if (y - h / 2f < minY) minY = y - h / 2f;
            if (y + h / 2f > maxY) maxY = y + h / 2f;
        }
        if (data.parts != null) foreach (PartData p in data.parts) GrowBounds(p.x, p.y, p.w, p.h);
        GrowBounds(data.goal.x, data.goal.y, data.goal.w, data.goal.h);
        // The camera STARTS at playerStart, so its max travel is the distance from
        // playerStart to the FARTHEST edge (not half the total width). Using half
        // the total under-scales when the start is near an edge, and the black gap
        // shows on the far side. Take the larger reach on each axis.
        float reachXLeft = data.playerStart.x - minX;
        float reachXRight = maxX - data.playerStart.x;
        float reachYDown = data.playerStart.y - minY;
        float reachYUp = maxY - data.playerStart.y;
        float halfSpanX = Mathf.Max(reachXLeft, reachXRight);
        float halfSpanY = Mathf.Max(reachYDown, reachYUp);
        var bgGo = BackgroundArt.Build(cameraFollow.CameraTransform, data.bg, cameraFollow.ViewSize, halfSpanX, halfSpanY);
        if (bgGo != null)
        {
            var lp = bgGo.transform.localPosition;
            bgGo.transform.localPosition = new Vector3(lp.x, lp.y, 20f); // in front of the far clip? push to +Z (into scene)
        }

        // Skip the ghost + par entirely when the creator hid it — for trick /
        // blind / "find the real route" stages where showing the answer ruins it.
        if (!string.IsNullOrEmpty(GameSession.RemoteStageId) && !data.hideGhost)
        {
            StartCoroutine(LoadGhost(driver, data));
        }
    }

    /// <summary>The creator's verified replay plays back as a translucent ghost with a par time.</summary>
    private IEnumerator LoadGhost(SimDriver driver, StageData data)
    {
        using var request = UnityWebRequest.Get($"{GameSession.ApiOrigin}/api/stages/{GameSession.RemoteStageId}/ghost");
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success) yield break;

        GhostData ghost = JsonUtility.FromJson<GhostData>(request.downloadHandler.text);
        if (ghost?.replay?.rle == null || ghost.replay.rle.Length == 0) yield break;

        gameManager.SetPar(ghost.bestTimeMs > 0 ? ghost.bestTimeMs : ghost.clearTimeMs);

        GameObject ghostGo = CreateRectObject("Ghost", null,
            new Vector3(data.playerStart.x, data.playerStart.y, 0f), Vector3.one);
        var renderer = ghostGo.GetComponent<SpriteRenderer>();
        renderer.color = new Color(1f, 1f, 1f, 0.28f);

        driver.AttachGhost(SimWorld.FromStage(data), ghost.replay.rle, ghostGo.transform);
    }

    private void BuildPartVisual(PartData part, Transform parent,
        List<Transform> moverViews, List<SpriteRenderer> crumbleViews,
        List<Transform> fallerViews, List<GameObject> gateViews,
        List<GameObject> keyViews, List<GameObject> doorViews,
        List<GravitySetIndicator> gravSetViews,
        List<Transform> rotorHeadViews, List<GameObject> switchGateViews,
        List<GameObject> switchViews, List<GameObject> enemyViews,
        List<GameObject> bossDoorViews, List<Transform> waveViews)
    {
        Vector3 pos = new Vector3(part.x, part.y, 0f);
        Vector3 scale = new Vector3(part.w, part.h, 1f);

        switch (part.type)
        {
            case "faller":
            {
                GameObject go = CreateRectObject("Faller", parent, pos, scale);
                BuildAngryFace(go.transform, part.w, part.h);
                fallerViews.Add(go.transform);
                break;
            }
            case "conveyor":
            {
                GameObject go = CreateRectObject("Conveyor", parent, pos, scale);
                float beltSpeed = part.power > 0f ? part.power : 3f;
                var anim = go.AddComponent<ConveyorAnimator>();
                anim.Init(part.w, part.h, part.dirX, beltSpeed, GetWhiteSprite());
                break;
            }
            case "timedGate":
            {
                GameObject go = CreateRectObject("TimedGate", parent, pos, scale);
                gateViews.Add(go);
                break;
            }
            case "key":
            {
                // A white ring (square with a black hole) — clearly a
                // collectible, distinct from the solid danger diamond.
                GameObject go = CreateRectObject("Key", parent, pos, scale * 0.6f);
                CreateChildRect(go.transform, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), Color.black);
                keyViews.Add(go);
                break;
            }
            case "door":
            {
                // A locked WALL: brick-mortar pattern so it clearly reads as a
                // barrier around the goal, not just a block. Vanishes when all
                // keys are collected.
                GameObject go = CreateRectObject("Door", parent, pos, scale);
                BuildBrickPattern(go.transform, part.w, part.h);
                doorViews.Add(go);
                break;
            }
            case "cannon":
            {
                GameObject go = CreateRectObject("Cannon", parent, pos, scale);
                // a black muzzle notch on the firing side
                float d = part.dirX < 0f ? -1f : 1f;
                CreateChildRect(go.transform, new Vector2(0.32f * d, 0f), new Vector2(0.3f, 0.5f), Color.black);
                CreateChildRect(go.transform, new Vector2(0.05f * d, 0f), new Vector2(0.35f, 0.28f), Color.black);
                break;
            }
            case "solid":
                CreateRectObject("Solid", parent, pos, scale);
                break;
            case "movingPlatform":
            {
                GameObject go = CreateRectObject("MovingPlatform", parent, pos, scale);
                moverViews.Add(go.transform);
                break;
            }
            case "crumble":
            {
                GameObject go = CreateRectObject("Crumble", parent, pos, scale);
                crumbleViews.Add(go.GetComponent<SpriteRenderer>());
                break;
            }
            case "hazard":
            {
                // A near-SQUARE hazard reads as a single danger DIAMOND (rotate 45°).
                // A long/thin hazard (a spike strip or a wide pit floor) must NOT be
                // rotated — that turns it into a stray diagonal bar. Instead draw it
                // as an upright band with a ROW of little spike diamonds across it.
                float hazAspect = part.h > 0.01f ? part.w / part.h : 1f;
                bool elongated = hazAspect > 1.8f || hazAspect < (1f / 1.8f);
                if (!elongated)
                {
                    GameObject go = CreateRectObject("Hazard", parent, pos, scale * 0.75f);
                    go.transform.rotation = Quaternion.Euler(0f, 0f, 45f); // diamond = danger
                }
                else
                {
                    // Strip: a thin base bar + a row of small diamonds along it, all
                    // axis-aligned so nothing looks like a slanted stick.
                    var strip = new GameObject("HazardStrip");
                    strip.transform.SetParent(parent);
                    strip.transform.position = pos;
                    bool horizontal = part.w >= part.h;
                    float length = horizontal ? part.w : part.h;
                    float thick = horizontal ? part.h : part.w;
                    // base bar
                    var bar = CreateRectObject("Bar", strip.transform, pos, new Vector3(part.w, part.h, 1f));
                    // spike diamonds along the length
                    float spikeSize = Mathf.Max(thick * 0.9f, 0.3f);
                    int count = Mathf.Clamp(Mathf.RoundToInt(length / (spikeSize * 1.4f)), 1, 60);
                    float step = length / count;
                    for (int k = 0; k < count; k++)
                    {
                        float along = -length * 0.5f + step * (k + 0.5f);
                        Vector3 sp = horizontal ? new Vector3(pos.x + along, pos.y, 0f)
                                                : new Vector3(pos.x, pos.y + along, 0f);
                        var d = CreateRectObject("Spike", strip.transform, sp, new Vector3(spikeSize, spikeSize, 1f));
                        d.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                    }
                }
                break;
            }
            case "jumpPad":
            {
                // A springboard: a white base plate with a black up-chevron and
                // coil lines so it clearly reads "bounces you up". Built in a
                // uniform wrapper so the chevron stays crisp at any pad size.
                GameObject go = CreateRectObject("JumpPad", parent, pos, scale);
                float pw = Mathf.Max(part.w, 0.01f);
                float ph = Mathf.Max(part.h, 0.01f);
                var art = new GameObject("Art");
                art.transform.SetParent(go.transform, false);
                art.transform.localScale = new Vector3(1f / pw, 1f / ph, 1f);
                // two up-chevrons (stacked) — the springboard "launch" mark
                var c1 = CreateChildTriangle(art.transform, new Vector2(0f, -0.02f), new Vector2(0.5f, 0.34f), 90f);
                c1.GetComponent<SpriteRenderer>().color = Color.black;
                var c2 = CreateChildTriangle(art.transform, new Vector2(0f, 0.2f), new Vector2(0.5f, 0.34f), 90f);
                c2.GetComponent<SpriteRenderer>().color = Color.black;
                break;
            }
            case "boost":
            {
                // A sideways arrow (shaft + triangular head) pointing the launch
                // direction, so "blasts you left/right" reads at a glance. The
                // arrow lives in a UNIFORM-scale wrapper so nothing shears.
                float bh = Mathf.Max(part.h, 1.2f);
                GameObject go = CreateRectObject("Boost", parent, pos, new Vector3(1.2f, bh, 1f));
                float d = part.dirX < 0f ? -1f : 1f;
                // Wrapper cancels the parent's non-uniform (1.2 x bh) scale, so
                // children draw in true world units — the triangle stays crisp.
                var arrow = new GameObject("Arrow");
                arrow.transform.SetParent(go.transform, false);
                arrow.transform.localScale = new Vector3(1f / 1.2f, 1f / bh, 1f);
                // shaft
                CreateChildRect(arrow.transform, new Vector2(-0.18f * d, 0f), new Vector2(0.5f, 0.16f), Color.black);
                // head: right-pointing triangle, flipped for left via -X scale
                CreateChildTriangle(arrow.transform, new Vector2(0.32f * d, 0f), new Vector2(0.34f * d, 0.42f), 0f);
                break;
            }
            case "launcher":
            {
                GameObject go = CreateRectObject("Launcher", parent, pos, scale);
                // A black up-arrow so it clearly reads "flings you skyward".
                float lw = Mathf.Max(part.w, 0.01f);
                float lh = Mathf.Max(part.h, 0.01f);
                var arrow = new GameObject("Arrow");
                arrow.transform.SetParent(go.transform, false);
                arrow.transform.localScale = new Vector3(1f / lw, 1f / lh, 1f); // uniform world units
                CreateChildRect(arrow.transform, new Vector2(0f, -0.12f), new Vector2(0.16f, 0.5f), Color.black); // shaft
                CreateChildTriangle(arrow.transform, new Vector2(0f, 0.3f), new Vector2(0.42f, 0.4f), 90f); // up head
                break;
            }
            case "gravityFlip":
                CreateFrame("GravityFlip", parent, part.x, part.y, part.w, part.h, 0.12f);
                break;
            case "gravitySet":
            {
                // Hollow frame + a big arrow showing the FIXED direction gravity
                // will point after touching it (dirX<0 = up, else down). The
                // arrow's fill lights up when current gravity already matches.
                GameObject go = CreateFrame("GravitySet", parent, part.x, part.y, part.w, part.h, 0.12f);
                float lw = Mathf.Max(part.w, 0.01f);
                float lh = Mathf.Max(part.h, 0.01f);
                bool up = part.dirX < 0f;
                var arrow = new GameObject("Arrow");
                arrow.transform.SetParent(go.transform, false);
                arrow.transform.localScale = new Vector3(1f / lw, 1f / lh, 1f);
                var shaft = CreateChildRectGO(arrow.transform, new Vector2(0f, up ? -0.08f : 0.08f), new Vector2(0.16f, 0.42f), Color.white);
                var head = CreateChildTriangle(arrow.transform, new Vector2(0f, up ? 0.28f : -0.28f), new Vector2(0.4f, 0.38f), up ? 90f : -90f);
                head.GetComponent<SpriteRenderer>().color = Color.white;
                var ind = go.AddComponent<GravitySetIndicator>();
                ind.Init(up ? -1 : 1, shaft.GetComponent<SpriteRenderer>(), head.GetComponent<SpriteRenderer>());
                gravSetViews.Add(ind);
                break;
            }
            case "checkpoint":
            {
                // A hollow flag-post frame with an inner banner — clearly a
                // waypoint, not a hazard or a solid. Non-solid, walk through.
                GameObject go = CreateFrame("Checkpoint", parent, part.x, part.y, part.w, part.h, 0.1f);
                CreateChildRect(go.transform, new Vector2(part.w * 0.18f, part.h * 0.22f), new Vector2(part.w * 0.4f, part.h * 0.28f), Color.white);
                break;
            }
            case "rotatingHazard":
            {
                // A small center hub (marks the pivot) plus a free spike head the
                // driver moves around the orbit each tick.
                float radius = Mathf.Max(part.w, part.h) / 2f;
                CreateRectObject("RotorHub", parent, new Vector3(part.x, part.y, 0f), new Vector3(0.36f, 0.36f, 1f));
                float headSize = part.dx > 0f ? part.dx : 0.35f;
                var head = CreateRectObject("RotorHead", parent,
                    new Vector3(part.x + radius, part.y, 0f),
                    new Vector3(headSize * 2f, headSize * 2f, 1f));
                head.transform.rotation = Quaternion.Euler(0f, 0f, 45f); // spike diamond
                rotorHeadViews.Add(head.transform);
                break;
            }
            case "wave":
            {
                // A sweeping wall of death. Solid white slab so it reads as an
                // impassable front; the driver moves it along its sweep each frame.
                var wall = CreateRectObject("Wave", parent,
                    new Vector3(part.x, part.y, 0f),
                    new Vector3(part.w, part.h, 1f));
                var wsr = wall.GetComponent<SpriteRenderer>();
                if (wsr != null) wsr.color = new Color(1f, 1f, 1f, 0.85f);
                // A few inner stripes to give the wall a "surging" texture.
                CreateChildRect(wall.transform, new Vector2(0f, 0f), new Vector2(0.7f, 0.94f), new Color(0f, 0f, 0f, 0.18f));
                waveViews.Add(wall.transform);
                break;
            }
            case "teleporter":
            {
                // A hollow portal frame. Entry (dirX>=0) shows an inward chevron,
                // exit (dirX<0) an outward one, so a pair reads as in->out.
                GameObject go = CreateFrame("Teleporter", parent, part.x, part.y, part.w, part.h, 0.14f);
                bool entry = part.dirX >= 0f;
                CreateChildRect(go.transform, new Vector2(0f, 0f), new Vector2(0.24f, 0.24f), Color.white);
                CreateChildRect(go.transform, new Vector2(0f, entry ? 0.22f : -0.22f), new Vector2(0.5f, 0.1f), Color.white);
                break;
            }
            case "fan":
            {
                // A wind zone: a faint dashed border + flowing white streaks
                // (FanAnimator) that stream in the wind direction, so it clearly
                // reads as moving AIR you get carried by — not a solid block.
                float fw = Mathf.Max(part.w, 0.01f);
                float fh = Mathf.Max(part.h, 0.01f);
                var go = new GameObject("Fan");
                go.transform.SetParent(parent);
                go.transform.position = pos;
                go.transform.localScale = new Vector3(fw, fh, 1f);
                var anim = go.AddComponent<FanAnimator>();
                anim.Init(part.w, part.h, part.dirX, part.dy, part.power, GetWhiteSprite());

                // A big arrow pointing the EXACT wind direction — works for any
                // dirX/dy including diagonals. Built in a uniform wrapper (cancels
                // the zone's aspect) then rotated to the wind angle so the arrow
                // is never sheared.
                float wdx = part.dirX;
                float wdy = part.dy;
                if (wdx == 0f && wdy == 0f) wdy = 1f;   // default: straight up
                float ang = Mathf.Atan2(wdy, wdx) * Mathf.Rad2Deg;
                var art = new GameObject("Arrow");
                art.transform.SetParent(go.transform, false);
                art.transform.localScale = new Vector3(1f / fw, 1f / fh, 1f);
                art.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
                // In the rotated frame, +X = wind direction: shaft then head.
                CreateChildRect(art.transform, new Vector2(-0.14f, 0f), new Vector2(0.5f, 0.18f), Color.white);
                var head = CreateChildTriangle(art.transform, new Vector2(0.28f, 0f), new Vector2(0.5f, 0.5f), 0f);
                head.GetComponent<SpriteRenderer>().color = Color.white;
                break;
            }
            case "switch":
            {
                // A pressure button: a base plate with a raised black nub on top.
                // The nub (child "Nub") sinks down when pressed — SimDriver drives
                // the press state each tick.
                GameObject go = CreateRectObject("Switch", parent, pos, scale);
                var nub = CreateChildRectGO(go.transform, new Vector2(0f, 0.55f), new Vector2(0.6f, 0.5f), Color.black);
                nub.name = "Nub";
                switchViews.Add(go);
                break;
            }
            case "switchGate":
            {
                // A solid block with a hollow center ring so it's visually
                // distinct from plain terrain (it can open). Shown/hidden by the
                // driver based on whether it has sealed shut.
                GameObject go = CreateRectObject("SwitchGate", parent, pos, scale);
                CreateChildRect(go.transform, new Vector2(0f, 0f), new Vector2(0.34f, 0.34f), Color.black);
                switchGateViews.Add(go);
                break;
            }
            case "enemy":
            {
                // A monster: white body with a fanged, angry face. Bosses (dy>0)
                // wear little horns and are drawn a touch fiercer. The driver moves
                // it, flashes it on a hit, and hides it when defeated.
                GameObject go = CreateRectObject("Enemy", parent, pos, scale);
                bool boss = part.dy > 0f;
                BuildMonsterFace(go.transform, part.w, part.h, boss, (int)part.power);
                enemyViews.Add(go);
                break;
            }
            case "bossDoor":
            {
                // A heavy sealed wall (bar pattern) that blocks the goal until every
                // boss is defeated, then vanishes. Reads as a locked gate.
                GameObject go = CreateRectObject("BossDoor", parent, pos, scale);
                BuildBrickPattern(go.transform, part.w, part.h);
                bossDoorViews.Add(go);
                break;
            }
            default:
                Debug.LogWarning($"[PromptWorld] Unknown part type '{part.type}' — skipped.");
                break;
        }
    }

    private GameObject CreateRectObject(string name, Transform parent, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = scale;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = GetWhiteSprite();
        renderer.color = Color.white;
        return go;
    }

    /// <summary>A short white motion trail behind the player so speed and travel
    /// read at a glance, even on a flat straightaway. Purely visual.</summary>
    private void AddPlayerTrail(Transform player)
    {
        var go = new GameObject("Trail");
        go.transform.SetParent(player, false);
        go.transform.localPosition = Vector3.zero;
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.28f;                 // how long the tail lingers
        tr.startWidth = 0.6f;            // ~the player's width at the head
        tr.endWidth = 0f;               // taper to a point
        tr.minVertexDistance = 0.05f;    // smooth curve
        tr.autodestruct = false;
        tr.numCapVertices = 4;
        tr.sortingOrder = -1;            // behind the player square, above the bg
        tr.material = new Material(Shader.Find("Sprites/Default"));
        // Fade white -> transparent along the tail.
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;
    }

    /// <summary>Hollow rectangle (goal / gravity flip): four thin white edges around black space.</summary>
    private GameObject CreateFrame(string name, Transform parent, float x, float y, float w, float h, float thickness)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(x, y, 0f);

        CreateEdge(go.transform, new Vector2(0f, h / 2f - thickness / 2f), new Vector2(w, thickness));
        CreateEdge(go.transform, new Vector2(0f, -h / 2f + thickness / 2f), new Vector2(w, thickness));
        CreateEdge(go.transform, new Vector2(-w / 2f + thickness / 2f, 0f), new Vector2(thickness, h));
        CreateEdge(go.transform, new Vector2(w / 2f - thickness / 2f, 0f), new Vector2(thickness, h));
        return go;
    }

    private void CreateChildRect(Transform parent, Vector2 localPos, Vector2 localSize, Color color)
    {
        CreateChildRectGO(parent, localPos, localSize, color);
    }

    private GameObject CreateChildRectGO(Transform parent, Vector2 localPos, Vector2 localSize, Color color)
    {
        var child = new GameObject("Detail");
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPos;
        child.transform.localScale = new Vector3(localSize.x, localSize.y, 1f);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = GetWhiteSprite();
        renderer.color = color;
        renderer.sortingOrder = 1;
        return child;
    }

    private void CreateEdge(Transform parent, Vector2 localPos, Vector2 size)
    {
        var edge = new GameObject("Edge");
        edge.transform.SetParent(parent);
        edge.transform.localPosition = localPos;
        edge.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = edge.AddComponent<SpriteRenderer>();
        renderer.sprite = GetWhiteSprite();
        renderer.color = Color.white;
    }

    /// <summary>1x1-unit white sprite generated in memory — zero asset dependencies.</summary>
    private static Sprite GetWhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;

        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(pixels);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return whiteSprite;
    }

    private static Sprite triangleSprite;

    /// <summary>
    /// Solid right-pointing triangle sprite (apex at +X). Rotate the GameObject
    /// to aim it. Built at texture level so it stays a crisp triangle regardless
    /// of the object's scale — no rotated-square shearing.
    /// </summary>
    private static Sprite GetTriangleSprite()
    {
        if (triangleSprite != null) return triangleSprite;

        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var clear = new Color32(255, 255, 255, 0);
        var solid = new Color32(255, 255, 255, 255);
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            // width available at this row shrinks linearly toward the apex (+X)
            float ny = (y + 0.5f) / n;          // 0..1 across height
            float dy = Mathf.Abs(ny - 0.5f) * 2f; // 0 at center, 1 at edges
            float maxX = 1f - dy;                 // apex reach for this row
            for (int x = 0; x < n; x++)
            {
                float nx = (x + 0.5f) / n;        // 0..1 across width
                pixels[y * n + x] = nx <= maxX ? solid : clear;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        triangleSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return triangleSprite;
    }

    /// <summary>
    /// Draws a brick-wall mortar pattern (thin black lines) over a white door
    /// block so it reads as a locked barrier. Sizes are in the parent's
    /// normalized local space, so it looks right at any door w/h. Rows scale
    /// with the door's world height so bricks stay a consistent real size.
    /// </summary>
    private void BuildBrickPattern(Transform parent, float w, float h)
    {
        float mortar = 0.06f; // world-unit line thickness
        int rows = Mathf.Clamp(Mathf.RoundToInt(h / 0.7f), 2, 8);
        int cols = Mathf.Clamp(Mathf.RoundToInt(w / 0.7f), 1, 6);
        float lw = w <= 0f ? 1f : w;
        float lh = h <= 0f ? 1f : h;

        // horizontal mortar lines between rows
        for (int r = 1; r < rows; r++)
        {
            float ly = (r / (float)rows) - 0.5f;
            CreateChildRect(parent, new Vector2(0f, ly), new Vector2(1f, mortar / lh), Color.black);
        }
        // vertical mortar lines, staggered every other row (running bond)
        for (int r = 0; r < rows; r++)
        {
            float cy = ((r + 0.5f) / rows) - 0.5f;
            float rowH = (1f / rows) - (mortar / lh);
            bool offset = (r % 2) == 1;
            for (int c = 0; c <= cols; c++)
            {
                float baseX = (c / (float)cols) - 0.5f;
                float lx = baseX + (offset ? (0.5f / cols) : 0f);
                if (lx <= -0.5f || lx >= 0.5f) continue;
                CreateChildRect(parent, new Vector2(lx, cy), new Vector2(mortar / lw, rowH), Color.black);
            }
        }
    }

    /// <summary>
    /// A goal marker that actually reads as a goal: a hollow frame (the portal
    /// you enter) plus a checkered flag on a pole inside it. Unmistakably the
    /// finish, not just another white square.
    /// </summary>
    private void BuildGoal(Transform parent, float x, float y, float w, float h)
    {
        GameObject frame = CreateFrame("Goal", parent, x, y, w, h, 0.16f);

        // Pole + checkered flag, scaled to the goal box (uniform wrapper).
        float lw = w <= 0f ? 1f : w;
        float lh = h <= 0f ? 1f : h;
        var art = new GameObject("Flag");
        art.transform.SetParent(frame.transform, false);
        art.transform.localScale = new Vector3(1f / lw, 1f / lh, 1f);

        // vertical pole
        CreateChildRect(art.transform, new Vector2(-0.12f, 0f), new Vector2(0.08f, 0.9f), Color.white);
        // flag: a 2x2 checker near the top of the pole (white cloth + black squares)
        CreateChildRect(art.transform, new Vector2(0.16f, 0.28f), new Vector2(0.48f, 0.34f), Color.white);
        CreateChildRect(art.transform, new Vector2(0.04f, 0.36f), new Vector2(0.12f, 0.09f), Color.black);
        CreateChildRect(art.transform, new Vector2(0.28f, 0.36f), new Vector2(0.12f, 0.09f), Color.black);
        CreateChildRect(art.transform, new Vector2(0.16f, 0.2f), new Vector2(0.12f, 0.09f), Color.black);
    }

    /// <summary>
    /// An angry "enemy" face for the crusher: two eyes with inward-slanted
    /// brows (▶◀ tilt) plus a small clenched frown. Built inside a uniform
    /// wrapper so the slants don't shear under the crusher's non-uniform scale.
    /// </summary>
    private void BuildAngryFace(Transform parent, float w, float h)
    {
        float lw = w <= 0f ? 1f : w;
        float lh = h <= 0f ? 1f : h;
        var face = new GameObject("Face");
        face.transform.SetParent(parent, false);
        face.transform.localScale = new Vector3(1f / lw, 1f / lh, 1f); // true world units

        // Eyes: black squares, sitting a bit high.
        CreateChildRect(face.transform, new Vector2(-0.26f, 0.1f), new Vector2(0.2f, 0.2f), Color.black);
        CreateChildRect(face.transform, new Vector2(0.26f, 0.1f), new Vector2(0.2f, 0.2f), Color.black);
        // Angry raised brows (吊り眉): two slanted bars, MIRROR-symmetric — each
        // brow's inner end dips low and its outer end lifts high. Left brow
        // tilts one way, right brow the exact opposite (+/- same angle).
        var browL = CreateChildRectGO(face.transform, new Vector2(-0.26f, 0.32f), new Vector2(0.34f, 0.1f), Color.black);
        browL.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
        var browR = CreateChildRectGO(face.transform, new Vector2(0.26f, 0.32f), new Vector2(0.34f, 0.1f), Color.black);
        browR.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
        // Clenched frown mouth.
        CreateChildRect(face.transform, new Vector2(0f, -0.24f), new Vector2(0.34f, 0.08f), Color.black);
    }

    /// <summary>
    /// A little monster face on a white body: hollow glaring eyes, a jagged
    /// fanged mouth, angry brows, plus horns for a boss and HP "scar" pips for a
    /// multi-hit enemy. Drawn in a uniform wrapper so it stays crisp at any size.
    /// </summary>
    private void BuildMonsterFace(Transform parent, float w, float h, bool boss, int hp)
    {
        float lw = w <= 0f ? 1f : w;
        float lh = h <= 0f ? 1f : h;
        var face = new GameObject("Face");
        face.transform.SetParent(parent, false);
        face.transform.localScale = new Vector3(1f / lw, 1f / lh, 1f);

        // Big hollow eyes (white square with a black pupil) — a glare.
        foreach (float ex in new[] { -0.24f, 0.24f })
        {
            CreateChildRectGO(face.transform, new Vector2(ex, 0.12f), new Vector2(0.26f, 0.26f), Color.black);
            var pup = CreateChildRectGO(face.transform, new Vector2(ex, 0.10f), new Vector2(0.12f, 0.12f), Color.white);
            pup.name = "Pupil";
        }
        // Angry mirrored brows.
        var browL = CreateChildRectGO(face.transform, new Vector2(-0.24f, 0.33f), new Vector2(0.32f, 0.09f), Color.black);
        browL.transform.localRotation = Quaternion.Euler(0f, 0f, 26f);
        var browR = CreateChildRectGO(face.transform, new Vector2(0.24f, 0.33f), new Vector2(0.32f, 0.09f), Color.black);
        browR.transform.localRotation = Quaternion.Euler(0f, 0f, -26f);

        // Jagged fanged mouth: a black bar with two white fang triangles biting up.
        CreateChildRect(face.transform, new Vector2(0f, -0.26f), new Vector2(0.5f, 0.14f), Color.black);
        var fangL = CreateChildTriangle(face.transform, new Vector2(-0.12f, -0.24f), new Vector2(0.14f, 0.14f), 90f);
        fangL.GetComponent<SpriteRenderer>().color = Color.white;
        var fangR = CreateChildTriangle(face.transform, new Vector2(0.12f, -0.24f), new Vector2(0.14f, 0.14f), 90f);
        fangR.GetComponent<SpriteRenderer>().color = Color.white;

        // Boss horns: two black triangles poking up from the top corners.
        if (boss)
        {
            var hornL = CreateChildTriangle(face.transform, new Vector2(-0.36f, 0.52f), new Vector2(0.2f, 0.26f), 60f);
            hornL.GetComponent<SpriteRenderer>().color = Color.black;
            var hornR = CreateChildTriangle(face.transform, new Vector2(0.36f, 0.52f), new Vector2(0.2f, 0.26f), 120f);
            hornR.GetComponent<SpriteRenderer>().color = Color.black;
        }

        // HP pips: for a multi-hit enemy, small black notches along the bottom so
        // players can read "this one takes more than one stomp".
        if (hp > 1)
        {
            int n = Mathf.Min(hp, 5);
            float step = 0.16f;
            float x0 = -(n - 1) * step * 0.5f;
            for (int i = 0; i < n; i++)
            {
                CreateChildRect(face.transform, new Vector2(x0 + i * step, -0.44f), new Vector2(0.09f, 0.09f), Color.black);
            }
        }
    }

    /// <summary>A black triangle child pointing along +X (rotate to aim).</summary>
    private GameObject CreateChildTriangle(Transform parent, Vector2 localPos, Vector2 localSize, float zRotation)
    {
        var child = new GameObject("Arrow");
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPos;
        child.transform.localScale = new Vector3(localSize.x, localSize.y, 1f);
        child.transform.localRotation = Quaternion.Euler(0f, 0f, zRotation);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = GetTriangleSprite();
        renderer.color = Color.black;
        renderer.sortingOrder = 1;
        return child;
    }
}
