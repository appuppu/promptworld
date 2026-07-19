// Prompt World TAC — deterministic simulation, C# port of server/tacsim.js.
// PURE C# (no UnityEngine): compiled by both the Unity client and the dotnet
// crosscheck CLI. Every method mirrors tacsim.js statement-for-statement; any
// JS change must be mirrored here and scripts/tac-crosscheck.sh must print
// BIT-IDENTICAL before a mobile build ships. See TacMath.cs for the kernel.
using System;
using System.Collections.Generic;

public static class TAC
{
    public const double TICK = 0.02;
    public const int TURN = 65536;
    public const int HALF_TURN = 32768;
    public const int QUARTER_TURN = 16384;

    public const double PLAYER_R = 0.4;
    public const double PLAYER_H = 1.7;
    public const double EYE_H = 1.5;
    public const double CHEST_H = 1.1;
    public const double WALK_SPEED = 1.9;
    public const double RUN_SPEED = 4.6;
    public const double MOVE_RAMP_TICKS = 12.0;
    public const double MOVE_RAMP_MIN = 0.3;
    public const double GRAVITY = 20.0;
    public const double JUMP_V = 7.0;
    public const double STEP_UP = 0.35;
    public const int PITCH_MAX = 14563;
    public const int PITCH_MIN = -14563;

    public const int FIRE_CD = 9;
    public const double LOCK_COS = 0.906;
    public const double LOCK_RANGE = 24.0;
    public const double BULLET_SPEED = 55.0;
    public const int BULLET_TTL = 90;
    public const double ENEMY_BULLET_SPEED = 22.0;
    public const int ENEMY_BULLET_TTL = 200;

    public const double NOISE_RUN_R = 9.0;
    public const int NOISE_RUN_EVERY = 12;
    public const double NOISE_LAND_R = 7.0;
    public const double NOISE_SHOT_R = 28.0;
    public const double NOISE_BLAST_R = 45.0;

    public const double VISION_RANGE = 20.0;
    public const double VISION_COS2 = 0.587;
    public const double SNIPER_RANGE = 60.0;
    public const double SNIPER_COS2 = 0.75;
    public const double GAUGE_MAX = 100.0;
    public const double GAUGE_DECAY = 1.5;
    public const double SUSPICIOUS_AT = 45.0;

    public const int SOLDIER_HP = 2;
    public const double SOLDIER_PATROL_SPEED = 1.2;
    public const double SOLDIER_INVESTIGATE_SPEED = 1.6;
    public const double SOLDIER_CHASE_SPEED = 2.4;
    public const double SOLDIER_R = 0.4;
    public const double SOLDIER_H = 1.8;
    public const int RIFLE_CD = 60;
    public const int RIFLE_AIM = 20;
    public const int RIFLE_SUPPRESS_CD = 90;
    public const int RIFLE_REACT = 35;
    public const double RIFLE_RANGE = 22.0;
    public const int ENEMY_TURN_RATE = 500;

    public const int GATLING_HP = 4;
    public const double GATLING_R = 0.6;
    public const double GATLING_H = 1.6;
    public const int GATLING_SPRAY = 100;
    public const int GATLING_RELOAD = 50;
    public const int GATLING_SHOT_EVERY = 5;
    public const int GATLING_SWEEP = 2730;
    public const int GATLING_ALERT_TURN = 220;
    public const double GATLING_VISION = 32.0;

    public const int SNIPER_HP = 2;
    public const double SNIPER_R = 0.4;
    public const double SNIPER_H = 2.0;
    public const double SNIPER_BULLET_SPEED = 90.0;
    public const int SNIPER_WARN = 75;
    public const int SHIELD_HP = 4;
    public const double SHIELD_R = 0.5;
    public const double SHIELD_H = 1.9;
    public const double SHIELD_BLOCK_R = 1.0;
    public const int SHIELD_CYCLE = 300;
    public const int SHIELD_OPEN = 100;
    public const int SHIELD_STAGGER = 250;
    public const int SHIELD_BLAST_DMG = 2;
    public const int SHIELD_TURN = 90;
    public const int SNIPER_SWEEP = 8192;
    public const int SNIPER_SCAN_TURN = 55;
    public const int SNIPER_TRACK_TURN = 220;
    public const int SNIPER_COOLDOWN = 110;
    public const int SNIPER_WARN_DECAY = 5;

    public const double BARREL_R = 0.5;
    public const double BARREL_H = 1.0;
    public const double BARREL_ROLL = 5.0;
    public const int BARREL_FUSE = 150;
    public const int BARREL_CHAIN_FUSE = 15;
    public const double BLAST_R = 2.6;
    public const int BLAST_DMG = 10;
    public const int BLAST_PLAYER_DMG = 2;

    public const double MINE_R = 0.35;
    public const double MINE_TRIGGER_R = 1.1;
    public const int MINE_FUSE = 25;
    public const int MINE_SHOT_FUSE = 8;
    public const double MINE_BLAST_R = 2.0;

    public const int DRONE_HP = 1;
    public const double DRONE_R = 0.5;
    public const double DRONE_H = 0.5;
    public const double DRONE_FLY_Y = 3.2;
    public const double DRONE_PATROL_SPEED = 2.2;
    public const double DRONE_CHASE_SPEED = 3.4;
    public const double DRONE_DIVE_SPEED = 4.5;
    public const double DRONE_DIVE_AT = 2.4;
    public const double DRONE_BOOM_AT = 1.1;
    public const double DRONE_BLAST_R = 1.6;
    public const double DRONE_CRASH_SPEED = 3.5;

    public const int OPERATOR_HP = 1;
    public const double OPERATOR_R = 0.4;
    public const double OPERATOR_H = 1.7;
    public const double OPERATOR_FLEE_SPEED = 3.0;

    public const double PILOT_SPEED = 5.0;
    public const double PILOT_ALT = 6.0;
    public const double PILOT_LOCK_R = 9.0;
    public const double PILOT_DIVE_SPEED = 8.0;
    public const int PILOT_BATTERY = 1500;
    public const double PILOT_BLAST_R = 2.6;

    public const int BOMBER_HP = 2;
    public const double BOMBER_R = 0.42;
    public const double BOMBER_H = 1.75;
    public const double BOMBER_RANGE = 18.0;
    public const double BOMBER_MIN = 5.0;
    public const int BOMBER_CD = 600;
    public const int BOMB_FLIGHT = 90;
    public const int BOMB_FUSE = 250;
    public const double BOMB_BLAST_R = 3.0;
    public const int CRACKED_HP = 40;
    public const int GRENADE_CD = 400;
    public const double GRENADE_SPEED_H = 12.0;
    public const double GRENADE_SPEED_V = 6.0;
    public const double GRENADE_BLAST_R = 2.4;
    public const double GRENADE_R = 0.15;

    public const int INVESTIGATE_PAUSE = 70;
    public const int GROUP_MAX = 9;
    public const double CELL = 4.0;

    public const double RIVER_MUL = 0.45;
    public const double TRENCH_DEPTH = 0.9;
    public const double RIVER_DEPTH = 0.6;
    public const double CROUCH_H = 1.0;
    public const double CROUCH_CHEST = 0.55;
    public const double TRENCH_STANDOFF = 12.0;
    public const double TRENCH_PROBE = 8.0;
    public const double SLIDE_SPEED = 6.0;
    public const double SLIDE_LEN = 22.0;
    public const double SLIDE_CRUSH_R = 1.3;
    public const int SCOPE_MAX = 5;
    public const int SCOPE_CD = 300;
    public const double SCOPE_RANGE = 90.0;
    public const int SCOPE_DMG = 3;
    public const int SCOPE_YAW_RATE = 175;
    public const int SCOPE_PITCH_RATE = 100;
    public const double SCOPE_RAMP_TICKS = 32.0;
    // The rendered humanoid head sits at ~1.1×h — above the body hit cylinder,
    // which only reaches h. Aiming the crosshair at the visible head therefore
    // used to sail over an h-tall cylinder and miss. The scoped shot (the precise
    // weapon) extends its hit cylinder by this fraction of the enemy height so
    // the head is covered. Scoped-shot only; bullet/grenade/AI collision unchanged.
    public const double SCOPE_HEAD_FRAC = 0.18;
    public const double JAMMER_R = 12.0;
    public const double SQUAD_RADIO_R = 30.0;
    public const double SQUAD_FAN_R = 6.0;
    public const double SQUAD_FLANK_R = 9.0;
    public const double LAMP_R = 6.0;
    public const double SEARCH_R = 18.0;
    public const int SEARCH_HALF = 1300;
}

public class TacInput { public int b, m, yawQ, pitchQ; }

public class TacBox { public double x0, z0, x1, z1, yb, h; public int kind, hp; public bool alive; public string tint; }
public class TacSlopePart { public double x0, z0, x1, z1, h, ux, uz, rise; public int dir, steps; public string tint; }
public class TacRect { public double x0, z0, x1, z1; }
public class TacPit { public double x0, z0, x1, z1, depth; }
public class TacBarrel { public double x, z, y, dx, dz; public int state, fuse; public bool alive; }
public class TacMine { public double x, z, y; public int fuse; public bool alive; }
public class TacMedkit { public double x, z, y; public bool alive; }
public class TacSwitch { public double x, z, y, r; public bool alive; }
public class TacBoulder { public double x, y, z, traveled; public bool alive; }
public class TacSlide
{
    public double pileX, pileZ, w, d, dx, dz, postX, postZ, postY;
    public bool triggered;
    public List<TacBoulder> boulders = new List<TacBoulder>();
}
public class TacEnemy
{
    public int type;
    public double x, z, y, homeY, r, h, gauge, tx, tz, homeX, homeZ, patX, patZ;
    public int yawQ, baseYawQ, hp, group, state, pauseT, attackCd, bombCd, rifleCd, aimT, suppressT, fireFlash, cycleT, warnT, shieldStagT;
    public bool alive, hasPatrol, patToB, crouched, holdTrench, seesPlayer, diving, crashing, entrench, seekCover, dugIn, corpseSpotted;
    public int idx;
}
public class TacBullet { public double x, y, z, sx, sy, sz, vx, vy, vz; public int ttl; public bool fromPlayer, alive, gat; }
public class TacGrenade { public double x, y, z, vx, vy, vz; public bool alive; }
public class TacBomb { public double sx, sy, sz, x, y, z; public int t, fuse, state; }
public class TacPilot { public double x, y, z; public int battery, dive, yawQ; }
public class TacNoise { public double x, z, r; }
public class TacIntel { public double x, z, y; public bool alive; }
public class TacLamp { public double x, z, r; }
public class TacLight { public double x, z, r; public int angQ, speed; }

public class TacKill { public double x, y, z; public int type; }
public class TacExplosion { public double x, y, z, r; }
public class TacWallBreak { public int i; public double x, y, z; }
public class TacXYZ { public double x, y, z; }
public class TacXZ { public double x, z; public bool gat; }

public class TacEvents
{
    public bool cleared, timedOut, jumped, landed, shot, heard, spotted, groupAlert;
    public bool playerHit, playerDead, enemyHit, rifleShot, gatlingShot, sniperAim, sniperShot;
    public bool mineArmed, mineHit, barrelHit, switchDown, slideStart, bulletWall, medkit;
    public bool scopeOn, scopeOff, scopeShot, grenadeThrow;
    public bool radio, corpseFound;
    public bool shieldBlock;
    public bool droneLaunch, droneDive, droneDetonate, droneDead, droneGranted, droneCrash;
    public int crushed;
    public List<TacKill> kills;
    public List<TacXYZ> hits;
    public List<TacXZ> eshots;
    public List<TacExplosion> explosions;
    public List<TacNoise> noises;
    public List<TacWallBreak> wallBreaks;
    public TacXYZ jamZap;
    public TacXZ bomberThrow, bombLand;
    public TacIntelPick intelPick;
    public bool intelAll;
}
public class TacIntelPick { public int left; }

public class TacWorld
{
    public double arenaW, arenaD, timeLimit;
    public int maxTicks, tick;
    public double px, pz, py, vy;
    public int yawQ, faceQ, pitchQ;
    public bool onGround;
    public int moveT, fireCd, hp, maxHp, ammo, hurtCd, lockTarget, lockKind, droneUses;
    public TacPilot pilot;
    public int scopeSteerT, grenadeCd, prevB;
    public List<TacGrenade> grenades = new List<TacGrenade>();
    public bool dead, clearedFlag, timedOutFlag, sneaking;

    public List<TacBox> boxes = new List<TacBox>();
    public List<TacSlopePart> slopes = new List<TacSlopePart>();
    public List<TacBarrel> barrels = new List<TacBarrel>();
    public List<TacMine> mines = new List<TacMine>();
    public List<TacMedkit> medkits = new List<TacMedkit>();
    public List<TacRect> rivers = new List<TacRect>();
    public List<TacRect> trenches = new List<TacRect>();
    public List<TacSlide> slides = new List<TacSlide>();
    public List<TacSwitch> switches = new List<TacSwitch>();
    public int scopeCd, scopeShots, fireFlash;
    public bool crouched, scoped, playerJammed;
    public int aimYawQ, aimPitchQ;
    public List<TacEnemy> enemies = new List<TacEnemy>();
    public List<TacBullet> bullets = new List<TacBullet>();
    public List<TacBomb> bombs = new List<TacBomb>();
    public List<TacNoise> noises = new List<TacNoise>();
    public TacEvents events = new TacEvents();
    public int enemiesLeft, shotsFired;
    public int goalType, intelLeft;
    public List<TacIntel> intels = new List<TacIntel>();
    public TacRect exitZone;
    public bool night, playerLit;
    public bool squad;
    public List<TacLamp> lamps = new List<TacLamp>();
    public List<TacLight> lights = new List<TacLight>();
    public Dictionary<int, List<int>> opGroups = new Dictionary<int, List<int>>();
    public List<TacPit> pits = new List<TacPit>();
    public List<TacPit> pitDefs = new List<TacPit>();
    public TacJson.JObj palette; // render-only theme colors {ground, sky, water}

    int gridW, gridD;
    List<int>[] grid;
    int _stamp;
    int[] _stamps;

    static double Q(double v) { return TacMath.Q(v); }

