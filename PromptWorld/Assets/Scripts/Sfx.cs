using System.Collections.Generic;
using UnityEngine;

public enum SfxId { Jump, Pad, Boost, Flip, Respawn, Clear, GameOver, Tick }

/// <summary>
/// Procedural sound effects — every clip is synthesized from simple
/// waveforms at first use. No audio assets, matching the
/// everything-from-data aesthetic. Survives scene reloads.
/// </summary>
public static class Sfx
{
    private const int SampleRate = 44100;

    private static AudioSource source;
    private static AudioSource musicSource;
    private static Dictionary<SfxId, AudioClip> clips;

    private static bool prefsLoaded;
    private static bool soundEnabled = true;
    private static bool musicEnabled = true;
    private static bool musicWanted;

    public static bool SoundEnabled { get { LoadPrefs(); return soundEnabled; } }
    public static bool MusicEnabled { get { LoadPrefs(); return musicEnabled; } }

    private static void LoadPrefs()
    {
        if (prefsLoaded) return;
        prefsLoaded = true;
        soundEnabled = PlayerPrefs.GetInt("pw_sfx", 1) == 1;
        musicEnabled = PlayerPrefs.GetInt("pw_music", 1) == 1;
    }

    public static void SetSoundEnabled(bool on)
    {
        LoadPrefs();
        soundEnabled = on;
        PlayerPrefs.SetInt("pw_sfx", on ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void SetMusicEnabled(bool on)
    {
        LoadPrefs();
        musicEnabled = on;
        PlayerPrefs.SetInt("pw_music", on ? 1 : 0);
        PlayerPrefs.Save();
        if (!on)
        {
            if (musicSource != null && musicSource.isPlaying) musicSource.Stop();
        }
        else if (musicWanted)
        {
            StartMusic();
        }
    }

    public static void Play(SfxId id, float volume = 0.5f)
    {
        LoadPrefs();
        if (!soundEnabled) return;
        EnsureInit();
        source.PlayOneShot(clips[id], volume);
    }

    /// <summary>Looping synthesized drum groove — plays only during stage runs.</summary>
    private static AudioClip calmLoop;
    private static AudioClip bossLoop;
    private static bool bossMusicOn;

    public static void StartMusic()
    {
        musicWanted = true;
        bossMusicOn = false;
        LoadPrefs();
        if (!musicEnabled) return;
        EnsureInit();
        if (calmLoop == null) calmLoop = BuildDrumLoop();
        // If it's ALREADY the calm loop, leave it running (don't restart).
        if (musicSource.isPlaying && musicSource.clip == calmLoop) return;
        // Otherwise a different track (boss or a stage's custom BGM) is playing —
        // stop it and switch to the calm loop. Without this, a stage's custom
        // music would keep looping into the NEXT stage that has no music.
        if (musicSource.isPlaying) musicSource.Stop();
        musicSource.clip = calmLoop;
        musicSource.loop = true;
        musicSource.volume = 0.22f;
        musicSource.Play();
    }

    /// <summary>Switch to the driving, faster BOSS loop (louder, double-time).
    /// Idempotent — safe to call every tick a boss is alive.</summary>
    public static void StartBossMusic()
    {
        musicWanted = true;
        LoadPrefs();
        if (!musicEnabled) return;
        EnsureInit();
        if (bossMusicOn && musicSource.isPlaying) return;
        bossMusicOn = true;
        if (bossLoop == null) bossLoop = BuildBossLoop();
        musicSource.Stop();
        musicSource.clip = bossLoop;
        musicSource.loop = true;
        musicSource.volume = 0.3f;
        musicSource.Play();
    }

    public static bool IsBossMusic { get { return bossMusicOn; } }

    // Per-stage custom BGM, synthesized from a MusicData recipe. When a stage
    // supplies "music", this plays instead of the default calm/boss loops.
    // Append-only: the loops above are untouched.
    private static AudioClip stageLoop;
    private static string stageLoopKey; // recipe fingerprint the cached loop was built from

    /// <summary>Play a stage's custom synthesized BGM loop. Rebuilds when the
    /// recipe differs from the cached one, so each stage gets ITS OWN music and
    /// a previous stage's loop never bleeds into another.</summary>
    public static void StartStageMusic(MusicData m)
    {
        musicWanted = true;
        bossMusicOn = false;
        LoadPrefs();
        if (!musicEnabled) return;
        if (m == null) { StartMusic(); return; }
        EnsureInit();
        // Rebuild the loop whenever the recipe changed (different stage / edit).
        string key = MusicKey(m);
        if (stageLoop == null || stageLoopKey != key)
        {
            stageLoop = BuildStageMusic(m);
            stageLoopKey = key;
        }
        if (musicSource.isPlaying && musicSource.clip == stageLoop) return;
        if (musicSource.isPlaying) musicSource.Stop();
        musicSource.clip = stageLoop;
        musicSource.loop = true;
        musicSource.volume = 0.26f;
        musicSource.Play();
    }

    // A cheap fingerprint of a music recipe: two recipes with the same string
    // sound identical, so we can safely reuse a cached loop only when it matches.
    private static string MusicKey(MusicData m)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(m.bpm).Append('|').Append(m.key).Append('|')
          .Append(m.scale).Append('|').Append(m.drums).Append('|');
        if (m.bass != null) foreach (int d in m.bass) sb.Append(d).Append(',');
        sb.Append('|');
        if (m.lead != null)
        {
            sb.Append(m.lead.voice).Append(':');
            if (m.lead.notes != null) foreach (int d in m.lead.notes) sb.Append(d).Append(',');
        }
        sb.Append('|');
        if (m.chords != null)
        {
            sb.Append(m.chords.voice).Append('/').Append(m.chords.preset).Append(':');
            if (m.chords.prog != null)
                foreach (ChordStep cs in m.chords.prog)
                    if (cs?.notes != null) { foreach (int d in cs.notes) sb.Append(d).Append(','); sb.Append(';'); }
        }
        return sb.ToString();
    }

    public static void StopMusic()
    {
        musicWanted = false;
        if (musicSource != null && musicSource.isPlaying) musicSource.Stop();
    }

    /// <summary>Two bars of 4/4 at 128 BPM: kick, snare, hats — all synthesized.</summary>
    private static AudioClip BuildDrumLoop()
    {
        const float bpm = 128f;
        float stepDur = 60f / bpm / 4f;              // 16th note
        int stepSamples = (int)(SampleRate * stepDur);
        int totalSteps = 32;
        var samples = new float[stepSamples * totalSteps];
        var rng = new System.Random(777);

        void AddKick(int step)
        {
            int start = step * stepSamples;
            int n = (int)(SampleRate * 0.14f);
            float phase = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float freq = Mathf.Lerp(110f, 42f, p);
                phase += freq / SampleRate;
                samples[start + i] += Mathf.Sin(phase * 2f * Mathf.PI) * Mathf.Pow(1f - p, 1.6f) * 0.9f;
            }
        }
        void AddSnare(int step)
        {
            int start = step * stepSamples;
            int n = (int)(SampleRate * 0.11f);
            float last = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float white = (float)rng.NextDouble() * 2f - 1f;
                last = Mathf.Lerp(last, white, 0.6f);
                samples[start + i] += last * Mathf.Pow(1f - p, 1.4f) * 0.5f;
            }
        }
        void AddHat(int step, float loudness)
        {
            int start = step * stepSamples;
            int n = (int)(SampleRate * 0.028f);
            float prev = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float white = (float)rng.NextDouble() * 2f - 1f;
                float high = white - prev; // crude high-pass
                prev = white;
                samples[start + i] += high * (1f - p) * loudness;
            }
        }

        for (int bar = 0; bar < 2; bar++)
        {
            int b = bar * 16;
            AddKick(b + 0); AddKick(b + 7); AddKick(b + 10);
            AddSnare(b + 4); AddSnare(b + 12);
            for (int s = 0; s < 16; s += 2) AddHat(b + s, s % 4 == 2 ? 0.34f : 0.2f);
        }

        return ToClip("drumloop", samples);
    }

    /// <summary>A driving BOSS loop: faster (150 BPM), four-on-the-floor kicks, a
    /// menacing detuned bass pulse, and busy hats. Same synthesis palette, more
    /// intensity.</summary>
    private static AudioClip BuildBossLoop()
    {
        const float bpm = 150f;
        float stepDur = 60f / bpm / 4f;
        int stepSamples = (int)(SampleRate * stepDur);
        int totalSteps = 32;
        var samples = new float[stepSamples * totalSteps];
        var rng = new System.Random(1313);

        void AddKick(int step)
        {
            int start = step * stepSamples;
            int n = (int)(SampleRate * 0.13f);
            float phase = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float freq = Mathf.Lerp(140f, 46f, p);
                phase += freq / SampleRate;
                samples[start + i] += Mathf.Sin(phase * 2f * Mathf.PI) * Mathf.Pow(1f - p, 1.5f) * 1.0f;
            }
        }
        void AddSnare(int step)
        {
            int start = step * stepSamples;
            int n = (int)(SampleRate * 0.12f);
            float last = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float white = (float)rng.NextDouble() * 2f - 1f;
                last = Mathf.Lerp(last, white, 0.55f);
                samples[start + i] += last * Mathf.Pow(1f - p, 1.3f) * 0.55f;
            }
        }
        void AddHat(int step, float loudness)
        {
            int start = step * stepSamples;
            int n = (int)(SampleRate * 0.024f);
            float prev = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float white = (float)rng.NextDouble() * 2f - 1f;
                float high = white - prev;
                prev = white;
                samples[start + i] += high * (1f - p) * loudness;
            }
        }
        // A detuned two-oscillator bass note for menace.
        void AddBass(int step, int lenSteps, float hz)
        {
            int start = step * stepSamples;
            int n = stepSamples * lenSteps;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float ph = (float)(start + i) / SampleRate;
                float a = Osc(Wave.Saw, ph * hz);
                float bq = Osc(Wave.Saw, ph * hz * 1.008f); // slight detune
                float env = 1f - 0.4f * p;
                samples[start + i] += (a + bq) * 0.16f * env;
            }
        }

        for (int bar = 0; bar < 2; bar++)
        {
            int b = bar * 16;
            // four-on-the-floor kick + extra pickups
            AddKick(b + 0); AddKick(b + 4); AddKick(b + 8); AddKick(b + 12);
            AddKick(b + 14);
            AddSnare(b + 4); AddSnare(b + 12);
            for (int s = 0; s < 16; s++) AddHat(b + s, s % 2 == 0 ? 0.22f : 0.32f);
            // ominous bass line
            AddBass(b + 0, 4, 55f);   // A1
            AddBass(b + 8, 4, 61.74f); // B1
        }

        return ToClip("bossloop", samples);
    }

    private static void EnsureInit()
    {
        if (source != null) return;

        var go = new GameObject("Sfx");
        Object.DontDestroyOnLoad(go);
        source = go.AddComponent<AudioSource>();
        musicSource = go.AddComponent<AudioSource>();

        clips = new Dictionary<SfxId, AudioClip>
        {
            { SfxId.Jump, Sweep("jump", 0.09f, 350f, 650f, Wave.Square, 0.9f) },
            { SfxId.Pad, Sweep("pad", 0.18f, 180f, 880f, Wave.Square, 0.8f) },
            { SfxId.Boost, Noise("boost", 0.22f) },
            { SfxId.Flip, Wobble("flip", 0.16f, 620f, 240f) },
            { SfxId.Respawn, Sweep("respawn", 0.25f, 420f, 90f, Wave.Saw, 1.2f) },
            { SfxId.Clear, Arpeggio("clear", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.09f) },
            { SfxId.GameOver, Arpeggio("gameover", new[] { 220f, 174.61f, 130.81f }, 0.22f) },
            { SfxId.Tick, Sweep("tick", 0.03f, 1000f, 1000f, Wave.Square, 0.5f) },
        };
    }

    private enum Wave { Square, Saw, Sine }

    private static float Osc(Wave wave, float phase)
    {
        float t = phase - Mathf.Floor(phase);
        switch (wave)
        {
            case Wave.Square: return t < 0.5f ? 0.6f : -0.6f;
            case Wave.Saw: return (t * 2f - 1f) * 0.7f;
            default: return Mathf.Sin(t * 2f * Mathf.PI);
        }
    }

    private static AudioClip Sweep(string name, float duration, float fromHz, float toHz, Wave wave, float decay)
    {
        int n = (int)(SampleRate * duration);
        var samples = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = (float)i / n;
            phase += Mathf.Lerp(fromHz, toHz, p) / SampleRate;
            samples[i] = Osc(wave, phase) * Mathf.Pow(1f - p, decay);
        }
        return ToClip(name, samples);
    }

    private static AudioClip Noise(string name, float duration)
    {
        int n = (int)(SampleRate * duration);
        var samples = new float[n];
        var rng = new System.Random(12345);
        float last = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = (float)i / n;
            float white = (float)rng.NextDouble() * 2f - 1f;
            last = Mathf.Lerp(last, white, 0.35f); // soften the highs
            samples[i] = last * (1f - p) * 0.8f;
        }
        return ToClip(name, samples);
    }

    private static AudioClip Wobble(string name, float duration, float highHz, float lowHz)
    {
        int n = (int)(SampleRate * duration);
        var samples = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = (float)i / n;
            float freq = Mathf.Lerp(highHz, lowHz, Mathf.PingPong(p * 2f, 1f)); // down, then back up
            phase += freq / SampleRate;
            samples[i] = Osc(Wave.Sine, phase) * (1f - p);
        }
        return ToClip(name, samples);
    }

    private static AudioClip Arpeggio(string name, float[] notesHz, float noteDuration)
    {
        int perNote = (int)(SampleRate * noteDuration);
        var samples = new float[perNote * notesHz.Length];
        float phase = 0f;
        for (int k = 0; k < notesHz.Length; k++)
        {
            for (int i = 0; i < perNote; i++)
            {
                float env = Mathf.Pow(1f - (float)i / perNote, 0.6f);
                phase += notesHz[k] / SampleRate;
                samples[k * perNote + i] = Osc(Wave.Square, phase) * env;
            }
        }
        return ToClip(name, samples);
    }

    // ---------------------------------------------------------------- stage BGM
    // Synthesize a 4-bar loop from a MusicData recipe. Reuses the existing drum
    // and oscillator palette; adds a few melodic voices. Pure decoration.

    private const int MusicSteps = 16; // 4 bars * 4 beats; one melody/bass note per beat

    // Semitone offsets from the root for one octave of each scale. Degrees index
    // into this (wrapping up octaves): degree d -> octave d/len, step d%len.
    private static int[] ScaleSemis(string scale)
    {
        switch ((scale ?? "minor").ToLowerInvariant())
        {
            case "major":      return new[] { 0, 2, 4, 5, 7, 9, 11 };
            case "pentatonic": return new[] { 0, 3, 5, 7, 10 };        // minor pentatonic
            case "japanese":   return new[] { 0, 1, 5, 7, 8 };         // in-sen / miyako-bushi
            case "phrygian":   return new[] { 0, 1, 3, 5, 7, 8, 10 };
            default:            return new[] { 0, 2, 3, 5, 7, 8, 10 };  // natural minor
        }
    }

    // Famous chord progressions, as ROOT scale-degrees per chord (0=I, 3=IV,
    // 4=V, 5=vi, ...). Each expands to a 16-beat (4-bar) loop of triads
    // [root, root+2, root+4] stacked on the scale. Roman-numeral names in the
    // comments; these are the progressions pop/rock/jazz lean on constantly.
    private static readonly System.Collections.Generic.Dictionary<string, int[]> ChordRoots =
        new System.Collections.Generic.Dictionary<string, int[]>
    {
        { "axis",       new[] { 0, 4, 5, 3 } },            // I–V–vi–IV  (the "4 chords" / axis of awesome)
        { "doowop",     new[] { 0, 5, 3, 4 } },            // I–vi–IV–V  (50s doo-wop)
        { "fifties",    new[] { 0, 5, 3, 4 } },            // alias of doowop
        { "komuro",     new[] { 5, 3, 4, 0 } },            // vi–IV–V–I  (Komuro / uplifting J-pop)
        { "royal",      new[] { 3, 4, 2, 5 } },            // IV–V–iii–vi (J-pop "royal road")
        { "sad",        new[] { 5, 3, 0, 4 } },            // vi–IV–I–V   (emotional / ballad)
        { "pop",        new[] { 0, 3, 4, 3 } },            // I–IV–V–IV   (simple pop/rock)
        { "punk",       new[] { 0, 3, 5, 4 } },            // I–IV–vi–V   (pop-punk)
        { "andalusian", new[] { 5, 4, 3, 4 } },            // vi–V–IV–V   (Andalusian-ish descending, minor feel)
        { "jazz",       new[] { 1, 4, 0, 0 } },            // ii–V–I–I    (jazz turnaround)
        { "canon",      new[] { 0, 4, 5, 2, 3, 0, 3, 4 } },// I–V–vi–iii–IV–I–IV–V (Pachelbel's Canon)
        { "blues",      new[] { 0, 0, 3, 0, 4, 3, 0, 4 } },// 12-bar blues, condensed to 8
        { "epic",       new[] { 5, 3, 0, 4, 5, 3, 0, 4 } },// vi–IV–I–V ×2 (cinematic)
        { "wistful",    new[] { 0, 4, 5, 3, 3, 0, 1, 4 } },// I–V–vi–IV / IV–I–ii–V (bittersweet)
    };

    // Expand a preset name into a 16-beat array of chords (each chord = triad of
    // scale degrees). Unknown name -> null (caller then plays no chords).
    private static int[][] ChordPreset(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!ChordRoots.TryGetValue(name.ToLowerInvariant(), out int[] roots)) return null;
        var outArr = new int[MusicSteps][];
        int beatsPerChord = MusicSteps / roots.Length; // 4 chords -> 4 beats each; 8 -> 2
        if (beatsPerChord < 1) beatsPerChord = 1;
        for (int s = 0; s < MusicSteps; s++)
        {
            int chordIdx = s / beatsPerChord;
            if (chordIdx >= roots.Length) chordIdx = roots.Length - 1;
            int r = roots[chordIdx];
            outArr[s] = new[] { r, r + 2, r + 4 }; // triad stacked on the scale
        }
        return outArr;
    }

    private static float RootHz(string key)
    {
        // Semitones above A2 (110 Hz), so melodies sit in a pleasant mid register.
        int s;
        switch ((key ?? "A").ToUpperInvariant())
        {
            case "C": s = 3; break;   case "C#": s = 4; break;
            case "D": s = 5; break;   case "D#": s = 6; break;
            case "E": s = 7; break;   case "F": s = 8; break;
            case "F#": s = 9; break;  case "G": s = 10; break;
            case "G#": s = 11; break; case "A": s = 0; break;
            case "A#": s = 1; break;  case "B": s = 2; break;
            default: s = 0; break;
        }
        return 110f * Mathf.Pow(2f, s / 12f);
    }

    // Scale degree -> Hz. degree 0 = root, 7 = one octave up (for a 7-note scale),
    // negative = below root. -99 = rest (returns 0).
    private static float DegreeHz(int degree, float rootHz, int[] semis)
    {
        if (degree <= -99) return 0f;
        int len = semis.Length;
        int oct = degree >= 0 ? degree / len : -(((-degree) + len - 1) / len);
        int step = degree - oct * len; // always 0..len-1
        int semitone = semis[step] + oct * 12;
        return rootHz * Mathf.Pow(2f, semitone / 12f);
    }

    private static AudioClip BuildStageMusic(MusicData m)
    {
        float bpm = m.bpm > 0f ? Mathf.Clamp(m.bpm, 60f, 180f) : 100f;
        float beatDur = 60f / bpm;                 // one beat = one note step
        int beatSamples = (int)(SampleRate * beatDur);
        var samples = new float[beatSamples * MusicSteps];
        var rng = new System.Random(555);
        int[] semis = ScaleSemis(m.scale);
        float rootHz = RootHz(m.key);

        // --- drums ---
        void AddKick(int step)
        {
            int start = step * beatSamples;
            int n = (int)(SampleRate * 0.14f);
            float phase = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                phase += Mathf.Lerp(110f, 42f, p) / SampleRate;
                samples[start + i] += Mathf.Sin(phase * 2f * Mathf.PI) * Mathf.Pow(1f - p, 1.6f) * 0.9f;
            }
        }
        void AddSnare(int step)
        {
            int start = step * beatSamples;
            int n = (int)(SampleRate * 0.11f);
            float last = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float white = (float)rng.NextDouble() * 2f - 1f;
                last = Mathf.Lerp(last, white, 0.6f);
                samples[start + i] += last * Mathf.Pow(1f - p, 1.4f) * 0.5f;
            }
        }
        void AddHat(int subStep, float loudness)
        {
            // sub-beat position: subStep counts 8th notes (0..31)
            int start = (int)(subStep * beatSamples / 2f);
            int n = (int)(SampleRate * 0.026f);
            float prev = 0f;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float white = (float)rng.NextDouble() * 2f - 1f;
                float high = white - prev;
                prev = white;
                samples[start + i] += high * (1f - p) * loudness;
            }
        }

        string drums = (m.drums ?? "basic").ToLowerInvariant();
        if (drums != "none")
        {
            for (int bar = 0; bar < 4; bar++)
            {
                int b = bar * 4; // beat index of this bar's downbeat
                if (drums == "fourfloor")
                {
                    AddKick(b + 0); AddKick(b + 1); AddKick(b + 2); AddKick(b + 3);
                    AddSnare(b + 2);
                }
                else if (drums == "sparse")
                {
                    AddKick(b + 0);
                    AddSnare(b + 2);
                }
                else if (drums == "busy")
                {
                    AddKick(b + 0); AddKick(b + 2); AddKick(b + 3);
                    AddSnare(b + 1); AddSnare(b + 3);
                }
                else // basic
                {
                    AddKick(b + 0); AddKick(b + 2);
                    AddSnare(b + 1); AddSnare(b + 3);
                }
                // hats: 8th notes across the bar, accent on the beat
                for (int s = 0; s < 8; s++)
                    AddHat(bar * 8 + s, s % 2 == 0 ? 0.24f : 0.15f);
            }
        }

        // --- bass: detuned saw, one degree per beat ---
        void AddBass(int step, float hz)
        {
            if (hz <= 0f) return;
            int start = step * beatSamples;
            int n = beatSamples;
            for (int i = 0; i < n && start + i < samples.Length; i++)
            {
                float p = (float)i / n;
                float ph = (float)(start + i) / SampleRate;
                float a = Osc(Wave.Saw, ph * hz);
                float bq = Osc(Wave.Saw, ph * hz * 1.008f); // slight detune
                float env = 1f - 0.35f * p;
                samples[start + i] += (a + bq) * 0.15f * env;
            }
        }
        if (m.bass != null)
        {
            for (int s = 0; s < MusicSteps && s < m.bass.Length; s++)
            {
                float hz = DegreeHz(m.bass[s], rootHz, semis);
                if (hz > 0f) AddBass(s, hz * 0.5f); // one octave down for bass register
            }
        }

        // --- chords: a harmony voice, one chord per beat. Explicit "prog" wins;
        // otherwise a named "preset" (a famous progression) expands to triads.
        // Drawn BEFORE the lead so the melody sits on top. Kept quiet (0.45x).
        if (m.chords != null)
        {
            string cvoice = (m.chords.voice ?? "pad").ToLowerInvariant();
            int[][] chordDegrees = null;
            if (m.chords.prog != null && m.chords.prog.Length > 0)
            {
                chordDegrees = new int[m.chords.prog.Length][];
                for (int s = 0; s < m.chords.prog.Length; s++)
                    chordDegrees[s] = m.chords.prog[s]?.notes ?? new int[0];
            }
            else if (!string.IsNullOrEmpty(m.chords.preset))
            {
                chordDegrees = ChordPreset(m.chords.preset);
            }
            if (chordDegrees != null)
            {
                for (int s = 0; s < MusicSteps && s < chordDegrees.Length; s++)
                {
                    int[] notes = chordDegrees[s];
                    if (notes == null) continue;
                    int voices = notes.Length < 4 ? notes.Length : 4; // cap at 4
                    for (int n = 0; n < voices; n++)
                    {
                        float hz = DegreeHz(notes[n], rootHz, semis);
                        if (hz > 0f) AddLeadNote(samples, s * beatSamples, beatSamples, hz, cvoice, rng, 0.45f);
                    }
                }
            }
        }

        // --- lead melody ---
        if (m.lead != null && m.lead.notes != null)
        {
            string voice = (m.lead.voice ?? "square").ToLowerInvariant();
            for (int s = 0; s < MusicSteps && s < m.lead.notes.Length; s++)
            {
                float hz = DegreeHz(m.lead.notes[s], rootHz, semis);
                if (hz > 0f) AddLeadNote(samples, s * beatSamples, beatSamples, hz, voice, rng);
            }
        }

        return ToClip("stagemusic", samples);
    }

    // Render one melodic note of the chosen voice into the buffer (additively).
    private static void AddLeadNote(float[] buf, int start, int len, float hz, string voice, System.Random rng, float gain = 1f)
    {
        switch (voice)
        {
            case "bell":
            {
                // sine fundamental + octave + 3rd partial, long exponential decay
                for (int i = 0; i < len && start + i < buf.Length; i++)
                {
                    float p = (float)i / len;
                    float ph = (float)(start + i) / SampleRate;
                    float s = Mathf.Sin(ph * hz * 2f * Mathf.PI)
                            + 0.5f * Mathf.Sin(ph * hz * 2f * 2f * Mathf.PI)
                            + 0.25f * Mathf.Sin(ph * hz * 3f * 2f * Mathf.PI);
                    buf[start + i] += s * Mathf.Pow(1f - p, 2.2f) * 0.18f * gain;
                }
                break;
            }
            case "pad":
            {
                // two detuned saws, slow attack + sustain (legato drone)
                for (int i = 0; i < len && start + i < buf.Length; i++)
                {
                    float p = (float)i / len;
                    float ph = (float)(start + i) / SampleRate;
                    float a = Osc(Wave.Saw, ph * hz);
                    float b = Osc(Wave.Saw, ph * hz * 1.006f);
                    float atk = Mathf.Min(1f, p * 6f);          // ~0.17-beat fade-in
                    float env = atk * (1f - 0.2f * p);
                    buf[start + i] += (a + b) * 0.11f * env * gain;
                }
                break;
            }
            case "koto":
            {
                // plucked string: bright saw+square blend, fast pluck decay + a
                // little pitch-drop at the very start for that struck-string feel.
                for (int i = 0; i < len && start + i < buf.Length; i++)
                {
                    float p = (float)i / len;
                    float ph = (float)(start + i) / SampleRate;
                    float bend = i < len / 40 ? 1f + 0.04f * (1f - (float)i / (len / 40f)) : 1f;
                    float s = Osc(Wave.Saw, ph * hz * bend) * 0.6f
                            + Osc(Wave.Square, ph * hz * bend) * 0.4f;
                    buf[start + i] += s * Mathf.Pow(1f - p, 3.0f) * 0.22f * gain;
                }
                break;
            }
            case "flute":
            {
                // breathy sine with a gentle vibrato and a touch of noise
                for (int i = 0; i < len && start + i < buf.Length; i++)
                {
                    float p = (float)i / len;
                    float ph = (float)(start + i) / SampleRate;
                    float vib = 1f + 0.006f * Mathf.Sin(ph * 5f * 2f * Mathf.PI);
                    float tone = Mathf.Sin(ph * hz * vib * 2f * Mathf.PI);
                    float breath = ((float)rng.NextDouble() * 2f - 1f) * 0.05f;
                    float atk = Mathf.Min(1f, p * 5f);
                    float env = atk * (1f - 0.15f * p);
                    buf[start + i] += (tone + breath) * env * 0.16f * gain;
                }
                break;
            }
            default: // square / saw / sine — the classic chiptune voices
            {
                Wave w = voice == "saw" ? Wave.Saw : voice == "sine" ? Wave.Sine : Wave.Square;
                for (int i = 0; i < len && start + i < buf.Length; i++)
                {
                    float p = (float)i / len;
                    float ph = (float)(start + i) / SampleRate;
                    float env = Mathf.Pow(1f - p, 0.8f); // slight pluck, mostly sustained
                    buf[start + i] += Osc(w, ph * hz) * env * 0.18f;
                }
                break;
            }
        }
    }

    private static AudioClip ToClip(string name, float[] samples)
    {
        // Peak-normalize so summed voices (e.g. kick+snare+hat landing on the
        // same sample) never exceed the [-1,1] range Unity hard-clips to — that
        // clipping is what makes the audio sound crunchy/distorted, especially
        // once captured in a screen recording. Leave headroom (target 0.85).
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = samples[i] < 0f ? -samples[i] : samples[i];
            if (a > peak) peak = a;
        }
        if (peak > 0.85f)
        {
            float scale = 0.85f / peak;
            for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
        }

        var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
