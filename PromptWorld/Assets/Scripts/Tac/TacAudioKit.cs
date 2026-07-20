// TAC procedural audio: synthesized SFX clips (the app ships zero audio
// assets, mirroring the web client's oscillator blips) plus a minimal
// two-layer music pad from the stage's music recipe (stealth <-> combat).
using System.Collections.Generic;
using UnityEngine;

public class TacAudioKit : MonoBehaviour
{
    AudioSource sfxSrc, stealthSrc, combatSrc;
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
        // Two music layers that crossfade like the web client: an always-on light
        // stealth pulse and a drum-forward combat groove that rises when alerted.
        stealthSrc = gameObject.AddComponent<AudioSource>();
        stealthSrc.playOnAwake = false; stealthSrc.loop = true; stealthSrc.volume = 0.15f;
        combatSrc = gameObject.AddComponent<AudioSource>();
        combatSrc.playOnAwake = false; combatSrc.loop = true; combatSrc.volume = 0f;
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

    // Bright scales get the up-tempo melodic combat feel; dark ones a tighter,
    // moodier groove. Mirrors BRIGHT_SCALES in the web client (tac-client.js).
    static readonly HashSet<string> BRIGHT = new HashSet<string> { "major", "dorian", "pentatonic" };

    const int SR = 22050;
    int mSr;                 // samples per 16th step
    float[] mScale;          // 7 scale semitone offsets
    float mRootHz;           // stage key root (low octave)
    float[] mRoots;          // per-bar bass roots

    // Bake TWO looping clips — a light stealth pulse and a drum-forward combat
    // groove — from the stage recipe, then crossfade them (see Update). This
    // reproduces the web client's two-layer engine on-device, note for note.
    public void StartMusic(TacJson.JObj recipe, bool night = false)
    {
        float bpm = 116;
        int keyIdx = 9; // A
        string scaleName = "minor";
        int[] scale = SCALES["minor"];
        var prog = new List<int> { 0, 0, 2, -1 };
        if (recipe != null)
        {
            bpm = (float)recipe.Num("bpm", 116);
            string k = recipe.Has("key") ? recipe.Str("key") : "A";
            keyIdx = System.Array.IndexOf(KEYS, k);
            if (keyIdx < 0) keyIdx = 9;
            scaleName = recipe.Has("scale") ? recipe.Str("scale") : "minor";
            if (SCALES.ContainsKey(scaleName)) scale = SCALES[scaleName];
            if (recipe.Has("prog"))
            {
                prog.Clear();
                var pa = recipe.Arr("prog");
                for (int i = 0; i < pa.Count; i++) prog.Add((int)(double)pa.l[i]);
            }
        }
        bool bright = BRIGHT.Contains(scaleName);
        mRootHz = 55f * Mathf.Pow(2f, keyIdx / 12f); // A2-relative low root like web
        mScale = new float[7];
        for (int i = 0; i < 7; i++) mScale[i] = scale[i % scale.Length];
        mRoots = new float[4];
        for (int b = 0; b < 4; b++)
        {
            int deg = Mathf.RoundToInt(prog[b % prog.Count]);
            int oct = Mathf.FloorToInt(deg / 7f);
            int idx = ((deg % 7) + 7) % 7;
            mRoots[b] = mRootHz * Mathf.Pow(2f, (mScale[idx] + 12 * oct) / 12f);
        }
        float spb = 60f / bpm / 4f;          // seconds per 16th
        mSr = Mathf.Max(1, (int)(SR * spb));  // samples per step
        int total = mSr * 64;                 // 4 bars * 16 steps

        var stealth = new float[total];
        var combat = new float[total];
        for (int s = 0; s < 64; s++) BakeStep(s, stealth, combat, bright, night);

        stealthSrc.clip = MakeClip("mStealth", stealth);
        combatSrc.clip = MakeClip("mCombat", combat);
        combatMix = 0f; combatTarget = 0f;
        stealthSrc.volume = 0.15f * BgmVol; combatSrc.volume = 0f;
        stealthSrc.Play(); combatSrc.Play();
    }

    AudioClip MakeClip(string name, float[] data)
    {
        var c = AudioClip.Create(name, data.Length, 1, SR, false);
        c.SetData(data, 0);
        return c;
    }

    float NoteHz(int deg)
    {
        int oct = Mathf.FloorToInt(deg / 7f), idx = ((deg % 7) + 7) % 7;
        return mRootHz * Mathf.Pow(2f, (mScale[idx] + 12 * oct) / 12f);
    }

