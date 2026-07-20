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
    Stomped = 2048,      // an enemy took a stomp hit
    EnemyDown = 4096,    // an enemy was defeated (hp hit 0)
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
    public double PrevY, DeltaY;   // vertical movement this tick, to carry riders
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
    public double WallDist;   // muzzle-to-first-solid distance (bullet flies until it hits a wall)
    // Live bullet, recomputed each fire cycle (no per-bullet objects).
    public bool BulletActive;
    public double BulletX, BulletY;
}

// A deadly point orbiting a fixed center. Position is derived each tick from a
// baked 24-step unit-circle table (no trig -> byte-identical across C#/JS).
// X/Y hold the CENTER; the live hazard box is at the orbiting point.
public class SimRotor : SimBox
{
    public double Radius;     // orbit radius in world units
    public double SpinDir;    // +1 clockwise, -1 counter-clockwise
    public int PeriodTicks;   // ticks for one full revolution
    public int PhaseTicks;    // starting offset around the orbit
    public double HeadHalf;   // half-size of the deadly spike head
    public double HeadX, HeadY; // live spike-head position (for rendering)
}

// A wall of death that sweeps steadily in ONE direction, forcing the player to
// keep moving ahead of it. X/Y hold the START centre; each tick the live box
// advances by Speed along (DirX,DirY). Touching it = respawn. Position is a plain
// function of the tick, so it's byte-identical across C#/JS.
public class SimWave : SimBox
{
    public double Speed;      // world units per tick
    public double DirX;       // -1 / 0 / +1
    public double DirY;       // -1 / 0 / +1  (down->up = +1, up->down = -1)
    public int DelayTicks;    // ticks to wait after (re)start before it moves
    public double OffsetX, OffsetY; // wave-start MINUS player-start, so a respawn re-anchors it behind the respawn point (checkpoint-aware)
    public double AnchorX, AnchorY; // where the current sweep starts from (reset on respawn)
    public int RestartTick;   // the tick the current sweep's clock is measured from
    public double CurX, CurY; // live centre (for rendering + kill test)
}

// Teleporter endpoint. Entering an entry (isEntry) warps the player to its
// paired exit, preserving velocity. Pairing is by PairId (entry+exit share it).
public class SimTeleporter : SimBox
{
    public int PairId;
    public bool IsEntry;
    public int ExitIndex;     // resolved at load: index into Teleporters of the paired exit
    public bool WasOverlapping;
}

// A continuous wind zone: while the player overlaps it, the fan pushes them in
// a fixed direction each tick (unlike edge-based triggers). Dir is a unit
// direction; Power is the target speed along it.
public class SimFan : SimBox
{
    public double DirX, DirY, Power;
}

// A pressure switch. While the player stands on/overlaps it, its linked gate
// group (GateId) is held fully OPEN; on release the gate slowly closes.
public class SimSwitch : SimBox
{
    public int GateId;
}

// A portcullis/guillotine gate tied to a switch group (GateId). It's a SOLID
// block that hangs from a FIXED TOP edge: pressing a matching switch raises its
// bottom (opens the gap upward), releasing lets it descend again. The live
// collision box (Y/HalfH, inherited from SimBox) is recomputed each tick from
// OpenTicks. A player caught under the DESCENDING bottom edge and pinned to the
// floor is crushed — same rule as a faller.
public class SimSwitchGate : SimBox
{
    public int GateId;
    public int OpenTicks;     // 0 = fully closed (full height), OpenMax = fully open (~0 height, raised)
    public double FullHalfH;  // authored half-height (max)
    public double Top;        // fixed TOP edge (BaseY + FullHalfH) — the gate hangs from here
}

// A patrolling monster. Moves left/right across a span (triangle wave, like a
// mover). Stomp it from ABOVE to damage it (player bounces); touch it from the
// side or below and the PLAYER dies. Hp>1 = a tougher enemy that takes several
// stomps. IsBoss enemies gate the exit: while any boss is alive, the boss doors
// stay shut. X/Y hold the LIVE position; BaseX/BaseY the patrol anchor.
public class SimEnemy : SimBox
{
    public double BaseX, BaseY, Dx, Period;
    public int Hp;            // remaining hits; 0 = defeated (removed from play)
    public int MaxHp;
    public bool IsBoss;
    public bool Dead;
    public int HitTick;       // tick of the last stomp (for the flash/shrink effect)
    public int Facing;        // +1 / -1, for rendering the eyes
    // Behaviour: 0 = classic back-and-forth glide, 1 = CHASER (walks toward the
    // player, won't walk off ledges), 2 = PATROL (walks, turns at ledges/walls),
    // 3 = JUMPER (hops). WalkDir is the current heading for walking modes.
    public int Mode;
    public int WalkDir;       // +1 / -1
    public double Speed;      // walk speed (world u/s)
    // Boss behaviour (bosses jump and breathe fire). VyJump is live vertical
    // velocity for the hop; GroundY the patrol floor to land back on.
    public double VyJump;
    public double GroundY;
    public bool InAir;
    // One reusable fireball per boss (deterministic — at most one live at a time).
    public bool FireActive;
    public double FireX, FireY, FireDir;
    public int FireStartTick;
    public double FireLaunchX, FireLaunchY; // mouth position captured at fire time
}

public class SimWorld
{
    public const double Tick = 0.02;
    // Snappy (not floaty) jump: strong gravity + high launch keeps jump HEIGHT the
    // same as the original (~3.3u) while cutting airtime ~29% (0.95s -> 0.68s). All
    // the other vertical launches below are rescaled to the SAME gravity so their
    // heights are preserved too (e.g. the stomp bounce stays as high as before).
    public const double Gravity = 58.0;
    public const double MoveSpeed = 8.0;
    public const double JumpSpeed = 19.65;
    public const double PlayerHalf = 0.5;
    public const double BoostKick = 4.0;
    public const int CoyoteTicks = 5;
    public const int BufferTicks = 6;
    // Corner correction: when RISING into a ceiling but the player only clips its
    // corner by <= this much horizontally, slide them sideways past the edge
    // instead of stopping the jump dead. Pure kindness for near-miss jumps.
    public const double CornerNudge = 0.28;
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
    public const double FallerRideTolerance = 0.35; // feet-to-(pre-move)top gap that still counts as "riding" (covers one fall tick)
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
    public readonly List<SimRotor> Rotors = new List<SimRotor>();
    public readonly List<SimWave> Waves = new List<SimWave>();
    public readonly List<SimTeleporter> Teleporters = new List<SimTeleporter>();
    public readonly List<SimFan> Fans = new List<SimFan>();
    public readonly List<SimSwitch> Switches = new List<SimSwitch>();
    public readonly List<SimSwitchGate> SwitchGates = new List<SimSwitchGate>();
    public readonly List<SimEnemy> Enemies = new List<SimEnemy>();
    public readonly List<SimBox> BossDoors = new List<SimBox>();
    public int KeysCollected;

