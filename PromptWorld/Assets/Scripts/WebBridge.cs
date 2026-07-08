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

    public static void Copy(string text) => PW_CopyToClipboard(text);
    public static void OpenUrl(string url) => PW_OpenUrl(url);
#else
    public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;
    public static void OpenUrl(string url) => Application.OpenURL(url);
#endif
}
