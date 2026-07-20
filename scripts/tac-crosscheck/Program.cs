// TAC crosscheck (C# side). Emits: (1) a math-kernel trace, (2) full-stage
// per-tick simulation traces over the stages/ JSONs with a scripted input
// generator, (3) a replay-codec round trip re-verified through RunReplay.
// scripts/tac-crosscheck.js emits the identical text from tacsim.js;
// tac-crosscheck.sh diffs byte-for-byte.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class Program
{
    static int seed = 12345;
    static int Lcg()
    {
        seed = unchecked(seed * 1103515245 + 12345) & 0x3fffffff;
        return seed;
    }
    static double Rnd(double lo, double hi)
    {
        return lo + Lcg() * ((hi - lo) / 1073741824.0);
    }
    static string Bits(double v)
    {
        return ((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("x16");
    }

    static int heading, yaw, pitch;
    static TacInput MakeInput(int t)
    {
        if (t % 37 == 0) heading = (Lcg() & 255) > 200 ? 255 : (Lcg() & 127);
        if (t % 61 == 0) yaw = Lcg() & 65535;
        if (t % 53 == 0) pitch = (Lcg() % 20000) - 10000;
        int b = 0;
        if ((t % 97) < 40) b |= 2;
        if ((t % 131) < 50) b |= 4;
        if (t % 149 == 0) b |= 1;
        if (t % 211 == 0) b |= 16;
        if (t % 387 == 0) b |= 32;
        if (t % 501 == 0) b |= 8;
        return new TacInput { b = b, m = heading, yawQ = yaw, pitchQ = pitch };
    }

    static void Main(string[] args)
    {
        var sb = new StringBuilder();
        // ---- kernel trace ----
        for (int q = -70000; q < 140000; q += 17)
        {
            sb.Append("sin ").Append(q).Append(' ').Append(Bits(TacMath.SinQ(q))).Append('\n');
            sb.Append("cos ").Append(q).Append(' ').Append(Bits(TacMath.CosQ(q))).Append('\n');
        }
        for (int i = 0; i < 2000; i++)
        {
            double v = Rnd(-1000.0, 1000.0);
            sb.Append("q ").Append(Bits(TacMath.Q(v))).Append('\n');
        }
        for (int i = 0; i < 2000; i++)
        {
            int cur = Lcg() & 65535;
            int tgt = Lcg() & 65535;
            int rate = (Lcg() % 900) + 1;
            sb.Append("turn ").Append(TacMath.TurnToward(cur, tgt, rate)).Append('\n');
        }
        for (int i = 0; i < 2000; i++)
        {
            double dx = Rnd(-10.0, 10.0);
            double dz = Rnd(-10.0, 10.0);
            sb.Append("yaw ").Append(TacMath.YawFor(dx, dz)).Append('\n');
        }
        for (int i = 0; i < 3000; i++)
        {
            double x0 = Rnd(-5.0, 5.0), y0 = Rnd(0.0, 3.0), z0 = Rnd(-5.0, 5.0);
            double x1 = Rnd(-5.0, 5.0), y1 = Rnd(0.0, 3.0), z1 = Rnd(-5.0, 5.0);
            double cx = Rnd(-3.0, 3.0), cz = Rnd(-3.0, 3.0);
            double cr = Rnd(0.1, 2.0), ch = Rnd(0.2, 3.0);
            sb.Append("segcyl ").Append(Bits(TacMath.SegCylinder(x0, y0, z0, x1, y1, z1, cx, 0.0, cz, cr, ch))).Append('\n');
        }

        // ---- full-stage simulation traces ----
        string dir = args.Length > 0 ? args[0] : "stages";
        string[] names = { "kitchen", "breach", "expiry", "extract", "castle", "steps", "armor" };
        for (int sIdx = 0; sIdx < names.Length; sIdx++)
        {
            string json = File.ReadAllText(Path.Combine(dir, names[sIdx] + ".json"));
            var stage = TacJson.Parse(json);
            var w = new TacWorld(stage);
            seed = 1000 + sIdx;
            heading = 255; yaw = 0; pitch = 0;
            var recs = new List<TacInput>();
            sb.Append("STAGE ").Append(names[sIdx]).Append(' ').Append(Bits(w.px)).Append(' ').Append(Bits(w.py)).Append(' ').Append(Bits(w.pz)).Append(' ').Append(w.enemiesLeft).Append('\n');
            for (int t = 0; t < 2600; t++)
            {
                var inp = MakeInput(t);
                recs.Add(inp);
                w.Step(inp);
                int liveB = 0;
                for (int bi = 0; bi < w.bullets.Count; bi++) if (w.bullets[bi].alive) liveB++;
                int liveG = 0;
                for (int gi = 0; gi < w.grenades.Count; gi++) if (w.grenades[gi].alive) liveG++;
                int actBo = 0;
                for (int oi = 0; oi < w.bombs.Count; oi++) if (w.bombs[oi].state != 2) actBo++;
                sb.Append("P ").Append(w.tick).Append(' ').Append(Bits(w.px)).Append(' ').Append(Bits(w.py)).Append(' ').Append(Bits(w.pz)).Append(' ').Append(Bits(w.vy))
                  .Append(' ').Append(w.hp).Append(' ').Append(w.lockTarget).Append(' ').Append(w.lockKind).Append(' ').Append(w.enemiesLeft)
                  .Append(' ').Append(w.shotsFired).Append(' ').Append(w.scopeCd).Append(' ').Append(w.grenadeCd).Append(' ').Append(w.droneUses)
                  .Append(' ').Append(w.pilot != null ? 1 : 0).Append(' ').Append(w.crouched ? 1 : 0).Append(' ').Append(w.scoped ? 1 : 0)
                  .Append(' ').Append(liveB).Append(' ').Append(liveG).Append(' ').Append(actBo).Append(' ').Append(w.intelLeft).Append(' ').Append(w.playerLit ? 1 : 0).Append('\n');
                if ((t % 25) == 0)
                {
                    for (int e = 0; e < w.enemies.Count; e++)
                    {
                        var en = w.enemies[e];
                        sb.Append("E ").Append(w.tick).Append(' ').Append(e).Append(' ').Append(Bits(en.x)).Append(' ').Append(Bits(en.z)).Append(' ').Append(Bits(en.y))
                          .Append(' ').Append(en.yawQ).Append(' ').Append(en.state).Append(' ').Append(en.hp).Append(' ').Append(en.alive ? 1 : 0)
                          .Append(' ').Append(Bits(en.gauge)).Append('\n');
                    }
                }
                if (w.dead || w.clearedFlag || w.timedOutFlag) break;
            }
            sb.Append("END ").Append(names[sIdx]).Append(' ').Append(w.tick).Append(' ').Append(w.dead ? 1 : 0).Append(' ').Append(w.clearedFlag ? 1 : 0).Append(' ').Append(w.timedOutFlag ? 1 : 0).Append(' ').Append(w.hp).Append(' ').Append(w.enemiesLeft).Append('\n');
            // codec round trip + independent re-verification through RunReplay
            string enc = TacReplay.EncodeTrace(recs);
            var dec = TacReplay.DecodeTrace(enc, 100000);
            bool match = dec != null && dec.Count == recs.Count;
            if (match)
            {
                for (int i = 0; i < recs.Count; i++)
                {
                    if (dec[i].b != recs[i].b || dec[i].m != recs[i].m || dec[i].yawQ != recs[i].yawQ || dec[i].pitchQ != recs[i].pitchQ) { match = false; break; }
                }
            }
            var rr = TacReplay.RunReplay(TacJson.Parse(json), "t1", enc, 100000);
            sb.Append("CODEC ").Append(names[sIdx]).Append(' ').Append(enc.Length).Append(' ').Append(match ? 1 : 0).Append(' ').Append(rr.cleared ? 1 : 0).Append(' ').Append(rr.ticks).Append('\n');
        }
        Console.Out.Write(sb.ToString());
    }
}
