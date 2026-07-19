// TAC procedural audio: synthesized SFX clips (the app ships zero audio
// assets, mirroring the web client's oscillator blips) plus a minimal
// two-layer music pad from the stage's music recipe (stealth <-> combat).
using System.Collections.Generic;
using UnityEngine;

public class TacAudioKit : MonoBehaviour
{
    AudioSource sfxSrc, musicSrc;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    float combatMix, combatTarget;

    public static float BgmVol
    {
        get { return PlayerPrefs.GetFloat("tac_vol_bgm", 1f); }
        set { PlayerPrefs.SetFloat("tac_vol_bgm", value); }
    }
    public static float SfxVol
    {
        get { return PlayerPrefs.GetFloat("tac_vol_sfx", 1f); }
        set { PlayerPrefs.SetFloat("tac_vol_sfx", value); }
    }

    void Awake()
    {
        sfxSrc = gameObject.AddComponent<AudioSource>();
        sfxSrc.playOnAwake = false;
        musicSrc = gameObject.AddComponent<AudioSource>();
        musicSrc.playOnAwake = false;
        musicSrc.loop = true;
        musicSrc.volume = 0.13f;
        clips["shot"] = Blip(720, 160, 0.07f, Wave.Square, 0.5f);
        clips["eshot"] = Blip(300, 90, 0.09f, Wave.Square, 0.25f);
        clips["kill"] = Blip(500, 60, 0.25f, Wave.Saw, 0.6f);
        clips["hurt"] = Blip(220, 60, 0.25f, Wave.Saw, 0.7f);
        clips["alert"] = Blip(660, 520, 0.1f, Wave.Tri, 0.28f);
        clips["heard"] = Blip(330, 440, 0.1f, Wave.Tri, 0.4f);
        clips["beep"] = Blip(1100, 900, 0.05f, Wave.Square, 0.4f);
        clips["boom"] = Noise(0.5f, 0.9f);
        clips["crash"] = Noise(0.3f, 0.7f);
        clips["zap"] = Blip(900, 200, 0.18f, Wave.Saw, 0.4f);
        clips["whoosh"] = Blip(200, 420, 0.25f, Wave.Sine, 0.4f);
        clips["clear"] = Chord(new float[] { 523.25f, 659.25f, 784f }, 0.8f, 0.5f);
        clips["dead"] = Blip(300, 40, 0.8f, Wave.Saw, 0.5f);
    }

    enum Wave { Sine, Square, Saw, Tri }

    static AudioClip Blip(float f0, float f1, float dur, Wave w, float vol)
    {
        int sr = 22050;
        int n = (int)(sr * dur);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t);
            float ph = 2f * Mathf.PI * f * i / sr;
            float s = w == Wave.Sine ? Mathf.Sin(ph)
                : w == Wave.Square ? Mathf.Sign(Mathf.Sin(ph))
                : w == Wave.Saw ? (2f * ((ph / (2f * Mathf.PI)) % 1f) - 1f)
                : Mathf.PingPong(ph / Mathf.PI, 1f) * 2f - 1f;
            data[i] = s * vol * (1f - t);
        }
        var clip = AudioClip.Create("blip", n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip Noise(float dur, float vol)
    {
        int sr = 22050;
        int n = (int)(sr * dur);
        var data = new float[n];
        var rng = new System.Random(7);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.35f;
            data[i] = lp * vol * (1f - t);
        }
        var clip = AudioClip.Create("noise", n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip Chord(float[] freqs, float dur, float vol)
    {
        int sr = 22050;
        int n = (int)(sr * dur);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float s = 0;
            foreach (var f in freqs) s += Mathf.Sin(2f * Mathf.PI * f * i / sr);
            data[i] = s / freqs.Length * vol * (1f - t * t);
        }
        var clip = AudioClip.Create("chord", n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    public void Play(string key, float vol = 1f)
    {
        vol = vol * SfxVol;
        if (vol < 0.05f) return;
        if (clips.ContainsKey(key)) sfxSrc.PlayOneShot(clips[key], vol);
    }

    static readonly string[] KEYS = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    static readonly Dictionary<string, int[]> SCALES = new Dictionary<string, int[]>
    {
        { "minor", new[] { 0, 2, 3, 5, 7, 8, 10 } },
        { "major", new[] { 0, 2, 4, 5, 7, 9, 11 } },
        { "phrygian", new[] { 0, 1, 3, 5, 7, 8, 10 } },
        { "dorian", new[] { 0, 2, 3, 5, 7, 9, 10 } },
        { "pentatonic", new[] { 0, 3, 5, 7, 10 } },
    };

    // 4-bar loop from the stage recipe: bass pulse at bpm + soft pad triad
    public void StartMusic(TacJson.JObj recipe)
    {
        float bpm = 110;
        int keyIdx = 9; // A
        int[] scale = SCALES["minor"];
        var prog = new List<int> { 0, -2, 1, 0 };
        if (recipe != null)
        {
            bpm = (float)recipe.Num("bpm", 110);
            string k = recipe.Has("key") ? recipe.Str("key") : "A";
            keyIdx = System.Array.IndexOf(KEYS, k);
            if (keyIdx < 0) keyIdx = 9;
            string sc = recipe.Has("scale") ? recipe.Str("scale") : "minor";
            if (SCALES.ContainsKey(sc)) scale = SCALES[sc];
            if (recipe.Has("prog"))
            {
                prog.Clear();
                var pa = recipe.Arr("prog");
                for (int i = 0; i < pa.Count; i++) prog.Add((int)(double)pa.l[i]);
            }
        }
        int sr = 22050;
        float beat = 60f / bpm;
        float barLen = beat * 4f;
        int n = (int)(sr * barLen * prog.Count);
        var data = new float[n];
        float root = 110f * Mathf.Pow(2f, keyIdx / 12f);
        for (int bar = 0; bar < prog.Count; bar++)
        {
            int deg = ((prog[bar] % scale.Length) + scale.Length) % scale.Length;
            float f = root * Mathf.Pow(2f, scale[deg] / 12f);
            int start = (int)(sr * barLen * bar);
            int len = (int)(sr * barLen);
            for (int i = 0; i < len && start + i < n; i++)
            {
                float tt = (float)i / sr;
                float beatT = (tt % beat) / beat;
                float bass = Mathf.Sin(2f * Mathf.PI * f * 0.5f * tt) * Mathf.Exp(-beatT * 4f) * 0.34f;
                float padEnv = Mathf.Sin(Mathf.PI * ((float)i / len)); // swell per bar, no drone-y sustain
                float pad = (Mathf.Sin(2f * Mathf.PI * f * tt) + Mathf.Sin(2f * Mathf.PI * f * 1.5f * tt) * 0.5f) * 0.06f * padEnv;
                data[start + i] = bass + pad;
            }
        }
        var clip = AudioClip.Create("music", n, 1, sr, false);
        clip.SetData(data, 0);
        musicSrc.clip = clip;
        musicSrc.Play();
    }

    public void StopMusic()
    {
        if (musicSrc != null) musicSrc.Stop();
    }

    public void SetCombat(bool combat)
    {
        combatTarget = combat ? 1f : 0f;
    }

    void Update()
    {
        combatMix = Mathf.MoveTowards(combatMix, combatTarget, Time.deltaTime * 0.8f);
        if (musicSrc != null)
        {
            musicSrc.volume = (0.1f + combatMix * 0.04f) * BgmVol;
            musicSrc.pitch = 1f + combatMix * 0.04f;
        }
    }
}
