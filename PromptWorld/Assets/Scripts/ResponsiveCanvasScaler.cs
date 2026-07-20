using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the UI readable across every window shape — landscape desktop, tall
/// portrait browser, and phones — by adapting the CanvasScaler to the current
/// aspect ratio every frame the screen size changes.
///
/// The scenes are authored around a landscape reference. On a narrow/portrait
/// window, matching the reference by height alone makes wide rows overflow and
/// text overlap; matching by width alone makes a wide window's text tiny. So we
/// blend: below the design aspect we lean toward matching WIDTH (wide rows fit),
/// above it toward HEIGHT. Phones also use a smaller reference so touch targets
/// come out physically larger.
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
public class ResponsiveCanvasScaler : MonoBehaviour
{
    private const float DesignAspect = 1920f / 1080f; // ~1.78 landscape

    private CanvasScaler scaler;
    private int lastW, lastH;

    private void Awake()
    {
        scaler = GetComponent<CanvasScaler>();
        Apply();
    }

    private void Update()
    {
        // Browser windows resize freely; re-apply whenever the size changes.
        if (Screen.width != lastW || Screen.height != lastH) Apply();
    }

    private void Apply()
    {
        if (scaler == null) return;
        lastW = Screen.width;
        lastH = Screen.height;

        float aspect = lastH > 0 ? (float)lastW / lastH : DesignAspect;
        bool handheld = IsHandheld();

        // Phones get a much smaller reference (bigger UI/touch targets); desktop
        // keeps the authored one.
        Vector2 reference = handheld ? new Vector2(1100f, 620f) : new Vector2(1920f, 1080f);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = reference;

        // Aspect-aware match: portrait/narrow -> match width so wide rows fit;
        // wide -> match height so text isn't tiny. Smoothly between.
        if (aspect >= DesignAspect)
        {
            scaler.matchWidthOrHeight = 1f; // match height
        }
        else
        {
            // Narrower than design: 0 = pure width match at very tall screens,
            // ramping to 1 as we approach the design aspect.
            float t = Mathf.InverseLerp(0.5f, DesignAspect, aspect);
            scaler.matchWidthOrHeight = Mathf.Clamp01(t) * 0.5f;
        }
    }

    private static bool IsHandheld()
    {
#if UNITY_IOS || UNITY_ANDROID
        return true;
#elif UNITY_WEBGL && !UNITY_EDITOR
        return WebBridge.IsMobileBrowser(); // userAgent-based; reliable on WebGL
#else
        return false;
#endif
    }
}
