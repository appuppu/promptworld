using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEditor.iOS.Xcode;

/// <summary>
/// Registers deep-link handling into the native builds so shared stage URLs
/// open the app:
///   - iOS: adds a custom URL scheme (promptworld://) AND the Associated Domains
///     entitlement + applinks for promptworldgame.org, into the Xcode project.
///   - Android: an AndroidManifest.xml with intent filters is shipped under
///     Assets/Plugins/Android (see that file) — nothing to patch here.
/// A tapped link arrives at runtime via Application.deepLinkActivated
/// (MenuController) or, at cold start, Application.absoluteURL.
/// </summary>
public static class DeepLinkPostProcess
{
    private const string Scheme = "promptworld";
    private const string Domain = "promptworldgame.org";

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        // Runtime target check (NOT a #if UNITY_IOS guard) — in batch builds the
        // editor's active platform may not be iOS when this file is compiled, so
        // a compile-time guard would strip the whole body and no-op the build.
        if (target != BuildTarget.iOS) return;

        // 1. Custom URL scheme in Info.plist (promptworld://...)
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        var root = plist.root;

        var urlTypes = root.CreateArray("CFBundleURLTypes");
        var urlDict = urlTypes.AddDict();
        urlDict.SetString("CFBundleURLName", "com.appuppu.promptworld");
        var schemes = urlDict.CreateArray("CFBundleURLSchemes");
        schemes.AddString(Scheme);
        plist.WriteToFile(plistPath);

        // 2. Associated Domains entitlement for universal links (https://domain/?stage=).
        //    Requires the apple-app-site-association file hosted at the domain.
        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);
        string targetGuid = proj.GetUnityMainTargetGuid();

        string entitlementsRel = "Unity-iPhone/PromptWorld.entitlements";
        string entitlementsAbs = Path.Combine(path, entitlementsRel);
        var ent = new PlistDocument();
        var entArr = ent.root.CreateArray("com.apple.developer.associated-domains");
        entArr.AddString($"applinks:{Domain}");
        // Write ONLY when the content actually changes: rewriting an identical
        // file bumps its mtime and Xcode hard-fails the next incremental build
        // with "entitlements file was modified during the build".
        string tmpEnt = entitlementsAbs + ".tmp";
        ent.WriteToFile(tmpEnt);
        string newBytes = File.ReadAllText(tmpEnt);
        string oldBytes = File.Exists(entitlementsAbs) ? File.ReadAllText(entitlementsAbs) : null;
        if (oldBytes != newBytes) File.WriteAllText(entitlementsAbs, newBytes);
        File.Delete(tmpEnt);
        // idempotent: append-mode rebuilds run this every time — re-adding the
        // file entry each build slowly corrupts the pbxproj (duplicate GUIDs)
        if (!proj.ContainsFileByProjectPath(entitlementsRel)) proj.AddFile(entitlementsRel, entitlementsRel);
        proj.SetBuildProperty(targetGuid, "CODE_SIGN_ENTITLEMENTS", entitlementsRel);
        proj.WriteToFile(projPath);

        Debug.Log("[PromptWorld] iOS deep links registered (scheme + associated domains).");
    }
}
