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
/// TO TEST THE AD FLOW (when live ads don't show — usually inventory/review):
///   Add the PROMPTWORLD_ADTEST define (Player Settings → Scripting Define
///   Symbols, alongside PROMPTWORLD_ADMOB). It swaps in Google's public test
///   interstitials, which ALWAYS fill. Remove the define to restore the live
///   IDs — no code edit needed, so production can't be shipped with test ads by
///   accident. Test IDs: Android ca-app-pub-3940256099942544/1033173712,
///   iOS ca-app-pub-3940256099942544/4411468910.
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

    private static string UnitId =>
#if PROMPTWORLD_ADTEST
#if UNITY_ANDROID
        AndroidTestInterstitialId;
#elif UNITY_IOS
        IosTestInterstitialId;
#else
        AndroidTestInterstitialId;
#endif
#else
#if UNITY_ANDROID
        AndroidInterstitialId;
#elif UNITY_IOS
        IosInterstitialId;
#else
        AndroidInterstitialId;
#endif
#endif

    /// <summary>Call once at startup (e.g. from AppBootstrap) so the SDK is ready
    /// and the first interstitial is preloaded before it's needed.</summary>
    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        MobileAds.Initialize(_ => LoadInterstitial());
    }

    private static void LoadInterstitial()
    {
        var request = new AdRequest();
        InterstitialAd.Load(UnitId, request, (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogWarning("[AdMobBridge] interstitial load failed: " + error);
                return;
            }
            interstitial = ad;
            // Reload the next one after this one closes.
            ad.OnAdFullScreenContentClosed += LoadInterstitial;
            ad.OnAdFullScreenContentFailed += _ => LoadInterstitial();
        });
    }

    public static void ShowInterstitial()
    {
        if (!initialized) Initialize();
        if (interstitial != null && interstitial.CanShowAd())
        {
            interstitial.Show();
        }
        else
        {
            // Not ready — kick off a load so it's ready next time.
            LoadInterstitial();
        }
    }
#else
    private static bool warned;

    /// <summary>No-op until the AdMob plugin + PROMPTWORLD_ADMOB define are set.</summary>
    public static void Initialize() { }

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
