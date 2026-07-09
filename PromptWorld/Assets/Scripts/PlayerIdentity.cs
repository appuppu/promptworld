using UnityEngine;

/// <summary>
/// Anonymous per-device player identity. No account, no signup: a random id
/// persisted locally deduplicates votes and leaderboard entries, plus an
/// optional display name (empty = "anonymous").
/// </summary>
public static class PlayerIdentity
{
    private static string cachedId;

    public static string Id
    {
        get
        {
            if (cachedId == null)
            {
                cachedId = PlayerPrefs.GetString("pw_pid", "");
                if (cachedId.Length == 0)
                {
                    cachedId = System.Guid.NewGuid().ToString();
                    PlayerPrefs.SetString("pw_pid", cachedId);
                    PlayerPrefs.Save();
                }
            }
            return cachedId;
        }
    }

    public static string Name
    {
        get => PlayerPrefs.GetString("pw_name", "");
        set
        {
            PlayerPrefs.SetString("pw_name", value ?? "");
            PlayerPrefs.Save();
        }
    }

    public static string DisplayName
    {
        get
        {
            string trimmed = Name.Trim();
            return trimmed.Length == 0 ? "anonymous" : trimmed;
        }
    }
}