    // Enemy tuning.
    public const double EnemyStompBounce = 15.44;   // upward pop the player gets from a stomp (height preserved at g=58)
    public const double EnemyStompJumpBounce = 23.87; // higher pop when the jump button is held on the stomp (Mario-style; height preserved)
    // Boss behaviour tuning.
    public const int BossJumpPeriodTicks = 90;      // hops every 1.8s
    public const double BossJumpSpeed = 16.85;       // hop launch speed (height preserved at g=58)
    public const int BossFirePeriodTicks = 70;      // breathes fire every 1.4s
    public const double BossFireSpeed = 9.0;        // fireball speed
    public const double BossFireMaxDist = 18.0;     // fireball range: flies until it hits a wall OR this far (so the boss keeps refiring in open space)
    public const double FireballHalf = 0.28;
    public const int EnemyHitFlashTicks = 10;       // flash/shrink duration after a hit
    public const double EnemyStompMargin = 0.30;    // how far above the enemy's mid the player's feet must be to count as a stomp
    public const int EnemyStompGraceTicks = 8;       // after a stomp, ignore this enemy briefly while the player bounces clear
    public const double EnemyWalkSpeed = 3.5;        // ground walk speed for chaser/patrol modes
    public const int EnemyJumpPeriodTicks = 55;      // a jumper hops this often
    public const double EnemyJumpSpeed = 15.44;       // jumper hop launch speed (height preserved at g=58)
    public const double EnemyGroundProbeAhead = 0.35; // how far ahead to look for a ledge/wall

    // Baked 24-step unit circle for the rotating hazard. Written as identical
    // decimal literals in server/sim.js so both sides orbit byte-for-byte the
    // same (no trig at runtime, which would diverge across platforms).
    public const int OrbitSteps = 24;
    public static readonly double[] OrbitCos = {
        1, 0.9659258127212524, 0.8660253882408142, 0.7071067690849304, 0.5, 0.258819043636322, 0, -0.258819043636322, -0.5, -0.7071067690849304, -0.8660253882408142, -0.9659258127212524, -1, -0.9659258127212524, -0.8660253882408142, -0.7071067690849304, -0.5, -0.258819043636322, 0, 0.258819043636322, 0.5, 0.7071067690849304, 0.8660253882408142, 0.9659258127212524
    };
    public static readonly double[] OrbitSin = {
        0, 0.258819043636322, 0.5, 0.7071067690849304, 0.8660253882408142, 0.9659258127212524, 1, 0.9659258127212524, 0.8660253882408142, 0.7071067690849304, 0.5, 0.258819043636322, 0, -0.258819043636322, -0.5, -0.7071067690849304, -0.8660253882408142, -0.9659258127212524, -1, -0.9659258127212524, -0.8660253882408142, -0.7071067690849304, -0.5, -0.258819043636322
    };

    // Switch gate: how slowly it eases shut after you step off (ticks to go from
    // fully open back to fully closed). 90 ticks = 1.8s of grace.
    public const int SwitchGateCloseTicks = 90;
    public const int SwitchGateOpenTicks = 6; // near-instant open when pressed

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
    public int RidingFaller = -1;
    public int GroundConveyor = -1;
    public double AirCarryVx;
    public int TickCount;
    public int MaxTicks;
    public bool ClearedFlag;
    public bool TimedOutFlag;
    public int MaxLives;
    public int LivesLeft;
    public bool LivesOut;     // ran out of lives (distinct reason from time-out)
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
        MaxLives = 5;
        LivesLeft = 5;
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

    /// <summary>
    /// Ground-collision for crushers: a crusher may not sink through a solid
    /// floor. Clamp each crusher's slam distance so its BOTTOM edge comes to
    /// rest flush on the highest solid directly beneath it, instead of sinking
    /// until its top edge lines up with the floor. Runs after all parts load
    /// (order-independent).
    /// </summary>
    private void ResolveFallerLandings()
    {
        foreach (SimFaller f in Fallers)
        {
            double bottom = f.Y - f.HalfH;
            double cLeft = f.X - f.HalfW;
            double cRight = f.X + f.HalfW;
            double maxFall = f.Dy;
            foreach (SimBox s in Solids)
            {
                double sLeft = s.X - s.HalfW;
                double sRight = s.X + s.HalfW;
                if (cRight <= sLeft) continue;
                if (cLeft >= sRight) continue;
                double sTop = s.Y + s.HalfH;
                if (sTop > bottom) continue; // solid is not below the crusher
                double gap = bottom - sTop;
                if (gap < maxFall) maxFall = gap;
            }
            if (maxFall < 0.0) maxFall = 0.0;
            f.Dy = maxFall;
        }
    }

    /// <summary>
    /// Precompute each cannon's firing range: the distance from its muzzle to
    /// the first solid in the bullet's path. The bullet then flies that far
    /// (until it hits a wall) instead of vanishing at an arbitrary per-period
    /// distance. Runs after all parts load (order-independent).
    /// </summary>
    private void ResolveCannonRanges()
    {
        foreach (SimCannon c in Cannons)
        {
            double halfDir = c.Dir * c.HalfW;
            double muzzle = c.X + halfDir;
            double by = c.Y;
            double best = 1200.0; // effectively "off the world" if nothing blocks
            foreach (SimBox s in Solids)
            {
                double sLeft = s.X - s.HalfW;
                double sRight = s.X + s.HalfW;
                double sTop = s.Y + s.HalfH;
                double sBot = s.Y - s.HalfH;
                // must overlap the bullet's horizontal band vertically
                if (by + BulletHalf <= sBot) continue;
                if (by - BulletHalf >= sTop) continue;
                if (c.Dir > 0.0)
                {
                    // wall is to the right: near face is its left edge
                    double face = sLeft - BulletHalf;
                    double dist = face - muzzle;
                    if (dist < 0.0) continue;
                    if (dist < best) best = dist;
                }
                else
                {
                    double face = sRight + BulletHalf;
                    double dist = muzzle - face;
                    if (dist < 0.0) continue;
                    if (dist < best) best = dist;
                }
            }
            if (best < 0.0) best = 0.0;
            c.WallDist = best;
        }
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

    // Rotating hazard: a spike head orbiting the block's center. w/h set the
    // orbit diameter; power = seconds per revolution; dirX<0 spins the other way;
    // dx = head size (defaults small).
    public void AddRotor(double x, double y, double w, double h, double power, double dirX, double head)
    {
        double rw = Q(w) / 2.0;
        double rh = Q(h) / 2.0;
        double radius = rw;
        if (rh > radius) radius = rh;   // orbit radius = larger half-extent
        double spin = 1.0;
        if (Q(dirX) < 0.0) spin = -1.0;
        double p = Q(power);
        if (p <= 0.0) p = 2.0;
        double pt = p / Tick;
        double hh = Q(head);
        if (hh <= 0.0) hh = 0.35;
        var r = new SimRotor
        {
            X = Q(x), Y = Q(y), HalfW = rw, HalfH = rh,
            Radius = radius, SpinDir = spin,
            PeriodTicks = (int)pt, PhaseTicks = 0,
            HeadHalf = hh, HeadX = Q(x), HeadY = Q(y),
        };
        if (r.PeriodTicks < OrbitSteps) r.PeriodTicks = OrbitSteps;
        Rotors.Add(r);
    }

    // Sweeping wall of death. power = speed (units/sec); dirX/dirY pick the sweep
    // direction (left->right = dirX 1; down->up = dirY 1; up->down = dirY -1);
    // period = seconds to wait before it starts moving.
    public void AddWave(double x, double y, double w, double h, double power, double dirX, double dirY, double period)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        double spd = Q(power);
        if (spd <= 0.0) spd = 8.0;      // default ~= player run speed
        double perTick = spd * Tick;    // world units per tick
        double ddx = Q(dirX);
        double ddy = Q(dirY);
        // Normalize direction to one axis: prefer X if given, else Y.
        double ux = 0.0;
        double uy = 0.0;
        if (ddx > 0.0) ux = 1.0;
        else if (ddx < 0.0) ux = -1.0;
        else if (ddy > 0.0) uy = 1.0;
        else if (ddy < 0.0) uy = -1.0;
        else ux = 1.0;                  // default left->right
        double delay = Q(period);
        if (delay < 0.0) delay = 0.0;
        int startTick = (int)(delay / Tick);
        double wx = Q(x);
        double wy = Q(y);
        double offX = wx - StartX; // keep the wave's lead relative to the spawn point
        double offY = wy - StartY;
        var wv = new SimWave
        {
            X = wx, Y = wy, HalfW = hw, HalfH = hh,
            Speed = perTick, DirX = ux, DirY = uy, DelayTicks = startTick,
            OffsetX = offX, OffsetY = offY,
            AnchorX = wx, AnchorY = wy, RestartTick = startTick,
            CurX = wx, CurY = wy,
        };
        Waves.Add(wv);
    }