    // Write one 16th-note step into the stealth+combat buffers, mirroring the
    // web client's musicStep(): drums up front, a melodic hook, light stealth.
    void BakeStep(int s, float[] st, float[] cb, bool bright, bool night)
    {
        int bar = (s >> 4) & 3, pos = s & 15;
        float root = mRoots[bar];
        int at = s * mSr;

        // stealth: soft pulse + airy tick (no heavy drone)
        if (pos == 0) AddTone(st, at, root * 2, root * 2, 0.09f, Wave.Tri, 0.22f);
        if (pos == 8) AddTone(st, at, root * 3, root * 3, 0.07f, Wave.Tri, 0.14f);
        if ((s & 3) == 2) AddNoise(st, at, 0.02f, 0.035f, true);
        if (night && pos == 0) AddPad(st, at, root, mSr * 12);

        // combat: kick / snare / hats / bass / lead
        bool kick = bright ? (pos == 0 || pos == 6 || pos == 8 || pos == 14)
                           : (pos == 0 || pos == 4 || pos == 8 || pos == 12);
        if (kick) AddKick(cb, at, 0.9f);
        if (pos == 4 || pos == 12) AddSnare(cb, at, 0.5f);
        if ((s & 1) == 0) AddNoise(cb, at, bright ? 0.022f : 0.022f, bright ? 0.14f : 0.10f, true);
        if (bright && (pos == 2 || pos == 10)) AddNoise(cb, at, 0.06f, 0.11f, true);
        if ((s & 1) == 0)
        {
            float b = root * 2;
            if (pos == 14) b *= 1.335f;
            AddTone(cb, at, b, b, bright ? 0.10f : 0.13f, bright ? Wave.Square : Wave.Saw, bright ? 0.26f : 0.32f);
        }
        if (bright)
        {
            int[][] motifs = { new[] { 7, 9 }, new[] { 7, 11 }, new[] { 9, 12 }, new[] { 7, 10 } };
            var m = motifs[bar];
            if (pos == 0) AddTone(cb, at, NoteHz(m[0]), NoteHz(m[0]), 0.16f, Wave.Square, 0.16f);
            if (pos == 8) AddTone(cb, at, NoteHz(m[1]), NoteHz(m[1]), 0.16f, Wave.Square, 0.16f);
        }
        else
        {
            int[] stabs = { 7, 8, 10, 7 };
            int stab = stabs[bar];
            if (pos == 0)
            {
                AddTone(cb, at, NoteHz(stab), NoteHz(stab), 0.28f, Wave.Saw, 0.15f);
                AddTone(cb, at, NoteHz(stab + 2), NoteHz(stab + 2), 0.28f, Wave.Saw, 0.11f);
            }
        }
    }

    // --- additive voice writers (wrap-around into the loop buffer) ---
    void AddTone(float[] buf, int at, float f0, float f1, float dur, Wave w, float vol)
    {
        int len = (int)(SR * dur);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float f = Mathf.Lerp(f0, f1, t);
            float ph = 2f * Mathf.PI * f * i / SR;
            float sv = w == Wave.Sine ? Mathf.Sin(ph)
                : w == Wave.Square ? Mathf.Sign(Mathf.Sin(ph))
                : w == Wave.Saw ? (2f * ((ph / (2f * Mathf.PI)) % 1f) - 1f)
                : Mathf.PingPong(ph / Mathf.PI, 1f) * 2f - 1f;
            buf[(at + i) % buf.Length] += sv * vol * (1f - t);
        }
    }
    void AddKick(float[] buf, int at, float vol)
    {
        int len = (int)(SR * 0.18f);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float f = Mathf.Lerp(150f, 48f, Mathf.Clamp01(t * 2.2f));
            float env = Mathf.Exp(-t * 5f);
            buf[(at + i) % buf.Length] += Mathf.Sin(2f * Mathf.PI * f * i / SR) * vol * env;
        }
    }
    void AddSnare(float[] buf, int at, float vol)
    {
        AddNoise(buf, at, 0.12f, vol, false);
        int len = (int)(SR * 0.09f);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            buf[(at + i) % buf.Length] += Mathf.Sin(2f * Mathf.PI * 190f * i / SR) * vol * 0.5f * (1f - t);
        }
    }
    System.Random mRng = new System.Random(11);
    void AddNoise(float[] buf, int at, float dur, float vol, bool high)
    {
        int len = (int)(SR * dur);
        float lp = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / len;
            float raw = (float)mRng.NextDouble() * 2f - 1f;
            lp += (raw - lp) * (high ? 0.9f : 0.3f);  // high=hat/snare crack, low=body
            float v = high ? (raw - lp) : lp;          // crude high/low split
            buf[(at + i) % buf.Length] += v * vol * (1f - t);
        }
    }
    void AddPad(float[] buf, int at, float root, int len)
    {
        float[] parts = { root, root * 1.006f, root * 1.5f };
        for (int i = 0; i < len; i++)
        {
            float env = Mathf.Sin(Mathf.PI * ((float)i / len)) * 0.12f; // gentle swell
            float sv = 0f;
            foreach (var fr in parts) sv += Mathf.Sin(2f * Mathf.PI * fr * i / SR);
            buf[(at + i) % buf.Length] += (sv / parts.Length) * env;
        }
    }

    public void StopMusic()
    {
        if (stealthSrc != null) stealthSrc.Stop();
        if (combatSrc != null) combatSrc.Stop();
    }

    public void SetCombat(bool combat)
    {
        combatTarget = combat ? 1f : 0f;
    }

    void Update()
    {
        combatMix = Mathf.MoveTowards(combatMix, combatTarget, Time.deltaTime * 0.8f);
        if (stealthSrc != null) stealthSrc.volume = 0.15f * (1f - 0.85f * combatMix) * BgmVol;
        if (combatSrc != null) combatSrc.volume = 0.15f * combatMix * BgmVol;
    }
}