    public TacWorld(TacJson.JObj stage)
    {
        var w = this;
        var arena = stage.Obj("arena");
        w.arenaW = Q(arena.Num("w"));
        w.arenaD = Q(arena.Num("d"));
        w.timeLimit = Q(stage.Num("timeLimit", 600.0));
        w.maxTicks = (int)Math.Floor(w.timeLimit / TAC.TICK);
        w.tick = 0;

        var ps = stage.Obj("playerStart");
        w.px = Q(ps.Num("x"));
        w.pz = Q(ps.Num("z"));
        w.py = 0.0;
        w.vy = 0.0;
        double yawDeg = Q(ps.Num("yaw", 0.0));
        w.yawQ = (int)Math.Floor(yawDeg * 182.04444444444445) & 65535;
        w.faceQ = w.yawQ;
        w.pitchQ = 0;
        w.onGround = true;
        w.moveT = 0;
        w.fireCd = 0;
        double lives = stage.Num("lives", 0.0);
        w.hp = lives != 0.0 ? (int)Math.Floor(lives) : 1;
        w.maxHp = w.hp;
        double ammoIn = stage.Num("ammo", 0.0);
        w.ammo = ammoIn == 0.0 ? -1 : (int)Math.Floor(ammoIn);
        w.hurtCd = 0;
        w.lockTarget = -1;
        w.lockKind = 0;
        w.droneUses = 0;
        w.pilot = null;
        w.scopeSteerT = 0;
        w.grenadeCd = 0;
        w.prevB = 0;
        w.dead = false;
        w.clearedFlag = false;
        w.timedOutFlag = false;
        w.scopeCd = 0;
        w.scopeShots = TAC.SCOPE_MAX;
        w.crouched = false;
        w.fireFlash = 0;
        w.scoped = false;
        w.aimYawQ = 0;
        w.aimPitchQ = 0;
        w.playerJammed = false;
        w.enemiesLeft = 0;
        w.shotsFired = 0;
        w.goalType = stage.Has("goal") && stage.Str("goal") == "extract" ? 1 : 0;
        w.intelLeft = 0;
        w.night = stage.Has("night") && stage.Num("night") != 0.0;
        w.palette = stage.Has("palette") ? stage.Obj("palette") : null;
        w.squad = stage.Has("squad") && stage.Num("squad") != 0.0;
        w.playerLit = false;

        var parts = stage.Arr("parts");
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts.Obj(i);
            string ty = p.Str("type");
            if (ty == "rock") w.AddBox(p.Num("x"), p.Num("z"), p.Num("w"), p.Num("d"), p.Has("h") ? p.Num("h") : 1.4, 0);
            else if (ty == "wall") w.AddBox(p.Num("x"), p.Num("z"), p.Num("w"), p.Num("d"), p.Has("h") ? p.Num("h") : 3.0, 1);
            else if (ty == "platform") w.AddBox(p.Num("x"), p.Num("z"), p.Num("w"), p.Num("d"), p.Has("h") ? p.Num("h") : 2.0, 2);
            else if (ty == "crackedWall") w.AddBox(p.Num("x"), p.Num("z"), p.Num("w"), p.Num("d"), p.Has("h") ? p.Num("h") : 3.0, 3);
            else if (ty == "slope") { w.AddSlope(p.Num("x"), p.Num("z"), p.Num("w"), p.Num("d"), p.Has("h") ? p.Num("h") : 2.0, (int)p.Num("dir", 0.0)); if (p.Has("tint")) w.slopes[w.slopes.Count - 1].tint = p.Str("tint"); }
            else if (ty == "barrel") w.AddBarrel(p.Num("x"), p.Num("z"));
            else if (ty == "mine") w.mines.Add(new TacMine { x = Q(p.Num("x")), z = Q(p.Num("z")), y = 0.0, fuse = -1, alive = true });
            else if (ty == "medkit") w.medkits.Add(new TacMedkit { x = Q(p.Num("x")), z = Q(p.Num("z")), y = 0.0, alive = true });
            else if (ty == "block")
            {
                // freeform cuboid: any size, any height, optionally FLOATING (y0)
                w.AddBox(p.Num("x"), p.Num("z"), p.Num("w"), p.Num("d"), p.Has("h") ? p.Num("h") : 1.0, 4, p.Has("y0") ? p.Num("y0") : 0.0);
                if (p.Has("tint")) w.boxes[w.boxes.Count - 1].tint = p.Str("tint");
            }
            else if (ty == "pit")
            {
                double phw = Q(p.Num("w")) / 2.0;
                double phd = Q(p.Num("d")) / 2.0;
                double pcx = Q(p.Num("x"));
                double pcz = Q(p.Num("z"));
                w.pitDefs.Add(new TacPit { x0 = pcx - phw, z0 = pcz - phd, x1 = pcx + phw, z1 = pcz + phd,
                    depth = Q(p.Has("depth") ? p.Num("depth") : 1.5) });
            }
            else if (ty == "river" || ty == "trench")
            {
                double fhw = Q(p.Num("w")) / 2.0;
                double fhd = Q(p.Num("d")) / 2.0;
                double fcx = Q(p.Num("x"));
                double fcz = Q(p.Num("z"));
                var rect = new TacRect { x0 = fcx - fhw, z0 = fcz - fhd, x1 = fcx + fhw, z1 = fcz + fhd };
                if (ty == "river") w.rivers.Add(rect); else w.trenches.Add(rect);
            }
            else if (ty == "rockslide")
            {
                int sd = (int)p.Num("dir", 0.0) & 3;
                double sdx = sd == 1 ? 1.0 : (sd == 3 ? -1.0 : 0.0);
                double sdz = sd == 0 ? 1.0 : (sd == 2 ? -1.0 : 0.0);
                double pw = Q(p.Has("w") ? p.Num("w") : 6.0);
                double pdep = Q(p.Has("d") ? p.Num("d") : 3.0);
                double px2 = Q(p.Num("x"));
                double pz2 = Q(p.Num("z"));
                double poff = pdep / 2.0 + 0.6;
                w.slides.Add(new TacSlide
                {
                    pileX = px2, pileZ = pz2, w = pw, d = pdep, dx = sdx, dz = sdz,
                    postX = px2 + sdx * poff, postZ = pz2 + sdz * poff, postY = 0.0,
                    triggered = false
                });
            }
            else if (ty == "switch") w.switches.Add(new TacSwitch { x = Q(p.Num("x")), z = Q(p.Num("z")), y = 0.0, r = Q(p.Has("r") ? p.Num("r") : TAC.JAMMER_R), alive = true });
            else if (ty == "intel") w.intels.Add(new TacIntel { x = Q(p.Num("x")), z = Q(p.Num("z")), y = 0.0, alive = true });
            else if (ty == "lamp") w.lamps.Add(new TacLamp { x = Q(p.Num("x")), z = Q(p.Num("z")), r = Q(p.Has("r") ? p.Num("r") : TAC.LAMP_R) });
            else if (ty == "searchlight")
            {
                double slDeg = Q(p.Num("yaw", 0.0));
                int slBase = (int)Math.Floor(slDeg * 182.04444444444445) & 65535;
                double slPeriod = p.Has("period") ? p.Num("period") : 12.0;
                int slSpeed = (int)Math.Floor(65536.0 / (slPeriod * 50.0));
                if (slSpeed < 1) slSpeed = 1;
                w.lights.Add(new TacLight { x = Q(p.Num("x")), z = Q(p.Num("z")), r = Q(p.Has("r") ? p.Num("r") : TAC.SEARCH_R), angQ = slBase, speed = slSpeed });
            }
            else if (ty == "exit")
            {
                double xhw = Q(p.Has("w") ? p.Num("w") : 4.0) / 2.0;
                double xhd = Q(p.Has("d") ? p.Num("d") : 4.0) / 2.0;
                double xcx = Q(p.Num("x"));
                double xcz = Q(p.Num("z"));
                w.exitZone = new TacRect { x0 = xcx - xhw, z0 = xcz - xhd, x1 = xcx + xhw, z1 = xcz + xhd };
            }
        }
        var ens = stage.Arr("enemies");
        for (int e = 0; e < ens.Count; e++) w.AddEnemy(ens.Obj(e));
        for (int oe = 0; oe < w.enemies.Count; oe++)
        {
            var oen = w.enemies[oe];
            if (oen.type == 4 && oen.group > 0)
            {
                if (!w.opGroups.ContainsKey(oen.group)) w.opGroups[oen.group] = new List<int>();
                w.opGroups[oen.group].Add(oe);
            }
        }

        for (int pt = 0; pt < w.trenches.Count; pt++)
        {
            var tt = w.trenches[pt];
            w.pits.Add(new TacPit { x0 = tt.x0, z0 = tt.z0, x1 = tt.x1, z1 = tt.z1, depth = TAC.TRENCH_DEPTH });
        }
        for (int pr = 0; pr < w.rivers.Count; pr++)
        {
            var rr = w.rivers[pr];
            w.pits.Add(new TacPit { x0 = rr.x0, z0 = rr.z0, x1 = rr.x1, z1 = rr.z1, depth = TAC.RIVER_DEPTH });
        }
        for (int gp = 0; gp < w.pitDefs.Count; gp++)
        {
            var gpd = w.pitDefs[gp];
            w.pits.Add(new TacPit { x0 = gpd.x0, z0 = gpd.z0, x1 = gpd.x1, z1 = gpd.z1, depth = gpd.depth });
        }

