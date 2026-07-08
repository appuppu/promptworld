using System;
using System.Collections.Generic;

// =============================================================================
// PromptSim — deterministic gameplay simulation (C# side).
// server/sim.js is a LINE-FOR-LINE mirror of this file. Any change here MUST
// be applied there identically, or replay verification will break.
//
// Determinism rules (both sides):
// - all state is double; every stage-JSON value is quantized to float32
//   (C#: (double)(float)v, JS: Math.fround(v)) before entering the sim
// - only + - * / and comparisons; no transcendental functions
// - at most one floating-point operation per statement (prevents FMA fusion)
// - fixed step 0.02s (50 Hz), tick counters are integers
// =============================================================================

public struct SimInput
{
    public bool Left;
    public bool Right;
    public bool Jump; // pressed this tick (edge)

    public int Encode()
    {
        int code = 0;
        if (Left) code += 1;
        if (Right) code += 2;
        if (Jump) code += 4;
        return code;
    }

    public static SimInput Decode(int code)
    {
        return new SimInput
        {
            Left = (code & 1) != 0,
            Right = (code & 2) != 0,
            Jump = (code & 4) != 0,
        };
    }
}

[Flags]
public enum SimEvents
{
    None = 0,
    Jumped = 1,
    Bounced = 2,
    Boosted = 4,
    Flipped = 8,
    Respawned = 16,
    Crumbled = 32,
    Cleared = 64,
    TimedOut = 128,
}

public class SimBox
{
    public double X, Y, HalfW, HalfH;
}

public class SimMover : SimBox
{
    public double BaseX, BaseY, Dx, Dy, Period;
    public double PrevX, PrevY;
    public double DeltaX, DeltaY;
}

public class SimCrumble : SimBox
{
    public int TouchedTick = -1;
}

public class SimTrigger : SimBox
{
    public string Kind;      // hazard | pad | boost | flip | goal
    public double Power;
    public double DirX;
    public int LastFlipTick = -1000000;
    public bool WasOverlapping;
}

public class SimWorld
{
    public const double Tick = 0.02;
    public const double Gravity = 29.43;
    public const double MoveSpeed = 8.0;
    public const double JumpSpeed = 14.0;
    public const double PlayerHalf = 0.5;
    public const double BoostKick = 4.0;
    public const int CoyoteTicks = 5;
    public const int BufferTicks = 6;
    public const int BoostLockTicks = 30;
    public const int FlipCooldownTicks = 35;
    public const int CrumbleDelayTicks = 25;
    public const int CrumbleRespawnTicks = 125;
    public const double KillBottom = -12.0;
    public const double KillTop = 15.0;
    public const double GroundProbe = 0.06;

    public readonly List<SimBox> Solids = new List<SimBox>();
    public readonly List<SimMover> Movers = new List<SimMover>();
    public readonly List<SimCrumble> Crumbles = new List<SimCrumble>();
    public readonly List<SimTrigger> Triggers = new List<SimTrigger>();

    public double StartX, StartY;
    public double Px, Py, Vx, Vy;
    public double GravityDir = 1.0;
    public int LockTicks;
    public int LastGroundedTick = -1000000;
    public int JumpPressedTick = -1000000;
    public int GroundMover = -1;
    public int TickCount;
    public int MaxTicks;
    public bool ClearedFlag;
    public bool TimedOutFlag;
    public SimEvents Events;

    private static double Q(double v) { return (double)(float)v; }

    // stage values arrive already parsed; quantize everything to float32.
    public SimWorld(double startX, double startY, double timeLimit)
    {
        StartX = Q(startX);
        StartY = Q(startY);
        Px = StartX;
        Py = StartY;
        double limit = Q(timeLimit);
        double ticks = limit / Tick;
        MaxTicks = (int)ticks;
    }

