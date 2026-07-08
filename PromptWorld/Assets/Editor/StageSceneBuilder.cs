using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the generic "Stage" scene: camera, UI shell, GameManager and
/// StageLoader. All actual stage content is created at runtime by
/// StageLoader from a JSON file in StreamingAssets/Stages.
/// Menu: Prompt World > Build Stage Scene, or CLI -executeMethod StageSceneBuilder.Build.
/// </summary>
public static class StageSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Stage.unity";

    [MenuItem("Prompt World/Build Stage Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CameraFollow cameraFollow = BuildCamera();
        BuildUI(out TMP_Text timerText, out TMP_Text resultText, out TMP_Text stageNameText);

        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        var gmSo = new SerializedObject(gm);
        gmSo.FindProperty("timerText").objectReferenceValue = timerText;
        gmSo.FindProperty("resultText").objectReferenceValue = resultText;
        gmSo.FindProperty("stageNameText").objectReferenceValue = stageNameText;
        gmSo.ApplyModifiedPropertiesWithoutUndo();

        var loaderGo = new GameObject("StageLoader");
        var loader = loaderGo.AddComponent<StageLoader>();
        var loaderSo = new SerializedObject(loader);
        loaderSo.FindProperty("stageFile").stringValue = "stage-002.json";
        loaderSo.FindProperty("gameManager").objectReferenceValue = gm;
        loaderSo.FindProperty("cameraFollow").objectReferenceValue = cameraFollow;
        loaderSo.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene("Assets/Scenes/Day1.unity", true),
        };
        AssetDatabase.SaveAssets();
        Debug.Log($"[PromptWorld] Stage scene built: {ScenePath}");
    }

    private static CameraFollow BuildCamera()
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
        return go.AddComponent<CameraFollow>();
    }

    private static void BuildUI(out TMP_Text timerText, out TMP_Text resultText, out TMP_Text stageNameText)
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        timerText = CreateText(canvasGo.transform, "TimerText", "60", 48, TextAlignmentOptions.Center,
            anchor: new Vector2(0.5f, 1f), anchoredPos: new Vector2(0f, -40f), size: new Vector2(400f, 100f));
        resultText = CreateText(canvasGo.transform, "ResultText", "", 72, TextAlignmentOptions.Center,
            anchor: new Vector2(0.5f, 0.5f), anchoredPos: Vector2.zero, size: new Vector2(1200f, 260f));
        stageNameText = CreateText(canvasGo.transform, "StageNameText", "", 28, TextAlignmentOptions.Left,
            anchor: new Vector2(0f, 1f), anchoredPos: new Vector2(40f, -40f), size: new Vector2(600f, 60f));
    }

    private static TMP_Text CreateText(Transform parent, string name, string text, float fontSize,
        TextAlignmentOptions alignment, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
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
        tmp.alignment = alignment;
        tmp.color = Color.white;
        return tmp;
    }
}
