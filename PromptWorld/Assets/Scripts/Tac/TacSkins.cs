// Operative skins — device-local (no login): body/accent colors persisted in
// PlayerPrefs. Applied when the player view is built.
using UnityEngine;

public static class TacSkins
{
    public class Skin { public string name; public Color body, accent; }

    public static readonly Skin[] All =
    {
        new Skin { name = "AZURE",  body = new Color(0.20f, 0.55f, 0.85f), accent = new Color(0.75f, 0.88f, 1f) },
        new Skin { name = "CRIMSON", body = new Color(0.78f, 0.25f, 0.25f), accent = new Color(1f, 0.75f, 0.7f) },
        new Skin { name = "OLIVE",  body = new Color(0.42f, 0.52f, 0.32f), accent = new Color(0.85f, 0.9f, 0.7f) },
        new Skin { name = "SHADOW", body = new Color(0.2f, 0.22f, 0.26f), accent = new Color(0.6f, 0.65f, 0.72f) },
        new Skin { name = "SAND",   body = new Color(0.76f, 0.66f, 0.45f), accent = new Color(0.98f, 0.93f, 0.8f) },
        new Skin { name = "VIOLET", body = new Color(0.55f, 0.38f, 0.8f), accent = new Color(0.85f, 0.78f, 1f) },
    };

    public static int Index
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt("tac_skin", 0), 0, All.Length - 1); }
        set { PlayerPrefs.SetInt("tac_skin", Mathf.Clamp(value, 0, All.Length - 1)); PlayerPrefs.Save(); }
    }

    public static Skin Current { get { return All[Index]; } }
}
