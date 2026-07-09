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
    Slammed = 256,
    KeyPickup = 512,
    DoorOpened = 1024,
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
    public string Kind;      // hazard | pad | boost | flip | goal | launcher
    public double Power;
    public double DirX;
    public int LastFlipTick = -1000000;
    public bool WasOverlapping;
}

public class SimFaller : SimBox
{
    public double BaseY, Dy;
    public int State;       // 0 idle, 1 falling, 2 waiting, 3 rising
    public double Offset;
    public int WaitLeft;
}

public class SimConveyor : SimBox
{
    public double Dir, Speed;
}

public class SimGate : SimBox
{
    public int PeriodTicks, OnTicks, PhaseTicks;
}

public class SimKey : SimBox
{
    public bool Collected;
}

public class SimDoor : SimBox
{
}

public class SimCannon : SimBox
{
    public double Dir, Speed;
    public int PeriodTicks, PhaseTicks;
    // Live bullet, recomputed each fire cycle (no per-bullet objects).
    public bool BulletActive;
    public double BulletX, BulletY;
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
    public const double KillMarginBelow = 8.0;
    public const double KillMarginAbove = 12.0;
    public const double GroundProbe = 0.06;
    public const double FallerFallSpeed = 8.0;
    public const double FallerRiseSpeed = 3.0;
    public const int FallerWaitTicks = 25;
    public const int FallerTelegraphTicks = 18; // shudder before the slam
    public const double FallerMargin = 0.6;
    public const double AirDamping = 0.86;

    public readonly List<SimBox> Solids = new List<SimBox>();
    public readonly List<SimMover> Movers = new List<SimMover>();
    public readonly List<SimCrumble> Crumbles = new List<SimCrumble>();
    public readonly List<SimTrigger> Triggers = new List<SimTrigger>();
    public readonly List<SimFaller> Fallers = new List<SimFaller>();
    public readonly List<SimConveyor> Conveyors = new List<SimConveyor>();
    public readonly List<SimGate> Gates = new List<SimGate>();
    public readonly List<SimKey> Keys = new List<SimKey>();
    public readonly List<SimDoor> Doors = new List<SimDoor>();
    public readonly List<SimCannon> Cannons = new List<SimCannon>();
    public int KeysCollected;

    public const double BulletHalf = 0.22;

    public double StartX, StartY;
    public double KillBottom = -12.0;
    public double KillTop = 15.0;
    public double Px, Py, Vx, Vy;
    public double GravityDir = 1.0;
    public int LockTicks;
    public int LastGroundedTick = -1000000;
    public int JumpPressedTick = -1000000;
    public int GroundMover = -1;
    public int GroundConveyor = -1;
    public double AirCarryVx;
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

