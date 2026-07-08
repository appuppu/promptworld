using UnityEngine;

/// <summary>
/// Cross-scene state: which stage to play (built-in file or server stage),
/// plus creator-session data parsed from the page URL
/// (?stage=<id> to play, &key=<editKey> for a creator test session).
/// </summary>
public static class GameSession
{
    public static string SelectedStageFile;
    public static string RemoteStageId;
    public static string EditKey;
    public static bool DeepLinkConsumed;

    /// <summary>API origin — same host as the page on WebGL, production otherwise.</summary>
    public static string ApiOrigin
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var uri = new System.Uri(Application.absoluteURL);
            return uri.GetLeftPart(System.UriPartial.Authority);
#else
            return "https://promptworld.pages.dev";
#endif
        }
    }

    public static void ParseDeepLink()
    {
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url)) return;
        RemoteStageId = QueryParam(url, "stage");
        EditKey = QueryParam(url, "key");
    }

    private static string QueryParam(string url, string name)
    {
        int q = url.IndexOf('?');
        if (q < 0) return null;
        foreach (string pair in url.Substring(q + 1).Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (pair.Substring(0, eq) == name)
            {
                string value = pair.Substring(eq + 1);
                int hash = value.IndexOf('#');
                if (hash >= 0) value = value.Substring(0, hash);
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        return null;
    }
}
