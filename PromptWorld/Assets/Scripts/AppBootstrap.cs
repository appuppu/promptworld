using System.Collections;
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

#if UNITY_IOS || UNITY_ANDROID
        // Ads/ATT boot moved to a runner: iOS SILENTLY IGNORES an ATT request
        // made before the app is active (the prompt never appears AND the
        // completion callback never fires), which left AdMob uninitialized for
        // the whole session — the "ads never show" bug. The runner waits for
        // focus + a beat, then asks; a timeout net initializes ads regardless.
        var go = new GameObject("AdsBootstrap");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<AdsBootstrapRunner>();
#endif
    }
}

public class AdsBootstrapRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        yield return new WaitForSeconds(0.8f);
        while (!Application.isFocused) yield return null;
        AttPrompt.RequestThenInitAds();
        // Safety net: if the ATT completion never arrives (OS quirk, restricted
        // devices), initialize ads anyway — AdMob serves non-personalized ads
        // without consent, and Initialize() self-guards against double calls.
        yield return new WaitForSeconds(6f);
        AttPrompt.ForceInitAds();
        Destroy(gameObject);
    }
}
