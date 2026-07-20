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
            return "https://promptworldgame.org";
#endif
        }
    }

    public static void ParseDeepLink()
    {
        ParseDeepLink(Application.absoluteURL);
    }

    /// <summary>Parse a stage deep link from any URL — the page URL on WebGL,
    /// or a custom-scheme / universal link that launched the native app
    /// (e.g. promptworldgame.org/?stage=abc or promptworld://stage?stage=abc).</summary>
    public static void ParseDeepLink(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        string stage = QueryParam(url, "stage");
        if (string.IsNullOrEmpty(stage)) return; // keep any existing selection
        string key = QueryParam(url, "key");
        // If we're re-parsing the SAME stage (e.g. after SetUrlStage stripped the
        // key, or a reload of a url that lost it), don't wipe a key we already
        // hold — that would silently end the creator's test session. A key in the
        // url still wins; only a DIFFERENT stage resets the key.
        if (stage != RemoteStageId) EditKey = key;
        else if (!string.IsNullOrEmpty(key)) EditKey = key;
        RemoteStageId = stage;
        DeepLinkConsumed = false; // a new link arrived — let the menu act on it
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
