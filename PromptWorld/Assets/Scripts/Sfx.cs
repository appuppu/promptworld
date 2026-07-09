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

    public static void Play(SfxId id, float volume = 0.5f)
    {
        EnsureInit();
        source.PlayOneShot(clips[id], volume);
    }

    /// <summary>Looping synthesized drum groove — plays only during stage runs.</summary>
    public static void StartMusic()
    {
        EnsureInit();
        if (musicSource.isPlaying) return;
        if (musicSource.clip == null) musicSource.clip = BuildDrumLoop();
        musicSource.loop = true;
        musicSource.volume = 0.22f;
        musicSource.Play();
    }

    public static void StopMusic()
    {
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

    private static AudioClip ToClip(string name, float[] samples)
    {
        var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
