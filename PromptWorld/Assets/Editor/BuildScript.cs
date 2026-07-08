using UnityEditor;
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
}
