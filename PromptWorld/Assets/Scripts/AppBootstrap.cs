using UnityEngine;

/// <summary>
/// One-time display/runtime setup that runs before any scene loads. Chiefly:
/// force a high, steady frame rate — without this, iOS/Android default to 30 fps
/// (or an unsteady rate), which reads as "choppy". We aim for 60 and disable the
/// vsync clamp so targetFrameRate is honored.
/// </summary>
public static class AppBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // vSyncCount must be 0 for Application.targetFrameRate to take effect.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        // Ask for App Tracking Transparency (iOS 14+) THEN warm up mobile ads so
        // the first interstitial is preloaded. On Android/editor this skips
        // straight to ad init. No-op unless the AdMob plugin + PROMPTWORLD_ADMOB
        // define are present.
#if UNITY_IOS || UNITY_ANDROID
        AttPrompt.RequestThenInitAds();
#endif
    }
}
