using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Platform-agnostic ad entry point. One call — Ads.ShowInterstitial() — fires
/// a full-screen interstitial on game over. The platform split lives here only:
///   - WebGL: a JS overlay ad via the jslib bridge (AdSense / web-game SDK).
///   - iOS/Android: Google AdMob via the Mobile Ads plugin (wired in once the
///     plugin + real ad-unit IDs are added; until then this no-ops gracefully).
/// Currently uses TEST placements so the flow works end-to-end before real IDs
/// are configured. A frequency cap keeps failures from spamming ads.
/// </summary>
public static class Ads
{
    // Don't show an interstitial more than once per this many seconds.
    private const float MinIntervalSeconds = 45f;
    private static float lastShownTime = -1000f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void PW_ShowInterstitial();
    [DllImport("__Internal")] private static extern int PW_AdsReady();
#endif

    /// <summary>Show a full-screen ad if enough time has passed since the last one.
    /// The cap is armed ONLY when an ad actually displays — a not-ready miss must
    /// not consume the window, or rapid retries during no-fill would never see an
    /// ad at all (that exact bug shipped in the first AdMob build).</summary>
    public static void ShowInterstitial()
    {
        if (Time.realtimeSinceStartup - lastShownTime < MinIntervalSeconds) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        // web overlay gives no success signal — arm the cap on the attempt
        lastShownTime = Time.realtimeSinceStartup;
        try { PW_ShowInterstitial(); } catch { /* ad SDK absent — ignore */ }
#elif (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        if (AdMobBridge.TryShowInterstitial()) lastShownTime = Time.realtimeSinceStartup;
#else
        lastShownTime = Time.realtimeSinceStartup;
        Debug.Log("[Ads] (editor) interstitial would show here.");
#endif
    }
}
