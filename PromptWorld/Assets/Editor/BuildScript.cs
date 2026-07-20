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

    // TAC-only mobile app (user decision 2026-07-18): ships just the tac scene.
    public static void BuildTacIOS()
    {
        PlayerSettings.companyName = "Prompt World";
        PlayerSettings.productName = "Prompt World TAC";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.appuppu.promptworldtac");
        // App Store rejects build number 0; ship at least 1. Bump this (or the
        // bundleVersion) for every new submission of the same version string.
        if (string.IsNullOrEmpty(PlayerSettings.iOS.buildNumber) || PlayerSettings.iOS.buildNumber == "0")
            PlayerSettings.iOS.buildNumber = "1";
        // Portrait-only (user decision 2026-07-20): the TAC app plays vertically —
        // swipe move + auto-fire + thumb-arc buttons. The classic 2D app
        // (ApplyMobileSettings) stays landscape.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        // bake the signing team into every generated Xcode project so the
        // user's selection survives rebuilds (it lives in the pbxproj Unity rewrites)
        PlayerSettings.iOS.appleDeveloperTeamID = "M86QCKBHW8";
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

        // Dev and release builds must NOT append over each other: mixing
        // Development/non-Development artifacts in one Xcode project leaves
        // inconsistent object files behind (seen as "GenerateDSYMFile failed"
        // at Archive). If the existing output was built in the other mode,
        // regenerate fresh — the user must close/reopen the Xcode workspace once.
        const string modeMarker = "Builds/iOS-Tac/.pw_buildmode";
        string wantMode = TacIosDev ? "dev" : "release";
        if (System.IO.Directory.Exists("Builds/iOS-Tac"))
        {
            // no marker = built before this guard existed (mode unknown, and
            // today's release→dev appends DID mix) — always regenerate once
            string prevMode = System.IO.File.Exists(modeMarker) ? System.IO.File.ReadAllText(modeMarker).Trim() : "unknown";
            if (prevMode != wantMode)
            {
                Debug.Log("[BuildScript] build mode changed (" + prevMode + " -> " + wantMode + ") — regenerating Builds/iOS-Tac fresh. Close and reopen the Xcode workspace after this build.");
                System.IO.Directory.Delete("Builds/iOS-Tac", true);
            }
        }
        // Append into the existing Xcode project when present so Xcode can stay
        // open across rebuilds (script/scene changes flow in; just press Run).
        var opts = System.IO.Directory.Exists("Builds/iOS-Tac")
            ? BuildOptions.AcceptExternalModificationsToPlayer
            : BuildOptions.None;
        if (TacIosDev) opts |= BuildOptions.Development;
        BuildReport report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/Tac.unity" }, "Builds/iOS-Tac", BuildTarget.iOS, opts);
        if (report.summary.result == BuildResult.Succeeded)
            System.IO.File.WriteAllText(modeMarker, wantMode);
        Finish(report, "Builds/iOS-Tac (Xcode project, " + (opts.HasFlag(BuildOptions.AcceptExternalModificationsToPlayer) ? "append" : "fresh")
            + (TacIosDev ? ", DEVELOPMENT build → TEST ads" : ", release → LIVE ads") + ")");
        TacIosDev = false;
    }

    static bool TacIosDev;

    // Development twin of BuildTacIOS: same Xcode project, but flagged as a
    // Unity Development Build — Debug.isDebugBuild turns true on device, which
    // flips AdMobBridge onto Google's always-fill TEST ad units automatically
    // (clicking live ads on a dev device risks invalid-traffic flags). Use for
    // day-to-day device runs; ship with plain BuildTacIOS.
    public static void BuildTacIOSDev()
    {
        TacIosDev = true;
        BuildTacIOS();
    }

    // Android twin of BuildTacIOS: same portrait-only TAC-only app, straight to
    // an installable APK (debug-signed; a Play upload later needs a keystore + AAB).
    public static void BuildTacAndroid()
    {
        PlayerSettings.companyName = "Prompt World";
        PlayerSettings.productName = "Prompt World TAC";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.appuppu.promptworldtac");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        // The custom manifest (Plugins/Android/AndroidManifest.xml) declares
        // UnityPlayerActivity for its deep-link filters, so the entry point must
        // be Activity — the project default is GameActivity, which would ship a
        // second, filter-less launcher.
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

        // The debug-signed APK is a local-testing artifact — build it as a
        // Development Build so AdMobBridge serves TEST ads on it (the release
        // AAB below stays non-development → live ads).
        BuildReport report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/Tac.unity" },
            "Builds/Android-Tac/PromptWorldTac.apk", BuildTarget.Android, BuildOptions.Development);
        Finish(report, "Builds/Android-Tac/PromptWorldTac.apk (DEVELOPMENT build → TEST ads)");
    }

    // Play Store upload build: a RELEASE-SIGNED .aab (App Bundle). Play requires
    // an AAB signed with a real upload keystore — the debug APK above cannot be
    // uploaded. Secrets come from the environment (never hard-coded / committed):
    //   PW_KEYSTORE_PATH   absolute path to the .keystore
    //   PW_KEYSTORE_PASS   store password
    //   PW_KEY_ALIAS       key alias
    //   PW_KEY_PASS        key password
    // Generate a keystore once with:
    //   keytool -genkey -v -keystore promptworld.keystore -alias promptworld \
    //           -keyalg RSA -keysize 2048 -validity 10000
    public static void BuildTacAndroidRelease()
    {
        PlayerSettings.companyName = "Prompt World";
        PlayerSettings.productName = "Prompt World TAC";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.appuppu.promptworldtac");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        // Play requires 64-bit; ship both ABIs so the bundle covers all devices.
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

        string ksPath = System.Environment.GetEnvironmentVariable("PW_KEYSTORE_PATH");
        string ksPass = System.Environment.GetEnvironmentVariable("PW_KEYSTORE_PASS");
        string alias = System.Environment.GetEnvironmentVariable("PW_KEY_ALIAS");
        string keyPass = System.Environment.GetEnvironmentVariable("PW_KEY_PASS");
        if (string.IsNullOrEmpty(ksPath) || string.IsNullOrEmpty(ksPass) ||
            string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(keyPass))
        {
            Debug.LogError("[BuildScript] Release build needs PW_KEYSTORE_PATH / " +
                "PW_KEYSTORE_PASS / PW_KEY_ALIAS / PW_KEY_PASS in the environment. Aborting.");
            EditorApplication.Exit(1);
            return;
        }
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = ksPath;
        PlayerSettings.Android.keystorePass = ksPass;
        PlayerSettings.Android.keyaliasName = alias;
        PlayerSettings.Android.keyaliasPass = keyPass;
        // Output an App Bundle (.aab), not an APK.
        EditorUserBuildSettings.buildAppBundle = true;

        BuildReport report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/Tac.unity" },
            "Builds/Android-Tac/PromptWorldTac.aab", BuildTarget.Android, BuildOptions.None);
        Finish(report, "Builds/Android-Tac/PromptWorldTac.aab (release-signed App Bundle)");
    }

    // macOS standalone of the TAC scene — lets the agent launch + screenshot
    // the app locally to verify screens before asking the human to run Xcode.
    public static void BuildTacMac()
    {
        PlayerSettings.companyName = "Prompt World";
        PlayerSettings.productName = "PromptWorldTac";
        // portrait-shaped window (iPhone-ish aspect) so local screenshots
        // exercise the same layout the phone shows
        PlayerSettings.defaultScreenWidth = 420;
        PlayerSettings.defaultScreenHeight = 910;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        BuildReport report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/Tac.unity" }, "Builds/Mac-Tac.app", BuildTarget.StandaloneOSX, BuildOptions.None);
        Finish(report, "Builds/Mac-Tac.app");
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
        // Match the custom manifest's UnityPlayerActivity (see BuildTacAndroid).
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

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
