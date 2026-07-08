using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the complete Day 1 scene from code so the setup is reproducible
/// and headless-runnable (menu: Prompt World > Build Day 1 Scene, or CLI
/// -executeMethod Day1SceneBuilder.Build).
/// </summary>
public static class Day1SceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Day1.unity";
    private const string SpritePath = "Assets/Sprites/Square.png";
    private const string GroundLayerName = "Ground";

    [MenuItem("Prompt World/Build Day 1 Scene")]
    public static void Build()
    {
        EnsureLayer(GroundLayerName);
        Sprite square = EnsureSquareSprite();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildCamera();
        GameObject ground = BuildGround(square);
        PlayerController player = BuildPlayer(square);
        BuildGoal(square);
        BuildUI(out TMP_Text timerText, out TMP_Text resultText);
        BuildGameManager(player, timerText, resultText);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[PromptWorld] Day 1 scene built: {ScenePath} (ground={ground.name})");
    }

    private static void BuildCamera()
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        go.transform.position = new Vector3(0f, 0f, -10f);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        go.AddComponent<AudioListener>();
    }

    private static GameObject BuildGround(Sprite square)
    {
        GameObject go = CreateSpriteObject("Ground", square, new Vector3(0f, -4f, 0f), new Vector3(20f, 1f, 1f));
        go.layer = LayerMask.NameToLayer(GroundLayerName);
        go.AddComponent<BoxCollider2D>();
        return go;
    }

    private static PlayerController BuildPlayer(Sprite square)
    {
        GameObject go = CreateSpriteObject("Player", square, new Vector3(-6f, 0f, 0f), Vector3.one);
        go.tag = "Player";

        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = 3f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.freezeRotation = true;

        go.AddComponent<BoxCollider2D>();

        var controller = go.AddComponent<PlayerController>();
        var so = new SerializedObject(controller);
        so.FindProperty("groundLayer").intValue = LayerMask.GetMask(GroundLayerName);
        so.ApplyModifiedPropertiesWithoutUndo();
        return controller;
    }

    private static void BuildGoal(Sprite square)
    {
        GameObject go = CreateSpriteObject("Goal", square, new Vector3(7f, -2.5f, 0f), new Vector3(1f, 2f, 1f));
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        go.AddComponent<Goal>();
    }

    private static void BuildUI(out TMP_Text timerText, out TMP_Text resultText)
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        timerText = CreateText(canvasGo.transform, "TimerText", "60", 48,
            anchor: new Vector2(0.5f, 1f), anchoredPos: new Vector2(0f, -40f), size: new Vector2(400f, 100f));
        resultText = CreateText(canvasGo.transform, "ResultText", "", 72,
            anchor: new Vector2(0.5f, 0.5f), anchoredPos: Vector2.zero, size: new Vector2(1200f, 160f));
    }

    private static void BuildGameManager(PlayerController player, TMP_Text timerText, TMP_Text resultText)
    {
        var go = new GameObject("GameManager");
        var gm = go.AddComponent<GameManager>();
        var so = new SerializedObject(gm);
        so.FindProperty("player").objectReferenceValue = player;
        so.FindProperty("timerText").objectReferenceValue = timerText;
        so.FindProperty("resultText").objectReferenceValue = resultText;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject CreateSpriteObject(string name, Sprite sprite, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.transform.localScale = scale;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        return go;
    }

    private static TMP_Text CreateText(Transform parent, string name, string text, float fontSize,
        Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        return tmp;
    }

    /// <summary>Generates a plain white square sprite asset (no template dependency).</summary>
    private static Sprite EnsureSquareSprite()
    {
        if (!File.Exists(SpritePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SpritePath);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 64f;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite == null)
        {
            Debug.LogError($"[PromptWorld] Sprite failed to import at {SpritePath} — scene objects will be invisible.");
        }
        return sprite;
    }

    /// <summary>Registers a user layer in TagManager if it does not exist yet.</summary>
    private static void EnsureLayer(string layerName)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return;
        }
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
        }
        Debug.LogError($"[PromptWorld] No free layer slot for '{layerName}'.");
    }
}
