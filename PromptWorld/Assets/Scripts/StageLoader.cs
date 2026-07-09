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

        // Visual order MUST mirror SimWorld.FromStage's iteration (parts order).
        foreach (PartData part in data.parts)
        {
            BuildPartVisual(part, stageRoot, moverViews, crumbleViews, fallerViews, gateViews, keyViews, doorViews);
        }
        CreateFrame("Goal", stageRoot, data.goal.x, data.goal.y, data.goal.w, data.goal.h, 0.18f);

        GameObject player = CreateRectObject("Player", stageRoot,
            new Vector3(data.playerStart.x, data.playerStart.y, 0f), new Vector3(1f, 1f, 1f));

        SimWorld world = SimWorld.FromStage(data);
        var driver = gameObject.AddComponent<SimDriver>();
        driver.Init(world, player.transform, moverViews.ToArray(), crumbleViews.ToArray(), gameManager);
        driver.SetPartViews(fallerViews.ToArray(), gateViews.ToArray(), keyViews.ToArray(), doorViews.ToArray());

        gameManager.ConfigureSim(data.name, data.timeLimit);
        cameraFollow.SetTarget(player.transform);

        if (!string.IsNullOrEmpty(GameSession.RemoteStageId))
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
        List<GameObject> keyViews, List<GameObject> doorViews)
    {
        Vector3 pos = new Vector3(part.x, part.y, 0f);
        Vector3 scale = new Vector3(part.w, part.h, 1f);

        switch (part.type)
        {
            case "faller":
            {
                GameObject go = CreateRectObject("Faller", parent, pos, scale);
                CreateChildRect(go.transform, new Vector2(-0.22f, 0.18f), new Vector2(0.16f, 0.2f), Color.black);
                CreateChildRect(go.transform, new Vector2(0.22f, 0.18f), new Vector2(0.16f, 0.2f), Color.black);
                fallerViews.Add(go.transform);
                break;
            }
            case "conveyor":
            {
                GameObject go = CreateRectObject("Conveyor", parent, pos, scale);
                CreateChildRect(go.transform, new Vector2(0f, 0f), new Vector2(0.9f, 0.25f), Color.black);
                float notch = part.dirX < 0 ? -0.3f : 0.3f;
                CreateChildRect(go.transform, new Vector2(notch, 0f), new Vector2(0.12f, 0.25f), Color.white);
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
                GameObject go = CreateRectObject("Key", parent, pos, scale * 0.45f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                keyViews.Add(go);
                break;
            }
            case "door":
            {
                GameObject go = CreateRectObject("Door", parent, pos, scale);
                CreateChildRect(go.transform, new Vector2(0f, -0.05f), new Vector2(0.28f, 0.34f), Color.black);
                doorViews.Add(go);
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
                GameObject go = CreateRectObject("Hazard", parent, pos, scale * 0.75f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, 45f); // diamond = danger
                break;
            }
            case "jumpPad":
                CreateRectObject("JumpPad", parent, pos, scale);
                break;
            case "boost":
                CreateRectObject("Boost", parent, pos, scale);
                break;
            case "launcher":
            {
                GameObject go = CreateRectObject("Launcher", parent, pos, scale);
                // A hollow black up-chevron so it reads as "flings you skyward".
                CreateChildRect(go.transform, new Vector2(0f, 0.22f), new Vector2(0.5f, 0.14f), Color.black);
                CreateChildRect(go.transform, new Vector2(-0.16f, 0.02f), new Vector2(0.18f, 0.14f), Color.black);
                CreateChildRect(go.transform, new Vector2(0.16f, 0.02f), new Vector2(0.18f, 0.14f), Color.black);
                CreateChildRect(go.transform, new Vector2(0f, -0.2f), new Vector2(0.5f, 0.14f), Color.black);
                break;
            }
            case "gravityFlip":
                CreateFrame("GravityFlip", parent, part.x, part.y, part.w, part.h, 0.12f);
                break;
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
        var child = new GameObject("Detail");
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPos;
        child.transform.localScale = new Vector3(localSize.x, localSize.y, 1f);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = GetWhiteSprite();
        renderer.color = color;
        renderer.sortingOrder = 1;
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
}
