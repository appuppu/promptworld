using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Determinism cross-check: runs the C# sim over every built-in stage with a
/// scripted input pattern and dumps bit-exact state samples. The node twin
/// (server/crosscheck.js) produces the same file from sim.js; the two must be
/// byte-identical. CLI: -executeMethod SimCrossCheck.Run
/// </summary>
public static class SimCrossCheck
{
    private const int RunTicks = 3000;
    private const int SampleEvery = 20;

    public static void Run()
    {
        var sb = new StringBuilder();
        string dir = Path.Combine(Application.streamingAssetsPath, "Stages");
        string[] files = { "stage-001.json", "stage-002.json", "stage-003.json", "stage-test-parts.json" };

        foreach (string file in files)
        {
            string json = File.ReadAllText(Path.Combine(dir, file));
            StageData data = JsonUtility.FromJson<StageData>(json);
            SimWorld world = SimWorld.FromStage(data);

            for (int t = 0; t < RunTicks; t++)
            {
                var input = new SimInput
                {
                    Right = (t % 100) < 85,
                    Left = (t % 213) < 20,
                    Jump = (t % 37) == 0,
                };
                SimEvents ev = world.Step(input);

                if (world.TickCount % SampleEvery == 0 || ev != SimEvents.None)
                {
                    sb.AppendLine(Line(file, world, ev));
                }
                if (world.ClearedFlag || world.TimedOutFlag) break;
            }
            sb.AppendLine($"{file} END cleared={world.ClearedFlag} timedOut={world.TimedOutFlag} ticks={world.TickCount}");
        }

        string outPath = Path.Combine(Directory.GetCurrentDirectory(), "simcheck_cs.txt");
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log($"[PromptWorld] SimCrossCheck written: {outPath}");
    }

    private static string Line(string file, SimWorld w, SimEvents ev)
    {
        return $"{file} {w.TickCount} {Hex(w.Px)} {Hex(w.Py)} {Hex(w.Vx)} {Hex(w.Vy)} {(int)w.GravityDir} {(int)ev}";
    }

    private static string Hex(double v)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(v);
        return bits.ToString("x16");
    }
}
