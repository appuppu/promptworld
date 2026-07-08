using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>CLI entry points for headless scene rebuilds and WebGL builds.</summary>
public static class BuildScript
{
    public static void RebuildScenes()
    {
        MenuSceneBuilder.Build();
        StageSceneBuilder.Build();
    }

    public static void BuildWebGL()
    {
        PlayerSettings.companyName = "Prompt World";
        PlayerSettings.productName = "Prompt World";
        PlayerSettings.runInBackground = true;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        // Serve correctly from any static host without header configuration.
        PlayerSettings.WebGL.decompressionFallback = true;

        var scenes = new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Stage.unity" };
        BuildReport report = BuildPipeline.BuildPlayer(scenes, "Builds/WebGL", BuildTarget.WebGL, BuildOptions.None);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[PromptWorld] WebGL build failed: {report.summary.result}");
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log($"[PromptWorld] WebGL build OK ({report.summary.totalSize / (1024 * 1024)} MB) -> Builds/WebGL");
    }

    public static void BuildIOS()
    {
        ApplyMobileSettings(NamedBuildTarget.iOS);
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;

        BuildReport report = BuildPipeline.BuildPlayer(
            Scenes(), "Builds/iOS", BuildTarget.iOS, BuildOptions.None);
        Finish(report, "Builds/iOS (Xcode project)");
    }

    public static void BuildAndroid()
    {
        ApplyMobileSettings(NamedBuildTarget.Android);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        BuildReport report = BuildPipeline.BuildPlayer(
            Scenes(), "Builds/Android/PromptWorld.apk", BuildTarget.Android, BuildOptions.None);
        Finish(report, "Builds/Android/PromptWorld.apk");
    }

    private static void ApplyMobileSettings(NamedBuildTarget target)
    {
        PlayerSettings.companyName = "Prompt World";
        PlayerSettings.productName = "Prompt World";
        PlayerSettings.SetApplicationIdentifier(target, "com.appuppu.promptworld");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
    }

    private static string[] Scenes()
    {
        return new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Stage.unity" };
    }

    private static void Finish(BuildReport report, string label)
    {
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[PromptWorld] Build failed: {report.summary.result}");
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log($"[PromptWorld] Build OK -> {label}");
    }
}
