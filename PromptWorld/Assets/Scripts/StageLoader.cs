using System.IO;
using UnityEngine;

/// <summary>
/// The heart of Prompt World: reads a stage JSON (schema v0.2) and builds a
/// playable world out of it at runtime. The client never executes user code —
/// it interprets this data. Visual language is strictly white-on-black;
/// parts are distinguished by shape (see docs/parts-catalog.md).
/// </summary>
public class StageLoader : MonoBehaviour
{
    [SerializeField] private string stageFile = "stage-001.json";
    [SerializeField] private GameManager gameManager;
    [SerializeField] private CameraFollow cameraFollow;

    private static Sprite whiteSprite;

    private void Awake()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Stages", stageFile);
        if (!File.Exists(path))
        {
            Debug.LogError($"[PromptWorld] Stage file not found: {path}");
            return;
        }

        StageData data = JsonUtility.FromJson<StageData>(File.ReadAllText(path));
        Debug.Log($"[PromptWorld] Loading stage '{data.name}' ({data.id}), {data.parts.Length} parts, {data.timeLimit}s");

        var stageRoot = new GameObject("Stage").transform;
        foreach (PartData part in data.parts) BuildPart(part, stageRoot);
        BuildGoal(data.goal, stageRoot);

        PlayerController player = SpawnPlayer(new Vector2(data.playerStart.x, data.playerStart.y));

        gameManager.Configure(player, data.name, data.timeLimit);
        cameraFollow.SetTarget(player.transform);
    }

    private void BuildPart(PartData part, Transform parent)
    {
        switch (part.type)
        {
            case "solid":
            {
                GameObject go = CreateRect("Solid", parent, part);
                go.layer = LayerMask.NameToLayer("Ground");
                go.AddComponent<BoxCollider2D>();
                break;
            }
            case "hazard":
            {
                GameObject go = CreateRect("Hazard", parent, part);
                go.transform.rotation = Quaternion.Euler(0f, 0f, 45f); // diamond = danger
                go.transform.localScale *= 0.75f;
                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                go.AddComponent<Hazard>();
                break;
            }
            case "jumpPad":
            {
                GameObject go = CreateRect("JumpPad", parent, part);
                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = new Vector2(1f, 2.5f); // tall trigger so fast falls can't tunnel past
                col.offset = new Vector2(0f, 0.75f);
                go.AddComponent<JumpPad>().power = part.power > 0f ? part.power : 22f;
                break;
            }
            case "boost":
            {
                GameObject go = CreateRect("Boost", parent, part);
                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                var boost = go.AddComponent<Boost>();
                boost.directionX = part.dirX >= 0f ? 1f : -1f;
                boost.power = part.power > 0f ? part.power : 10f;
                break;
            }
            case "gravityFlip":
            {
                GameObject go = CreateFrame("GravityFlip", parent, part.x, part.y, part.w, part.h, 0.12f);
                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = new Vector2(part.w, part.h);
                go.AddComponent<GravityFlipBlock>();
                break;
            }
            default:
                Debug.LogWarning($"[PromptWorld] Unknown part type '{part.type}' — skipped.");
                break;
        }
    }

    private void BuildGoal(RectData goal, Transform parent)
    {
        GameObject go = CreateFrame("Goal", parent, goal.x, goal.y, goal.w, goal.h, 0.18f);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(goal.w, goal.h);
        go.AddComponent<Goal>();
    }

    private PlayerController SpawnPlayer(Vector2 spawn)
    {
        var go = new GameObject("Player");
        go.tag = "Player";
        go.transform.position = spawn;
        AddWhiteSpriteRenderer(go);

        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = 3f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.freezeRotation = true;

        go.AddComponent<BoxCollider2D>();

        var player = go.AddComponent<PlayerController>();
        player.Init(LayerMask.GetMask("Ground"), spawn);
        return player;
    }

    private GameObject CreateRect(string name, Transform parent, PartData part)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(part.x, part.y, 0f);
        go.transform.localScale = new Vector3(part.w, part.h, 1f);
        AddWhiteSpriteRenderer(go);
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

    private void CreateEdge(Transform parent, Vector2 localPos, Vector2 size)
    {
        var edge = new GameObject("Edge");
        edge.transform.SetParent(parent);
        edge.transform.localPosition = localPos;
        edge.transform.localScale = new Vector3(size.x, size.y, 1f);
        AddWhiteSpriteRenderer(edge);
    }

    private void AddWhiteSpriteRenderer(GameObject go)
    {
        var renderer = go.AddComponent<SpriteRenderer>();
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