    public void AddFaller(double x, double y, double w, double h, double dy)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        double fall = Q(dy);
        if (fall <= 0.0) fall = 4.0;
        var f = new SimFaller { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh, BaseY = Q(y), Dy = fall };
        Fallers.Add(f);
    }

    public void AddConveyor(double x, double y, double w, double h, double dirX, double power)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        double speed = Q(power);
        if (speed <= 0.0) speed = 3.0;
        double dir = 1.0;
        if (Q(dirX) < 0.0) dir = -1.0;
        Conveyors.Add(new SimConveyor { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh, Dir = dir, Speed = speed });
    }

    public void AddGate(double x, double y, double w, double h, double period, double phase)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        double p = Q(period);
        if (p <= 0.0) p = 2.0;
        double pt = p / Tick;
        double ph = Q(phase);
        double pht = ph / Tick;
        var gate = new SimGate
        {
            X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh,
            PeriodTicks = (int)pt,
            PhaseTicks = (int)pht,
        };
        gate.OnTicks = gate.PeriodTicks / 2;
        if (gate.PeriodTicks < 2) gate.PeriodTicks = 2;
        if (gate.OnTicks < 1) gate.OnTicks = 1;
        Gates.Add(gate);
    }

    public void AddKey(double x, double y, double w, double h)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        Keys.Add(new SimKey { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh });
    }

    public void AddDoor(double x, double y, double w, double h)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        Doors.Add(new SimDoor { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh });
    }

    public void AddCannon(double x, double y, double w, double h, double dirX, double power, double period, double phase)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        double speed = Q(power);
        if (speed <= 0.0) speed = 7.0;
        double dir = 1.0;
        if (Q(dirX) < 0.0) dir = -1.0;
        double p = Q(period);
        if (p <= 0.0) p = 2.0;
        double pt = p / Tick;
        double ph = Q(phase);
        double pht = ph / Tick;
        var cannon = new SimCannon
        {
            X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh,
            Dir = dir, Speed = speed,
            PeriodTicks = (int)pt, PhaseTicks = (int)pht,
        };
        if (cannon.PeriodTicks < 2) cannon.PeriodTicks = 2;
        Cannons.Add(cannon);
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

    public bool GateActive(SimGate g)
    {
        int phase = TickCount + g.PhaseTicks;
        int m = phase % g.PeriodTicks;
        return m < g.OnTicks;
    }

    public bool DoorsOpen()
    {
        if (Keys.Count == 0) return true;
        return KeysCollected >= Keys.Count;
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
        foreach (SimConveyor cv in Conveyors)
        {
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, cv)) return true;
        }
        foreach (SimGate g in Gates)
        {
            if (!GateActive(g)) continue;
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, g)) return true;
        }
        if (!DoorsOpen())
        {
            foreach (SimDoor d in Doors)
            {
                if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, d)) return true;
            }
        }
        foreach (SimFaller f in Fallers)
        {
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, f)) return true;
        }
        foreach (SimCannon cn in Cannons)
        {
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, cn)) return true;
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
        foreach (SimConveyor cv in Conveyors) ResolveAgainst(cv, xAxis, null);
        foreach (SimGate g in Gates)
        {
            if (!GateActive(g)) continue;
            ResolveAgainst(g, xAxis, null);
        }
        if (!DoorsOpen())
        {
            foreach (SimDoor d in Doors) ResolveAgainst(d, xAxis, null);
        }
        foreach (SimFaller f in Fallers) ResolveAgainst(f, xAxis, null);
        foreach (SimCannon cn in Cannons) ResolveAgainst(cn, xAxis, null);
    }

    private bool OverlapsAnySolid()
    {
        foreach (SimBox s in Solids)
        {
            if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, s)) return true;
        }
        foreach (SimMover m in Movers)
        {
            if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, m)) return true;
        }
        foreach (SimConveyor cv in Conveyors)
        {
            if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, cv)) return true;
        }
        foreach (SimGate g in Gates)
        {
            if (!GateActive(g)) continue;
            if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, g)) return true;
        }
        if (!DoorsOpen())
        {
            foreach (SimDoor d in Doors)
            {
                if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, d)) return true;
            }
        }
        foreach (SimCannon cn in Cannons)
        {
            if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, cn)) return true;
        }
        return false;
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
        GroundConveyor = -1;
        AirCarryVx = 0.0;
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

        // 1b. fallers (crushers): trigger when the player passes below, slam
        // down, wait, rise back. Contact while falling crushes (respawn).
        foreach (SimFaller f in Fallers)
        {
            if (f.State == 0)
            {
                // Trigger when the player is under the faller — with a wider
                // horizontal margin so it fires early and telegraphs.
                double lo = f.X - f.HalfW;
                lo = lo - FallerMargin;
                double hi = f.X + f.HalfW;
                hi = hi + FallerMargin;
                double bottom = f.Y - f.HalfH;
                if (Px > lo && Px < hi && Py < bottom)
                {
                    f.State = 4; // telegraph before slamming
                    f.WaitLeft = FallerTelegraphTicks;
                }
            }
            else if (f.State == 4)
            {
                // Held in place, shuddering, so the player sees it coming.
                f.WaitLeft = f.WaitLeft - 1;
                if (f.WaitLeft <= 0) f.State = 1;
            }
            else if (f.State == 1)
            {
                double d = FallerFallSpeed * Tick;
                f.Offset = f.Offset + d;
                if (f.Offset >= f.Dy)
                {
                    f.Offset = f.Dy;
                    f.State = 2;
                    f.WaitLeft = FallerWaitTicks;
                    Events |= SimEvents.Slammed;
                }
                f.Y = f.BaseY - f.Offset;
                if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, f))
                {
                    Respawn();
                }
                continue;
            }
            else if (f.State == 2)
            {
                f.WaitLeft = f.WaitLeft - 1;
                if (f.WaitLeft <= 0) f.State = 3;
            }
            else
            {
                double d = FallerRiseSpeed * Tick;
                f.Offset = f.Offset - d;
                if (f.Offset <= 0.0)
                {
                    f.Offset = 0.0;
                    f.State = 0;
                }
            }
            f.Y = f.BaseY - f.Offset;
        }

        // 2. crumble reset
        foreach (SimCrumble c in Crumbles)
        {
            if (c.TouchedTick < 0) continue;
            int returnAt = c.TouchedTick + CrumbleDelayTicks + CrumbleRespawnTicks;
            if (TickCount >= returnAt) c.TouchedTick = -1;
        }

        // 3. carry by ground mover / conveyor drift
        if (GroundMover >= 0)
        {
            SimMover gm = Movers[GroundMover];
            Px = Px + gm.DeltaX;
            Py = Py + gm.DeltaY;
        }
        if (GroundConveyor >= 0)
        {
            SimConveyor gc = Conveyors[GroundConveyor];
            double push = gc.Speed * Tick;
            double pd = push * gc.Dir;
            Px = Px + pd;
        }

        // 4. input
        if (input.Jump) JumpPressedTick = TickCount;
        double axis = 0.0;
        if (input.Right) axis = axis + 1.0;
        if (input.Left) axis = axis - 1.0;

        // 5. grounded
        if (ProbeGround()) LastGroundedTick = TickCount;

        // 6. control: direct while steering; releasing decays air speed
        // quickly (controllable), but ride-inherited velocity persists so
        // moving floors don't leave you behind
        bool groundedNow = LastGroundedTick == TickCount;
        if (groundedNow) AirCarryVx = 0.0;
        if (LockTicks > 0)
        {
            LockTicks = LockTicks - 1;
        }
        else if (axis != 0.0)
        {
            double steer = axis * MoveSpeed;
            Vx = steer + AirCarryVx;
        }
        else if (groundedNow)
        {
            Vx = 0.0;
        }
        else
        {
            double rel = Vx - AirCarryVx;
            double damped = rel * AirDamping;
            Vx = AirCarryVx + damped;
        }

        // 7. jump (coyote + buffer); inherits the velocity of whatever you
        // were riding so platform jumps feel glued
        int sincePress = TickCount - JumpPressedTick;
        int sinceGround = TickCount - LastGroundedTick;
        if (sincePress <= BufferTicks && sinceGround <= CoyoteTicks)
        {
            Vy = JumpSpeed * GravityDir;
            // Moving platforms carry you: jumping inherits their velocity so you
            // are not left behind. A conveyor only slides your feet — jumping
            // releases you cleanly with your own steering velocity, not the belt's.
            if (GroundMover >= 0)
            {
                SimMover jm = Movers[GroundMover];
                double mv = jm.DeltaX / Tick;
                AirCarryVx = mv;
                Vx = Vx + mv;
            }
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

        // 9b. anti-wedge safety net: if the player is still overlapping a solid
        // after resolution (squeezed between two blocks), lift them out (against
        // gravity) until clear so they can never get permanently stuck.
        double lift = 0.12 * -GravityDir;
        for (int guard = 0; guard < 16; guard++)
        {
            if (!OverlapsAnySolid()) break;
            Py = Py + lift;
            Vy = 0.0;
        }

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
            else if (tr.Kind == "launcher")
            {
                // Flings the player straight up hard; they sail off the top
                // of the world and respawn. A deadly floating trap.
                double p = tr.Power;
                if (p <= 0.0) p = 40.0;
                Vx = 0.0;
                Vy = p * GravityDir;
                LockTicks = BoostLockTicks * 3;
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

        // 10b. keys unlock the doors once all are collected
        if (!respawned)
        {
            foreach (SimKey k in Keys)
            {
                if (k.Collected) continue;
                if (!Overlaps(Px, Py, PlayerHalf, PlayerHalf, k)) continue;
                k.Collected = true;
                KeysCollected = KeysCollected + 1;
                Events |= SimEvents.KeyPickup;
                if (KeysCollected >= Keys.Count) Events |= SimEvents.DoorOpened;
            }
        }

        // 10c. cannons: each fires one bullet per period; the bullet flies
        // straight until it hits a solid or leaves the world. Position is
        // derived from ticks-since-fire (no per-bullet objects).
        if (!respawned)
        {
            foreach (SimCannon c in Cannons)
            {
                int phase = TickCount + c.PhaseTicks;
                int intoCycle = phase % c.PeriodTicks;
                double perTick = c.Speed * Tick;
                double traveled = intoCycle * perTick;
                double signedTravel = c.Dir * traveled;
                double halfDir = c.Dir * c.HalfW;
                double muzzle = c.X + halfDir;
                double bx = muzzle + signedTravel;
                double by = c.Y;

                // stop the bullet at the first solid in its path
                bool blocked = false;
                foreach (SimBox s in Solids)
                {
                    if (Overlaps(bx, by, BulletHalf, BulletHalf, s)) { blocked = true; break; }
                }
                bool offWorld = bx < -600.0 || bx > 600.0;
                c.BulletActive = !blocked && !offWorld;
                c.BulletX = bx;
                c.BulletY = by;

                if (c.BulletActive && Overlaps(bx, by, BulletHalf, BulletHalf, new SimBox { X = Px, Y = Py, HalfW = PlayerHalf, HalfH = PlayerHalf }))
                {
                    Respawn();
                    respawned = true;
                    break;
                }
            }
        }

        // 11. kill bounds (derived from stage geometry — tall stages welcome)
        if (!respawned)
        {
            if (Py < KillBottom || Py > KillTop) Respawn();
        }

        // 12. ground mover / conveyor for next tick's carry
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
        GroundConveyor = -1;
        for (int i = 0; i < Conveyors.Count; i++)
        {
            if (Overlaps(Px, gProbeY, PlayerHalf, PlayerHalf, Conveyors[i]))
            {
                GroundConveyor = i;
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
                case "faller": world.AddFaller(p.x, p.y, p.w, p.h, p.dy); break;
                case "conveyor": world.AddConveyor(p.x, p.y, p.w, p.h, p.dirX, p.power); break;
                case "timedGate": world.AddGate(p.x, p.y, p.w, p.h, p.period, p.dx); break;
                case "key": world.AddKey(p.x, p.y, p.w, p.h); break;
                case "door": world.AddDoor(p.x, p.y, p.w, p.h); break;
                case "cannon": world.AddCannon(p.x, p.y, p.w, p.h, p.dirX, p.power, p.period, p.dx); break;
                case "hazard": world.AddTrigger("hazard", p.x, p.y, p.w, p.h, 0, 0); break;
                case "jumpPad": world.AddTrigger("pad", p.x, p.y, p.w, p.h, p.power, 0); break;
                case "boost": world.AddTrigger("boost", p.x, p.y, p.w, p.h, p.power, p.dirX); break;
                case "launcher": world.AddTrigger("launcher", p.x, p.y, p.w, p.h, p.power, 0); break;
                case "gravityFlip": world.AddTrigger("flip", p.x, p.y, p.w, p.h, 0, 0); break;
            }
        }
        world.AddTrigger("goal", data.goal.x, data.goal.y, data.goal.w, data.goal.h, 0, 0);
        world.ComputeKillBounds();
        return world;
    }

    /// <summary>Kill bounds follow the stage's vertical extent so stages can climb arbitrarily high.</summary>
    public void ComputeKillBounds()
    {
        double minY = StartY;
        double maxY = StartY;
        void Consider(SimBox b)
        {
            double lo = b.Y - b.HalfH;
            double hi = b.Y + b.HalfH;
            if (lo < minY) minY = lo;
            if (hi > maxY) maxY = hi;
        }
        foreach (SimBox s in Solids) Consider(s);
        foreach (SimMover m in Movers) Consider(m);
        foreach (SimCrumble c in Crumbles) Consider(c);
        foreach (SimTrigger t in Triggers) Consider(t);
        foreach (SimFaller f in Fallers) Consider(f);
        foreach (SimConveyor cv in Conveyors) Consider(cv);
        foreach (SimGate g in Gates) Consider(g);
        foreach (SimKey k in Keys) Consider(k);
        foreach (SimDoor d in Doors) Consider(d);
        foreach (SimCannon c in Cannons) Consider(c);
        KillBottom = minY - KillMarginBelow;
        KillTop = maxY + KillMarginAbove;
    }
}
