using UnityEngine;

/// <summary>
/// Insets a RectTransform to the device safe area (avoids the notch, rounded
/// corners and home indicator on modern phones). Re-applies when the safe area
/// changes (rotation, browser resize). Attach to a full-screen panel that all
/// UI content lives under.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform rect;
    private Rect lastSafe;
    private Vector2Int lastScreen;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != lastSafe ||
            Screen.width != lastScreen.x || Screen.height != lastScreen.y)
        {
            Apply();
        }
    }

    private void Apply()
    {
        lastSafe = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);

        if (Screen.width <= 0 || Screen.height <= 0) return;

        Vector2 min = lastSafe.position;
        Vector2 max = lastSafe.position + lastSafe.size;
        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
