using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

/// <summary>
/// App Tracking Transparency launch flow. On iOS 14+, AdMob wants the IDFA, so
/// Apple requires we show the ATT prompt (and ship the usage-description string
/// — injected into Info.plist by TacPostProcess). We request tracking ONCE at
/// launch, then initialize ads regardless of the answer: AdMob still serves
/// (non-personalized) ads when tracking is denied, so gameplay/monetization
/// proceeds either way.
///
/// Call AttPrompt.RequestThenInitAds() once from startup. On Android and in the
/// editor there is no ATT concept, so it goes straight to ad init.
/// </summary>
public static class AttPrompt
{
    private static bool done;

    public static void RequestThenInitAds()
    {
        if (done) return;
        done = true;

#if UNITY_IOS && !UNITY_EDITOR
        // Show the system prompt; OnTrackingResolved fires with the user's choice.
        PW_RequestTracking(OnTrackingResolved);
#else
        // Android / editor: no ATT — initialize ads immediately.
        InitAds();
#endif
    }

    /// <summary>Timeout fallback: initialize ads even if the ATT completion
    /// never fired. Safe to call repeatedly (AdMobBridge.Initialize self-guards).</summary>
    public static void ForceInitAds() { InitAds(); }

    private static void InitAds()
    {
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        AdMobBridge.Initialize();
#else
        Debug.Log("[AttPrompt] (editor) would initialize ads here.");
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    private delegate void TrackingCallback(int status);

    [DllImport("__Internal")]
    private static extern void PW_RequestTracking(TrackingCallback cb);

    [MonoPInvokeCallback(typeof(TrackingCallback))]
    private static void OnTrackingResolved(int status)
    {
        // status: 0 notDetermined, 1 restricted, 2 denied, 3 authorized.
        // We initialize ads in every case — denial just means non-personalized.
        Debug.Log("[AttPrompt] tracking authorization status: " + status);
        InitAds();
    }
#endif
}