    // Teleporter endpoint. dirX>=0 marks an entry, dirX<0 marks an exit; period
    // carries the pair id (integer) so an entry warps to the exit sharing it.
    public void AddTeleporter(double x, double y, double w, double h, double dirX, double pairId)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        bool isEntry = Q(dirX) >= 0.0;
        int pid = (int)Q(pairId);
        Teleporters.Add(new SimTeleporter
        {
            X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh,
            PairId = pid, IsEntry = isEntry, ExitIndex = -1,
        });
    }

    // Fan: a wind zone that pushes the player while overlapping. dirX/power set
    // the horizontal component, dy the vertical (default: straight up).
    public void AddFan(double x, double y, double w, double h, double dirX, double power, double dy)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        double px = Q(dirX);
        double py = Q(dy);
        if (px == 0.0 && py == 0.0) py = 1.0;   // default blows up
        double spd = Q(power);
        if (spd <= 0.0) spd = 12.0;
        Fans.Add(new SimFan
        {
            X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh,
            DirX = px, DirY = py, Power = spd,
        });
    }

    // Pressure switch. period carries the gate-group id it toggles.
    public void AddSwitch(double x, double y, double w, double h, double gateId)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        int gid = (int)Q(gateId);
        Switches.Add(new SimSwitch { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh, GateId = gid });
    }

    // Switch-linked gate. period carries the gate-group id. Starts closed (solid).
    public void AddSwitchGate(double x, double y, double w, double h, double gateId)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        int gid = (int)Q(gateId);
        double cy = Q(y);
        double top = cy + hh;
        SwitchGates.Add(new SimSwitchGate
        {
            X = Q(x), Y = cy, HalfW = hw, HalfH = hh, GateId = gid, OpenTicks = 0,
            FullHalfH = hh, Top = top,
        });
    }

    // An enemy. mode (dirX): 0 glide, 1 chaser, 2 patrol-walk, 3 jumper. dx/period
    // = patrol span & round-trip seconds (glide) or hop cadence (jumper). power =
    // HP (default 1); dyFlag>0 marks it a BOSS.
    public void AddEnemy(double x, double y, double w, double h, double dx, double period, double power, double dyFlag, double mode)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        int hp = (int)Q(power);
        if (hp < 1) hp = 1;
        double p = Q(period);
        if (p <= 0.0) p = 4.0;
        int md = (int)Q(mode);
        if (md < 0 || md > 3) md = 0;
        var e = new SimEnemy
        {
            BaseX = Q(x), BaseY = Q(y), HalfW = hw, HalfH = hh,
            Dx = Q(dx), Period = p,
            Hp = hp, MaxHp = hp,
            IsBoss = Q(dyFlag) > 0.0,
            Dead = false, HitTick = -1000000, Facing = 1,
            GroundY = Q(y), InAir = false, VyJump = 0.0,
            FireActive = false, FireStartTick = -1000000,
            Mode = md, WalkDir = 1, Speed = EnemyWalkSpeed,
        };
        e.X = e.BaseX;
        e.Y = e.BaseY;
        Enemies.Add(e);
    }

    // Does a solid box block the enemy's BODY at x = nx (a wall it can't walk
    // through)? Ignores the floor it stands on (bodyBot lifted by 0.1).
    private bool EnemyBlockedBy(SimEnemy en, double nx, SimBox b)
    {
        double sTop = b.Y + b.HalfH;
        double sBot = b.Y - b.HalfH;
        double sLeft = b.X - b.HalfW;
        double sRight = b.X + b.HalfW;
        double foot = en.BaseY - en.HalfH;
        double bodyTop = en.BaseY + en.HalfH;
        double bodyBot = foot + 0.1;
        return (nx + en.HalfW > sLeft && nx - en.HalfW < sRight &&
                bodyTop > sBot && bodyBot < sTop);
    }

    // Can a walking enemy stand at x = nx? True when ground supports its feet just
    // ahead (won't walk off a ledge) and no solid wall blocks its body there —
    // this includes plain solids, closed doors, closed switch gates and shut boss
    // doors, so a monster can't stroll through a wall or a locked exit.
    private bool EnemyCanStand(SimEnemy en, double nx)
    {
        double foot = en.BaseY - en.HalfH;
        double dir = nx >= en.X ? 1.0 : -1.0;
        double edgeX = nx + dir * en.HalfW;
        bool groundAhead = false;
        foreach (SimBox s in Solids)
        {
            double sTop = s.Y + s.HalfH;
            double sLeft = s.X - s.HalfW;
            double sRight = s.X + s.HalfW;
            double gap = foot - sTop;
            if (gap < 0.0) gap = -gap;
            if (gap <= EnemyGroundProbeAhead && edgeX > sLeft && edgeX < sRight)
            {
                groundAhead = true;
            }
            if (EnemyBlockedBy(en, nx, s)) return false;
        }
        if (!BossDoorsOpen())
        {
            foreach (SimBox d in BossDoors) if (EnemyBlockedBy(en, nx, d)) return false;
        }
        if (!DoorsOpen())
        {
            foreach (SimDoor d in Doors) if (EnemyBlockedBy(en, nx, d)) return false;
        }
        foreach (SimSwitchGate sg in SwitchGates)
        {
            if (SwitchGateSolid(sg) && EnemyBlockedBy(en, nx, sg)) return false;
        }
        return groundAhead;
    }

    // A boss door: a solid wall that stays shut while any boss enemy is alive and
    // vanishes once every boss is defeated (opening the way to the goal).
    public void AddBossDoor(double x, double y, double w, double h)
    {
        double hw = Q(w) / 2.0;
        double hh = Q(h) / 2.0;
        BossDoors.Add(new SimBox { X = Q(x), Y = Q(y), HalfW = hw, HalfH = hh });
    }

    // True once every boss enemy has been defeated (or there are none).
    public bool BossDoorsOpen()
    {
        foreach (SimEnemy e in Enemies)
        {
            if (e.IsBoss && !e.Dead) return false;
        }
        return true;
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

    // Recompute a switch gate's LIVE collision box from OpenTicks: the gate hangs
    // from its FIXED TOP edge and its height shrinks from FullHalfH*2 (closed,
    // reaching the floor) toward ~0 (open, raised up). The BOTTOM edge rises as
    // it opens and descends as it closes — a portcullis. Returns true while it
    // still has enough height to collide. Called each collision pass; OpenTicks
    // is constant within a tick so the mutation is idempotent (deterministic).
    public bool SwitchGateSolid(SimSwitchGate g)
    {
        // remain: 1 at OpenTicks=0 (full, hanging to the floor), 0 when raised.
        double frac = (double)g.OpenTicks / (double)SwitchGateCloseTicks;
        double remain = 1.0 - frac;
        double fullH = g.FullHalfH + g.FullHalfH;
        double liveH = fullH * remain;
        double liveHalfH = liveH / 2.0;
        g.HalfH = liveHalfH;
        g.Y = g.Top - liveHalfH; // hang from the fixed top; bottom rises as it opens
        return liveHalfH > 0.02;
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

    /// Highest solid/gate/door surface top strictly below `fromY` that overlaps
    /// the player's horizontal span. Returns a very low value if none — used by
    /// the crusher to decide whether a slam squishes the player against a floor.
    private double SurfaceTopBelow(double fromY)
    {
        double best = -1000000.0;
        foreach (SimBox s in Solids)
        {
            if (s.X - s.HalfW >= Px + PlayerHalf) continue;
            if (s.X + s.HalfW <= Px - PlayerHalf) continue;
            double top = s.Y + s.HalfH;
            if (top <= fromY && top > best) best = top;
        }
        foreach (SimGate g in Gates)
        {
            if (!GateActive(g)) continue;
            if (g.X - g.HalfW >= Px + PlayerHalf) continue;
            if (g.X + g.HalfW <= Px - PlayerHalf) continue;
            double top = g.Y + g.HalfH;
            if (top <= fromY && top > best) best = top;
        }
        if (!DoorsOpen())
        {
            foreach (SimDoor d in Doors)
            {
                if (d.X - d.HalfW >= Px + PlayerHalf) continue;
                if (d.X + d.HalfW <= Px - PlayerHalf) continue;
                double top = d.Y + d.HalfH;
                if (top <= fromY && top > best) best = top;
            }
        }
        if (!BossDoorsOpen())
        {
            foreach (SimBox d in BossDoors)
            {
                if (d.X - d.HalfW >= Px + PlayerHalf) continue;
                if (d.X + d.HalfW <= Px - PlayerHalf) continue;
                double top = d.Y + d.HalfH;
                if (top <= fromY && top > best) best = top;
            }
        }
        foreach (SimSwitchGate sg in SwitchGates)
        {
            if (!SwitchGateSolid(sg)) continue;
            if (sg.X - sg.HalfW >= Px + PlayerHalf) continue;
            if (sg.X + sg.HalfW <= Px - PlayerHalf) continue;
            double top = sg.Y + sg.HalfH;
            if (top <= fromY && top > best) best = top;
        }
        return best;
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
        if (!BossDoorsOpen())
        {
            foreach (SimBox d in BossDoors)
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
        foreach (SimSwitchGate sg in SwitchGates)
        {
            if (!SwitchGateSolid(sg)) continue;
            if (Overlaps(Px, probeY, PlayerHalf, PlayerHalf, sg)) return true;
        }
        return false;
    }

    private void ResolveAxis(bool xAxis)
    {
        foreach (SimBox s in Solids) ResolveAgainst(s, xAxis, null);

        // The platform a player is riding must not shove them HORIZONTALLY: while
        // standing on it there is always a sub-pixel vertical overlap, so an
        // x-axis resolve would nudge the player toward the platform's nearer edge
        // every tick and slide them off (the "drifts left off the lift" bug).
        // Vertical resolution still runs so the platform holds them up.
        for (int i = 0; i < Movers.Count; i++)
        {
            if (xAxis && i == GroundMover) continue;
            ResolveAgainst(Movers[i], xAxis, null);
        }
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
        if (!BossDoorsOpen())
        {
            foreach (SimBox d in BossDoors) ResolveAgainst(d, xAxis, null);
        }
        // The crusher a player is riding must not shove them (they'd be ejected
        // sideways out of its box); every other crusher still collides normally.
        for (int i = 0; i < Fallers.Count; i++)
        {
            if (i == RidingFaller) continue;
            ResolveAgainst(Fallers[i], xAxis, null);
        }
        foreach (SimCannon cn in Cannons) ResolveAgainst(cn, xAxis, null);
        foreach (SimSwitchGate sg in SwitchGates)
        {
            if (!SwitchGateSolid(sg)) continue;
            ResolveAgainst(sg, xAxis, null);
        }
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
        if (!BossDoorsOpen())
        {
            foreach (SimBox d in BossDoors)
            {
                if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, d)) return true;
            }
        }
        foreach (SimCannon cn in Cannons)
        {
            if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, cn)) return true;
        }
        foreach (SimSwitchGate sg in SwitchGates)
        {
            if (!SwitchGateSolid(sg)) continue;
            if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, sg)) return true;
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
            bool hitFromBelow = Py < b.Y; // moving up into a ceiling (normal gravity)
            if (hitFromBelow)
            {
                // CORNER CORRECTION: if the player only barely clips this ceiling's
                // corner, slide them sideways past the edge so the jump isn't
                // stopped dead. Compute how far they overlap each vertical edge.
                double pRight = Px + PlayerHalf;
                double bLeft = b.X - b.HalfW;
                double overlapL = pRight - bLeft;   // player's right past block's left edge
                double pLeft = Px - PlayerHalf;
                double bRight = b.X + b.HalfW;
                double overlapR = bRight - pLeft;   // block's right past player's left edge
                if (overlapL > 0.0 && overlapL <= CornerNudge && overlapR > overlapL)
                {
                    // Clipping the LEFT corner: nudge left, slip past, keep rising.
                    double nx = bLeft - PlayerHalf;
                    Px = nx;
                    return;
                }
                if (overlapR > 0.0 && overlapR <= CornerNudge && overlapL > overlapR)
                {
                    // Clipping the RIGHT corner: nudge right, slip past, keep rising.
                    double nx2 = bRight + PlayerHalf;
                    Px = nx2;
                    return;
                }
                Py = b.Y - lim;
            }
            else
            {
                Py = b.Y + lim;
            }
            Vy = 0.0;
            // If a launched/boosted player slams into a ceiling, release the
            // control lock so they immediately free-fall under their own
            // steering instead of drifting locked. (Direction-aware: "ceiling"
            // is the far side along gravity.)
            bool intoCeiling = GravityDir > 0.0 ? hitFromBelow : !hitFromBelow;
            if (intoCeiling && LockTicks > 0) LockTicks = 0;
        }
    }

    /// <summary>
    /// True when the player was standing on top of this crusher at the START of
    /// the tick (feet resting on its PRE-MOVE top edge, within its width). Tested
    /// against PrevY so a fast slam that drops away from the feet still counts as
    /// riding — the player is then glued to the new top. (Fallers move only in Y,
    /// so top = +Y.)
    /// </summary>
    private bool RidingFallerTop(SimFaller f)
    {
        double left = f.X - f.HalfW;
        double right = f.X + f.HalfW;
        if (Px + PlayerHalf <= left) return false;
        if (Px - PlayerHalf >= right) return false;
        // Not "riding" while jumping/rising AWAY from the top: a player moving up
        // (against gravity) is leaping off, not standing. Without this the glue
        // re-captures them ~0.35u after a jump and yanks them back down, so jumps
        // off a crusher barely leave the surface.
        double vUp = Vy * GravityDir; // >0 means moving against gravity (upward)
        if (vUp > 0.0) return false;
        double prevTop = f.PrevY + f.HalfH;
        double feet = Py - PlayerHalf;
        double gap = feet - prevTop;
        if (gap < 0.0) gap = -gap;
        return gap <= FallerRideTolerance;
    }

    /// <summary>Glue a riding player's feet exactly to the crusher's current top.</summary>
    private void StickToFallerTop(SimFaller f)
    {
        double top = f.Y + f.HalfH;
        Py = top + PlayerHalf;
        if (Vy < 0.0) Vy = 0.0; // cancel accumulated fall so gravity doesn't fight the ride
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
        // Dying returns collected keys to the stage: you must re-collect them,
        // so a death after grabbing a key can't leave doors permanently open
        // (which would let you skip the whole key challenge).
        if (KeysCollected > 0)
        {
            foreach (SimKey k in Keys) k.Collected = false;
            KeysCollected = 0;
        }
        // Dying also RESURRECTS every enemy you defeated — a death is a full retry,
        // so enemies return to their spawn state (same idea as keys returning).
        foreach (SimEnemy en in Enemies)
        {
            en.Dead = false;
            en.Hp = en.MaxHp;
            en.HitTick = -1000000;
            en.Facing = 1;
            en.X = en.BaseX;
            en.Y = en.BaseY;
            en.GroundY = en.BaseY;
            en.InAir = false;
            en.VyJump = 0.0;
            en.FireActive = false;
            en.FireStartTick = -1000000;
            en.WalkDir = 1;
        }
        // Waves also restart: re-anchor behind the (possibly checkpoint-moved)
        // respawn point and re-arm the delay, so you don't respawn straight into a
        // wave that kept sweeping while you were dead.
        foreach (SimWave wv in Waves)
        {
            wv.AnchorX = StartX + wv.OffsetX;
            wv.AnchorY = StartY + wv.OffsetY;
            wv.RestartTick = TickCount;
            wv.CurX = wv.AnchorX;
            wv.CurY = wv.AnchorY;
        }
        Events |= SimEvents.Respawned;

        // Each death costs a life; running out ends the run (game over), same
        // terminal state as a time-out but for a different reason.
        if (LivesLeft > 0) LivesLeft = LivesLeft - 1;
        if (LivesLeft <= 0)
        {
            LivesOut = true;
            TimedOutFlag = true;
            Events |= SimEvents.TimedOut;
        }
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
        // A rider standing on top is remembered here (pre-move) and re-glued to
        // the crusher's top AFTER the player's own physics runs (section 9c),
        // so a fast slam can't drop out from under them.
        RidingFaller = -1;
        for (int fi = 0; fi < Fallers.Count; fi++)
        {
            SimFaller f = Fallers[fi];
            f.PrevY = f.Y;
            bool wasOnTop = RidingFallerTop(f);
            if (wasOnTop) RidingFaller = fi;
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
                f.DeltaY = f.Y - f.PrevY;
                // A rider on top rides the slam down (glued later in 9c). A player
                // caught UNDER the descending crusher is not killed on mere touch:
                // the crusher pushes them down like a heavy ceiling. They are only
                // crushed if pinned against a surface below with less room than
                // their own body — i.e. actually squished.
                if (!wasOnTop && Overlaps(Px, Py, PlayerHalf, PlayerHalf, f))
                {
                    double fallerBottom = f.Y - f.HalfH;
                    // Shove the player's top down to the crusher's bottom.
                    double pushedPy = fallerBottom - PlayerHalf;
                    double surfaceTop = SurfaceTopBelow(fallerBottom);
                    double room = fallerBottom - surfaceTop; // vertical gap under the crusher
                    if (room < PlayerHalf + PlayerHalf)
                    {
                        // Not enough space for the player's body -> squished.
                        Respawn();
                    }
                    else if (pushedPy < Py)
                    {
                        // Room to be pushed: ride down with the crusher, don't die.
                        Py = pushedPy;
                        if (Vy > 0.0) Vy = 0.0;
                    }
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
            f.DeltaY = f.Y - f.PrevY;
            // (Riding is applied in section 9c, after player physics.)
        }

        // 1c. rotating hazards: advance each spike head around its orbit using
        // the baked unit-circle table (no trig). Kill check happens post-physics.
        foreach (SimRotor r in Rotors)
        {
            int phase = TickCount + r.PhaseTicks;
            double frac = phase / (double)r.PeriodTicks;
            double flr = Math.Floor(frac);
            double intoRev = frac - flr;          // 0..1 around the circle
            double stepF = intoRev * OrbitSteps;
            int idx = (int)stepF;
            if (idx >= OrbitSteps) idx = idx - OrbitSteps;
            if (r.SpinDir < 0.0)
            {
                idx = OrbitSteps - idx;
                if (idx >= OrbitSteps) idx = idx - OrbitSteps;
            }
            double ox = OrbitCos[idx] * r.Radius;
            double oy = OrbitSin[idx] * r.Radius;
            r.HeadX = r.X + ox;
            r.HeadY = r.Y + oy;
        }

        // 1c3. waves sweep steadily from their anchor once the delay has passed.
        // Anchor + restart tick reset on respawn so the wave re-chases from behind
        // the respawn point. Position is linear in elapsed ticks (byte-identical).
        foreach (SimWave wv in Waves)
        {
            int since = TickCount - wv.RestartTick;
            int elapsed = since - wv.DelayTicks;
            if (elapsed < 0) elapsed = 0;
            double dist = elapsed * wv.Speed;
            double ax = dist * wv.DirX;
            double ay = dist * wv.DirY;
            wv.CurX = wv.AnchorX + ax;
            wv.CurY = wv.AnchorY + ay;
        }

        // 1c2. enemies move per their Mode. Defeated enemies stop.
        foreach (SimEnemy en in Enemies)
        {
            if (en.Dead) continue;
            double prevX = en.X;
            if (en.Mode == 0)
            {
                // Classic glide: triangle wave across the span (like a mover).
                double ett = TickCount * Tick;
                double es = ett / en.Period;
                double efl = Math.Floor(es);
                double ef = es - efl;
                double ek;
                if (ef < 0.5) ek = ef * 2.0;
                else { double eu = 1.0 - ef; ek = eu * 2.0; }
                double eox = en.Dx * ek;
                en.X = en.BaseX + eox;
            }
            else if (en.Mode == 1)
            {
                // CHASER: step toward the player. If that way is blocked (ledge or
                // wall, e.g. the player is across a gap), PACE the other way instead
                // of freezing so it always looks alive; flip pace when both ends block.
                int want = Px >= en.X ? 1 : -1;
                double towardX = en.X + en.Speed * Tick * want;
                if (EnemyCanStand(en, towardX)) { en.X = towardX; en.WalkDir = want; }
                else
                {
                    double paceX = en.X + en.Speed * Tick * en.WalkDir;
                    if (EnemyCanStand(en, paceX)) en.X = paceX;
                    else en.WalkDir = -en.WalkDir;
                }
            }
            else if (en.Mode == 2)
            {
                // PATROL: walk straight; flip at a ledge or wall.
                double stepx = en.Speed * Tick * en.WalkDir;
                double nx = en.X + stepx;
                if (EnemyCanStand(en, nx)) en.X = nx;
                else en.WalkDir = -en.WalkDir; // turn around
            }
            else // Mode 3: JUMPER — hops toward the player (a lively hopping chaser)
            {
                int want = Px >= en.X ? 1 : -1;
                en.WalkDir = want;
                double stepx = en.Speed * 0.6 * Tick * want;
                double nx = en.X + stepx;
                if (EnemyCanStand(en, nx)) en.X = nx;
            }
            double move = en.X - prevX;
            if (move > 0.0) en.Facing = 1;
            else if (move < 0.0) en.Facing = -1;

            // JUMPER vertical hop (non-boss). Uses the same jump integrator as the
            // boss but with enemy tuning and no fire.
            if (!en.IsBoss && en.Mode == 3)
            {
                int jp = TickCount % EnemyJumpPeriodTicks;
                if (jp == 0 && !en.InAir)
                {
                    en.InAir = true;
                    en.VyJump = EnemyJumpSpeed;
                }
                if (en.InAir)
                {
                    double dvj = Gravity * Tick;
                    en.VyJump = en.VyJump - dvj;
                    double dyj = en.VyJump * Tick;
                    en.Y = en.Y + dyj;
                    if (en.Y <= en.GroundY)
                    {
                        en.Y = en.GroundY;
                        en.InAir = false;
                        en.VyJump = 0.0;
                    }
                }
            }

            if (en.IsBoss)
            {
                // Boss HOP: launch on a fixed cadence, fall under gravity, land
                // back on the patrol floor. Adds vertical menace to the fight.
                int jphase = TickCount % BossJumpPeriodTicks;
                if (jphase == 0 && !en.InAir)
                {
                    en.InAir = true;
                    en.VyJump = BossJumpSpeed;
                }
                if (en.InAir)
                {
                    double dvj = Gravity * Tick;
                    en.VyJump = en.VyJump - dvj;
                    double dyj = en.VyJump * Tick;
                    en.Y = en.Y + dyj;
                    if (en.Y <= en.GroundY)
                    {
                        en.Y = en.GroundY;
                        en.InAir = false;
                        en.VyJump = 0.0;
                    }
                }
                else
                {
                    en.Y = en.GroundY;
                }

                // Boss FIRE: breathe a fireball in the facing direction on a fixed
                // cadence. When it fires, snapshot the MOUTH position (the boss's
                // live x plus its facing edge) and fly the fireball out from THERE
                // — not from the patrol anchor, which drifts as the boss moves.
                // The fireball flies until it hits a solid wall (same rule as
                // cannon bullets), with a far safety cap so an open-air shot
                // still expires.
                if (en.FireActive)
                {
                    int flived = TickCount - en.FireStartTick;
                    double travel = flived * BossFireSpeed * Tick;
                    double signed = en.FireDir * travel;
                    en.FireX = en.FireLaunchX + signed;
                    en.FireY = en.FireLaunchY;
                    if (travel >= BossFireMaxDist)
                    {
                        en.FireActive = false;
                    }
                    else
                    {
                        foreach (SimBox s in Solids)
                        {
                            if (Overlaps(en.FireX, en.FireY, FireballHalf, FireballHalf, s))
                            {
                                en.FireActive = false;
                                break;
                            }
                        }
                    }
                }
                if (!en.FireActive)
                {
                    int fphase = TickCount % BossFirePeriodTicks;
                    if (fphase == 0)
                    {
                        en.FireActive = true;
                        en.FireStartTick = TickCount;
                        // Always breathe fire TOWARD the player (aim at whichever
                        // side they're on), and turn to face them so the mouth
                        // lines up with the shot.
                        en.FireDir = Px >= en.X ? 1.0 : -1.0;
                        en.Facing = en.FireDir >= 0.0 ? 1 : -1;
                        en.FireLaunchX = en.X + en.FireDir * (en.HalfW + FireballHalf);
                        en.FireLaunchY = en.Y;
                        en.FireX = en.FireLaunchX;
                        en.FireY = en.FireLaunchY;
                    }
                }
            }
            else if (en.Mode != 3)
            {
                // Non-boss, non-jumper: pinned to its ground line.
                en.Y = en.BaseY;
            }
        }

        // 1d. switch gates: any switch of a group held down pushes its gates
        // toward OPEN; released groups ease shut slowly (grace window). Uses the
        // PREVIOUS tick's player position — the switch overlap is evaluated here.
        foreach (SimSwitchGate sg in SwitchGates)
        {
            bool pressed = false;
            foreach (SimSwitch sw in Switches)
            {
                if (sw.GateId != sg.GateId) continue;
                if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, sw)) { pressed = true; break; }
            }
            if (pressed)
            {
                int add = SwitchGateCloseTicks / SwitchGateOpenTicks;
                sg.OpenTicks = sg.OpenTicks + add;
                if (sg.OpenTicks > SwitchGateCloseTicks) sg.OpenTicks = SwitchGateCloseTicks;
            }
            else if (sg.OpenTicks > 0)
            {
                sg.OpenTicks = sg.OpenTicks - 1;
            }

            // A CLOSING (descending) gate that catches the player against the
            // floor crushes them — same rule as a faller: if the space between
            // the gate's descended bottom and the surface below is smaller than
            // the player's body, they are squished. Otherwise the bottom shoves
            // them down.
            if (SwitchGateSolid(sg) &&
                Overlaps(Px, Py, PlayerHalf, PlayerHalf, sg))
            {
                double gateBottom = sg.Y - sg.HalfH;
                double pushedPy = gateBottom - PlayerHalf;
                double floor = SurfaceTopBelow(gateBottom);
                double room = gateBottom - floor;
                if (room < PlayerHalf + PlayerHalf)
                {
                    Respawn();
                }
                else if (pushedPy < Py)
                {
                    Py = pushedPy;
                    if (Vy > 0.0) Vy = 0.0;
                }
            }
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
            // Releasing from a crusher you were riding: cancel the ride so 9c
            // does not re-glue your feet to its top and swallow the jump.
            RidingFaller = -1;
            JumpPressedTick = -1000000;
            LastGroundedTick = -1000000;
            Events |= SimEvents.Jumped;
        }

        // 8. gravity
        double dv = Gravity * Tick;
        double dvg = dv * GravityDir;
        Vy = Vy - dvg;

        // 8b. fans: while the player is inside a wind zone, ease their velocity
        // toward the fan's target velocity along its direction. An upward fan
        // can hold the player aloft against gravity (updraft/hover).
        foreach (SimFan fan in Fans)
        {
            if (!Overlaps(Px, Py, PlayerHalf, PlayerHalf, fan)) continue;
            double tvx = fan.DirX * fan.Power;
            double tvy = fan.DirY * fan.Power;
            if (fan.DirX != 0.0)
            {
                double relx = tvx - Vx;
                double addx = relx * 0.25;
                Vx = Vx + addx;
            }
            if (fan.DirY != 0.0)
            {
                double rely = tvy - Vy;
                double addy = rely * 0.25;
                Vy = Vy + addy;
            }
        }

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

        // 9c. faller ride: if the player was standing on a crusher at the start
        // of the tick, glue their feet to its (post-move) top now — AFTER their
        // own gravity/collision — so even a fast slam can't drop out from under
        // them. This runs last so it has final say on the rider's Y.
        if (RidingFaller >= 0)
        {
            SimFaller rf = Fallers[RidingFaller];
            StickToFallerTop(rf);
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
                // Snap the player's feet to the pad surface first, so the bounce
                // launches from true contact instead of mid-overlap. Under
                // normal gravity that's the pad's top; when flipped, its bottom.
                if (GravityDir > 0.0)
                {
                    double padTop = tr.Y + tr.HalfH;
                    Py = padTop + PlayerHalf;
                }
                else
                {
                    double padBot = tr.Y - tr.HalfH;
                    Py = padBot - PlayerHalf;
                }
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
            else if (tr.Kind == "gravset")
            {
                // Sets gravity to this block's FIXED direction (dir<0 = up,
                // else down). Idempotent: touching it again does nothing, so
                // the arrow it shows always matches the resulting gravity.
                double want = tr.DirX < 0.0 ? -1.0 : 1.0;
                if (GravityDir != want)
                {
                    GravityDir = want;
                    Events |= SimEvents.Flipped;
                }
            }
            else if (tr.Kind == "checkpoint")
            {
                // Reaching a checkpoint moves the respawn point here. Snap the
                // saved spot to the checkpoint's top so a death drops you onto
                // it, not inside it. Only ever moves forward (edge-fire once).
                double cpTop = tr.Y + tr.HalfH;
                StartX = tr.X;
                StartY = cpTop + PlayerHalf;
                Events |= SimEvents.KeyPickup; // reuse the pickup chime
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
                double perTick = c.Speed * Tick;
                double halfDir = c.Dir * c.HalfW;
                double muzzle = c.X + halfDir;
                double by = c.Y;

                // A bullet must be allowed to fly the WHOLE way to its wall
                // before the next one fires — otherwise it would vanish at the
                // period boundary (the old "stops at a fixed distance" bug).
                // So the effective fire cadence is at least the flight time.
                int ticksToWall = (int)(c.WallDist / perTick) + 1;
                int cycle = c.PeriodTicks;
                if (ticksToWall > cycle) cycle = ticksToWall;

                int phase = TickCount + c.PhaseTicks;
                int intoCycle = phase % cycle;
                double traveled = intoCycle * perTick;

                // Once it has traveled to the wall it has hit and is gone until
                // the next fire cycle.
                bool reachedWall = traveled >= c.WallDist;
                double signedTravel = c.Dir * traveled;
                double bx = muzzle + signedTravel;
                c.BulletActive = !reachedWall;
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

        // 10d. rotating hazards: die on contact with the orbiting spike head.
        if (!respawned)
        {
            foreach (SimRotor r in Rotors)
            {
                var head = new SimBox { X = r.HeadX, Y = r.HeadY, HalfW = r.HeadHalf, HalfH = r.HeadHalf };
                if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, head))
                {
                    Respawn();
                    respawned = true;
                    break;
                }
            }
        }

        // 10d2. waves: touching the sweeping wall kills you. Its live box is at
        // (CurX,CurY) with the wave's half-extents.
        if (!respawned)
        {
            foreach (SimWave wv in Waves)
            {
                var box = new SimBox { X = wv.CurX, Y = wv.CurY, HalfW = wv.HalfW, HalfH = wv.HalfH };
                if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, box))
                {
                    Respawn();
                    respawned = true;
                    break;
                }
            }
        }

        // 10e. teleporters: entering EITHER endpoint warps you to its partner,
        // preserving velocity. Two-way — you can go back the way you came.
        // Edge-based, and the destination is marked overlapping so you don't
        // instantly bounce back.
        if (!respawned)
        {
            foreach (SimTeleporter tp in Teleporters)
            {
                bool overlap = Overlaps(Px, Py, PlayerHalf, PlayerHalf, tp);
                bool fire = overlap && !tp.WasOverlapping;
                tp.WasOverlapping = overlap;
                if (!fire) continue;
                if (tp.ExitIndex < 0) continue;
                SimTeleporter exit = Teleporters[tp.ExitIndex];
                Px = exit.X;
                Py = exit.Y;
                // Mark the exit as currently-overlapping so it doesn't instantly
                // re-fire (which for a two-way pair would bounce you back).
                exit.WasOverlapping = true;
                Events |= SimEvents.Flipped; // reuse the warp/whoosh chime
                break;
            }
        }

        // 10f. enemies: STOMP from above (player descending, feet over the enemy's
        // upper half) damages the enemy and bounces the player; any other contact
        // kills the PLAYER. Defeated enemies are removed and, if they were bosses,
        // may open the boss doors.
        if (!respawned)
        {
            foreach (SimEnemy en in Enemies)
            {
                if (en.Dead) continue;
                if (!Overlaps(Px, Py, PlayerHalf, PlayerHalf, en)) continue;
                // Just bounced off this enemy? The player is still overlapping it
                // for a tick or two while rising away — don't read that as a
                // side-hit kill.
                int sinceHit = TickCount - en.HitTick;
                if (sinceHit >= 0 && sinceHit < EnemyStompGraceTicks) continue;

                // "From above" is relative to gravity: descending onto the enemy's
                // top. feet = the player's lower edge along gravity.
                double feet = Py - PlayerHalf * GravityDir;
                double descending = Vy * GravityDir; // <0 means moving toward the enemy from above
                double overTop = (feet - en.Y) * GravityDir; // >0 when feet are on the enemy's top side
                // Favour the STOMP two ways: (1) the player is falling onto the
                // enemy's upper half (a normal stomp), OR (2) the player's feet are
                // clearly ABOVE the enemy's centre — they're on top of it, whatever
                // their own velocity. Case (2) covers a boss that JUMPS UP into a
                // player standing/landing on its head: contact from above is always
                // a stomp, never a cheap hit.
                bool stompFalling = descending < 0.0 && overTop > -EnemyStompMargin;
                bool onTop = overTop > EnemyStompMargin;
                bool stomp = stompFalling || onTop;
                // A jumping enemy that is RISING up and away, overhead of the
                // player, is leaving — don't let its underside deal a cheap graze.
                // But if the player is genuinely UNDER a boss (it's descending onto
                // them, or they ran into its underside), that MUST still hit. So
                // only pass through when the enemy is BOTH mostly overhead AND
                // currently moving up (its jump is ascending).
                bool enemyAbovePlayer = (en.Y - Py) * GravityDir > 0.0;
                bool enemyRising = en.InAir && (en.VyJump * GravityDir) > 0.0;
                bool passOverhead = enemyAbovePlayer && enemyRising;

                if (stomp)
                {
                    en.Hp = en.Hp - 1;
                    en.HitTick = TickCount;
                    // Mario-style: holding/pressing JUMP as you land the stomp gives
                    // a much higher bounce; a plain stomp gives the small pop.
                    int stompSincePress = TickCount - JumpPressedTick;
                    bool jumpHeld = stompSincePress <= BufferTicks;
                    double bounce = jumpHeld ? EnemyStompJumpBounce : EnemyStompBounce;
                    Vy = bounce * GravityDir; // pop back up
                    if (jumpHeld)
                    {
                        // consume the buffered press so it doesn't also fire a normal jump
                        JumpPressedTick = -1000000;
                        Events |= SimEvents.Jumped;
                    }
                    Events |= SimEvents.Stomped;
                    if (en.Hp <= 0)
                    {
                        en.Dead = true;
                        Events |= SimEvents.EnemyDown;
                    }
                }
                else if (passOverhead)
                {
                    // enemy is jumping up and away overhead — a graze, not a hit
                }
                else
                {
                    Respawn();
                    respawned = true;
                    break;
                }
            }
        }

        // 10g. boss fireballs: touching a live fireball kills the player.
        if (!respawned)
        {
            foreach (SimEnemy en in Enemies)
            {
                if (en.Dead || !en.IsBoss || !en.FireActive) continue;
                var fb = new SimBox { X = en.FireX, Y = en.FireY, HalfW = FireballHalf, HalfH = FireballHalf };
                if (Overlaps(Px, Py, PlayerHalf, PlayerHalf, fb))
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
        // Lives are FIXED at 5 for every stage — a deliberate whole-game rule, so
        // a stage's own "lives" field (whatever a creator set, old or new) is
        // ignored. Keeps difficulty consistent across the catalog.
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
                case "gravitySet": world.AddTrigger("gravset", p.x, p.y, p.w, p.h, 0, p.dirX); break;
                case "checkpoint": world.AddTrigger("checkpoint", p.x, p.y, p.w, p.h, 0, 0); break;
                case "rotatingHazard": world.AddRotor(p.x, p.y, p.w, p.h, p.power, p.dirX, p.dx); break;
                case "wave": world.AddWave(p.x, p.y, p.w, p.h, p.power, p.dirX, p.dy, p.period); break;
                case "teleporter": world.AddTeleporter(p.x, p.y, p.w, p.h, p.dirX, p.period); break;
                case "fan": world.AddFan(p.x, p.y, p.w, p.h, p.dirX, p.power, p.dy); break;
                case "switch": world.AddSwitch(p.x, p.y, p.w, p.h, p.period); break;
                case "switchGate": world.AddSwitchGate(p.x, p.y, p.w, p.h, p.period); break;
                case "enemy": world.AddEnemy(p.x, p.y, p.w, p.h, p.dx, p.period, p.power, p.dy, p.dirX); break;
                case "bossDoor": world.AddBossDoor(p.x, p.y, p.w, p.h); break;
            }
        }
        world.AddTrigger("goal", data.goal.x, data.goal.y, data.goal.w, data.goal.h, 0, 0);
        world.ResolveFallerLandings();
        world.ResolveCannonRanges();
        world.ResolveTeleporterPairs();
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
        foreach (SimRotor r in Rotors) Consider(r);
        foreach (SimTeleporter tp in Teleporters) Consider(tp);
        foreach (SimFan fn in Fans) Consider(fn);
        foreach (SimSwitch sw in Switches) Consider(sw);
        foreach (SimSwitchGate sg in SwitchGates) Consider(sg);
        foreach (SimEnemy en in Enemies) Consider(en);
        foreach (SimBox bd in BossDoors) Consider(bd);
        KillBottom = minY - KillMarginBelow;
        KillTop = maxY + KillMarginAbove;
    }

    // Pair each teleporter entry with the FIRST exit sharing its PairId (and
    // vice-versa for a two-way pair). Unpaired endpoints simply never fire.
    public void ResolveTeleporterPairs()
    {
        for (int i = 0; i < Teleporters.Count; i++)
        {
            SimTeleporter a = Teleporters[i];
            a.ExitIndex = -1;
            for (int j = 0; j < Teleporters.Count; j++)
            {
                if (j == i) continue;
                SimTeleporter b = Teleporters[j];
                if (b.PairId != a.PairId) continue; // partner = the other endpoint with the same pair id
                a.ExitIndex = j;
                break;
            }
        }
    }
}
