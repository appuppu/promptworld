using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds the title / stage-select scene with the UI design system (see UI.cs):
/// a controls column (title, name, search, vertical sort, settings, create) and
/// a stage list, arranged by MenuLayout into two columns (landscape) or stacked
/// (portrait), inside a SafeArea. No hand-placed pixels — auto-layout only.
/// Menu: Prompt World > Build Menu Scene, or CLI -executeMethod MenuSceneBuilder.Build.
/// </summary>
public static class MenuSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Menu.unity";

    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.AddComponent<AudioListener>();

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<ResponsiveCanvasScaler>();

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // Safe-area root: everything lives under here, clear of notch/home bar.
        var safe = UI.Stretch(canvasGo.transform, "SafeArea");
        safe.gameObject.AddComponent<SafeAreaFitter>();
        // Uniform screen padding.
        var padRoot = UI.Stretch(safe, "Pad", new Vector4(UI.S4, UI.S3, UI.S4, UI.S3));

        // ---- controls column (auto-stacked) --------------------------------
        var controls = UI.Box(padRoot, "Controls", UI.Clear);
        // No inner gutters: controls and the stage list both fill the Pad width
        // so the search box and the cards line up on BOTH edges. (In landscape
        // the two columns are already separated by MenuLayout's 0-40% / 42-100%
        // anchors, so no extra right gutter is needed here.)
        var vlist = UI.VStack(controls, UI.S1 + 4f, TextAnchor.UpperLeft,
            new RectOffset(0, 0, 0, 0));

        var title = UI.Text(controls, "Title", "PROMPT WORLD", UI.FontTitle, UI.Fg, TextAlignmentOptions.Left, 2f, true);
        UI.Sized(title.gameObject, minH: 68f);
        var tagline = UI.Text(controls, "Tagline", "WORLDS MADE OF PROMPTS", 14f, UI.Dim, TextAlignmentOptions.Left, 4f);
        UI.Sized(tagline.gameObject, minH: 20f);

        var nameInput = UI.Input(controls, "PlayerName", "PLAYER NAME", 26f);
        UI.Sized(nameInput.gameObject, minH: 56f);
        var searchInput = UI.Input(controls, "Search", "SEARCH STAGES…", 26f);
        UI.Sized(searchInput.gameObject, minH: 56f);

        // Sort buttons (NEW/TOP/HARD/EASY) are built at runtime; the layout group
        // (horizontal on portrait/mobile, vertical on wide desktop) is chosen by
        // MenuController based on aspect.
        var sortHolder = UI.Box(controls, "SortHolder", UI.Clear);
        UI.Sized(sortHolder.gameObject, minH: 48f, flexH: 0f);

        // ---- stage list (scroll, fixed frame) ------------------------------
        RectTransform listContent = BuildScrollList(padRoot, out RectTransform listPanel);

        // ---- CREATE bar: its OWN block so MenuLayout can pin it below the list
        // (portrait) or in the controls column (landscape). Always visible.
        // It holds the CREATE button (fills the width) plus a SETTINGS button
        // pinned to its right — so settings sits next to "create your own world"
        // instead of floating over the stage list (where it overlapped cards).
        var createBar = UI.Box(padRoot, "CreateBar", UI.Clear);
        const float SettingsW = 92f;   // wide enough for the localized "SETTINGS"
        var createButton = UI.Button(createBar, "CreateButton", "CREATE YOUR OWN WORLD  →", 24f);
        var cbRect = (RectTransform)createButton.transform;
        cbRect.anchorMin = Vector2.zero; cbRect.anchorMax = Vector2.one;
        cbRect.offsetMin = Vector2.zero;
        cbRect.offsetMax = new Vector2(-(SettingsW + UI.S1), 0f); // leave room for settings

        // SETTINGS button — visible bordered button, right of the CREATE button.
        // Label set to the localized "settings" word at runtime; smaller font so
        // it fits the button width without clipping.
        var settingsButton = UI.Button(createBar, "SettingsButton", "SETTINGS", 15f, ghost: false,
            align: TextAlignmentOptions.Center);
        var sbRect = (RectTransform)settingsButton.transform;
        sbRect.anchorMin = new Vector2(1f, 0f); sbRect.anchorMax = new Vector2(1f, 1f);
        sbRect.pivot = new Vector2(1f, 0.5f);
        sbRect.sizeDelta = new Vector2(SettingsW, 0f);
        sbRect.anchoredPosition = Vector2.zero;

        // ---- settings sheet/modal (hidden) ---------------------------------
        RectTransform settingsPanel = BuildSettingsPanel(safe);

        // ---- layout driver --------------------------------------------------
        var layout = canvasGo.AddComponent<MenuLayout>();
        var layoutSo = new SerializedObject(layout);
        layoutSo.FindProperty("controls").objectReferenceValue = controls;
        layoutSo.FindProperty("list").objectReferenceValue = listPanel;
        layoutSo.FindProperty("createBar").objectReferenceValue = createBar;
        layoutSo.ApplyModifiedPropertiesWithoutUndo();

        var controllerGo = new GameObject("MenuController");
        var controller = controllerGo.AddComponent<MenuController>();
        var so = new SerializedObject(controller);
        so.FindProperty("listRoot").objectReferenceValue = listContent;
        so.FindProperty("searchInput").objectReferenceValue = searchInput;
        so.FindProperty("nameInput").objectReferenceValue = nameInput;
        so.FindProperty("createButton").objectReferenceValue = createButton;
        so.FindProperty("sortHolder").objectReferenceValue = sortHolder;
        so.FindProperty("settingsButton").objectReferenceValue = settingsButton;
        so.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        so.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[PromptWorld] Menu scene built: {ScenePath}");
    }

    private static RectTransform BuildScrollList(Transform parent, out RectTransform panel)
    {
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(parent, false);
        panel = scrollGo.GetComponent<RectTransform>();
        // MenuLayout sets its anchors; start filled.
        panel.anchorMin = new Vector2(0.42f, 0f); panel.anchorMax = Vector2.one;
        panel.offsetMin = Vector2.zero; panel.offsetMax = Vector2.zero;
        scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        var vr = viewport.GetComponent<RectTransform>();
        vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var cr = content.GetComponent<RectTransform>();
        // Left-top pivot (not center) so childForceExpandWidth measures the
        // content width from the left edge and cards match the viewport width
        // exactly — a 0.5 pivot let the width drift and shaved the left edge.
        cr.anchorMin = new Vector2(0f, 1f); cr.anchorMax = new Vector2(1f, 1f); cr.pivot = new Vector2(0f, 1f);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12f;
        // Left/right padding so cards sit INSET from the viewport's RectMask2D
        // edge — otherwise the card (and its left accent bar / thumbnail frame)
        // hugs the clip boundary and the left edge gets shaved off on PC.
        vlg.padding = new RectOffset(16, 16, 4, 4);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = content.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = cr; scroll.viewport = vr;
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40f;
        return cr;
    }

    private static RectTransform BuildSettingsPanel(Transform parent)
    {
        // Dim backdrop covering the whole safe area, catches taps to close via a button.
        var backdrop = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(parent, false);
        var br = backdrop.GetComponent<RectTransform>();
        br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        // Centered card; MenuController fills its content at runtime.
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(backdrop.transform, false);
        var crd = card.GetComponent<RectTransform>();
        crd.anchorMin = new Vector2(0.5f, 0.5f); crd.anchorMax = new Vector2(0.5f, 0.5f);
        crd.pivot = new Vector2(0.5f, 0.5f);
        crd.sizeDelta = new Vector2(720f, 480f);
        card.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 1f);
        UI.AddBorder(card.transform, UI.Line);

        backdrop.SetActive(false);
        return br;
    }
}
