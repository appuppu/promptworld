using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Writes the real AdMob App IDs into the Google Mobile Ads plugin's settings
/// asset (Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset) —
/// the plugin injects them into Info.plist (GADApplicationIdentifier) and the
/// merged AndroidManifest at build time. Uses reflection so the project still
/// compiles when the plugin package is absent. Runs automatically before every
/// iOS/Android build; also available from the menu for a manual run.
/// </summary>
public static class AdMobSetup
{
    // AdMob app "promptworld" — App IDs (NOT ad unit ids; those live in AdMobBridge).
    const string AndroidAppId = "ca-app-pub-3107120992746486~1047842996";
    const string IosAppId = "ca-app-pub-3107120992746486~6247510427";

    [MenuItem("PromptWorld/Configure AdMob App IDs")]
    public static void Configure()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings"))
            .FirstOrDefault(t => t != null);
        if (type == null)
        {
            Debug.LogWarning("[AdMobSetup] Google Mobile Ads plugin not found — skipping App ID setup.");
            return;
        }
        var load = type.GetMethod("LoadInstance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var settings = (ScriptableObject)load.Invoke(null, null);
        var so = new SerializedObject(settings);
        var android = so.FindProperty("adMobAndroidAppId");
        var ios = so.FindProperty("adMobIOSAppId");
        if (android == null || ios == null)
        {
            Debug.LogError("[AdMobSetup] GoogleMobileAdsSettings fields renamed — update AdMobSetup.cs.");
            return;
        }
        android.stringValue = AndroidAppId;
        ios.stringValue = IosAppId;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[AdMobSetup] AdMob App IDs written to GoogleMobileAdsSettings.");
    }
}

class AdMobSetupPreBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.iOS || report.summary.platform == BuildTarget.Android)
            AdMobSetup.Configure();
    }
}
