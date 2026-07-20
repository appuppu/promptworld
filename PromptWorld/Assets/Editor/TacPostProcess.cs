// Pins iOS signing so the user's team survives every Unity (re)generation of
// the Xcode project: team + automatic signing are applied to ALL targets and
// configurations after each build. Without this the pbxproj Unity rewrites
// comes back with an empty DEVELOPMENT_TEAM on some configs and Xcode demands
// re-selecting the team by hand.
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class TacPostProcess
{
    const string TeamId = "M86QCKBHW8";

    [PostProcessBuild(200)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);
        string app = proj.GetUnityMainTargetGuid();
        string fw = proj.GetUnityFrameworkTargetGuid();
        // SetTeamId adds the target to TargetAttributes without an existence
        // check and throws on append-mode rebuilds where it's already there;
        // DEVELOPMENT_TEAM as a plain build property is the idempotent route.
        try { proj.SetTeamId(app, TeamId); }
        catch (System.ArgumentException) { /* attribute already present from a prior append build */ }
        proj.SetBuildProperty(app, "DEVELOPMENT_TEAM", TeamId);
        proj.SetBuildProperty(app, "CODE_SIGN_STYLE", "Automatic");
        // Unity rewrites the entitlements file on every build; don't let Xcode
        // hard-fail if a rebuild lands between its read and its sign step.
        proj.SetBuildProperty(app, "CODE_SIGN_ALLOW_ENTITLEMENTS_MODIFICATION", "YES");
        proj.SetBuildProperty(fw, "DEVELOPMENT_TEAM", TeamId);

        // AttBridge.mm needs the ATT + AdSupport frameworks. Weak-link ATT so
        // the app still launches on iOS < 14 where the framework is absent.
        proj.AddFrameworkToProject(fw, "AppTrackingTransparency.framework", true);
        proj.AddFrameworkToProject(fw, "AdSupport.framework", true);

        // ---- ATT dialog copy, localized (5 languages) ----
        // iOS reads <bundle>/xx.lproj/InfoPlist.strings for the tracking dialog.
        // Ship each language as an xx.lproj FOLDER REFERENCE so the folder is
        // copied verbatim into the bundle root. Append-mode safe: files are only
        // rewritten when their bytes differ, folder refs added exactly once.
        var attCopy = new System.Collections.Generic.Dictionary<string, string>
        {
            { "en", "This lets us show ads relevant to you. Your choice never affects gameplay." },
            { "ja", "あなたに合った広告を表示するために使用します。どちらを選んでもゲームプレイには一切影響しません。" },
            { "zh-Hans", "用于向您展示更相关的广告。您的选择不会影响游戏体验。" },
            { "es", "Esto nos permite mostrarte anuncios relevantes. Tu elección nunca afecta al juego." },
            { "ko", "회원님에게 맞는 광고를 표시하는 데 사용됩니다. 어떤 선택을 해도 게임 플레이에는 영향이 없습니다." },
        };
        foreach (var kv in attCopy)
        {
            string lproj = kv.Key + ".lproj";
            string lprojDir = System.IO.Path.Combine(path, lproj);
            System.IO.Directory.CreateDirectory(lprojDir);
            string strings = "NSUserTrackingUsageDescription = \"" + kv.Value + "\";\n";
            string fp = System.IO.Path.Combine(lprojDir, "InfoPlist.strings");
            if (!System.IO.File.Exists(fp) || System.IO.File.ReadAllText(fp) != strings)
                System.IO.File.WriteAllText(fp, strings);
            if (!proj.ContainsFileByProjectPath(lproj))
            {
                string lguid = proj.AddFolderReference(lproj, lproj, PBXSourceTree.Source);
                proj.AddFileToBuild(app, lguid);
            }
        }
        proj.WriteToFile(projPath);

        // App Tracking Transparency: AdMob asks for the IDFA, so App Review
        // requires the NSUserTrackingUsageDescription string OR the app gets
        // rejected under Guideline 5.1.2. The AttPrompt runtime shows the system
        // dialog on first launch; this writes the copy the dialog displays.
        string plistPath = path + "/Info.plist";
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSUserTrackingUsageDescription",
            "This lets us show ads relevant to you. Your choice never affects gameplay.");
        // declare the languages the bundle localizes (the xx.lproj folders above)
        plist.root.SetString("CFBundleDevelopmentRegion", "en");
        var blocs = plist.root.CreateArray("CFBundleLocalizations");
        blocs.AddString("en"); blocs.AddString("ja"); blocs.AddString("zh-Hans");
        blocs.AddString("es"); blocs.AddString("ko");

        // SKAdNetwork: ad-attribution IDs so installs are still measured when the
        // user denies tracking. Google's own network plus the common partners
        // AdMob mediates; without these, ad attribution silently drops.
        string[] skan = {
            "cstr6suwn9.skadnetwork", // Google (AdMob)
            "4fzdc2evr5.skadnetwork", "2fnua5tdw4.skadnetwork", "ydx93a7ass.skadnetwork",
            "p78axxw29g.skadnetwork", "v72qych5uu.skadnetwork", "ludvb6z3bs.skadnetwork",
            "cp8zw746q7.skadnetwork", "3sh42y64q3.skadnetwork", "c6k4g5qg8m.skadnetwork",
            "s39g8k73mm.skadnetwork", "3qy4746246.skadnetwork", "hs6bdukanm.skadnetwork",
            "mlmmfzh3r3.skadnetwork", "v4nxqhlyqp.skadnetwork", "wzmmz9fp6w.skadnetwork",
            "su67r6k2v3.skadnetwork", "yclnxrl5pm.skadnetwork", "7ug5zh24hu.skadnetwork",
            "gta9lk7p23.skadnetwork", "n38lu8286q.skadnetwork", "prcb7njmu6.skadnetwork"
        };
        var items = plist.root.CreateArray("SKAdNetworkItems");
        foreach (var id in skan)
        {
            var dict = items.AddDict();
            dict.SetString("SKAdNetworkIdentifier", id);
        }

        plist.WriteToFile(plistPath);
    }
}
