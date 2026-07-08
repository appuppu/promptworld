using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds semi-transparent on-screen controls (◄ ► / JUMP) at runtime on
/// touch devices. Desktop players never see them.
/// </summary>
public class TouchControls : MonoBehaviour
{
    [SerializeField] private Canvas canvas;

    private void Start()
    {
        if (!Application.isMobilePlatform && !Input.touchSupported) return;
        if (canvas == null) canvas = GetComponent<Canvas>();

        CreateButton("<", new Vector2(0f, 0f), new Vector2(150f, 150f), new Vector2(180f, 180f),
            () => MobileInput.LeftHeld = true, () => MobileInput.LeftHeld = false);
        CreateButton(">", new Vector2(0f, 0f), new Vector2(360f, 150f), new Vector2(180f, 180f),
            () => MobileInput.RightHeld = true, () => MobileInput.RightHeld = false);
        CreateButton("JUMP", new Vector2(1f, 0f), new Vector2(-220f, 150f), new Vector2(240f, 240f),
            MobileInput.QueueJump, null);
    }

    private void CreateButton(string label, Vector2 anchor, Vector2 anchoredPos, Vector2 size,
        System.Action onDown, System.Action onUp)
    {
        var go = new GameObject($"Touch{label}", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.15f);

        var button = go.AddComponent<TouchButton>();
        button.onDown = onDown;
        button.onUp = onUp;

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 44;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.75f);
        tmp.raycastTarget = false;
    }
}