    public void AddSolid(double x, double y, double w, double h)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        Solids.Add(new SimBox { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh });
    }

    public void AddMover(double x, double y, double w, double h, double dx, double dy, double period)
    {
        double p = Q(period);
        if (p <= 0.0) p = 4.0;
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        var m = new SimMover
        {
            BaseX = Q(x), BaseY = Q(y), HalfW = hw, HalfH = hh,
            Dx = Q(dx), Dy = Q(dy), Period = p,
        };
        m.X = m.BaseX;
        m.Y = m.BaseY;
        m.PrevX = m.BaseX;
        m.PrevY = m.BaseY;
        Movers.Add(m);
    }

    public void AddCrumble(double x, double y, double w, double h)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        Crumbles.Add(new SimCrumble { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh });
    }

    public void AddTrigger(string kind, double x, double y, double w, double h, double power, double dirX)
    {
        double hw;
        double hh;
        double cy = Q(y);
        if (kind == "hazard")
        {
            hw = Q(w) * 0.35;
            hh = Q(h) * 0.35;
        }
        else if (kind == "pad")
        {
            hw = Q(w) / 2.0;
            double grown = Q(h) + 0.5;
            hh = grown / 2.0;
            cy = cy + 0.25;
        }
        else
        {
            hw = Q(w) / 2.0;
            hh = Q(h) / 2.0;
        }
        Triggers.Add(new SimTrigger
        {
            Kind = kind, X = Q(x), Y = cy, HalfW = hw, HalfH = hh,
            Power = Q(power), DirX = Q(dirX),
        });
    }

    private bool CrumbleActive(SimCrumble c)
    {
        if (c.TouchedTick < 0) return true;
        int vanishAt = c.TouchedTick + CrumbleDelayTicks;
        return TickCount < vanishAt;
    }

    private static bool Overlaps(double ax, double ay, double ahw, double ahh, SimBox b)
    {
        double dx = ax - b.X;
        if (dx < 0.0) dx = -dx;
        double dy = ay - b.Y;
        if (dy < 0.0) dy = -dy;
        double limX = ahw + b.HalfW;
        double limY = ahh + b.HalfH;
        if (dx >= limX) return false;
        if (dy >= limY) return false;
        return true;
    }

    private bool ProbeGround()
    {
        double shift = GroundProbe * GravityDir;
        double probeY = Py - shift;
        foreach (SimBox s in Solids)
        {
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, s)) return true;
        }
        foreach (SimMover m in Movers)
        {
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, m)) return true;
        }
        foreach (SimCrumble c in Crumbles)
        {
            if (!CrumbleActive(c)) continue;
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, c)) return true;
        }
        return false;
    }

    private void ResolveAxis(bool xAxis)
    {
        foreach (SimBox s in Solids) ResolveAgainst(s, xAxis, null);
        foreach (SimMover m in Movers) ResolveAgainst(m, xAxis, null);
        foreach (SimCrumble c in Crumbles)
        {
            if (!CrumbleActive(c)) continue;
            ResolveAgainst(c, xAxis, c);
        }
    }

    private void ResolveAgainst(SimBox b, bool xAxis, SimCrumble crumble)
    {
        if (!Overlaps(Px, Py, PlayerHalf, PlayerHalf, b)) return;

        if (crumble != null && crumble.TouchedTick < 0)
        {
            crumble.TouchedTick = TickCount;
            Events |= SimEvents.Crumbled;
        }

        if (xAxis)
        {
            double lim = PlayerHalf + b.HalfW;
            if (Px < b.X)
            {
                Px = b.X - lim;
            }
            else
            {
                Px = b.X + lim;
            }
            Vx = 0.0;
        }
        else
        {
            double lim = PlayerHalf + b.HalfH;
            if (Py < b.Y)
            {
                Py = b.Y - lim;
            }
            else
            {
                Py = b.Y + lim;
            }
            Vy = 0.0;
        }
    }

    private void Respawn()
    {
        Px = StartX;
        Py = StartY;
        Vx = 0.0;
        Vy = 0.0;
        GravityDir = 1.0;
        LockTicks = 0;
        GroundMover = -1;
        LastGroundedTick = -1000000;
        JumpPressedTick = -1000000;
        Events |= SimEvents.Respawned;
    }

    public SimEvents Step(SimInput input)
    {
        Events = SimEvents.None;
        if (ClearedFlag || TimedOutFlag) return Events;

        TickCount = TickCount + 1;

        // 1. movers
        foreach (SimMover m in Movers)
        {
            double tt = TickCount * Tick;
            double s = tt / m.Period;
            double fl = Math.Floor(s);
            double f = s - fl;
            double k;
            if (f < 0.5)
            {
                k = f * 2.0;
            }
            else
            {
                double u = 1.0 - f;
                k = u * 2.0;
            }
            double ox = m.Dx * k;
            double oy = m.Dy * k;
            double nx = m.BaseX + ox;
            double ny = m.BaseY + oy;
            m.PrevX = m.X;
            m.PrevY = m.Y;
            m.X = nx;
            m.Y = ny;
            m.DeltaX = m.X - m.PrevX;
            m.DeltaY = m.Y - m.PrevY;
        }

        // 2. crumble reset
        foreach (SimCrumble c in Crumbles)
        {
            if (c.TouchedTick < 0) continue;
            int returnAt = c.TouchedTick + CrumbleDelayTicks + CrumbleRespawnTicks;
            if (TickCount >= returnAt) c.TouchedTick = -1;
        }

        // 3. carry by ground mover
        if (GroundMover >= 0)
        {
            SimMover gm = Movers[GroundMover];
            Px = Px + gm.DeltaX;
            Py = Py + gm.DeltaY;
        }

        // 4. input
        if (input.Jump) JumpPressedTick = TickCount;
        double axis = 0.0;
        if (input.Right) axis = axis + 1.0;
        if (input.Left) axis = axis - 1.0;

        // 5. grounded
        if (ProbeGround()) LastGroundedTick = TickCount;

        // 6. control
        if (LockTicks > 0)
        {
            LockTicks = LockTicks - 1;
        }
        else
        {
            Vx = axis * MoveSpeed;
        }

        // 7. jump (coyote + buffer)
        int sincePress = TickCount - JumpPressedTick;
        int sinceGround = TickCount - LastGroundedTick;
        if (sincePress <= BufferTicks && sinceGround <= CoyoteTicks)
        {
            Vy = JumpSpeed * GravityDir;
            JumpPressedTick = -1000000;
            LastGroundedTick = -1000000;
            Events |= SimEvents.Jumped;
        }

        // 8. gravity
        double dv = Gravity * Tick;
        double dvg = dv * GravityDir;
        Vy = Vy - dvg;

        // 9. integrate + collide, axis separated
        double mx = Vx * Tick;
        Px = Px + mx;
        ResolveAxis(true);
        double my = Vy * Tick;
        Py = Py + my;
        ResolveAxis(false);

        // 10. triggers (edge-based, stage order within kind order)
        bool respawned = false;
        foreach (SimTrigger tr in Triggers)
        {
            bool overlap = Overlaps(Px, Py, PlayerHalf, PlayerHalf, tr);
            bool fire = overlap && !tr.WasOverlapping;
            tr.WasOverlapping = overlap;
            if (!fire) continue;

            if (tr.Kind == "hazard")
            {
                Respawn();
                respawned = true;
                break;
            }
            if (tr.Kind == "pad")
            {
                double p = tr.Power;
                if (p <= 0.0) p = 22.0;
                Vy = p * GravityDir;
                Events |= SimEvents.Bounced;
            }
            else if (tr.Kind == "boost")
            {
                double p = tr.Power;
                if (p <= 0.0) p = 10.0;
                double d = 1.0;
                if (tr.DirX < 0.0) d = -1.0;
                Vx = d * p;
                Vy = BoostKick * GravityDir;
                LockTicks = BoostLockTicks;
                Events |= SimEvents.Boosted;
            }
            else if (tr.Kind == "flip")
            {
                int since = TickCount - tr.LastFlipTick;
                if (since >= FlipCooldownTicks)
                {
                    GravityDir = -GravityDir;
                    tr.LastFlipTick = TickCount;
                    Events |= SimEvents.Flipped;
                }
            }
            else if (tr.Kind == "goal")
            {
                ClearedFlag = true;
                Events |= SimEvents.Cleared;
                return Events;
            }
        }

        // 11. kill zones
        if (!respawned)
        {
            if (Py < KillBottom || Py > KillTop) Respawn();
        }

        // 12. ground mover for next tick's carry
        GroundMover = -1;
        double gShift = GroundProbe * GravityDir;
        double gProbeY = Py - gShift;
        for (int i = 0; i < Movers.Count; i++)
        {
            if (Overlaps(Px, gProbeY, PlayerHalf, PlayerHalf, Movers[i]))
            {
                GroundMover = i;
                break;
            }
        }

        // 13. time limit
        if (TickCount >= MaxTicks)
        {
            TimedOutFlag = true;
            Events |= SimEvents.TimedOut;
        }
        return Events;
    }

    /// <summary>Builds a SimWorld from parsed stage data (client convenience).</summary>
    public static SimWorld FromStage(StageData data)
    {
        var world = new SimWorld(data.playerStart.x, data.playerStart.y, data.timeLimit);
        foreach (PartData p in data.parts)
        {
            switch (p.type)
            {
                case "solid": world.AddSolid(p.x, p.y, p.w, p.h); break;
                case "movingPlatform": world.AddMover(p.x, p.y, p.w, p.h, p.dx, p.dy, p.period); break;
                case "crumble": world.AddCrumble(p.x, p.y, p.w, p.h); break;
                case "hazard": world.AddTrigger("hazard", p.x, p.y, p.w, p.h, 0, 0); break;
                case "jumpPad": world.AddTrigger("pad", p.x, p.y, p.w, p.h, p.power, 0); break;
                case "boost": world.AddTrigger("boost", p.x, p.y, p.w, p.h, p.power, p.dirX); break;
                case "gravityFlip": world.AddTrigger("flip", p.x, p.y, p.w, p.h, 0, 0); break;
            }
        }
        world.AddTrigger("goal", data.goal.x, data.goal.y, data.goal.w, data.goal.h, 0, 0);
        return world;
    }
}
