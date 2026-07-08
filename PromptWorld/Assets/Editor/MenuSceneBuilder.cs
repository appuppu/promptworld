using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds the title / stage-select scene. Stage buttons themselves are
/// created at runtime by MenuController from the stage manifest.
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

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        CreateText(canvasGo.transform, "Title", "PROMPT WORLD", 100,
            new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1400f, 140f));
        CreateText(canvasGo.transform, "Tagline", "worlds made of prompts", 30,
            new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1000f, 50f));

        var listGo = new GameObject("StageList", typeof(RectTransform));
        listGo.transform.SetParent(canvasGo.transform, false);
        var listRect = listGo.GetComponent<RectTransform>();
        listRect.anchorMin = new Vector2(0.5f, 0.5f);
        listRect.anchorMax = new Vector2(0.5f, 0.5f);
        listRect.pivot = new Vector2(0.5f, 1f);
        listRect.anchoredPosition = new Vector2(0f, 100f);
        listRect.sizeDelta = new Vector2(800f, 600f);
        var layout = listGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        var controllerGo = new GameObject("MenuController");
        var controller = controllerGo.AddComponent<MenuController>();
        var so = new SerializedObject(controller);
        so.FindProperty("listRoot").objectReferenceValue = listRect;
        so.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[PromptWorld] Menu scene built: {ScenePath}");
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
