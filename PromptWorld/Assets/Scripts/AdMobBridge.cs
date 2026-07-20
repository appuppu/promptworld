using UnityEngine;
#if PROMPTWORLD_ADMOB
using GoogleMobileAds.Api;
#endif

/// <summary>
/// Native (iOS/Android) interstitial bridge.
///
/// Two modes, switched by the PROMPTWORLD_ADMOB scripting-define symbol:
///  - WITHOUT the define (default): a safe no-op stub, so the project compiles
///    even though the Google Mobile Ads plugin isn't imported yet.
///  - WITH the define + the plugin imported: real AdMob interstitials.
///
/// TO ENABLE REAL ADS:
///  1. Import the Google Mobile Ads Unity plugin (from Google's GitHub releases,
///     or via the OpenUPM / registry package `com.google.ads.mobile`).
///  2. In AdMob, register the iOS and Android apps; put the AdMob App IDs in
///     GoogleMobileAdsSettings (the plugin's settings menu), and the
///     interstitial unit IDs into the constants below.
///  3. Add PROMPTWORLD_ADMOB to Player Settings → Scripting Define Symbols
///     (iOS + Android). Then it compiles against the SDK and shows real ads.
///
/// TEST vs LIVE ad units (automatic):
///   Development contexts — the Unity editor and any Development Build
///   (BuildTacIOSDev, the local BuildTacAndroid APK) — automatically use
///   Google's public TEST interstitials, which always fill. Release builds
///   (BuildTacIOS, BuildTacAndroidRelease) use the live IDs. The
///   PROMPTWORLD_ADTEST define still force-overrides a release build to test
///   ads if ever needed. Initialize() logs which mode is active.
/// </summary>
public static class AdMobBridge
{
    // Production interstitial unit IDs (AdMob app "promptworld").
    public const string AndroidInterstitialId = "ca-app-pub-3107120992746486/1630442714";
    public const string IosInterstitialId = "ca-app-pub-3107120992746486/1231675823";

    // Google's public TEST interstitials — ALWAYS fill, so they prove the ad
    // flow works independently of your account's live inventory / review state.
    // Activated by adding the PROMPTWORLD_ADTEST scripting-define symbol; remove
    // the define to go back to the production IDs (no code change to revert).
    public const string AndroidTestInterstitialId = "ca-app-pub-3940256099942544/1033173712";
    public const string IosTestInterstitialId = "ca-app-pub-3940256099942544/4411468910";

#if PROMPTWORLD_ADMOB
    private static bool initialized;
    private static InterstitialAd interstitial;
    private static bool loading;
    private static int failCount;

    // Hidden runner so retry delays can use a coroutine (static class has none).
    private class AdRunner : MonoBehaviour { }
    private static AdRunner runner;
    private static AdRunner Runner()
    {
        if (runner == null)
        {
            var go = new GameObject("AdMobRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<AdRunner>();
        }
        return runner;
    }

    // Test IDs are selected AUTOMATICALLY for any development context: the
    // Unity editor and every Development Build (Debug.isDebugBuild) — i.e.
    // BuildTacIOSDev and the local-install BuildTacAndroid APK. Release builds
    // (BuildTacIOS, BuildTacAndroidRelease) get the live IDs. PROMPTWORLD_ADTEST
    // still force-overrides a release build to test ads when needed. Serving
    // live units on dev devices risks invalid-traffic flags on the AdMob
    // account, so the default is: dev = always test ads.
    private static bool UseTestIds
    {
        get
        {
#if PROMPTWORLD_ADTEST
            return true;
#else
            return Debug.isDebugBuild;
#endif
        }
    }

    private static string UnitId
    {
        get
        {
#if UNITY_IOS
            return UseTestIds ? IosTestInterstitialId : IosInterstitialId;
#else
            return UseTestIds ? AndroidTestInterstitialId : AndroidInterstitialId;
#endif
        }
    }

    /// <summary>Call once at startup (e.g. from AppBootstrap) so the SDK is ready
    /// and the first interstitial is preloaded before it's needed.</summary>
    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        Debug.Log("[AdMobBridge] initializing with " + (UseTestIds ? "TEST" : "LIVE") + " ad units");
        // Deliver SDK callbacks on the Unity main thread. Without this the
        // load/closed events fire on a background thread, where scheduling the
        // next load (or anything touching Unity APIs) can die silently.
        MobileAds.RaiseAdEventsOnUnityMainThread = true;
        MobileAds.Initialize(_ => LoadInterstitial());
    }

    private static void LoadInterstitial()
    {
        if (loading) return;
        loading = true;
        Debug.Log("[AdMobBridge] loading interstitial " + UnitId);
        InterstitialAd.Load(UnitId, new AdRequest(), (ad, error) =>
        {
            loading = false;
            if (error != null || ad == null)
            {
                // No-fill / network errors are NORMAL (especially for brand-new
                // live units). Retry with backoff so one early failure doesn't
                // leave the whole session ad-less.
                failCount++;
                float delay = Mathf.Min(120f, 5f * (1 << Mathf.Min(failCount, 5)));
                Debug.LogWarning("[AdMobBridge] interstitial load failed (" + error + ") — retrying in " + delay + "s");
                Runner().StartCoroutine(RetryAfter(delay));
                return;
            }
            failCount = 0;
            interstitial = ad;
            Debug.Log("[AdMobBridge] interstitial loaded, ready to show");
            // Reload the next one after this one closes.
            ad.OnAdFullScreenContentClosed += LoadInterstitial;
            ad.OnAdFullScreenContentFailed += _ => LoadInterstitial();
        });
    }

    private static System.Collections.IEnumerator RetryAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        LoadInterstitial();
    }

    /// <summary>Show if one is loaded. Returns true only when an ad actually
    /// went full screen — callers use this to decide whether to arm frequency
    /// caps (a missed show must NOT consume the cap window).</summary>
    public static bool TryShowInterstitial()
    {
        if (!initialized) Initialize();
        if (interstitial != null && interstitial.CanShowAd())
        {
            var ad = interstitial;
            interstitial = null; // consumed; the Closed handler loads the next
            ad.Show();
            return true;
        }
        Debug.Log("[AdMobBridge] interstitial not ready — kicking a load for next time");
        LoadInterstitial();
        return false;
    }

    public static void ShowInterstitial() { TryShowInterstitial(); }
#else
    private static bool warned;

    /// <summary>No-op until the AdMob plugin + PROMPTWORLD_ADMOB define are set.</summary>
    public static void Initialize() { }

    public static bool TryShowInterstitial()
    {
        ShowInterstitial();
        return false;
    }

    public static void ShowInterstitial()
    {
        if (!warned)
        {
            warned = true;
            Debug.Log("[AdMobBridge] Interstitial requested. Import the Google " +
                      "Mobile Ads plugin and add the PROMPTWORLD_ADMOB define to enable ads.");
        }
    }
#endif
}