        w.BuildGrid();
        w.py = w.GroundY(w.px, w.pz, 1000.0, TAC.PLAYER_R);
        for (int k = 0; k < w.enemies.Count; k++)
        {
            var en = w.enemies[k];
            en.y = w.GroundY(en.x, en.z, 1000.0, en.r);
            if (en.type == 3) en.y = en.y + TAC.DRONE_FLY_Y;
            en.homeY = en.y;
        }
        for (int b2 = 0; b2 < w.barrels.Count; b2++)
        {
            var ba = w.barrels[b2];
            ba.y = w.GroundY(ba.x, ba.z, 1000.0, TAC.BARREL_R);
        }
        for (int m2 = 0; m2 < w.mines.Count; m2++)
        {
            var mi = w.mines[m2];
            mi.y = w.GroundY(mi.x, mi.z, 1000.0, TAC.MINE_R);
        }
        for (int h2 = 0; h2 < w.medkits.Count; h2++)
        {
            var mk = w.medkits[h2];
            mk.y = w.GroundY(mk.x, mk.z, 1000.0, 0.3);
        }
        for (int it2 = 0; it2 < w.intels.Count; it2++)
        {
            var itm = w.intels[it2];
            itm.y = w.GroundY(itm.x, itm.z, 1000.0, 0.3);
        }
        w.intelLeft = w.intels.Count;
        for (int sw2 = 0; sw2 < w.switches.Count; sw2++)
        {
            var swo = w.switches[sw2];
            swo.y = w.GroundY(swo.x, swo.z, 1000.0, 0.3);
        }
        for (int sl2 = 0; sl2 < w.slides.Count; sl2++)
        {
            var so2 = w.slides[sl2];
            so2.postY = w.GroundY(so2.postX, so2.postZ, 1000.0, 0.3);
        }
    }

    public bool InRiver(double x, double z)
    {
        for (int i = 0; i < rivers.Count; i++)
        {
            var r = rivers[i];
            if (x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1) return true;
        }
        return false;
    }
    public int TrenchAt(double x, double z)
    {
        for (int i = 0; i < trenches.Count; i++)
        {
            var t = trenches[i];
            if (x >= t.x0 && x <= t.x1 && z >= t.z0 && z <= t.z1) return i;
        }
        return -1;
    }
    public bool InActiveJammer(double x, double z)
    {
        for (int i = 0; i < switches.Count; i++)
        {
            var j = switches[i];
            if (!j.alive) continue;
            double dx = x - j.x;
            double dz = z - j.z;
            if (dx * dx + dz * dz <= j.r * j.r) return true;
        }
        return false;
    }

    public void AddBox(double x, double z, double bw, double bd, double h, int kind, double yb = 0.0)
    {
        double hw = Q(bw) / 2.0;
        double hd = Q(bd) / 2.0;
        double cx = Q(x);
        double cz = Q(z);
        double b0 = Q(yb);
        // yb = bottom, h = ABSOLUTE top. Ground-based parts keep yb 0 => identical.
        this.boxes.Add(new TacBox { x0 = cx - hw, z0 = cz - hd, x1 = cx + hw, z1 = cz + hd, yb = b0, h = b0 + Q(h), kind = kind, alive = true, hp = kind == 3 ? TAC.CRACKED_HP : 0, tint = null });
    }

    public void AddSlope(double x, double z, double bw, double bd, double h, int dir)
    {
        double hw = Q(bw) / 2.0;
        double hd = Q(bd) / 2.0;
        double cx = Q(x);
        double cz = Q(z);
        int d = dir & 3;
        double ux = 0.0, uz = 0.0;
        if (d == 0) uz = 1.0;
        else if (d == 1) ux = 1.0;
        else if (d == 2) uz = -1.0;
        else ux = -1.0;
        double qh = Q(h);
        int nSteps = (int)Math.Ceiling(qh / 0.32);
        if (nSteps < 2) nSteps = 2;
        slopes.Add(new TacSlopePart { x0 = cx - hw, z0 = cz - hd, x1 = cx + hw, z1 = cz + hd, h = qh, dir = d, ux = ux, uz = uz, steps = nSteps, rise = Q(qh / nSteps) });
    }

    public void AddBarrel(double x, double z)
    {
        barrels.Add(new TacBarrel { x = Q(x), z = Q(z), y = 0.0, state = 0, fuse = -1, dx = 0.0, dz = 0.0, alive = true });
    }

    public void AddEnemy(TacJson.JObj spec)
    {
        string sty = spec.Str("type");
        int type = sty == "gatling" ? 1 : (sty == "sniper" ? 2 : (sty == "drone" ? 3 : (sty == "operator" ? 4 : (sty == "bomber" ? 5 : (sty == "shield" ? 6 : 0)))));
        int defHp = type == 1 ? TAC.GATLING_HP : (type == 2 ? TAC.SNIPER_HP : (type == 3 ? TAC.DRONE_HP : (type == 4 ? TAC.OPERATOR_HP : (type == 5 ? TAC.BOMBER_HP : (type == 6 ? TAC.SHIELD_HP : TAC.SOLDIER_HP)))));
        double hpIn = spec.Num("hp", 0.0);
        int hp = hpIn != 0.0 ? (int)Math.Floor(hpIn) : defHp;
        double yawDeg = Q(spec.Num("yaw", 0.0));
        int yawQ = (int)Math.Floor(yawDeg * 182.04444444444445) & 65535;
        bool hasPatrol = spec.Has("patrolX") && spec.Has("patrolZ");
        var en = new TacEnemy
        {
            type = type,
            x = Q(spec.Num("x")), z = Q(spec.Num("z")), y = 0.0, homeY = 0.0,
            r = type == 1 ? TAC.GATLING_R : (type == 2 ? TAC.SNIPER_R : (type == 3 ? TAC.DRONE_R : (type == 4 ? TAC.OPERATOR_R : (type == 5 ? TAC.BOMBER_R : (type == 6 ? TAC.SHIELD_R : TAC.SOLDIER_R))))),
            h = type == 1 ? TAC.GATLING_H : (type == 2 ? TAC.SNIPER_H : (type == 3 ? TAC.DRONE_H : (type == 4 ? TAC.OPERATOR_H : (type == 5 ? TAC.BOMBER_H : (type == 6 ? TAC.SHIELD_H : TAC.SOLDIER_H))))),
            yawQ = yawQ, baseYawQ = yawQ,
            hp = hp, alive = true,
            group = spec.Has("group") ? (int)Math.Floor(spec.Num("group")) : 0,
            state = 0,
            gauge = 0.0,
            tx = 0.0, tz = 0.0,
            homeX = Q(spec.Num("x")), homeZ = Q(spec.Num("z")),
            patX = hasPatrol ? Q(spec.Num("patrolX")) : 0.0,
            patZ = hasPatrol ? Q(spec.Num("patrolZ")) : 0.0,
            hasPatrol = hasPatrol,
            patToB = true,
            pauseT = 0,
            attackCd = 0,
            bombCd = 0,
            rifleCd = 0,
            aimT = 0,
            suppressT = 0,
            fireFlash = 0,
            cycleT = 0,
            warnT = 0,
            crouched = false,
            holdTrench = false,
            diving = false,
            crashing = false,
            idx = this.enemies.Count,
            corpseSpotted = false,
            shieldStagT = 0,
            entrench = spec.Has("entrench") && spec.Num("entrench") != 0.0,
            seekCover = false,
            dugIn = false,
            seesPlayer = false
        };
        enemies.Add(en);
        enemiesLeft++;
    }

    public void BuildGrid()
    {
        var w = this;
        w.gridW = (int)Math.Floor(w.arenaW / TAC.CELL) + 1;
        w.gridD = (int)Math.Floor(w.arenaD / TAC.CELL) + 1;
        var cells = new List<int>[w.gridW * w.gridD];
        for (int b = 0; b < w.boxes.Count; b++)
        {
            var box = w.boxes[b];
            int cx0 = (int)Math.Floor(box.x0 / TAC.CELL); if (cx0 < 0) cx0 = 0;
            int cz0 = (int)Math.Floor(box.z0 / TAC.CELL); if (cz0 < 0) cz0 = 0;
            int cx1 = (int)Math.Floor(box.x1 / TAC.CELL); if (cx1 >= w.gridW) cx1 = w.gridW - 1;
            int cz1 = (int)Math.Floor(box.z1 / TAC.CELL); if (cz1 >= w.gridD) cz1 = w.gridD - 1;
            for (int gz = cz0; gz <= cz1; gz++)
            {
                for (int gx = cx0; gx <= cx1; gx++)
                {
                    int idx = gz * w.gridW + gx;
                    if (cells[idx] == null) cells[idx] = new List<int>();
                    cells[idx].Add(b);
                }
            }
        }
        w.grid = cells;
    }

    public void ForBoxesIn(double x0, double z0, double x1, double z1, Action<int> fn)
    {
        var w = this;
        if (w._stamps == null) { w._stamp = 1; w._stamps = new int[w.boxes.Count]; }
        int stamp = ++w._stamp;
        int cx0 = (int)Math.Floor(x0 / TAC.CELL); if (cx0 < 0) cx0 = 0;
        int cz0 = (int)Math.Floor(z0 / TAC.CELL); if (cz0 < 0) cz0 = 0;
        int cx1 = (int)Math.Floor(x1 / TAC.CELL); if (cx1 >= w.gridW) cx1 = w.gridW - 1;
        int cz1 = (int)Math.Floor(z1 / TAC.CELL); if (cz1 >= w.gridD) cz1 = w.gridD - 1;
        for (int gz = cz0; gz <= cz1; gz++)
        {
            for (int gx = cx0; gx <= cx1; gx++)
            {
                var cell = w.grid[gz * w.gridW + gx];
                if (cell == null) continue;
                for (int i = 0; i < cell.Count; i++)
                {
                    int bi = cell[i];
                    if (w._stamps[bi] == stamp) continue;
                    w._stamps[bi] = stamp;
                    fn(bi);
                }
            }
        }
    }

    public double SlopeYAt(TacSlopePart s, double x, double z)
    {
        double t;
        if (s.dir == 0) t = (z - s.z0) / (s.z1 - s.z0);
        else if (s.dir == 1) t = (x - s.x0) / (s.x1 - s.x0);
        else if (s.dir == 2) t = (s.z1 - z) / (s.z1 - s.z0);
        else t = (s.x1 - x) / (s.x1 - s.x0);
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;
        int idx = (int)Math.Floor(t * s.steps);
        if (idx >= s.steps) idx = s.steps - 1;
        return s.rise * (idx + 1);
    }

    public double GroundY(double x, double z, double refY, double r)
    {
        var w = this;
        double best = 0.0;
        for (int pi = 0; pi < w.pits.Count; pi++)
        {
            var pp = w.pits[pi];
            if (x >= pp.x0 && x <= pp.x1 && z >= pp.z0 && z <= pp.z1)
            {
                double pf = -pp.depth;
                if (pf < best) best = pf;
            }
        }
        double lim = refY + TAC.STEP_UP;
        double bestBox = best;
        w.ForBoxesIn(x - r, z - r, x + r, z + r, (bi) =>
        {
            var b = w.boxes[bi];
            if (!b.alive) return;
            if (b.h > lim) return;
            if (refY < b.yb) return; // beneath a floating block: its top is not your floor
            if (x + r <= b.x0 || x - r >= b.x1 || z + r <= b.z0 || z - r >= b.z1) return;
            if (b.h > bestBox) bestBox = b.h;
        });
        best = bestBox;
        for (int s = 0; s < w.slopes.Count; s++)
        {
            var sl = w.slopes[s];
            if (x < sl.x0 || x > sl.x1 || z < sl.z0 || z > sl.z1) continue;
            double sy = w.SlopeYAt(sl, x, z);
            if (sy <= lim && sy > best) best = sy;
        }
        for (int bi2 = 0; bi2 < w.barrels.Count; bi2++)
        {
            var ba = w.barrels[bi2];
            if (!ba.alive || ba.state != 0) continue;
            double ddx = x - ba.x;
            double ddz = z - ba.z;
            double rr = r + TAC.BARREL_R;
            double d2 = ddx * ddx + ddz * ddz;
            if (d2 >= rr * rr) continue;
            double top = ba.y + TAC.BARREL_H;
            if (top <= lim && top > best) best = top;
        }
        return best;
    }

    public int SegCrackedHit(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var w = this;
        double dx = x1 - x0;
        double dy = y1 - y0;
        double dz = z1 - z0;
        for (int i = 0; i < w.boxes.Count; i++)
        {
            var b = w.boxes[i];
            if (b.kind != 3) continue;
            if (!b.alive) continue;
            double t0 = 0.0, t1 = 1.0;
            if (dx > 0.000001 || dx < -0.000001)
            {
                double txa = (b.x0 - x0) / dx;
                double txb = (b.x1 - x0) / dx;
                double txmin = txa < txb ? txa : txb;
                double txmax = txa < txb ? txb : txa;
                if (txmin > t0) t0 = txmin;
                if (txmax < t1) t1 = txmax;
            }
            else if (x0 <= b.x0 || x0 >= b.x1) continue;
            if (dz > 0.000001 || dz < -0.000001)
            {
                double tza = (b.z0 - z0) / dz;
                double tzb = (b.z1 - z0) / dz;
                double tzmin = tza < tzb ? tza : tzb;
                double tzmax = tza < tzb ? tzb : tza;
                if (tzmin > t0) t0 = tzmin;
                if (tzmax < t1) t1 = tzmax;
            }
            else if (z0 <= b.z0 || z0 >= b.z1) continue;
            if (dy > 0.000001 || dy < -0.000001)
            {
                double tya = (b.yb - y0) / dy;
                double tyb = (b.h - y0) / dy;
                double tymin = tya < tyb ? tya : tyb;
                double tymax = tya < tyb ? tyb : tya;
                if (tymin > t0) t0 = tymin;
                if (tymax < t1) t1 = tymax;
            }
            else if (y0 <= b.yb || y0 >= b.h) continue;
            if (t0 <= t1) return i;
        }
        return -1;
    }

    public void BreakWall(int bi)
    {
        var w = this;
        var b = w.boxes[bi];
        b.alive = false;
        if (w.events.wallBreaks == null) w.events.wallBreaks = new List<TacWallBreak>();
        w.events.wallBreaks.Add(new TacWallBreak { i = bi, x = (b.x0 + b.x1) / 2.0, y = b.h / 2.0, z = (b.z0 + b.z1) / 2.0 });
    }

    public TacSlopePart SlopeUnder(double x, double z, double y)
    {
        for (int s = 0; s < slopes.Count; s++)
        {
            var sl = slopes[s];
            if (x < sl.x0 || x > sl.x1 || z < sl.z0 || z > sl.z1) continue;
            double sy = SlopeYAt(sl, x, z);
            double dy = y - sy;
            if (dy > -0.1 && dy < 0.12) return sl;
        }
        return null;
    }

    public struct TacMove { public double x, z; }

    public TacMove MoveCircle(double x, double z, double y, double r, double h, double dx, double dz)
    {
        var w = this;
        double blockAbove = y + TAC.STEP_UP;
        double nx = x + dx;
        double nz = z + dz;
        for (int iter = 0; iter < 3; iter++)
        {
            bool pushedAny = false;
            w.ForBoxesIn(nx - r, nz - r, nx + r, nz + r, (bi) =>
            {
                var b = w.boxes[bi];
                if (!b.alive) return;
                if (b.h <= blockAbove) return;
                if (b.yb >= y + h) return; // floating span is above the mover: pass beneath
                double cx = nx < b.x0 ? b.x0 : (nx > b.x1 ? b.x1 : nx);
                double cz = nz < b.z0 ? b.z0 : (nz > b.z1 ? b.z1 : nz);
                double ox = nx - cx;
                double oz = nz - cz;
                double d2 = ox * ox + oz * oz;
                if (d2 >= r * r) return;
                if (d2 > 0.000001)
                {
                    double d = Math.Sqrt(d2);
                    double push = r - d;
                    double pux = ox / d;
                    double puz = oz / d;
                    nx = nx + pux * push;
                    nz = nz + puz * push;
                }
                else
                {
                    double lx = nx - b.x0;
                    double rx = b.x1 - nx;
                    double lz = nz - b.z0;
                    double rz = b.z1 - nz;
                    if (lx <= rx && lx <= lz && lx <= rz) nx = b.x0 - r;
                    else if (rx <= lz && rx <= rz) nx = b.x1 + r;
                    else if (lz <= rz) nz = b.z0 - r;
                    else nz = b.z1 + r;
                }
                pushedAny = true;
            });
            // staircases (slopes) are solid where a tread is too tall to step onto: the
            // raised sides and back become walls so nobody walks INTO the stairs and ends
            // up buried under a tread (unshootable). The low ramp entry — treads within
            // STEP_UP of the feet — stays open, so climbing works from any angle as before.
            for (int si = 0; si < w.slopes.Count; si++)
            {
                var sl2 = w.slopes[si];
                double scx = nx < sl2.x0 ? sl2.x0 : (nx > sl2.x1 ? sl2.x1 : nx);
                double scz = nz < sl2.z0 ? sl2.z0 : (nz > sl2.z1 ? sl2.z1 : nz);
                double sox = nx - scx;
                double soz = nz - scz;
                double sd2 = sox * sox + soz * soz;
                if (sd2 >= r * r) continue;
                double tread = w.SlopeYAt(sl2, scx, scz);
                if (tread <= blockAbove) continue;
                if (y >= tread) continue;
                if (sd2 > 0.000001)
                {
                    double sdd = Math.Sqrt(sd2);
                    double spush = r - sdd;
                    nx = nx + (sox / sdd) * spush;
                    nz = nz + (soz / sdd) * spush;
                }
                else
                {
                    double slx = nx - sl2.x0;
                    double srx = sl2.x1 - nx;
                    double slz = nz - sl2.z0;
                    double srz = sl2.z1 - nz;
                    if (slx <= srx && slx <= slz && slx <= srz) nx = sl2.x0 - r;
                    else if (srx <= slz && srx <= srz) nx = sl2.x1 + r;
                    else if (slz <= srz) nz = sl2.z0 - r;
                    else nz = sl2.z1 + r;
                }
                pushedAny = true;
            }
            for (int i = 0; i < w.barrels.Count; i++)
            {
                var ba = w.barrels[i];
                if (!ba.alive || ba.state != 0) continue;
                double top = ba.y + TAC.BARREL_H;
                if (top <= blockAbove) continue;
                if (y >= top) continue;
                double ddx = nx - ba.x;
                double ddz = nz - ba.z;
                double rr = r + TAC.BARREL_R;
                double d2b = ddx * ddx + ddz * ddz;
                if (d2b < rr * rr && d2b > 0.000001)
                {
                    double d3 = Math.Sqrt(d2b);
                    double push2 = rr - d3;
                    double pux2 = ddx / d3;
                    double puz2 = ddz / d3;
                    nx = nx + pux2 * push2;
                    nz = nz + puz2 * push2;
                    pushedAny = true;
                }
            }
            if (!pushedAny) break;
        }
        if (nx < r) nx = r;
        if (nx > w.arenaW - r) nx = w.arenaW - r;
        if (nz < r) nz = r;
        if (nz > w.arenaD - r) nz = w.arenaD - r;
        return new TacMove { x = nx, z = nz };
    }

    // lowest box UNDERSIDE above fromY over the footprint (jump head-bump)
    public double CeilingY(double x, double z, double r, double fromY)
    {
        var w = this;
        double best = 1.0e9;
        w.ForBoxesIn(x - r, z - r, x + r, z + r, (bi) =>
        {
            var b = w.boxes[bi];
            if (!b.alive) return;
            if (b.yb < fromY + 0.2) return;
            if (x + r <= b.x0 || x - r >= b.x1 || z + r <= b.z0 || z - r >= b.z1) return;
            if (b.yb < best) best = b.yb;
        });
        return best;
    }

    public bool SegBlocked(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var w = this;
        bool hit = false;
        double minX = x0 < x1 ? x0 : x1;
        double maxX = x0 < x1 ? x1 : x0;
        double minZ = z0 < z1 ? z0 : z1;
        double maxZ = z0 < z1 ? z1 : z0;
        double dx = x1 - x0;
        double dy = y1 - y0;
        double dz = z1 - z0;
        if (w.pits.Count > 0 && w.PitRimBlocked(x0, y0, z0, x1, y1, z1)) return true;
        w.ForBoxesIn(minX, minZ, maxX, maxZ, (bi) =>
        {
            if (hit) return;
            var b = w.boxes[bi];
            if (!b.alive) return;
            double t0 = 0.0, t1 = 1.0;
            if (dx > 0.000001 || dx < -0.000001)
            {
                double txa = (b.x0 - x0) / dx;
                double txb = (b.x1 - x0) / dx;
                double txmin = txa < txb ? txa : txb;
                double txmax = txa < txb ? txb : txa;
                if (txmin > t0) t0 = txmin;
                if (txmax < t1) t1 = txmax;
            }
            else if (x0 <= b.x0 || x0 >= b.x1) return;
            if (dz > 0.000001 || dz < -0.000001)
            {
                double tza = (b.z0 - z0) / dz;
                double tzb = (b.z1 - z0) / dz;
                double tzmin = tza < tzb ? tza : tzb;
                double tzmax = tza < tzb ? tzb : tza;
                if (tzmin > t0) t0 = tzmin;
                if (tzmax < t1) t1 = tzmax;
            }
            else if (z0 <= b.z0 || z0 >= b.z1) return;
            if (dy > 0.000001 || dy < -0.000001)
            {
                double tya = (b.yb - y0) / dy;
                double tyb = (b.h - y0) / dy;
                double tymin = tya < tyb ? tya : tyb;
                double tymax = tya < tyb ? tyb : tya;
                if (tymin > t0) t0 = tymin;
                if (tymax < t1) t1 = tymax;
            }
            else if (y0 <= b.yb || y0 >= b.h) return;
            if (t0 <= t1) hit = true;
        });
        return hit;
    }

    public bool PitRimBlocked(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var w = this;
        double dx = x1 - x0;
        double dy = y1 - y0;
        double dz = z1 - z0;
        for (int i = 0; i < w.pits.Count; i++)
        {
            var pp = w.pits[i];
            double t0 = 0.0, t1 = 1.0;
            if (dx > 0.000001 || dx < -0.000001)
            {
                double ta = (pp.x0 - x0) / dx;
                double tb = (pp.x1 - x0) / dx;
                double mn = ta < tb ? ta : tb;
                double mx = ta < tb ? tb : ta;
                if (mn > t0) t0 = mn;
                if (mx < t1) t1 = mx;
            }
            else if (x0 <= pp.x0 || x0 >= pp.x1) continue;
            if (dz > 0.000001 || dz < -0.000001)
            {
                double tc = (pp.z0 - z0) / dz;
                double td = (pp.z1 - z0) / dz;
                double mn2 = tc < td ? tc : td;
                double mx2 = tc < td ? td : tc;
                if (mn2 > t0) t0 = mn2;
                if (mx2 < t1) t1 = mx2;
            }
            else if (z0 <= pp.z0 || z0 >= pp.z1) continue;
            if (t0 > t1) continue;
            if (t0 > 0.0 && t0 < 1.0)
            {
                double yEnter = y0 + dy * t0;
                if (yEnter < 0.0 && yEnter > -pp.depth - 3.0) return true;
            }
            if (t1 > 0.0 && t1 < 1.0)
            {
                double yExit = y0 + dy * t1;
                if (yExit < 0.0 && yExit > -pp.depth - 3.0) return true;
            }
        }
        return false;
    }

    public void AddNoise(double x, double z, double r)
    {
        noises.Add(new TacNoise { x = x, z = z, r = r });
        if (events.noises == null) events.noises = new List<TacNoise>();
        events.noises.Add(new TacNoise { x = x, z = z, r = r });
    }

    public TacEvents Step(TacInput input)
    {
        var w = this;
        w.events = new TacEvents();
        w.noises.Clear();
        if (w.dead || w.clearedFlag || w.timedOutFlag) return w.events;
        w.tick++;
        w.sneaking = (input.b & 4) != 0;

        w.StepPlayer(input);
        w.StepBullets();
        w.StepBombs();
        w.StepGrenades();
        w.StepBarrels();
        w.StepMines();
        w.StepSlides();
        w.StepMedkits();
        w.StepIntel();
        w.StepLights();
        w.StepEnemies();

        bool goalMet = false;
        if (w.goalType == 1)
        {
            if (w.intelLeft <= 0 && w.exitZone != null &&
                w.px >= w.exitZone.x0 && w.px <= w.exitZone.x1 &&
                w.pz >= w.exitZone.z0 && w.pz <= w.exitZone.z1 &&
                w.py > -0.5 && w.py < 1.0) goalMet = true;
        }
        else
        {
            if (w.enemiesLeft <= 0) goalMet = true;
        }
        if (goalMet && !w.dead)
        {
            w.clearedFlag = true;
            w.events.cleared = true;
        }
        else if (w.tick >= w.maxTicks)
        {
            w.timedOutFlag = true;
            w.events.timedOut = true;
        }
        return w.events;
    }

    public void StepPlayer(TacInput input)
    {
        var w = this;
        w.yawQ = input.yawQ & 65535;
        int p = input.pitchQ;
        if (p > TAC.PITCH_MAX) p = TAC.PITCH_MAX;
        if (p < TAC.PITCH_MIN) p = TAC.PITCH_MIN;
        w.pitchQ = p;
        if (w.fireCd > 0) w.fireCd--;
        if (w.hurtCd > 0) w.hurtCd--;
        if (w.fireFlash > 0) w.fireFlash--;
        if (w.scopeCd > 0) w.scopeCd--;

        bool droneEdge = (input.b & 8) != 0 && (w.prevB & 8) == 0;
        bool fireEdge = (input.b & 2) != 0 && (w.prevB & 2) == 0;
        bool grenEdge = (input.b & 16) != 0 && (w.prevB & 16) == 0;
        bool scopeEdge = (input.b & 32) != 0 && (w.prevB & 32) == 0;
        w.prevB = input.b;
        if (w.grenadeCd > 0) w.grenadeCd--;
        w.playerJammed = w.InActiveJammer(w.px, w.pz);

        if (scopeEdge && w.pilot == null)
        {
            if (w.scoped)
            {
                w.scoped = false;
                w.events.scopeOff = true;
            }
            else if (w.onGround)
            {
                w.scoped = true;
                // start the scope from where the player is already looking: yaw =
                // current facing, pitch = the live camera pitch (carried in the
                // recorded input, so this stays replay-deterministic)
                w.aimYawQ = w.faceQ;
                w.aimPitchQ = input.pitchQ;
                if (w.aimPitchQ > TAC.PITCH_MAX) w.aimPitchQ = TAC.PITCH_MAX;
                if (w.aimPitchQ < TAC.PITCH_MIN) w.aimPitchQ = TAC.PITCH_MIN;
                w.events.scopeOn = true;
            }
        }
        if (w.scoped)
        {
            // Direct-drag aiming: the reticle follows the camera angle carried in
            // the recorded input (yawQ/pitchQ), so dragging ANYWHERE on screen
            // moves the aim 1:1 like a standard FPS sniper — no directional stick.
            // Absolute angles keep this fully replay-deterministic.
            w.aimYawQ = input.yawQ & 65535;
            w.aimPitchQ = input.pitchQ;
            if (w.aimPitchQ > TAC.PITCH_MAX) w.aimPitchQ = TAC.PITCH_MAX;
            if (w.aimPitchQ < TAC.PITCH_MIN) w.aimPitchQ = TAC.PITCH_MIN;
            if (fireEdge && w.scopeShots > 0)
            {
                w.FireScopedShot();
                w.scopeShots--;
            }
            w.faceQ = w.aimYawQ;
            w.crouched = w.py < -0.45;
            w.lockTarget = -1;
            return;
        }
        if (droneEdge)
        {
            if (w.pilot == null && w.droneUses > 0 && w.onGround)
            {
                w.droneUses--;
                w.pilot = new TacPilot { x = w.px, y = w.py + 1.2, z = w.pz, battery = TAC.PILOT_BATTERY, dive = -1, yawQ = w.faceQ };
                w.events.droneLaunch = true;
            }
        }
        if (w.pilot != null)
        {
            var pd = w.pilot;
            if (w.InActiveJammer(pd.x, pd.z))
            {
                w.events.jamZap = new TacXYZ { x = pd.x, y = pd.y, z = pd.z };
                w.ExplodeAt(pd.x, pd.y, pd.z, TAC.PILOT_BLAST_R, 2);
                w.events.droneDead = true;
                w.pilot = null;
                w.lockTarget = -1;
                return;
            }
            if (pd.dive >= 0)
            {
                var den = pd.dive < w.enemies.Count ? w.enemies[pd.dive] : null;
                double tx3 = pd.x, ty3 = 0.4, tz3 = pd.z;
                if (den != null && den.alive)
                {
                    tx3 = den.x;
                    ty3 = den.y + den.h * 0.5;
                    tz3 = den.z;
                }
                double ddx3 = tx3 - pd.x;
                double ddy3 = ty3 - pd.y;
                double ddz3 = tz3 - pd.z;
                double dl3 = Math.Sqrt(ddx3 * ddx3 + ddy3 * ddy3 + ddz3 * ddz3);
                double dstep3 = TAC.PILOT_DIVE_SPEED * TAC.TICK;
                if (dl3 <= 1.0 || dl3 <= dstep3)
                {
                    w.ExplodeAt(tx3, ty3, tz3, TAC.PILOT_BLAST_R, 2);
                    w.events.droneDetonate = true;
                    w.pilot = null;
                }
                else
                {
                    pd.x = pd.x + (ddx3 / dl3) * dstep3;
                    pd.y = pd.y + (ddy3 / dl3) * dstep3;
                    pd.z = pd.z + (ddz3 / dl3) * dstep3;
                }
                w.lockTarget = -1;
                return;
            }
            if (input.m != 255)
            {
                int pq = (w.yawQ + input.m * 512) & 65535;
                double pvx = TacMath.SinQ(pq);
                double pvz = TacMath.CosQ(pq);
                pd.yawQ = pq;
                double pstep = TAC.PILOT_SPEED * TAC.TICK;
                var pres = w.MoveCircle(pd.x, pd.z, pd.y, 0.5, 0.5, pvx * pstep, pvz * pstep);
                pd.x = pres.x;
                pd.z = pres.z;
            }
            double pg = w.GroundY(pd.x, pd.z, 1000.0, 0.5);
            double pWant = pg + TAC.PILOT_ALT;
            double pRise = 3.0 * TAC.TICK;
            if (pd.y < pWant - pRise) pd.y = pd.y + pRise;
            else if (pd.y > pWant + pRise) pd.y = pd.y - pRise;
            else pd.y = pWant;
            pd.battery--;
            int pBest = -1;
            double pBest2 = TAC.PILOT_LOCK_R * TAC.PILOT_LOCK_R;
            for (int pe = 0; pe < w.enemies.Count; pe++)
            {
                var pen = w.enemies[pe];
                if (!pen.alive) continue;
                double phx = pen.x - pd.x;
                double phz = pen.z - pd.z;
                double ph2 = phx * phx + phz * phz;
                if (ph2 < pBest2) { pBest2 = ph2; pBest = pe; }
            }
            if (fireEdge)
            {
                if (pBest >= 0)
                {
                    pd.dive = pBest;
                    w.events.droneDive = true;
                }
                else
                {
                    w.ExplodeAt(pd.x, pg + 0.9, pd.z, TAC.PILOT_BLAST_R, 2);
                    w.events.droneDetonate = true;
                    w.pilot = null;
                }
            }
            else if (pd.battery <= 0)
            {
                w.pilot = null;
                w.events.droneDead = true;
            }
            w.lockTarget = pBest;
            w.lockKind = 0;
            return;
        }

        if (grenEdge && w.grenadeCd == 0)
        {
            if (w.py < -0.45) w.fireFlash = 15;
            double ggx = TacMath.SinQ(w.faceQ);
            double ggz = TacMath.CosQ(w.faceQ);
            w.grenades.Add(new TacGrenade
            {
                x = w.px + ggx * 0.5, y = w.py + TAC.CHEST_H, z = w.pz + ggz * 0.5,
                vx = ggx * TAC.GRENADE_SPEED_H, vy = TAC.GRENADE_SPEED_V, vz = ggz * TAC.GRENADE_SPEED_H,
                alive = true
            });
            w.grenadeCd = TAC.GRENADE_CD;
            w.events.grenadeThrow = true;
        }

        bool sneak = (input.b & 4) != 0;
        bool inPitLow = w.py < -0.45;
        bool autoCrouch = inPitLow && w.TrenchAt(w.px, w.pz) >= 0;
        bool fireHeld = (input.b & 2) != 0;
        w.crouched = (autoCrouch || sneak) && w.fireFlash == 0 && !(fireHeld && inPitLow);
        if (w.crouched) sneak = true;
        bool moving = input.m != 255;
        double dx = 0.0, dz = 0.0;

        if (moving) { if (w.moveT < TAC.MOVE_RAMP_TICKS) w.moveT++; } else { w.moveT = 0; }
        double rampFrac = w.moveT / TAC.MOVE_RAMP_TICKS;
        double rampSpan = 1.0 - TAC.MOVE_RAMP_MIN;
        double ramp = TAC.MOVE_RAMP_MIN + rampSpan * rampFrac;

        if (moving)
        {
            int mq2 = (w.yawQ + input.m * 512) & 65535;
            w.faceQ = mq2;
            double mvx2 = TacMath.SinQ(mq2);
            double mvz2 = TacMath.CosQ(mq2);
            double spd2 = sneak ? TAC.WALK_SPEED : TAC.RUN_SPEED;
            double spd2R = spd2 * ramp;
            double stepd2 = spd2R * TAC.TICK;
            dx = mvx2 * stepd2;
            dz = mvz2 * stepd2;
            if (!sneak && w.onGround && (w.tick % TAC.NOISE_RUN_EVERY) == 0) w.AddNoise(w.px, w.pz, TAC.NOISE_RUN_R);
        }

        if (dx != 0.0 || dz != 0.0)
        {
            // wading slows you ONLY when actually down in the water — a deck
            // built OVER a channel is dry land
            if (w.py < -0.2 && w.InRiver(w.px, w.pz))
            {
                dx = dx * TAC.RIVER_MUL;
                dz = dz * TAC.RIVER_MUL;
            }
            var res = w.MoveCircle(w.px, w.pz, w.py, TAC.PLAYER_R, TAC.PLAYER_H, dx, dz);
            w.px = res.x;
            w.pz = res.z;
        }

        if ((input.b & 1) != 0 && w.onGround)
        {
            w.vy = TAC.JUMP_V;
            w.onGround = false;
            w.events.jumped = true;
        }

        double dv = TAC.GRAVITY * TAC.TICK;
        w.vy = w.vy - dv;
        double dyy = w.vy * TAC.TICK;
        w.py = w.py + dyy;
        if (w.vy > 0.0)
        {
            double ceil = w.CeilingY(w.px, w.pz, TAC.PLAYER_R, w.py - dyy);
            if (w.py + TAC.PLAYER_H > ceil)
            {
                w.py = ceil - TAC.PLAYER_H;
                w.vy = 0.0;
            }
        }
        double g = w.GroundY(w.px, w.pz, w.onGround ? w.py + 0.01 : w.py, TAC.PLAYER_R);
        if (w.py <= g && w.vy <= 0.0)
        {
            if (!w.onGround && w.vy < -6.0)
            {
                w.AddNoise(w.px, w.pz, TAC.NOISE_LAND_R);
                w.events.landed = true;
            }
            w.py = g;
            w.vy = 0.0;
            w.onGround = true;
        }
        else if (w.py > g + 0.02)
        {
            w.onGround = false;
        }

        w.UpdateLock();

        if ((input.b & 2) != 0 && w.fireCd == 0 && w.ammo != 0)
        {
            if (w.py < -0.45) w.fireFlash = 8;
            double mfx = TacMath.SinQ(w.faceQ);
            double mfz = TacMath.CosQ(w.faceQ);
            double mrx = mfz;
            double mrz = -mfx;
            double muzX = w.px + mfx * 0.7 + mrx * 0.25;
            double muzY = w.py + (w.crouched && w.fireFlash == 0 ? 0.6 : 1.05);
            double muzZ = w.pz + mfz * 0.7 + mrz * 0.25;
            double dirx, diry, dirz;
            if (w.lockTarget >= 0)
            {
                double ltx, lty, ltz;
                if (w.lockKind == 1)
                {
                    var lb = w.barrels[w.lockTarget];
                    ltx = lb.x;
                    lty = lb.y + 0.5;
                    ltz = lb.z;
                }
                else if (w.lockKind == 2)
                {
                    var lm = w.mines[w.lockTarget];
                    ltx = lm.x;
                    lty = lm.y + 0.1;
                    ltz = lm.z;
                }
                else if (w.lockKind == 3)
                {
                    var lsd = w.slides[w.lockTarget];
                    ltx = lsd.postX;
                    lty = lsd.postY + 0.6;
                    ltz = lsd.postZ;
                }
                else if (w.lockKind == 4)
                {
                    var lsw = w.switches[w.lockTarget];
                    ltx = lsw.x;
                    lty = lsw.y + 0.6;
                    ltz = lsw.z;
                }
                else
                {
                    var lt = w.enemies[w.lockTarget];
                    ltx = lt.x;
                    lty = lt.y + lt.h * 0.6;
                    ltz = lt.z;
                }
                double tdx = ltx - muzX;
                double tdy = lty - muzY;
                double tdz = ltz - muzZ;
                double tl = Math.Sqrt(tdx * tdx + tdy * tdy + tdz * tdz);
                dirx = tdx / tl;
                diry = tdy / tl;
                dirz = tdz / tl;
            }
            else
            {
                dirx = mfx;
                diry = 0.0;
                dirz = mfz;
            }
            w.bullets.Add(new TacBullet
            {
                x = muzX, y = muzY, z = muzZ,
                sx = muzX, sy = muzY, sz = muzZ,
                vx = dirx * TAC.BULLET_SPEED, vy = diry * TAC.BULLET_SPEED, vz = dirz * TAC.BULLET_SPEED,
                ttl = TAC.BULLET_TTL, fromPlayer = true, alive = true
            });
            w.fireCd = TAC.FIRE_CD;
            if (w.ammo > 0) w.ammo--;
            w.shotsFired++;
            w.AddNoise(w.px, w.pz, TAC.NOISE_SHOT_R);
            w.events.shot = true;
        }
    }

    public void UpdateLock()
    {
        var w = this;
        double fx = TacMath.SinQ(w.faceQ);
        double fz = TacMath.CosQ(w.faceQ);
        double ex = w.px;
        double ey = w.py + TAC.CHEST_H;
        double ez = w.pz;
        int best = -1;
        int bestKind = 0;
        int bestTier = 99;
        double bestD2 = 1.0e18;
        double cos = TAC.LOCK_COS;
        double range2 = TAC.LOCK_RANGE * TAC.LOCK_RANGE;
        Action<int, int, int, double, double, double> consider = (i, kind, tier, cx, cy, cz) =>
        {
            if (tier > bestTier) return;
            double tx = cx - ex;
            double ty = cy - ey;
            double tz = cz - ez;
            double d2 = tx * tx + ty * ty + tz * tz;
            if (d2 > range2 || d2 < 0.0001) return;
            double dh2 = tx * tx + tz * tz;
            if (dh2 < 0.0001) return;
            double dh = Math.Sqrt(dh2);
            double dot = (fx * tx + fz * tz) / dh;
            if (dot <= cos) return;
            if (tier == bestTier && d2 >= bestD2) return;
            if (w.SegBlocked(ex, ey, ez, cx, cy, cz)) return;
            best = i;
            bestKind = kind;
            bestTier = tier;
            bestD2 = d2;
        };
        for (int i2 = 0; i2 < w.enemies.Count; i2++)
        {
            var en = w.enemies[i2];
            if (!en.alive) continue;
            consider(i2, 0, 0, en.x, en.y + TacEnemyH(en) * 0.6, en.z);
        }
        for (int b = 0; b < w.barrels.Count; b++)
        {
            var ba = w.barrels[b];
            if (!ba.alive) continue;
            consider(b, 1, 1, ba.x, ba.y + 0.5, ba.z);
        }
        for (int m = 0; m < w.mines.Count; m++)
        {
            var mi = w.mines[m];
            if (!mi.alive) continue;
            consider(m, 2, 1, mi.x, mi.y + 0.1, mi.z);
        }
        for (int sl = 0; sl < w.slides.Count; sl++)
        {
            var sld2 = w.slides[sl];
            if (sld2.triggered) continue;
            consider(sl, 3, 1, sld2.postX, sld2.postY + 0.6, sld2.postZ);
        }
        for (int sw = 0; sw < w.switches.Count; sw++)
        {
            var swc = w.switches[sw];
            if (!swc.alive) continue;
            consider(sw, 4, 1, swc.x, swc.y + 0.6, swc.z);
        }
        w.lockTarget = best;
        w.lockKind = bestKind;
    }

    public static double SegBoxT(double x0, double y0, double z0, double x1, double y1, double z1, TacBox b)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double dz = z1 - z0;
        double t0 = 0.0, t1 = 1.0;
        if (dx > 0.000001 || dx < -0.000001)
        {
            double ta = (b.x0 - x0) / dx;
            double tb = (b.x1 - x0) / dx;
            double mn = ta < tb ? ta : tb;
            double mx = ta < tb ? tb : ta;
            if (mn > t0) t0 = mn;
            if (mx < t1) t1 = mx;
        }
        else if (x0 <= b.x0 || x0 >= b.x1) return 2.0;
        if (dz > 0.000001 || dz < -0.000001)
        {
            double tc = (b.z0 - z0) / dz;
            double td = (b.z1 - z0) / dz;
            double mn2 = tc < td ? tc : td;
            double mx2 = tc < td ? td : tc;
            if (mn2 > t0) t0 = mn2;
            if (mx2 < t1) t1 = mx2;
        }
        else if (z0 <= b.z0 || z0 >= b.z1) return 2.0;
        if (dy > 0.000001 || dy < -0.000001)
        {
            double te = (b.yb - y0) / dy;
            double tf = (b.h - y0) / dy;
            double mn3 = te < tf ? te : tf;
            double mx3 = te < tf ? tf : te;
            if (mn3 > t0) t0 = mn3;
            if (mx3 < t1) t1 = mx3;
        }
        else if (y0 <= b.yb || y0 >= b.h) return 2.0;
        if (t0 <= t1) return t0;
        return 2.0;
    }

    public void FireScopedShot()
    {
        var w = this;
        double cp = TacMath.CosQ(w.aimPitchQ);
        double sp = TacMath.SinQ(w.aimPitchQ);
        double sy = TacMath.SinQ(w.aimYawQ);
        double cy = TacMath.CosQ(w.aimYawQ);
        double ox = w.px;
        double oy = w.py + (w.crouched ? 1.05 : TAC.EYE_H);
        double oz = w.pz;
        double ex = ox + sy * cp * TAC.SCOPE_RANGE;
        double ey = oy + sp * TAC.SCOPE_RANGE;
        double ez = oz + cy * cp * TAC.SCOPE_RANGE;
        double bestT = 2.0;
        double minX = ox < ex ? ox : ex;
        double maxX = ox < ex ? ex : ox;
        double minZ = oz < ez ? oz : ez;
        double maxZ = oz < ez ? ez : oz;
        w.ForBoxesIn(minX, minZ, maxX, maxZ, (bi) =>
        {
            if (!w.boxes[bi].alive) return;
            double t = SegBoxT(ox, oy, oz, ex, ey, ez, w.boxes[bi]);
            if (t < bestT) bestT = t;
        });
        if (w.pits.Count > 0 && w.PitRimBlocked(ox, oy, oz, ex, ey, ez))
        {
            bestT = -1.0;
        }
        if (bestT == -1.0) { w.AddNoise(w.px, w.pz, TAC.NOISE_SHOT_R); w.events.scopeShot = true; return; }
        int hitEnemy = -1;
        for (int e = 0; e < w.enemies.Count; e++)
        {
            var en = w.enemies[e];
            if (!en.alive) continue;
            if (w.ShieldUp(en))
            {
                double sfx2 = TacMath.SinQ(en.yawQ);
                double sfz2 = TacMath.CosQ(en.yawQ);
                if ((ex - ox) * sfx2 + (ez - oz) * sfz2 < 0.0)
                {
                    double ts = TacMath.SegCylinder(ox, oy, oz, ex, ey, ez, en.x + sfx2 * 0.45, en.y, en.z + sfz2 * 0.45, TAC.SHIELD_BLOCK_R, TAC.SHIELD_H);
                    if (ts >= 0.0 && ts < bestT) { bestT = ts; hitEnemy = -1; w.events.shieldBlock = true; continue; }
                }
            }
            // extend the cylinder up to the rendered head so a crosshair-on-head
            // shot connects (see SCOPE_HEAD_FRAC)
            double hitH = TacEnemyH(en) * (1.0 + TAC.SCOPE_HEAD_FRAC);
            double te = TacMath.SegCylinder(ox, oy, oz, ex, ey, ez, en.x, en.y, en.z, en.r, hitH);
            if (te >= 0.0 && te < bestT) { bestT = te; hitEnemy = e; }
        }
        w.AddNoise(w.px, w.pz, TAC.NOISE_SHOT_R);
        w.events.scopeShot = true;
        if (hitEnemy >= 0) w.DamageEnemy(w.enemies[hitEnemy], TAC.SCOPE_DMG);
    }

    public void HurtPlayer(int dmg, double kx, double kz)
    {
        var w = this;
        if (w.dead || w.hurtCd > 0) return;
        w.hp -= dmg;
        w.hurtCd = 40;
        w.events.playerHit = true;
        if (kx != 0.0 || kz != 0.0)
        {
            var res = w.MoveCircle(w.px, w.pz, w.py, TAC.PLAYER_R, TAC.PLAYER_H, kx, kz);
            w.px = res.x;
            w.pz = res.z;
        }
        if (w.hp <= 0)
        {
            w.dead = true;
            w.events.playerDead = true;
        }
    }

    public void DamageEnemy(TacEnemy en, int dmg)
    {
        var w = this;
        if (!en.alive) return;
        en.hp -= dmg;
        w.events.enemyHit = true;
        if (w.events.hits == null) w.events.hits = new List<TacXYZ>();
        w.events.hits.Add(new TacXYZ { x = en.x, y = en.y + en.h * 0.6, z = en.z });
        if (en.hp <= 0)
        {
            en.alive = false;
            w.enemiesLeft--;
            if (w.events.kills == null) w.events.kills = new List<TacKill>();
            w.events.kills.Add(new TacKill { x = en.x, y = en.y, z = en.z, type = en.type });
            if (en.type == 4)
            {
                w.droneUses++;
                w.events.droneGranted = true;
            }
        }
        else if (en.type == 0)
        {
            w.AlertEnemy(en, w.px, w.pz);
        }
    }

    // tower shield: raised while alive, in the CLOSED phase of the world-wide
    // formation drill, and not blast-staggered.
    public bool ShieldUp(TacEnemy en)
    {
        if (en.type != 6 || !en.alive || en.shieldStagT > 0) return false;
        return (this.tick % TAC.SHIELD_CYCLE) < (TAC.SHIELD_CYCLE - TAC.SHIELD_OPEN);
    }

    public bool SegShieldBlocked(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var w = this;
        for (int i = 0; i < w.enemies.Count; i++)
        {
            var en = w.enemies[i];
            if (!w.ShieldUp(en)) continue;
            double fx = TacMath.SinQ(en.yawQ);
            double fz = TacMath.CosQ(en.yawQ);
            if ((x1 - x0) * fx + (z1 - z0) * fz >= 0.0) continue;
            double scx = en.x + fx * 0.45;
            double scz = en.z + fz * 0.45;
            double t = TacMath.SegCylinder(x0, y0, z0, x1, y1, z1, scx, en.y, scz, TAC.SHIELD_BLOCK_R, TAC.SHIELD_H);
            if (t >= 0.0) return true;
        }
        return false;
    }

    // shield bearer: no weapon — he IS the wall. Faces the threat and holds.
    public void StepShield(TacEnemy en, double range2)
    {
        var w = this;
        w.VisionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);
        if (en.state == 2)
        {
            en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(w.px - en.x, w.pz - en.z), TAC.SHIELD_TURN);
        }
        else
        {
            en.yawQ = TacMath.TurnToward(en.yawQ, en.baseYawQ, TAC.SHIELD_TURN);
        }
    }

    // squad doctrine (stage "squad": true): units radio what they know. Nearby
    // mobile units converge on a shared last-known position, fanning out so they
    // arrive from different bearings instead of a single-file conga line.
    public void SquadShareYellow(double x, double z, TacEnemy exceptEn)
    {
        var w = this;
        if (!w.squad) return;
        int linked = 0;
        for (int i = 0; i < w.enemies.Count && linked < 3; i++)
        {
            var o = w.enemies[i];
            if (!o.alive || o == exceptEn || o.state >= 2) continue;
            if (o.type != 0 && o.type != 4 && o.type != 5) continue;
            double dx = o.x - x;
            double dz = o.z - z;
            if (dx * dx + dz * dz > TAC.SQUAD_RADIO_R * TAC.SQUAD_RADIO_R) continue;
            int dirQ = TacMath.YawFor(dx, dz);
            int off = linked == 0 ? 0 : (linked == 1 ? 10923 : -10923);
            int fq = (dirQ + off) & 65535;
            double fx = x + TacMath.SinQ(fq) * TAC.SQUAD_FAN_R;
            double fz = z + TacMath.CosQ(fq) * TAC.SQUAD_FAN_R;
            if (fx < 0.8) fx = 0.8;
            if (fx > w.arenaW - 0.8) fx = w.arenaW - 0.8;
            if (fz < 0.8) fz = 0.8;
            if (fz > w.arenaD - 0.8) fz = w.arenaD - 0.8;
            o.state = 1;
            o.tx = fx;
            o.tz = fz;
            o.pauseT = 0;
            linked++;
        }
        if (linked > 0) w.events.radio = true;
    }

    public void SquadCorpseCheck(TacEnemy en)
    {
        var w = this;
        for (int i = 0; i < w.enemies.Count; i++)
        {
            var c = w.enemies[i];
            if (c.alive || c.corpseSpotted || c.type == 3) continue;
            double dx = c.x - en.x;
            double dz = c.z - en.z;
            double d2 = dx * dx + dz * dz;
            if (d2 > TAC.VISION_RANGE * TAC.VISION_RANGE || d2 < 0.01) continue;
            double fx = TacMath.SinQ(en.yawQ);
            double fz = TacMath.CosQ(en.yawQ);
            double dot = fx * dx + fz * dz;
            if (dot <= 0.0) continue;
            if (dot * dot < TAC.VISION_COS2 * d2) continue;
            if (w.SegBlocked(en.x, en.y + TacEnemyH(en) - 0.2, en.z, c.x, c.y + 0.4, c.z)) continue;
            c.corpseSpotted = true;
            en.state = 1;
            en.tx = c.x;
            en.tz = c.z;
            en.pauseT = 0;
            w.events.corpseFound = true;
            w.SquadShareYellow(c.x, c.z, en);
            return;
        }
    }

    public void SquadIlluminate(TacEnemy drone)
    {
        var w = this;
        bool any = false;
        for (int i = 0; i < w.enemies.Count; i++)
        {
            var o = w.enemies[i];
            if (!o.alive || o == drone || o.type == 3) continue;
            double dx = o.x - drone.x;
            double dz = o.z - drone.z;
            if (dx * dx + dz * dz > TAC.SQUAD_RADIO_R * TAC.SQUAD_RADIO_R) continue;
            if (o.state == 0) { o.state = 1; o.pauseT = 0; }
            o.tx = w.px;
            o.tz = w.pz;
            any = true;
        }
        if (any) w.events.radio = true;
    }

    public void AlertEnemy(TacEnemy en, double x, double z)
    {
        var w = this;
        bool wasAlert = en.state == 2;
        if (!wasAlert) en.rifleCd = TAC.RIFLE_REACT;
        en.state = 2;
        en.tx = x;
        en.tz = z;
        en.gauge = TAC.GAUGE_MAX;
        if (en.group > 0)
        {
            for (int i = 0; i < w.enemies.Count; i++)
            {
                var o = w.enemies[i];
                if (!o.alive || o.group != en.group || o.state == 2) continue;
                o.state = 2;
                o.rifleCd = TAC.RIFLE_REACT;
                o.tx = x;
                o.tz = z;
                o.gauge = TAC.GAUGE_MAX;
            }
            w.events.groupAlert = true;
        }
        if (!wasAlert) w.SquadShareYellow(x, z, en); // share on the TRANSITION only
    }

    public void StepBullets()
    {
        var w = this;
        for (int i = 0; i < w.bullets.Count; i++)
        {
            var bu = w.bullets[i];
            if (!bu.alive) continue;
            bu.ttl--;
            if (bu.ttl <= 0) { bu.alive = false; continue; }
            double nx = bu.x + bu.vx * TAC.TICK;
            double ny = bu.y + bu.vy * TAC.TICK;
            double nz = bu.z + bu.vz * TAC.TICK;

            if (nx < 0.0 || nx > w.arenaW || nz < 0.0 || nz > w.arenaD)
            {
                bu.alive = false;
                continue;
            }
            // inside a pit the floor is sunken — you can fire from the bottom
            // of a moat, and shots can dive into one
            double floorY = 0.0;
            for (int fp = 0; fp < w.pits.Count; fp++)
            {
                var fpp = w.pits[fp];
                if (nx >= fpp.x0 && nx <= fpp.x1 && nz >= fpp.z0 && nz <= fpp.z1)
                {
                    if (-fpp.depth < floorY) floorY = -fpp.depth;
                }
            }
            if (ny <= floorY)
            {
                bu.alive = false;
                continue;
            }
            int ck = w.SegCrackedHit(bu.x, bu.y, bu.z, nx, ny, nz);
            if (ck >= 0)
            {
                if (bu.gat)
                {
                    var cb = w.boxes[ck];
                    cb.hp--;
                    if (cb.hp <= 0) w.BreakWall(ck);
                }
                bu.alive = false;
                w.events.bulletWall = true;
                continue;
            }
            if (w.SegBlocked(bu.x, bu.y, bu.z, nx, ny, nz))
            {
                bu.alive = false;
                w.events.bulletWall = true;
                continue;
            }
            var sl = w.SlopeUnder(nx, nz, ny);
            if (sl == null)
            {
                for (int s = 0; s < w.slopes.Count; s++)
                {
                    var so = w.slopes[s];
                    if (nx < so.x0 || nx > so.x1 || nz < so.z0 || nz > so.z1) continue;
                    if (ny < w.SlopeYAt(so, nx, nz)) { sl = so; break; }
                }
            }
            if (sl != null && ny < w.SlopeYAt(sl, nx, nz))
            {
                bu.alive = false;
                continue;
            }
            bool hitBarrel = false;
            for (int bi = 0; bi < w.barrels.Count; bi++)
            {
                var ba = w.barrels[bi];
                if (!ba.alive) continue;
                double t = TacMath.SegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, ba.x, ba.y, ba.z, TAC.BARREL_R, TAC.BARREL_H);
                if (t >= 0.0)
                {
                    w.IgniteBarrel(ba, bu.vx, bu.vz, TAC.BARREL_FUSE);
                    bu.alive = false;
                    hitBarrel = true;
                    break;
                }
            }
            if (hitBarrel) continue;
            bool hitMine = false;
            for (int mj = 0; mj < w.mines.Count; mj++)
            {
                var mi = w.mines[mj];
                if (!mi.alive) continue;
                double tm = TacMath.SegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, mi.x, mi.y, mi.z, TAC.MINE_R, 0.25);
                if (tm >= 0.0)
                {
                    if (mi.fuse < 0 || mi.fuse > TAC.MINE_SHOT_FUSE) mi.fuse = TAC.MINE_SHOT_FUSE;
                    w.events.mineHit = true;
                    bu.alive = false;
                    hitMine = true;
                    break;
                }
            }
            if (hitMine) continue;
            bool hitObj = false;
            for (int sw3 = 0; sw3 < w.switches.Count; sw3++)
            {
                var swb = w.switches[sw3];
                if (!swb.alive) continue;
                double tsw = TacMath.SegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, swb.x, swb.y, swb.z, 0.4, 1.2);
                if (tsw >= 0.0)
                {
                    w.KillSwitch(swb);
                    bu.alive = false;
                    hitObj = true;
                    break;
                }
            }
            if (hitObj) continue;
            for (int sl3 = 0; sl3 < w.slides.Count; sl3++)
            {
                var sld = w.slides[sl3];
                if (sld.triggered) continue;
                double tps = TacMath.SegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, sld.postX, sld.postY, sld.postZ, 0.3, 1.2);
                if (tps >= 0.0)
                {
                    w.TriggerSlide(sld);
                    bu.alive = false;
                    hitObj = true;
                    break;
                }
            }
            if (hitObj) continue;

            if (bu.fromPlayer && w.SegShieldBlocked(bu.x, bu.y, bu.z, nx, ny, nz))
            {
                bu.alive = false;
                w.events.shieldBlock = true;
                continue;
            }
            if (bu.fromPlayer)
            {
                bool hitEnemy = false;
                for (int e = 0; e < w.enemies.Count; e++)
                {
                    var en = w.enemies[e];
                    if (!en.alive) continue;
                    double te = TacMath.SegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, en.x, en.y, en.z, en.r, TacEnemyH(en));
                    if (te < 0.0) continue;
                    w.DamageEnemy(en, 1);
                    bu.alive = false;
                    hitEnemy = true;
                    break;
                }
                if (hitEnemy) continue;
            }
            else
            {
                double tp = TacMath.SegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, w.px, w.py, w.pz, TAC.PLAYER_R, w.PlayerHitH());
                if (tp >= 0.0)
                {
                    bu.alive = false;
                    w.HurtPlayer(1, 0.0, 0.0);
                    continue;
                }
            }
            bu.x = nx;
            bu.y = ny;
            bu.z = nz;
        }
        if ((w.tick % 100) == 0 && w.bullets.Count > 64)
        {
            var live = new List<TacBullet>();
            for (int k = 0; k < w.bullets.Count; k++) if (w.bullets[k].alive) live.Add(w.bullets[k]);
            w.bullets = live;
        }
    }

    public void StepGrenades()
    {
        var w = this;
        for (int i = 0; i < w.grenades.Count; i++)
        {
            var g = w.grenades[i];
            if (!g.alive) continue;
            double dv = TAC.GRAVITY * TAC.TICK;
            g.vy = g.vy - dv;
            double nx = g.x + g.vx * TAC.TICK;
            double ny = g.y + g.vy * TAC.TICK;
            double nz = g.z + g.vz * TAC.TICK;
            bool boom = false;
            double bx = nx, by = ny, bz = nz;
            if (nx < 0.0 || nx > w.arenaW || nz < 0.0 || nz > w.arenaD)
            {
                boom = true;
                bx = g.x; by = g.y; bz = g.z;
            }
            else if (w.SegBlocked(g.x, g.y, g.z, nx, ny, nz))
            {
                boom = true;
                bx = g.x; by = g.y; bz = g.z;
            }
            else
            {
                double gy = w.GroundY(nx, nz, g.y, TAC.GRENADE_R);
                if (ny <= gy + 0.05)
                {
                    boom = true;
                    by = gy + 0.3;
                }
                else
                {
                    for (int e = 0; e < w.enemies.Count; e++)
                    {
                        var en = w.enemies[e];
                        if (!en.alive) continue;
                        double te = TacMath.SegCylinder(g.x, g.y, g.z, nx, ny, nz, en.x, en.y, en.z, en.r + TAC.GRENADE_R, en.h);
                        if (te >= 0.0) { boom = true; break; }
                    }
                }
            }
            if (boom)
            {
                g.alive = false;
                w.ExplodeAt(bx, by, bz, TAC.GRENADE_BLAST_R, 2);
            }
            else
            {
                g.x = nx;
                g.y = ny;
                g.z = nz;
            }
        }
        if ((w.tick % 100) == 0 && w.grenades.Count > 16)
        {
            var live = new List<TacGrenade>();
            for (int k = 0; k < w.grenades.Count; k++) if (w.grenades[k].alive) live.Add(w.grenades[k]);
            w.grenades = live;
        }
    }

    public void IgniteBarrel(TacBarrel ba, double vx, double vz, int fuse)
    {
        var w = this;
        if (!ba.alive) return;
        if (ba.fuse < 0) ba.fuse = fuse;
        if (ba.state == 0)
        {
            double hl = Math.Sqrt(vx * vx + vz * vz);
            if (hl > 0.0001)
            {
                ba.dx = vx / hl;
                ba.dz = vz / hl;
                ba.state = 1;
            }
            w.events.barrelHit = true;
        }
    }

    public void StepBarrels()
    {
        var w = this;
        for (int i = 0; i < w.barrels.Count; i++)
        {
            var ba = w.barrels[i];
            if (!ba.alive) continue;
            if (ba.state == 1)
            {
                double stepd = TAC.BARREL_ROLL * TAC.TICK;
                double dx = ba.dx * stepd;
                double dz = ba.dz * stepd;
                var res = w.MoveCircle(ba.x, ba.z, ba.y, TAC.BARREL_R, TAC.BARREL_H, dx, dz);
                double movedX = res.x - ba.x;
                double movedZ = res.z - ba.z;
                ba.x = res.x;
                ba.z = res.z;
                double want2 = dx * dx + dz * dz;
                double got2 = movedX * movedX + movedZ * movedZ;
                if (got2 < want2 * 0.25) ba.state = 2;
                double g = w.GroundY(ba.x, ba.z, ba.y + 0.01, TAC.BARREL_R);
                if (g < ba.y - 0.01) ba.y = g;
            }
            if (ba.fuse >= 0)
            {
                ba.fuse--;
                if (ba.fuse <= 0) w.ExplodeBarrel(ba);
            }
        }
    }

    public void ExplodeAt(double cx, double cy, double cz, double radius, int playerDmg)
    {
        var w = this;
        if (w.events.explosions == null) w.events.explosions = new List<TacExplosion>();
        w.events.explosions.Add(new TacExplosion { x = cx, y = cy, z = cz, r = radius });
        w.AddNoise(cx, cz, TAC.NOISE_BLAST_R);
        double r2 = radius * radius;
        for (int e = 0; e < w.enemies.Count; e++)
        {
            var en = w.enemies[e];
            if (!en.alive) continue;
            double ex = en.x - cx;
            double ey = en.y + en.h * 0.5 - cy;
            double ez = en.z - cz;
            double d2 = ex * ex + ey * ey + ez * ez;
            if (d2 <= r2)
            {
                if (en.type == 6)
                {
                    en.shieldStagT = TAC.SHIELD_STAGGER;
                    w.DamageEnemy(en, TAC.SHIELD_BLAST_DMG);
                }
                else
                {
                    w.DamageEnemy(en, TAC.BLAST_DMG);
                }
            }
        }
        double px2 = w.px - cx;
        double pyy = w.py + TAC.CHEST_H - cy;
        double pz2 = w.pz - cz;
        double pd2 = px2 * px2 + pyy * pyy + pz2 * pz2;
        if (pd2 <= r2) w.HurtPlayer(playerDmg, 0.0, 0.0);
        for (int b = 0; b < w.barrels.Count; b++)
        {
            var o = w.barrels[b];
            if (!o.alive) continue;
            double ox = o.x - cx;
            double oy = o.y + 0.5 - cy;
            double oz = o.z - cz;
            double od2 = ox * ox + oy * oy + oz * oz;
            if (od2 <= r2 && o.fuse < 0) w.IgniteBarrel(o, ox, oz, TAC.BARREL_CHAIN_FUSE);
        }
        for (int cw = 0; cw < w.boxes.Count; cw++)
        {
            var cbx = w.boxes[cw];
            if (cbx.kind != 3) continue;
            if (!cbx.alive) continue;
            double qx = cx < cbx.x0 ? cbx.x0 : (cx > cbx.x1 ? cbx.x1 : cx);
            double qy = cy < 0.0 ? 0.0 : (cy > cbx.h ? cbx.h : cy);
            double qz = cz < cbx.z0 ? cbx.z0 : (cz > cbx.z1 ? cbx.z1 : cz);
            double wx = cx - qx;
            double wy = cy - qy;
            double wz = cz - qz;
            if (wx * wx + wy * wy + wz * wz <= r2) w.BreakWall(cw);
        }
        for (int m = 0; m < w.mines.Count; m++)
        {
            var mi = w.mines[m];
            if (!mi.alive) continue;
            double mx = mi.x - cx;
            double my = mi.y - cy;
            double mz = mi.z - cz;
            double md2 = mx * mx + my * my + mz * mz;
            if (md2 <= r2 && (mi.fuse < 0 || mi.fuse > TAC.MINE_SHOT_FUSE)) mi.fuse = TAC.MINE_SHOT_FUSE;
        }
        for (int sw4 = 0; sw4 < w.switches.Count; sw4++)
        {
            var swx = w.switches[sw4];
            if (!swx.alive) continue;
            double swdx = swx.x - cx;
            double swdz = swx.z - cz;
            if (swdx * swdx + swdz * swdz <= r2) w.KillSwitch(swx);
        }
        for (int sl4 = 0; sl4 < w.slides.Count; sl4++)
        {
            var slx = w.slides[sl4];
            if (slx.triggered) continue;
            double sldx = slx.postX - cx;
            double sldz = slx.postZ - cz;
            if (sldx * sldx + sldz * sldz <= r2) w.TriggerSlide(slx);
        }
    }

    public void ExplodeBarrel(TacBarrel ba)
    {
        var w = this;
        if (!ba.alive) return;
        ba.alive = false;
        w.ExplodeAt(ba.x, ba.y + 0.5, ba.z, TAC.BLAST_R, TAC.BLAST_PLAYER_DMG);
    }

    public void KillSwitch(TacSwitch sw)
    {
        var w = this;
        if (!sw.alive) return;
        sw.alive = false;
        w.events.switchDown = true;
    }

    public void TriggerSlide(TacSlide sld)
    {
        var w = this;
        if (sld.triggered) return;
        sld.triggered = true;
        w.events.slideStart = true;
        w.AddNoise(sld.pileX, sld.pileZ, TAC.NOISE_BLAST_R);
        double perpX = sld.dz;
        double perpZ = sld.dx;
        int n = (int)Math.Floor(sld.w / 1.4);
        if (n < 3) n = 3;
        if (n > 8) n = 8;
        for (int i = 0; i < n; i++)
        {
            double frac = n == 1 ? 0.0 : ((double)i / (n - 1)) - 0.5;
            double off = frac * (sld.w - 1.2);
            double bx = sld.pileX + perpX * off;
            double bz = sld.pileZ + perpZ * off;
            double by = w.GroundY(bx, bz, 1000.0, 0.6);
            sld.boulders.Add(new TacBoulder { x = bx, y = by, z = bz, traveled = 0.0, alive = true });
        }
    }

    public void StepSlides()
    {
        var w = this;
        for (int i = 0; i < w.slides.Count; i++)
        {
            var sld = w.slides[i];
            if (!sld.triggered) continue;
            for (int b = 0; b < sld.boulders.Count; b++)
            {
                var bo = sld.boulders[b];
                if (!bo.alive) continue;
                double stepd = TAC.SLIDE_SPEED * TAC.TICK;
                var res = w.MoveCircle(bo.x, bo.z, bo.y, 0.6, 1.2, sld.dx * stepd, sld.dz * stepd);
                double got = (res.x - bo.x) * sld.dx + (res.z - bo.z) * sld.dz;
                bo.x = res.x;
                bo.z = res.z;
                bo.traveled += stepd;
                double g = w.GroundY(bo.x, bo.z, bo.y + 0.01, 0.6);
                if (g < bo.y - 0.01) bo.y = bo.y - 8.0 * TAC.TICK;
                if (bo.y < g) bo.y = g;
                if (got < stepd * 0.2 || bo.traveled >= TAC.SLIDE_LEN) { bo.alive = false; continue; }
                double r2 = TAC.SLIDE_CRUSH_R * TAC.SLIDE_CRUSH_R;
                for (int e = 0; e < w.enemies.Count; e++)
                {
                    var en = w.enemies[e];
                    if (!en.alive || en.type == 3) continue;
                    double ex = en.x - bo.x;
                    double ez = en.z - bo.z;
                    double eyd = en.y - bo.y;
                    if (eyd > -1.0 && eyd < 1.6 && ex * ex + ez * ez <= r2)
                    {
                        w.DamageEnemy(en, TAC.BLAST_DMG);
                        w.events.crushed++;
                    }
                }
                double pxd = w.px - bo.x;
                double pzd = w.pz - bo.z;
                double pyd = w.py - bo.y;
                if (pyd > -1.0 && pyd < 1.6 && pxd * pxd + pzd * pzd <= r2) w.HurtPlayer(2, 0.0, 0.0);
            }
        }
    }

    public void StepMines()
    {
        var w = this;
        for (int i = 0; i < w.mines.Count; i++)
        {
            var mi = w.mines[i];
            if (!mi.alive) continue;
            if (mi.fuse < 0)
            {
                double tr2 = TAC.MINE_TRIGGER_R * TAC.MINE_TRIGGER_R;
                double pdx = w.px - mi.x;
                double pdz = w.pz - mi.z;
                double pdy = w.py - mi.y;
                if (pdy > -0.6 && pdy < 0.6 && pdx * pdx + pdz * pdz <= tr2)
                {
                    mi.fuse = TAC.MINE_FUSE;
                    w.events.mineArmed = true;
                }
                else
                {
                    for (int e = 0; e < w.enemies.Count; e++)
                    {
                        var en = w.enemies[e];
                        if (!en.alive || en.type == 1 || en.type == 2) continue;
                        double edy = en.y - mi.y;
                        if (edy < -0.6 || edy > 0.6) continue;
                        double edx = en.x - mi.x;
                        double edz = en.z - mi.z;
                        if (edx * edx + edz * edz <= tr2)
                        {
                            mi.fuse = TAC.MINE_FUSE;
                            w.events.mineArmed = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                mi.fuse--;
                if (mi.fuse <= 0)
                {
                    mi.alive = false;
                    w.ExplodeAt(mi.x, mi.y + 0.1, mi.z, TAC.MINE_BLAST_R, 2);
                }
            }
        }
    }

    public double PlayerChestY()
    {
        return py + (crouched ? TAC.CROUCH_CHEST : TAC.CHEST_H);
    }
    public double PlayerHitH()
    {
        return crouched ? TAC.CROUCH_H : TAC.PLAYER_H;
    }
    public static double TacEnemyH(TacEnemy en)
    {
        return en.crouched ? TAC.CROUCH_H : en.h;
    }

    public double PlayerVisibleFrom(TacEnemy en, double range2, double cos2)
    {
        var w = this;
        double dx = w.px - en.x;
        double dz = w.pz - en.z;
        double d2 = dx * dx + dz * dz;
        if (d2 > range2) return -1.0;
        if (d2 < 0.0001) return 0.0;
        double fx = TacMath.SinQ(en.yawQ);
        double fz = TacMath.CosQ(en.yawQ);
        double dot = fx * dx + fz * dz;
        if (dot <= 0.0) return -1.0;
        double dd = dot * dot;
        double lim = cos2 * d2;
        if (dd < lim) return -1.0;
        double eyeY = en.y + TacEnemyH(en) - 0.2;
        double chestY = w.PlayerChestY();
        if (w.SegBlocked(en.x, eyeY, en.z, w.px, chestY, w.pz)) return -1.0;
        return d2;
    }

    public bool MoveEnemy(TacEnemy en, double txx, double tzz, double speed, bool fly = false)
    {
        var w = this;
        double dx = txx - en.x;
        double dz = tzz - en.z;
        double d2 = dx * dx + dz * dz;
        if (d2 < 0.01) return true;
        double d = Math.Sqrt(d2);
        double stepd = speed * TAC.TICK;
        if (stepd > d) stepd = d;
        double mx = (dx / d) * stepd;
        double mz = (dz / d) * stepd;
        var res = w.MoveCircle(en.x, en.z, en.y, en.r, en.h, mx, mz);
        if (!fly && en.y < 0.5 && w.InRiver(res.x, res.z) && !w.InRiver(en.x, en.z))
        {
            en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(dx, dz), TAC.ENEMY_TURN_RATE);
            return false;
        }
        en.x = res.x;
        en.z = res.z;
        if (!fly)
        {
            double g = w.GroundY(en.x, en.z, en.y + 0.01, en.r);
            en.y = g;
        }
        en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(dx, dz), TAC.ENEMY_TURN_RATE);
        return d < 0.35;
    }

    public void StepEnemies()
    {
        var w = this;
        double range2 = TAC.VISION_RANGE * TAC.VISION_RANGE;
        double snipRange2 = TAC.SNIPER_RANGE * TAC.SNIPER_RANGE;
        double gatRange2 = TAC.GATLING_VISION * TAC.GATLING_VISION;

        for (int i = 0; i < w.enemies.Count; i++)
        {
            var en = w.enemies[i];
            if (!en.alive) continue;
            if (en.attackCd > 0) en.attackCd--;

            if (en.state < 2)
            {
                for (int n = 0; n < w.noises.Count; n++)
                {
                    var no = w.noises[n];
                    double ndx = no.x - en.x;
                    double ndz = no.z - en.z;
                    double nd2 = ndx * ndx + ndz * ndz;
                    if (nd2 <= no.r * no.r)
                    {
                        en.state = 1;
                        en.tx = no.x;
                        en.tz = no.z;
                        en.pauseT = 0;
                        w.events.heard = true;
                        // entrench doctrine: gunfire sends him sprinting for the nearest
                        // trench, not toward the sound
                        if (en.entrench && en.type == 0 && !en.dugIn && w.trenches.Count > 0)
                        {
                            double bd2 = 1.0e30;
                            for (int tr = 0; tr < w.trenches.Count; tr++)
                            {
                                var trc = w.trenches[tr];
                                double cxp = en.x < trc.x0 + 0.7 ? trc.x0 + 0.7 : (en.x > trc.x1 - 0.7 ? trc.x1 - 0.7 : en.x);
                                double czp = en.z < trc.z0 + 0.7 ? trc.z0 + 0.7 : (en.z > trc.z1 - 0.7 ? trc.z1 - 0.7 : en.z);
                                double cdx = cxp - en.x;
                                double cdz = czp - en.z;
                                double cd2 = cdx * cdx + cdz * cdz;
                                if (cd2 < bd2) { bd2 = cd2; en.tx = cxp; en.tz = czp; }
                            }
                            en.seekCover = true;
                        }
                    }
                }
            }

            if (w.squad && en.state < 2 && en.type != 3) w.SquadCorpseCheck(en);

            if (en.shieldStagT > 0) en.shieldStagT--;

            if (en.type == 0) w.StepSoldier(en, range2);
            else if (en.type == 1) w.StepGatling(en, gatRange2);
            else if (en.type == 2) w.StepSniper(en, snipRange2);
            else if (en.type == 3) w.StepDrone(en, range2);
            else if (en.type == 5) w.StepBomber(en, range2);
            else if (en.type == 6) w.StepShield(en, range2);
            else w.StepOperator(en, range2);
        }
    }

    public void VisionGauge(TacEnemy en, double range2, double cos2, double sneakMul)
    {
        var w = this;
        double d2 = w.PlayerVisibleFrom(en, range2, cos2);
        en.seesPlayer = d2 >= 0.0;
        if (d2 >= 0.0)
        {
            double d = Math.Sqrt(d2);
            double range = Math.Sqrt(range2);
            double near = 1.0 - d / range;
            double fill = 2.0 + 9.0 * near;
            fill = fill * sneakMul;
            en.gauge += fill;
            if (en.gauge >= TAC.GAUGE_MAX)
            {
                en.gauge = TAC.GAUGE_MAX;
                if (en.state != 2) w.events.spotted = true;
                w.AlertEnemy(en, w.px, w.pz);
            }
            else if (en.gauge >= TAC.SUSPICIOUS_AT && en.state == 0)
            {
                en.state = 1;
                en.tx = w.px;
                en.tz = w.pz;
                en.pauseT = 0;
            }
            if (en.state == 2) { en.tx = w.px; en.tz = w.pz; }
        }
        else
        {
            en.gauge -= TAC.GAUGE_DECAY;
            if (en.gauge < 0.0) en.gauge = 0.0;
        }
    }

    public void FireEnemyBullet(TacEnemy en)
    {
        var w = this;
        double dx = w.px - en.x;
        double dz = w.pz - en.z;
        double d = Math.Sqrt(dx * dx + dz * dz);
        if (d < 0.2) return;
        double ux = dx / d;
        double uz = dz / d;
        double muzY = en.y + en.h - 0.5;
        double spd = en.type == 2 ? TAC.SNIPER_BULLET_SPEED : TAC.ENEMY_BULLET_SPEED;
        double t = d / spd;
        double wantY = w.PlayerChestY();
        double vy = (wantY - muzY) / t;
        double vmax = spd * 0.5;
        if (vy > vmax) vy = vmax;
        if (vy < -vmax) vy = -vmax;
        w.bullets.Add(new TacBullet
        {
            x = en.x + ux * 0.5, y = muzY, z = en.z + uz * 0.5,
            sx = en.x, sy = muzY, sz = en.z,
            vx = ux * spd, vy = vy, vz = uz * spd,
            ttl = TAC.ENEMY_BULLET_TTL, fromPlayer = false, alive = true
        });
        w.events.rifleShot = true;
        if (w.events.eshots == null) w.events.eshots = new List<TacXZ>();
        w.events.eshots.Add(new TacXZ { x = en.x, z = en.z });
    }

    public void FireSuppressive(TacEnemy en, int aimQ, double dist)
    {
        var w = this;
        double ux = TacMath.SinQ(aimQ);
        double uz = TacMath.CosQ(aimQ);
        double muzY = en.y + TacEnemyH(en) - 0.5;
        double t = dist / TAC.ENEMY_BULLET_SPEED;
        double vy = (1.1 - muzY) / t;
        double vmax = TAC.ENEMY_BULLET_SPEED * 0.35;
        if (vy > vmax) vy = vmax;
        if (vy < -vmax) vy = -vmax;
        w.bullets.Add(new TacBullet
        {
            x = en.x + ux * 0.5, y = muzY, z = en.z + uz * 0.5,
            sx = en.x, sy = muzY, sz = en.z,
            vx = ux * TAC.ENEMY_BULLET_SPEED, vy = vy, vz = uz * TAC.ENEMY_BULLET_SPEED,
            ttl = TAC.ENEMY_BULLET_TTL, fromPlayer = false, alive = true
        });
        w.events.rifleShot = true;
        if (w.events.eshots == null) w.events.eshots = new List<TacXZ>();
        w.events.eshots.Add(new TacXZ { x = en.x, z = en.z });
    }

    public void StepSoldier(TacEnemy en, double range2)
    {
        var w = this;
        if (en.fireFlash > 0) en.fireFlash--;
        bool inTrench = w.TrenchAt(en.x, en.z) >= 0;
        if (en.entrench && !en.dugIn && inTrench) { en.dugIn = true; en.seekCover = false; }
        bool peek = true;
        if (inTrench)
        {
            if (en.state == 2) peek = (w.tick % 90) < 70;
            else peek = (w.tick % 130) < 35;
        }
        en.crouched = inTrench && !peek && en.aimT == 0 && en.fireFlash == 0;
        en.holdTrench = inTrench;
        w.VisionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);
        if (en.rifleCd > 0) en.rifleCd--;

        if (en.state == 2)
        {
            double dx = w.px - en.x;
            double dz = w.pz - en.z;
            double d2 = dx * dx + dz * dz;
            double engage = TAC.RIFLE_RANGE * TAC.RIFLE_RANGE;
            if (en.seesPlayer && d2 <= engage)
            {
                en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(dx, dz), TAC.ENEMY_TURN_RATE);
                if (en.rifleCd == 0 && !en.crouched)
                {
                    en.aimT++;
                    if (en.aimT >= TAC.RIFLE_AIM)
                    {
                        w.FireEnemyBullet(en);
                        en.rifleCd = TAC.RIFLE_CD;
                        en.aimT = 0;
                        en.fireFlash = 50;
                    }
                }
            }
            else if (en.holdTrench)
            {
                en.aimT = 0;
                double spx = en.tx - en.x;
                double spz = en.tz - en.z;
                double spd2 = spx * spx + spz * spz;
                if (!en.crouched && en.rifleCd == 0 && spd2 > 9.0 && spd2 <= engage)
                {
                    en.suppressT = en.suppressT + 1;
                    int wob = ((w.tick * 13 + en.suppressT * 29) % 9) - 4;
                    int sprayQ = (TacMath.YawFor(spx, spz) + wob * 700) & 65535;
                    en.yawQ = TacMath.TurnToward(en.yawQ, sprayQ, TAC.ENEMY_TURN_RATE);
                    w.FireSuppressive(en, sprayQ, Math.Sqrt(spd2));
                    en.rifleCd = TAC.RIFLE_SUPPRESS_CD;
                    en.fireFlash = 50;
                }
                else
                {
                    en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(spx, spz), TAC.ENEMY_TURN_RATE);
                }
            }
            else if (w.TrenchAt(en.tx, en.tz) >= 0 && w.TrenchAt(en.x, en.z) < 0 &&
                     dx * dx + dz * dz < TAC.TRENCH_STANDOFF * TAC.TRENCH_STANDOFF)
            {
                en.aimT = 0;
                en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(dx, dz), TAC.ENEMY_TURN_RATE);
            }
            else
            {
                en.aimT = 0;
                double fx2 = en.tx;
                double fz2 = en.tz;
                if (w.squad && (en.idx & 1) == 1)
                {
                    // odd-numbered soldiers are the flankers: while the pinning fire keeps
                    // the player busy, they swing wide and come in from the side
                    double fdx = en.x - en.tx;
                    double fdz = en.z - en.tz;
                    if (fdx * fdx + fdz * fdz > TAC.SQUAD_FLANK_R * TAC.SQUAD_FLANK_R)
                    {
                        int dirQ2 = TacMath.YawFor(fdx, fdz);
                        int side = (en.idx & 2) == 0 ? 16384 : 49152;
                        int fq2 = (dirQ2 + side) & 65535;
                        fx2 = en.tx + TacMath.SinQ(fq2) * TAC.SQUAD_FLANK_R;
                        fz2 = en.tz + TacMath.CosQ(fq2) * TAC.SQUAD_FLANK_R;
                        if (fx2 < 0.8) fx2 = 0.8;
                        if (fx2 > w.arenaW - 0.8) fx2 = w.arenaW - 0.8;
                        if (fz2 < 0.8) fz2 = 0.8;
                        if (fz2 > w.arenaD - 0.8) fz2 = w.arenaD - 0.8;
                    }
                }
                w.MoveEnemy(en, fx2, fz2, TAC.SOLDIER_CHASE_SPEED);
                if (!en.seesPlayer)
                {
                    double ldx = en.tx - en.x;
                    double ldz = en.tz - en.z;
                    if (ldx * ldx + ldz * ldz < 0.25)
                    {
                        en.state = 1;
                        en.pauseT = 0;
                    }
                }
            }
        }
        else if (en.state == 1)
        {
            bool arrived;
            if (en.dugIn)
            {
                arrived = true; // dug-in: scan from the trench, never leave it
            }
            else if (en.seekCover)
            {
                arrived = w.MoveEnemy(en, en.tx, en.tz, TAC.SOLDIER_CHASE_SPEED);
            }
            else if (w.TrenchAt(en.tx, en.tz) >= 0 && w.TrenchAt(en.x, en.z) < 0)
            {
                double idx2 = en.tx - en.x;
                double idz2 = en.tz - en.z;
                if (idx2 * idx2 + idz2 * idz2 < TAC.TRENCH_PROBE * TAC.TRENCH_PROBE)
                {
                    en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(idx2, idz2), TAC.ENEMY_TURN_RATE);
                    arrived = true;
                }
                else
                {
                    arrived = w.MoveEnemy(en, en.tx, en.tz, TAC.SOLDIER_INVESTIGATE_SPEED);
                }
            }
            else
            {
                arrived = w.MoveEnemy(en, en.tx, en.tz, TAC.SOLDIER_INVESTIGATE_SPEED);
            }
            if (arrived)
            {
                en.pauseT++;
                en.yawQ = (en.yawQ + 300) & 65535;
                if (en.pauseT >= TAC.INVESTIGATE_PAUSE)
                {
                    en.state = 0;
                    en.gauge = 0.0;
                    en.pauseT = 0;
                }
            }
        }
        else
        {
            if (en.hasPatrol && !en.dugIn)
            {
                double gx = en.patToB ? en.patX : en.homeX;
                double gz = en.patToB ? en.patZ : en.homeZ;
                bool got = w.MoveEnemy(en, gx, gz, TAC.SOLDIER_PATROL_SPEED);
                if (got) en.patToB = !en.patToB;
            }
            else
            {
                en.yawQ = TacMath.TurnToward(en.yawQ, en.baseYawQ, 80);
            }
        }
    }

    public void StepGatling(TacEnemy en, double range2)
    {
        var w = this;
        w.VisionGauge(en, range2, TAC.VISION_COS2, 1.0);

        int aimQ;
        if (en.state == 2)
        {
            double dx = w.px - en.x;
            double dz = w.pz - en.z;
            en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(dx, dz), TAC.GATLING_ALERT_TURN);
            aimQ = en.yawQ;
            if (!en.seesPlayer)
            {
                en.gauge -= TAC.GAUGE_DECAY;
                if (en.gauge <= 0.0) { en.state = 0; en.gauge = 0.0; }
            }
        }
        else
        {
            int cyc = en.cycleT % 200;
            int ph = cyc < 100 ? cyc : 200 - cyc;
            int off = (ph * TAC.GATLING_SWEEP * 2) / 100 - TAC.GATLING_SWEEP;
            aimQ = (en.baseYawQ + off) & 65535;
            en.yawQ = aimQ;
        }
        en.cycleT++;

        int fireCyc = en.cycleT % (TAC.GATLING_SPRAY + TAC.GATLING_RELOAD);
        if (fireCyc < TAC.GATLING_SPRAY && (fireCyc % TAC.GATLING_SHOT_EVERY) == 0)
        {
            double s = TacMath.SinQ(aimQ);
            double c = TacMath.CosQ(aimQ);
            double muzY = en.y + en.h - 0.5;
            double vy = 0.0;
            if (en.state == 2 || en.seesPlayer)
            {
                double tdx = w.px - en.x;
                double tdz = w.pz - en.z;
                double td = Math.Sqrt(tdx * tdx + tdz * tdz);
                if (td > 1.0)
                {
                    double wantY = w.PlayerChestY();
                    vy = (wantY - muzY) / (td / TAC.ENEMY_BULLET_SPEED);
                    double vmax = TAC.ENEMY_BULLET_SPEED * 0.5;
                    if (vy > vmax) vy = vmax;
                    if (vy < -vmax) vy = -vmax;
                }
            }
            w.bullets.Add(new TacBullet
            {
                x = en.x + s * 0.7, y = muzY, z = en.z + c * 0.7,
                sx = en.x, sy = muzY, sz = en.z,
                vx = s * TAC.ENEMY_BULLET_SPEED, vy = vy, vz = c * TAC.ENEMY_BULLET_SPEED,
                ttl = TAC.ENEMY_BULLET_TTL, fromPlayer = false, alive = true, gat = (en.state == 2 || en.seesPlayer)
            });
            w.events.gatlingShot = true;
            if (w.events.eshots == null) w.events.eshots = new List<TacXZ>();
            w.events.eshots.Add(new TacXZ { x = en.x, z = en.z, gat = true });
        }
    }

    public void StepBomber(TacEnemy en, double range2)
    {
        var w = this;
        w.VisionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);
        if (en.bombCd > 0) en.bombCd--;
        if (en.state == 2)
        {
            double dx = w.px - en.x;
            double dz = w.pz - en.z;
            double d2 = dx * dx + dz * dz;
            double tx = en.seesPlayer ? w.px : en.tx;
            double tz = en.seesPlayer ? w.pz : en.tz;
            double tdx = tx - en.x;
            double tdz = tz - en.z;
            double td2 = tdx * tdx + tdz * tdz;
            en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(tdx, tdz), TAC.ENEMY_TURN_RATE);
            if (en.bombCd == 0 && td2 >= TAC.BOMBER_MIN * TAC.BOMBER_MIN && td2 <= TAC.BOMBER_RANGE * TAC.BOMBER_RANGE)
            {
                w.ThrowBomb(en, tx, tz);
                en.bombCd = TAC.BOMBER_CD;
            }
            else if (td2 > TAC.BOMBER_RANGE * TAC.BOMBER_RANGE)
            {
                w.MoveEnemy(en, tx, tz, TAC.SOLDIER_CHASE_SPEED);
            }
            else if (en.seesPlayer && d2 < TAC.BOMBER_MIN * TAC.BOMBER_MIN)
            {
                w.MoveEnemy(en, en.x - dx, en.z - dz, TAC.SOLDIER_CHASE_SPEED);
            }
            if (!en.seesPlayer)
            {
                double ldx = en.tx - en.x;
                double ldz = en.tz - en.z;
                if (ldx * ldx + ldz * ldz < 0.25)
                {
                    en.state = 1;
                    en.pauseT = 0;
                }
            }
        }
        else if (en.state == 1)
        {
            bool arrived = w.MoveEnemy(en, en.tx, en.tz, TAC.SOLDIER_INVESTIGATE_SPEED);
            if (arrived)
            {
                en.pauseT++;
                en.yawQ = (en.yawQ + 300) & 65535;
                if (en.pauseT >= TAC.INVESTIGATE_PAUSE)
                {
                    en.state = 0;
                    en.gauge = 0.0;
                    en.pauseT = 0;
                }
            }
        }
        else
        {
            if (en.hasPatrol)
            {
                double gx = en.patToB ? en.patX : en.homeX;
                double gz = en.patToB ? en.patZ : en.homeZ;
                bool got = w.MoveEnemy(en, gx, gz, TAC.SOLDIER_INVESTIGATE_SPEED);
                if (got) en.patToB = !en.patToB;
            }
            else
            {
                en.yawQ = TacMath.TurnToward(en.yawQ, en.baseYawQ, 80);
            }
        }
    }

    public void ThrowBomb(TacEnemy en, double tx, double tz)
    {
        var w = this;
        double ty = w.GroundY(tx, tz, 1000.0, 0.35);
        w.bombs.Add(new TacBomb { sx = en.x, sy = en.y + 1.4, sz = en.z, x = tx, y = ty, z = tz, t = 0, fuse = TAC.BOMB_FUSE, state = 0 });
        w.events.bomberThrow = new TacXZ { x = en.x, z = en.z };
    }

    public void StepBombs()
    {
        var w = this;
        for (int i = 0; i < w.bombs.Count; i++)
        {
            var bo = w.bombs[i];
            if (bo.state == 2) continue;
            if (bo.state == 0)
            {
                bo.t++;
                if (bo.t >= TAC.BOMB_FLIGHT)
                {
                    bo.state = 1;
                    w.events.bombLand = new TacXZ { x = bo.x, z = bo.z };
                }
            }
            else
            {
                bo.fuse--;
                if (bo.fuse <= 0)
                {
                    bo.state = 2;
                    w.ExplodeAt(bo.x, bo.y + 0.2, bo.z, TAC.BOMB_BLAST_R, 2);
                }
            }
        }
    }

    public void StepLights()
    {
        var w = this;
        for (int i = 0; i < w.lights.Count; i++)
        {
            var li = w.lights[i];
            li.angQ = (li.angQ + li.speed) & 65535;
        }
        if (!w.night) { w.playerLit = true; return; }
        w.playerLit = false;
        for (int l = 0; l < w.lamps.Count; l++)
        {
            var la = w.lamps[l];
            double ldx = w.px - la.x;
            double ldz = w.pz - la.z;
            if (ldx * ldx + ldz * ldz <= la.r * la.r) { w.playerLit = true; return; }
        }
        for (int s2 = 0; s2 < w.lights.Count; s2++)
        {
            var sl = w.lights[s2];
            double sdx = w.px - sl.x;
            double sdz = w.pz - sl.z;
            double sd2 = sdx * sdx + sdz * sdz;
            if (sd2 > sl.r * sl.r || sd2 < 0.04) continue;
            int toQ = TacMath.YawFor(sdx, sdz);
            int diff = (toQ - sl.angQ) & 65535;
            if (diff > 32768) diff = 65536 - diff;
            if (diff > TAC.SEARCH_HALF) continue;
            if (w.SegBlocked(sl.x, 3.0, sl.z, w.px, w.PlayerChestY(), w.pz)) continue;
            w.playerLit = true;
            return;
        }
    }

    public void StepIntel()
    {
        var w = this;
        if (w.goalType != 1) return;
        for (int i = 0; i < w.intels.Count; i++)
        {
            var it = w.intels[i];
            if (!it.alive) continue;
            double dx = w.px - it.x;
            double dz = w.pz - it.z;
            double dy = w.py - it.y;
            if (dy > -0.6 && dy < 1.2 && dx * dx + dz * dz <= 0.81)
            {
                it.alive = false;
                w.intelLeft--;
                w.events.intelPick = new TacIntelPick { left = w.intelLeft };
                if (w.intelLeft <= 0) w.events.intelAll = true;
            }
        }
    }

    public void StepMedkits()
    {
        var w = this;
        if (w.hp >= w.maxHp) return;
        for (int i = 0; i < w.medkits.Count; i++)
        {
            var mk = w.medkits[i];
            if (!mk.alive) continue;
            double dx = w.px - mk.x;
            double dz = w.pz - mk.z;
            double dy = w.py - mk.y;
            if (dy > -0.5 && dy < 1.0 && dx * dx + dz * dz <= 0.81)
            {
                mk.alive = false;
                w.hp++;
                w.events.medkit = true;
            }
        }
    }

    public void FryDrone(TacEnemy en)
    {
        var w = this;
        en.alive = false;
        w.enemiesLeft--;
        if (w.events.kills == null) w.events.kills = new List<TacKill>();
        w.events.kills.Add(new TacKill { x = en.x, y = en.y, z = en.z, type = en.type });
        w.events.jamZap = new TacXYZ { x = en.x, y = en.y, z = en.z };
        w.ExplodeAt(en.x, en.y + 0.25, en.z, TAC.DRONE_BLAST_R, 2);
    }

    public void StepDrone(TacEnemy en, double range2)
    {
        var w = this;
        if (w.squad && en.seesPlayer && en.alive && !en.crashing) w.SquadIlluminate(en);
        // enemy drones are UNAFFECTED by EMP veils (veil only denies the
        // player's captured drone). They fly through freely.

        if (!en.crashing && en.group > 0 && w.opGroups.ContainsKey(en.group))
        {
            var ops = w.opGroups[en.group];
            bool anyAlive = false;
            for (int oi = 0; oi < ops.Count; oi++)
            {
                if (w.enemies[ops[oi]].alive) { anyAlive = true; break; }
            }
            if (!anyAlive)
            {
                en.crashing = true;
                w.events.droneCrash = true;
            }
        }
        if (en.crashing)
        {
            double fallStep = TAC.DRONE_CRASH_SPEED * TAC.TICK;
            en.y = en.y - fallStep;
            double gy = w.GroundY(en.x, en.z, en.y, en.r);
            if (en.y <= gy + 0.15)
            {
                en.alive = false;
                w.enemiesLeft--;
                if (w.events.kills == null) w.events.kills = new List<TacKill>();
                w.events.kills.Add(new TacKill { x = en.x, y = en.y, z = en.z, type = en.type });
                w.ExplodeAt(en.x, en.y + 0.25, en.z, TAC.DRONE_BLAST_R, 2);
            }
            return;
        }
        w.VisionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);

        if (en.state == 2)
        {
            double dx = w.px - en.x;
            double dz = w.pz - en.z;
            double dh2 = dx * dx + dz * dz;
            if (!en.diving && dh2 <= TAC.DRONE_DIVE_AT * TAC.DRONE_DIVE_AT)
            {
                en.diving = true;
                w.events.droneDive = true;
            }
            if (en.diving)
            {
                w.MoveEnemy(en, w.px, w.pz, TAC.DRONE_CHASE_SPEED, true);
                double targetY = w.py + TAC.CHEST_H * 0.5;
                double fall = TAC.DRONE_DIVE_SPEED * TAC.TICK;
                if (en.y > targetY + fall) en.y = en.y - fall;
                else if (en.y < targetY - fall) en.y = en.y + fall;
                else en.y = targetY;
                double pcy = w.py + w.PlayerHitH() * 0.5;
                double dy = en.y - pcy;
                double d3 = dh2 + dy * dy;
                if (d3 <= TAC.DRONE_BOOM_AT * TAC.DRONE_BOOM_AT)
                {
                    en.alive = false;
                    w.enemiesLeft--;
                    if (w.events.kills == null) w.events.kills = new List<TacKill>();
                    w.events.kills.Add(new TacKill { x = en.x, y = en.y, z = en.z, type = en.type });
                    w.ExplodeAt(en.x, en.y + 0.25, en.z, TAC.DRONE_BLAST_R, 2);
                }
            }
            else
            {
                w.MoveEnemy(en, en.tx, en.tz, TAC.DRONE_CHASE_SPEED, true);
                if (!en.seesPlayer)
                {
                    double ldx = en.tx - en.x;
                    double ldz = en.tz - en.z;
                    if (ldx * ldx + ldz * ldz < 0.25)
                    {
                        en.state = 1;
                        en.pauseT = 0;
                    }
                }
            }
        }
        else if (en.state == 1)
        {
            bool arrived = w.MoveEnemy(en, en.tx, en.tz, TAC.DRONE_PATROL_SPEED, true);
            if (arrived)
            {
                en.pauseT++;
                en.yawQ = (en.yawQ + 300) & 65535;
                if (en.pauseT >= TAC.INVESTIGATE_PAUSE)
                {
                    en.state = 0;
                    en.gauge = 0.0;
                    en.pauseT = 0;
                }
            }
        }
        else
        {
            if (en.hasPatrol)
            {
                double gx = en.patToB ? en.patX : en.homeX;
                double gz = en.patToB ? en.patZ : en.homeZ;
                bool got = w.MoveEnemy(en, gx, gz, TAC.DRONE_PATROL_SPEED, true);
                if (got) en.patToB = !en.patToB;
            }
            else
            {
                en.yawQ = TacMath.TurnToward(en.yawQ, en.baseYawQ, 80);
            }
            if (en.y < en.homeY)
            {
                double rise = 2.0 * TAC.TICK;
                en.y = en.y + rise;
                if (en.y > en.homeY) en.y = en.homeY;
            }
        }
    }

    public void StepOperator(TacEnemy en, double range2)
    {
        var w = this;
        w.VisionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);

        if (en.state == 2)
        {
            double dx = en.x - w.px;
            double dz = en.z - w.pz;
            double d2 = dx * dx + dz * dz;
            if (d2 > 0.01)
            {
                double d = Math.Sqrt(d2);
                double fx = en.x + (dx / d) * 4.0;
                double fz = en.z + (dz / d) * 4.0;
                w.MoveEnemy(en, fx, fz, TAC.OPERATOR_FLEE_SPEED);
            }
            if (!en.seesPlayer)
            {
                en.gauge -= TAC.GAUGE_DECAY;
                if (en.gauge <= 0.0)
                {
                    en.gauge = 0.0;
                    en.state = 1;
                    en.pauseT = 0;
                }
            }
        }
        else if (en.state == 1)
        {
            en.pauseT++;
            en.yawQ = (en.yawQ + 300) & 65535;
            if (en.pauseT >= TAC.INVESTIGATE_PAUSE)
            {
                en.state = 0;
                en.gauge = 0.0;
                en.pauseT = 0;
            }
        }
        else
        {
            en.yawQ = TacMath.TurnToward(en.yawQ, en.baseYawQ, 80);
        }
    }

    public void StepSniper(TacEnemy en, double range2)
    {
        var w = this;
        if (en.attackCd > 0)
        {
            en.warnT = 0;
            en.seesPlayer = false;
            return;
        }
        double d2 = w.PlayerVisibleFrom(en, range2, TAC.SNIPER_COS2);
        en.seesPlayer = d2 >= 0.0;
        if (!en.seesPlayer)
        {
            // no target: sweep the scope across the arc instead of staring down one lane
            int cyc = en.cycleT % 600;
            int ph = cyc < 300 ? cyc : 600 - cyc;
            int off = (ph * TAC.SNIPER_SWEEP * 2) / 300 - TAC.SNIPER_SWEEP;
            en.yawQ = TacMath.TurnToward(en.yawQ, (en.baseYawQ + off) & 65535, TAC.SNIPER_SCAN_TURN);
            en.cycleT++;
        }
        if (en.seesPlayer)
        {
            en.yawQ = TacMath.TurnToward(en.yawQ, TacMath.YawFor(w.px - en.x, w.pz - en.z), TAC.SNIPER_TRACK_TURN);
            en.warnT++;
            en.tx = w.px;
            en.tz = w.pz;
            if (en.warnT == 1) w.events.sniperAim = true;
            if (en.warnT >= TAC.SNIPER_WARN)
            {
                w.FireEnemyBullet(en);
                w.events.sniperShot = true;
                en.warnT = 0;
                en.attackCd = TAC.SNIPER_COOLDOWN;
                w.AddNoise(en.x, en.z, TAC.NOISE_SHOT_R);
            }
        }
        else
        {
            en.warnT -= TAC.SNIPER_WARN_DECAY;
            if (en.warnT < 0) en.warnT = 0;
        }
    }
}

