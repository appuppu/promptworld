using UnityEngine;

/// <summary>
/// Opens the OS share sheet on native platforms so players can share a stage to
/// any app (X, LINE, Messages, …), not just a Twitter web page. WebGL uses
/// navigator.share via the jslib; this class covers iOS/Android/editor.
/// </summary>
public static class NativeShare
{
    public static void Share(string text, string url)
    {
        string message = string.IsNullOrEmpty(url) ? text : text + "\n" + url;
        // Always keep a clipboard copy as a reliable fallback.
        GUIUtility.systemCopyBuffer = message;

#if UNITY_ANDROID && !UNITY_EDITOR
        ShareAndroid(message);
#elif UNITY_IOS && !UNITY_EDITOR
        ShareIOS(message);
#else
        // Editor / other: fall back to the Twitter web intent so sharing is
        // still testable without a device.
        Application.OpenURL("https://twitter.com/intent/tweet?text=" +
            System.Uri.EscapeDataString(message));
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static void ShareAndroid(string message)
    {
        try
        {
            using var intentClass = new AndroidJavaClass("android.content.Intent");
            using var intent = new AndroidJavaObject("android.content.Intent");
            intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
            intent.Call<AndroidJavaObject>("setType", "text/plain");
            intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), message);

            using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
            using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Share");
            activity.Call("startActivity", chooser);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PromptWorld] Android share failed: " + e.Message);
        }
    }
#endif

#if UNITY_IOS && !UNITY_EDITOR
    // The iOS share sheet is invoked through a tiny native plugin (see
    // Plugins/iOS/NativeShare.mm). If the plugin is missing at link time this
    // call is stripped; the clipboard copy above still gives a working fallback.
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _pwNativeShare(string message);

    private static void ShareIOS(string message)
    {
        try { _pwNativeShare(message); }
        catch (System.Exception e) { Debug.LogWarning("[PromptWorld] iOS share failed: " + e.Message); }
    }
#endif
}
