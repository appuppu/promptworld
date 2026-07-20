using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Cross-platform haptic feedback. Light/Medium/Heavy taps for UI interactions
/// and a Success buzz for the world-first-clear celebration. iOS uses the native
/// taptic engine (HapticBridge.mm); Android falls back to Handheld.Vibrate. No-op
/// in the editor and on unsupported devices.
/// </summary>
public static class Haptic
{
    public enum Style { Light = 0, Medium = 1, Heavy = 2, Success = 3 }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void PW_Haptic(int style);
#endif

    public static void Play(Style style)
    {
#if UNITY_IOS && !UNITY_EDITOR
        PW_Haptic((int)style);
#elif UNITY_ANDROID && !UNITY_EDITOR
        // Android has no per-style API without a plugin; a short system vibrate is
        // the closest cross-device fallback (heavier styles omitted to stay subtle).
        if (style != Style.Light) Handheld.Vibrate();
#endif
    }

    public static void Tap() { Play(Style.Light); }
    public static void Success() { Play(Style.Success); }
}