// --- replay codec + verification (mirror of tacsim.js) -------------------------
public static class TacReplay
{
    const string B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public static string B64Encode(List<int> bytes)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i + 2 < bytes.Count)
        {
            int n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
            sb.Append(B64[(n >> 18) & 63]).Append(B64[(n >> 12) & 63]).Append(B64[(n >> 6) & 63]).Append(B64[n & 63]);
            i += 3;
        }
        int rest = bytes.Count - i;
        if (rest == 1)
        {
            int n1 = bytes[i] << 16;
            sb.Append(B64[(n1 >> 18) & 63]).Append(B64[(n1 >> 12) & 63]).Append("==");
        }
        else if (rest == 2)
        {
            int n2 = (bytes[i] << 16) | (bytes[i + 1] << 8);
            sb.Append(B64[(n2 >> 18) & 63]).Append(B64[(n2 >> 12) & 63]).Append(B64[(n2 >> 6) & 63]).Append('=');
        }
        return sb.ToString();
    }

    public static int[] B64Decode(string str)
    {
        if (str == null || str.Length % 4 != 0) return null;
        var lut = new int[128];
        for (int c = 0; c < 128; c++) lut[c] = -1;
        for (int c2 = 0; c2 < 64; c2++) lut[B64[c2]] = c2;
        int pad = 0;
        if (str.Length >= 2)
        {
            if (str[str.Length - 1] == '=') pad++;
            if (str[str.Length - 2] == '=') pad++;
        }
        int outLen = (str.Length / 4) * 3 - pad;
        var bytes = new int[outLen];
        int o = 0;
        for (int i = 0; i < str.Length; i += 4)
        {
            char ca = str[i], cb = str[i + 1], cc = str[i + 2], cd = str[i + 3];
            if (ca >= 128 || cb >= 128 || cc >= 128 || cd >= 128) return null;
            int a = lut[ca], b = lut[cb];
            int c3 = cc == '=' ? 0 : lut[cc];
            int d = cd == '=' ? 0 : lut[cd];
            if (a < 0 || b < 0 || c3 < 0 || d < 0) return null;
            int n = (a << 18) | (b << 12) | (c3 << 6) | d;
            if (o < outLen) bytes[o++] = (n >> 16) & 255;
            if (o < outLen) bytes[o++] = (n >> 8) & 255;
            if (o < outLen) bytes[o++] = n & 255;
        }
        return bytes;
    }

    public static string EncodeTrace(List<TacInput> recs)
    {
        var bytes = new List<int>();
        int i = 0;
        while (i < recs.Count)
        {
            var r = recs[i];
            int count = 1;
            if ((r.b & 57) == 0)
            {
                while (i + count < recs.Count && count < 65535)
                {
                    var s = recs[i + count];
                    if (s.b != r.b || s.m != r.m || s.yawQ != r.yawQ || s.pitchQ != r.pitchQ) break;
                    count++;
                }
            }
            int p = r.pitchQ & 65535;
            bytes.Add(r.b & 255); bytes.Add(r.m & 255);
            bytes.Add(r.yawQ & 255); bytes.Add((r.yawQ >> 8) & 255);
            bytes.Add(p & 255); bytes.Add((p >> 8) & 255);
            bytes.Add(count & 255); bytes.Add((count >> 8) & 255);
            i += count;
        }
        return B64Encode(bytes);
    }

    public static List<TacInput> DecodeTrace(string data, int maxTicks)
    {
        var bytes = B64Decode(data);
        if (bytes == null || bytes.Length % 8 != 0) return null;
        var recs = new List<TacInput>();
        for (int i = 0; i < bytes.Length; i += 8)
        {
            int b = bytes[i];
            int m = bytes[i + 1];
            if (b > 63) return null;
            if (m > 127 && m != 255) return null;
            int yawQ = bytes[i + 2] | (bytes[i + 3] << 8);
            int pu = bytes[i + 4] | (bytes[i + 5] << 8);
            int pitchQ = pu >= 32768 ? pu - 65536 : pu;
            if (pitchQ > TAC.PITCH_MAX || pitchQ < TAC.PITCH_MIN) return null;
            int count = bytes[i + 6] | (bytes[i + 7] << 8);
            if (count < 1) return null;
            if ((b & 57) != 0 && count != 1) return null;
            if (recs.Count + count > maxTicks) return null;
            for (int n = 0; n < count; n++)
            {
                recs.Add(new TacInput { b = n == 0 ? b : (b & 6), m = m, yawQ = yawQ, pitchQ = pitchQ });
            }
        }
        return recs;
    }

    public class ReplayResult { public bool cleared; public int ticks; public string error; }

    public static ReplayResult RunReplay(TacJson.JObj stageData, string v, string data, int hardTickCap)
    {
        if (v != "t1" || data == null)
        {
            return new ReplayResult { cleared = false, ticks = 0, error = "bad replay" };
        }
        if (data.Length > 900000)
        {
            return new ReplayResult { cleared = false, ticks = 0, error = "replay too large" };
        }
        TacWorld world;
        try
        {
            world = new TacWorld(stageData);
        }
        catch (Exception)
        {
            return new ReplayResult { cleared = false, ticks = 0, error = "bad stage" };
        }
        int cap = world.maxTicks < hardTickCap ? world.maxTicks : hardTickCap;
        var recs = DecodeTrace(data, cap);
        if (recs == null) return new ReplayResult { cleared = false, ticks = 0, error = "bad trace" };
        for (int i = 0; i < recs.Count; i++)
        {
            var r = recs[i];
            world.Step(r);
            if (world.clearedFlag) return new ReplayResult { cleared = true, ticks = world.tick };
            if (world.dead || world.timedOutFlag) return new ReplayResult { cleared = false, ticks = world.tick };
        }
        return new ReplayResult { cleared = false, ticks = world.tick };
    }
}
