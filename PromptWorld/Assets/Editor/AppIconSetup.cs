using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Assigns the master app icon (Assets/Editor/AppIcon/icon_1024.png) to every
/// iOS and Android icon slot. Unity downscales the single 1024 master to each
/// required size at build time, so we only maintain one file. Run once from the
/// menu (or it runs automatically before a mobile build via AppIconPreBuild).
///
/// The master is a flat, fully-opaque, square PNG (no alpha, no rounded corners)
/// — the exact form the App Store and Play require; the stores round the corners
/// themselves.
/// </summary>
public static class AppIconSetup
{
    const string MasterPath = "Assets/Editor/AppIcon/icon_1024.png";

    [MenuItem("PromptWorld/Configure App Icon")]
    public static void Configure()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MasterPath);
        if (tex == null)
        {
            Debug.LogError("[AppIconSetup] master icon not found at " + MasterPath);
            return;
        }

        Apply(NamedBuildTarget.iOS, tex);
        Apply(NamedBuildTarget.Android, tex);
        AssetDatabase.SaveAssets();
        Debug.Log("[AppIconSetup] app icon assigned to iOS + Android slots.");
    }

    private static void Apply(NamedBuildTarget platform, Texture2D tex)
    {
        // Fill every icon kind's every layer/size slot with the same master
        // texture (Unity 6 PlatformIcon API). Unity downscales the 1024 master to
        // each required size at build time.
        var kinds = PlayerSettings.GetSupportedIconKinds(platform);
        foreach (var kind in kinds)
        {
            var icons = PlayerSettings.GetPlatformIcons(platform, kind);
            foreach (var icon in icons)
            {
                var layers = new Texture2D[icon.maxLayerCount];
                for (int i = 0; i < layers.Length; i++) layers[i] = tex;
                icon.SetTextures(layers);
            }
            PlayerSettings.SetPlatformIcons(platform, kind, icons);
        }
    }
}

class AppIconPreBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 1;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.iOS ||
            report.summary.platform == BuildTarget.Android)
            AppIconSetup.Configure();
    }
}
