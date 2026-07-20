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

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<ResponsiveCanvasScaler>();

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var touchControls = canvasGo.AddComponent<TouchControls>();
        var touchSo = new SerializedObject(touchControls);
        touchSo.FindProperty("canvas").objectReferenceValue = canvas;
        touchSo.ApplyModifiedPropertiesWithoutUndo();

        // Everything lives under a SafeArea root.
        var safe = UI.Stretch(canvasGo.transform, "SafeArea");
        safe.gameObject.AddComponent<SafeAreaFitter>();

        // ---- HUD -----------------------------------------------------------
        // Top bar: left = timer + lives, right = MENU. Auto-laid, never overlaps.
        var topBar = new GameObject("TopBar", typeof(RectTransform));
        topBar.transform.SetParent(safe, false);
        var tbr = topBar.GetComponent<RectTransform>();
        tbr.anchorMin = new Vector2(0f, 1f); tbr.anchorMax = new Vector2(1f, 1f); tbr.pivot = new Vector2(0.5f, 1f);
        tbr.offsetMin = new Vector2(UI.S3, -132f); tbr.offsetMax = new Vector2(-UI.S3, -UI.S2);

        var hudLeft = UI.Box((RectTransform)topBar.transform, "HudLeft", UI.Clear);
        hudLeft.anchorMin = new Vector2(0f, 0f); hudLeft.anchorMax = new Vector2(0f, 1f); hudLeft.pivot = new Vector2(0f, 0.5f);
        hudLeft.sizeDelta = new Vector2(500f, 0f); hudLeft.anchoredPosition = Vector2.zero;
        UI.VStack(hudLeft, 6f, TextAnchor.UpperLeft);
        var timerText = UI.Text(hudLeft, "TimerText", "60", 48f, UI.Fg, TextAlignmentOptions.Left, 1f, true);
        UI.Sized(timerText.gameObject, minH: 52f);
        var livesText = UI.Text(hudLeft, "LivesText", "", 26f, UI.Dim, TextAlignmentOptions.Left, 1f);
        UI.Sized(livesText.gameObject, minH: 20f);

        Button homeButton = UI.Button(topBar.transform, "HomeButton", "MENU", 22f, ghost: true);
        var hbr = (RectTransform)homeButton.transform;
        hbr.anchorMin = new Vector2(1f, 0.5f); hbr.anchorMax = new Vector2(1f, 0.5f); hbr.pivot = new Vector2(1f, 0.5f);
        hbr.sizeDelta = new Vector2(120f, 46f); hbr.anchoredPosition = Vector2.zero;

        // Key prompt: centered under the top bar.
        var keyText = UI.Text(safe, "KeyText", "", 26f, UI.Dim, TextAlignmentOptions.Center, 2f);
        var kr = (RectTransform)keyText.transform;
        kr.anchorMin = new Vector2(0f, 1f); kr.anchorMax = new Vector2(1f, 1f); kr.pivot = new Vector2(0.5f, 1f);
        kr.offsetMin = new Vector2(UI.S3, -178f); kr.offsetMax = new Vector2(-UI.S3, -144f);
        keyText.gameObject.SetActive(false);

        // Stage name: bottom-left, subtle.
        var stageNameText = UI.Text(safe, "StageNameText", "", 24f, UI.Dim, TextAlignmentOptions.Left, 1f);
        var snr = (RectTransform)stageNameText.transform;
        snr.anchorMin = new Vector2(0f, 0f); snr.anchorMax = new Vector2(1f, 0f); snr.pivot = new Vector2(0f, 0f);
        snr.offsetMin = new Vector2(UI.S3, UI.S2); snr.offsetMax = new Vector2(-UI.S3, UI.S2 + 34f);

        // ---- RESULT PANEL (a centered auto-layout column) ------------------
        // Full-screen dim backdrop; a centered VStack holds every result element
        // so hidden ones collapse and nothing ever overlaps.
        var resultRoot = UI.Box(safe, "ResultRoot", new Color(0f, 0f, 0f, 0.92f));
        resultRoot.anchorMin = Vector2.zero; resultRoot.anchorMax = Vector2.one;
        resultRoot.offsetMin = Vector2.zero; resultRoot.offsetMax = Vector2.zero;

        // Column fills the safe area (with side margins) rather than a fixed
        // 720px-tall box: on a landscape phone that fixed height poked above the
        // safe area and the CLEAR text collided with the browser URL bar. The
        // VStack is MiddleCenter so the content sits centered in whatever height
        // is actually available, never touching the top edge.
        var column = UI.Box(resultRoot, "Column", UI.Clear);
        column.anchorMin = new Vector2(0.5f, 0f); column.anchorMax = new Vector2(0.5f, 1f);
        column.pivot = new Vector2(0.5f, 0.5f);
        // Width fixed (560), height stretches to the safe area with top/bottom
        // insets so content is centered and never reaches the screen top edge.
        column.sizeDelta = new Vector2(560f, 0f);
        column.offsetMin = new Vector2(-280f, UI.S3);   // x: half width; y: bottom inset
        column.offsetMax = new Vector2(280f, -UI.S3);   // x: half width; y: top inset
        UI.VStack(column, UI.S1 + 2f, TextAnchor.MiddleCenter);

        var resultText = UI.Text(column, "ResultText", "", 60f, UI.Fg, TextAlignmentOptions.Center, 2f, true);
        resultText.enableWordWrapping = true;
        UI.Sized(resultText.gameObject, minH: 100f);

        var leaderboardText = UI.Text(column, "LeaderboardText", "", 22f, UI.Dim, TextAlignmentOptions.Center, 1f);
        leaderboardText.enableWordWrapping = true;
        UI.Sized(leaderboardText.gameObject, minH: 0f);

        // Primary action row: RETRY / MENU / NEXT.
        var actionRow = UI.Box(column, "Actions", UI.Clear);
        UI.HStack(actionRow, UI.S1, TextAnchor.MiddleCenter);
        UI.Sized(actionRow.gameObject, minH: 60f);
        Button retryButton = MakeRowButton(actionRow, "RetryButton", "RETRY");
        Button menuButton = MakeRowButton(actionRow, "MenuButton", "MENU");
        Button nextButton = MakeRowButton(actionRow, "NextButton", "NEXT >");

        // Secondary row: SHARE / GOOD / BAD.
        var subRow = UI.Box(column, "SubActions", UI.Clear);
        UI.HStack(subRow, UI.S1, TextAnchor.MiddleCenter);
        UI.Sized(subRow.gameObject, minH: 48f);
        Button shareButton = MakeRowButton(subRow, "ShareButton", "SHARE URL", 40f);
        Button voteGood = MakeRowButton(subRow, "VoteGood", "GOOD", 40f);
        Button voteBad = MakeRowButton(subRow, "VoteBad", "BAD", 40f);

        // (No CREATE link on the play/result screen — it belongs in the menu.)

        resultText.gameObject.SetActive(false);

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
        gmSo.FindProperty("createLinkButton").objectReferenceValue = null;
        gmSo.FindProperty("voteGoodButton").objectReferenceValue = voteGood;
        gmSo.FindProperty("voteBadButton").objectReferenceValue = voteBad;
        gmSo.FindProperty("leaderboardText").objectReferenceValue = leaderboardText;
        gmSo.FindProperty("homeButton").objectReferenceValue = homeButton;
        gmSo.FindProperty("keyText").objectReferenceValue = keyText;
        gmSo.FindProperty("livesText").objectReferenceValue = livesText;
        gmSo.FindProperty("resultRoot").objectReferenceValue = resultRoot.gameObject;
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

    /// <summary>A flex-width button for a horizontal result-screen row.</summary>
    private static Button MakeRowButton(Transform parent, string name, string label, float minWidth = 60f)
    {
        var btn = UI.Button(parent, name, label, 24f);
        UI.Sized(btn.gameObject, minH: 56f, minW: minWidth, flexW: 1f);
        return btn;
    }
}
