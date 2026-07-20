using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>Browser interop (clipboard, new-tab links) with editor fallbacks.</summary>
public static class WebBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void PW_CopyToClipboard(string text);
    [DllImport("__Internal")] private static extern void PW_OpenUrl(string url);
    [DllImport("__Internal")] private static extern string PW_GetLang();
    [DllImport("__Internal")] private static extern void PW_SetLang(string lang);
    [DllImport("__Internal")] private static extern void PW_Share(string text, string url);
    [DllImport("__Internal")] private static extern int PW_IsMobile();
    [DllImport("__Internal")] private static extern void PW_SetUrlStage(string id, string key);

    public static void Copy(string text) => PW_CopyToClipboard(text);
    public static void OpenUrl(string url) => PW_OpenUrl(url);
    public static string GetBrowserLang() => PW_GetLang();
    public static void SaveLang(string lang) => PW_SetLang(lang);
    /// <summary>True on a mobile browser (userAgent-based; reliable on WebGL).</summary>
    public static bool IsMobileBrowser() => PW_IsMobile() != 0;
    /// <summary>Reflect the current stage id (and creator editKey, if any) in the
    /// browser address bar (?stage=id[&key=editKey]).</summary>
    public static void SetUrlStage(string id, string key = null) => PW_SetUrlStage(id, key);
    /// <summary>Native share sheet if available (mobile browsers), else opens an X/Twitter intent.</summary>
    public static void Share(string text, string url) => PW_Share(text, url);
#else
    public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;
    public static void OpenUrl(string url) => Application.OpenURL(url);
    // Native language detection from the device OS locale (WebGL reads the
    // browser). Loc maps this string through its own detect logic.
    public static string GetBrowserLang()
    {
        switch (Application.systemLanguage)
        {
            case SystemLanguage.Japanese: return "ja";
            case SystemLanguage.Chinese:
            case SystemLanguage.ChineseSimplified:
            case SystemLanguage.ChineseTraditional: return "zh";
            case SystemLanguage.Spanish: return "es";
            case SystemLanguage.Korean: return "ko";
            case SystemLanguage.English: return "en";
            default: return null;
        }
    }
    // Persist the chosen language on device (WebGL uses localStorage). Loc also
    // writes PlayerPrefs, so this is belt-and-suspenders / future-proofing.
    public static void SaveLang(string lang)
    {
        PlayerPrefs.SetString("pw_lang", lang);
        PlayerPrefs.Save();
    }
    public static bool IsMobileBrowser() => Application.isMobilePlatform;
    public static void SetUrlStage(string id, string key = null) { }
    public static void Share(string text, string url) => NativeShare.Share(text, url);
#endif
}
