using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
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
        Canvas canvas = BuildUI(out TMP_Text timerText, out TMP_Text resultText, out TMP_Text stageNameText);

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var touchControls = canvas.gameObject.AddComponent<TouchControls>();
        var touchSo = new SerializedObject(touchControls);
        touchSo.FindProperty("canvas").objectReferenceValue = canvas;
        touchSo.ApplyModifiedPropertiesWithoutUndo();

        TMP_Text leaderboardText = CreateText(canvas.transform, "LeaderboardText", "", 24, TextAlignmentOptions.Center,
            anchor: new Vector2(0.5f, 0.5f), anchoredPos: new Vector2(0f, -25f), size: new Vector2(760f, 200f));
        Button voteGood = CreateTextButton(canvas.transform, "VoteGood", "GOOD", 26,
            new Vector2(0.5f, 0.5f), new Vector2(-100f, -150f), new Vector2(170f, 54f));
        Button voteBad = CreateTextButton(canvas.transform, "VoteBad", "BAD", 26,
            new Vector2(0.5f, 0.5f), new Vector2(100f, -150f), new Vector2(170f, 54f));
        Button retryButton = CreateTextButton(canvas.transform, "RetryButton", "RETRY", 42,
            new Vector2(0.5f, 0.5f), new Vector2(-280f, -240f), new Vector2(280f, 84f));
        Button menuButton = CreateTextButton(canvas.transform, "MenuButton", "MENU", 42,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -240f), new Vector2(240f, 84f));
        Button nextButton = CreateTextButton(canvas.transform, "NextButton", "NEXT >", 42,
            new Vector2(0.5f, 0.5f), new Vector2(280f, -240f), new Vector2(280f, 84f));
        Button shareButton = CreateTextButton(canvas.transform, "ShareButton", "SHARE URL", 32,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(340f, 64f));
        Button createLink = CreateTextButton(canvas.transform, "CreateLink", "CREATE YOUR OWN WORLD  >", 24,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -405f), new Vector2(620f, 50f));
        createLink.GetComponentInChildren<TMPro.TMP_Text>().color = new Color(1f, 1f, 1f, 0.6f);

        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        var gmSo = new SerializedObject(gm);
        gmSo.FindProperty("timerText").objectReferenceValue = timerText;
        gmSo.FindProperty("resultText").objectReferenceValue = resultText;
        gmSo.FindProperty("stageNameText").objectReferenceValue = stageNameText;
        gmSo.FindProperty("retryButton").objectReferenceValue = retryButton;
        gmSo.FindProperty("menuButton").objectReferenceValue = menuButton;
        gmSo.FindProperty("shareButton").objectReferenceValue = shareButton;
        gmSo.FindProperty("nextButton").objectReferenceValue = nextButton;
        gmSo.FindProperty("createLinkButton").objectReferenceValue = createLink;
        gmSo.FindProperty("voteGoodButton").objectReferenceValue = voteGood;
        gmSo.FindProperty("voteBadButton").objectReferenceValue = voteBad;
        gmSo.FindProperty("leaderboardText").objectReferenceValue = leaderboardText;
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
            new EditorBuildSettingsScene("Assets/Scenes/Menu.unity", true),
            new EditorBuildSettingsScene(ScenePath, true),
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

    private static Button CreateTextButton(Transform parent, string name, string label, float fontSize,
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
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        var button = go.AddComponent<Button>();
        button.targetGraphic = tmp;
        return button;
    }

    private static Canvas BuildUI(out TMP_Text timerText, out TMP_Text resultText, out TMP_Text stageNameText)
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        timerText = CreateText(canvasGo.transform, "TimerText", "60", 48, TextAlignmentOptions.Center,
            anchor: new Vector2(0.5f, 1f), anchoredPos: new Vector2(0f, -40f), size: new Vector2(400f, 100f));
        resultText = CreateText(canvasGo.transform, "ResultText", "", 68, TextAlignmentOptions.Center,
            anchor: new Vector2(0.5f, 0.5f), anchoredPos: new Vector2(0f, 165f), size: new Vector2(1200f, 240f));
        stageNameText = CreateText(canvasGo.transform, "StageNameText", "", 28, TextAlignmentOptions.Left,
            anchor: new Vector2(0f, 1f), anchoredPos: new Vector2(40f, -40f), size: new Vector2(600f, 60f));
        return canvas;
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
