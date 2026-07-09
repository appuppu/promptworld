using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds the title / stage-select scene: title, search field, scrollable
/// stage list (entries created at runtime by MenuController) and the
/// creator-funnel button.
/// Menu: Prompt World > Build Menu Scene, or CLI -executeMethod MenuSceneBuilder.Build.
/// </summary>
public static class MenuSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Menu.unity";

    [MenuItem("Prompt World/Build Menu Scene")]
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

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        CreateText(canvasGo.transform, "Title", "PROMPT WORLD", 76,
            new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1400f, 110f));

        TMP_InputField searchInput = CreateInputField(canvasGo.transform, "Search", "SEARCH STAGES",
            new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(680f, 58f), 28);
        TMP_InputField nameInput = CreateInputField(canvasGo.transform, "PlayerName", "PLAYER NAME",
            new Vector2(1f, 1f), new Vector2(-30f, -28f), new Vector2(300f, 48f), 22);
        RectTransform content = CreateScrollList(canvasGo.transform);
        Button createButton = CreateFooterButton(canvasGo.transform);

        var controllerGo = new GameObject("MenuController");
        var controller = controllerGo.AddComponent<MenuController>();
        var so = new SerializedObject(controller);
        so.FindProperty("listRoot").objectReferenceValue = content;
        so.FindProperty("searchInput").objectReferenceValue = searchInput;
        so.FindProperty("nameInput").objectReferenceValue = nameInput;
        so.FindProperty("createButton").objectReferenceValue = createButton;
        so.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[PromptWorld] Menu scene built: {ScenePath}");
    }

    private static TMP_InputField CreateInputField(Transform parent, string name, string placeholderText,
        Vector2 anchor, Vector2 anchoredPos, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var input = go.AddComponent<TMP_InputField>();

        var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        areaGo.transform.SetParent(go.transform, false);
        var areaRect = areaGo.GetComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(20f, 6f);
        areaRect.offsetMax = new Vector2(-20f, -6f);

        TextMeshProUGUI text = CreateInputText(areaGo.transform, "Text", "", 1f, fontSize);
        TextMeshProUGUI placeholder = CreateInputText(areaGo.transform, "Placeholder", placeholderText, 0.35f, fontSize);

        input.textViewport = areaRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.caretColor = Color.white;
        input.customCaretColor = true;
        input.selectionColor = new Color(1f, 1f, 1f, 0.3f);
        return input;
    }

    private static TextMeshProUGUI CreateInputText(Transform parent, string name, string content, float alpha, float fontSize = 28)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(1f, 1f, 1f, alpha);
        return tmp;
    }

    private static RectTransform CreateScrollList(Transform parent)
    {
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(parent, false);
        var scrollRect = scrollGo.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0.5f, 0f);
        scrollRect.anchorMax = new Vector2(0.5f, 1f);
        scrollRect.pivot = new Vector2(0.5f, 0.5f);
        scrollRect.sizeDelta = new Vector2(800f, -470f);
        scrollRect.anchoredPosition = new Vector2(0f, -85f);
        scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = new Vector2(0f, 0f);
        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.viewport = viewportRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 40f;

        return contentRect;
    }

    private static Button CreateFooterButton(Transform parent)
    {
        var go = new GameObject("CreateButton", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 44f);
        rect.sizeDelta = new Vector2(640f, 64f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "CREATE YOUR OWN WORLD  →";
        tmp.fontSize = 27;
        tmp.characterSpacing = 6f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.85f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = tmp;
        return button;
    }

    private static void CreateText(Transform parent, string name, string text, float fontSize,
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
    }
}
