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
        proj.WriteToFile(projPath);
    }
}
