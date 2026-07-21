// Prompt World — TAC deterministic simulation (game:"tac", 3D TPS stealth shooter).
//
// This file is shared VERBATIM between:
//   - the browser client (tac.html loads it with <script src>), where it runs the game, and
//   - the Cloudflare worker (_worker.js is built by concatenating this file), where it
//     re-simulates replay certificates to verify clears before publish.
// Because both sides run the SAME JavaScript, determinism only requires that we avoid
// implementation-defined operations. Rules (mirrors the PromptSim/sim.js contract):
//   - all state is double; every stage-JSON value is quantized to float32 (tacQ) on load
//   - only + - * / comparisons and Math.sqrt/Math.floor (both exactly specified by
//     IEEE-754 / ECMA-262); NO Math.sin/cos/atan/pow etc. — trig uses tacSinQ below,
//     a fixed-order polynomial built from basic ops only
//   - fixed step 0.02 s (50 Hz); tick counters are integers; no RNG anywhere
//   - angles are integer "angle units": 65536 units = one full turn
// A future C# port for the mobile app must mirror this file line-for-line (same
// discipline as PromptSim.cs <-> sim.js), including one-float-op-per-statement style
// in the step math.
//
// No exports: defines globals TAC, TacWorld, tacRunReplay, tacEncodeTrace,
// tacDecodeTrace, tacSinQ, tacCosQ (concatenation + <script> pattern, same as sim.js).

'use strict';

var TAC = {
  TICK: 0.02,               // seconds per tick (50 Hz)
  TURN: 65536,              // angle units per full turn
  HALF_TURN: 32768,
  QUARTER_TURN: 16384,

  PLAYER_R: 0.4,            // player capsule radius (m)
  PLAYER_H: 1.7,            // player height (m)
  EYE_H: 1.5,               // player eye height above feet
  CHEST_H: 1.1,             // LOS / hit reference height
  WALK_SPEED: 3.3,          // sneak speed m/s (silent)
  RUN_SPEED: 7.8,           // run speed m/s
  MOVE_RAMP_TICKS: 12,      // ticks to reach full speed from standstill (0.24 s)
  MOVE_RAMP_MIN: 0.3,       // starting speed fraction — taps nudge, holds run (noisy)
  GRAVITY: 20.0,
  JUMP_V: 7.0,              // ~1.2 m apex
  STEP_UP: 0.35,            // default max ledge auto-step (m); stage.stepUp may raise to 0.55
  PITCH_MAX: 14563,         // ~+80 deg in angle units
  PITCH_MIN: -14563,


  FIRE_CD: 9,               // ticks between player shots
  LOCK_COS: 0.906,          // auto lock-on cone: ~25 deg around the player's FACING
  LOCK_RANGE: 24.0,         // auto lock-on max distance — just past enemy vision
  BULLET_SPEED: 88.0,
  BULLET_TTL: 90,           // ticks (~99 m max range)
  ENEMY_BULLET_SPEED: 36.0,
  ENEMY_BULLET_TTL: 200,

  NOISE_RUN_R: 9.0,         // running footstep noise radius (m)
  NOISE_RUN_EVERY: 12,      // ticks between footstep noise pulses
  NOISE_LAND_R: 7.0,
  NOISE_SHOT_R: 28.0,
  NOISE_BLAST_R: 45.0,

  VISION_RANGE: 26.0,
  VISION_COS2: 0.470,       // cos^2(~47 deg) — widened half-angle of the standard cone
  SNIPER_RANGE: 66.0,
  SNIPER_COS2: 0.75,        // cos^2(30 deg)
  GAUGE_MAX: 100.0,
  GAUGE_DECAY: 1.2,
  SUSPICIOUS_AT: 45.0,

  SOLDIER_HP: 2,
  SOLDIER_PATROL_SPEED: 2.5,
  SOLDIER_INVESTIGATE_SPEED: 3.7,
  SOLDIER_CHASE_SPEED: 5.6,
  SOLDIER_R: 0.4,
  SOLDIER_H: 1.8,
  RIFLE_CD: 52,             // ticks between a soldier's aimed shots (1.04 s)
  RIFLE_AIM: 20,            // aim-telegraph ticks before each shot (yellow line)
  RIFLE_SUPPRESS_CD: 90,    // cadence of blind suppressive fire from trenches
  RIFLE_REACT: 28,          // aim delay after becoming alert (0.56 s of counterplay)
  RIFLE_RANGE: 22.0,        // soldier stops and shoots inside this distance
  ENEMY_TURN_RATE: 500,     // angle units per tick (~2.75 deg)

  GATLING_HP: 4,
  GATLING_R: 0.6,
  GATLING_H: 1.6,
  GATLING_SPRAY: 100,       // ticks firing
  GATLING_RELOAD: 50,       // ticks silent
  GATLING_SHOT_EVERY: 5,
  GATLING_SWEEP: 2730,      // sweep amplitude in angle units (~15 deg)
  GATLING_ALERT_TURN: 220,  // tracking turn rate when alerted
  GATLING_VISION: 36.0,     // emplacement optics: sees as far as its bullets fly

  SNIPER_HP: 2,
  SNIPER_R: 0.4,
  SNIPER_H: 2.0,
  SNIPER_BULLET_SPEED: 90.0, // near-hitscan: dodge the LASER (break LOS in the warn window), not the round
  SNIPER_WARN: 75,          // ticks of white laser before the kill shot (1.5 s)
  SHIELD_HP: 4,
  SHIELD_R: 0.5,
  SHIELD_H: 1.9,
  SHIELD_BLOCK_R: 1.0,      // the tower shield covers a full meter each side
  SHIELD_CYCLE: 300,        // formation drill: 6 s loop, synchronized world-wide
  SHIELD_OPEN: 100,         // ...of which the last 2 s are OPEN (firing windows both ways)
  SHIELD_STAGGER: 250,      // a nearby blast staggers the wall open for 5 s
  SHIELD_BLAST_DMG: 2,      // the shield soaks most of a blast (stagger is the real payoff)
  SHIELD_TURN: 90,          // ponderous facing turn under all that steel
  SNIPER_SWEEP: 8192,       // idle scan arc: +/-45 deg around the spawn bearing
  SNIPER_SCAN_TURN: 55,     // slow deliberate sweep (angle units per tick)
  SNIPER_TRACK_TURN: 220,   // eyes-on: swings to keep the target centered
  SNIPER_COOLDOWN: 110,
  SNIPER_WARN_DECAY: 5,

  // APC / light armored vehicle (type 7): a slow, high-HP rolling gun. Its
  // FRONT+SIDE armour (a cone around the hull's facing) shrugs off every small
  // round — you must flank to the REAR to do damage, or hit it with explosives
  // (grenades / barrels ignore armour entirely). A hull-mounted machine gun
  // suppresses while the turret is slow to swing round.
  APC_HP: 8,
  APC_R: 0.7,               // wide hull
  APC_H: 1.6,
  APC_PATROL_SPEED: 1.6,    // ponderous track speed while idle/searching
  APC_ADVANCE_SPEED: 2.6,   // grinds toward the player once alerted
  APC_TURN: 70,             // slow hull yaw (angle units/tick) — flank the rear
  APC_VISION: 26.0,         // commander optics
  APC_ARMOR_COS: 0.5,       // front+side armour arc: |angle to hull facing| < 60 deg is a bounce (cos 60 = 0.5)
  APC_GUN_CD: 8,            // ticks between machine-gun rounds
  APC_GUN_BURST: 60,        // ticks firing
  APC_GUN_RELOAD: 40,       // ticks silent between bursts
  APC_RANGE: 24.0,          // opens up inside this distance

  BARREL_R: 0.5,
  BARREL_H: 1.0,
  BARREL_ROLL: 5.0,
  BARREL_FUSE: 150,         // ticks from bullet hit to boom (3 s)
  BARREL_CHAIN_FUSE: 15,
  BLAST_R: 2.6,
  BLAST_DMG: 10,            // enough to kill any enemy
  BLAST_PLAYER_DMG: 2,

  MINE_R: 0.35,             // mine body radius (visible, shootable)
  MINE_TRIGGER_R: 1.1,      // stepping this close arms the fuse
  MINE_FUSE: 25,            // beep time before the boom (0.5 s to dash away)
  MINE_SHOT_FUSE: 8,        // fuse when detonated by a bullet / chained blast
  MINE_BLAST_R: 2.0,

  DRONE_HP: 1,
  DRONE_R: 0.5,
  DRONE_H: 0.5,
  DRONE_FLY_Y: 3.2,         // hover height above its spawn ground
  DRONE_PATROL_SPEED: 4.4,
  DRONE_CHASE_SPEED: 6.6,
  DRONE_DIVE_SPEED: 8.2,    // descent rate during the kamikaze dive
  DRONE_DIVE_AT: 2.4,       // horizontal distance that triggers the dive
  DRONE_BOOM_AT: 1.1,       // 3D distance to the player that sets off the boom
  DRONE_BLAST_R: 1.6,
  DRONE_CRASH_SPEED: 3.5,   // fall rate after losing its operator
  DRONE_ENGAGE_Y: 1.4,      // alerted-chase altitude above ground (the player's aim line)
  DRONE_DESCEND: 3.5,       // descent rate to reach it once alerted

  OPERATOR_HP: 1,
  OPERATOR_R: 0.4,
  OPERATOR_H: 1.7,
  OPERATOR_FLEE_SPEED: 5.1,

  PILOT_SPEED: 5.0,         // player-piloted drone flight speed
  PILOT_ALT: 6.0,           // hover height — clears every wall and platform
  PILOT_LOCK_R: 9.0,        // homing-dive lock radius (horizontal, from the drone)
  PILOT_DIVE_SPEED: 8.0,    // homing dive speed
  PILOT_BATTERY: 1500,      // flight time in ticks (30 s), then it powers down
  PILOT_BLAST_R: 2.6,       // detonation radius (barrel-sized)

  BOMBER_HP: 2,             // satchel bomber: soldier-tough, no rifle
  BOMBER_R: 0.42,
  BOMBER_H: 1.75,
  BOMBER_RANGE: 18.0,       // max lob distance
  BOMBER_MIN: 5.0,          // won't lob inside this (backs away instead)
  BOMBER_CD: 600,           // 12 s between throws
  BOMB_FLIGHT: 90,          // 1.8 s in the air (high overhead lob)
  BOMB_FUSE: 250,           // 5 s on the ground before it blows — RUN
  BOMB_BLAST_R: 3.0,
  CRACKED_HP: 40,           // gatling rounds to chew through a cracked wall
  GRENADE_CD: 400,          // recharge ticks between throws (8 s)
  GRENADE_SPEED_H: 12.0,    // horizontal launch speed (lands ~9 m out)
  GRENADE_SPEED_V: 6.0,     // upward launch speed (flat arc: can't reach high platforms)
  GRENADE_BLAST_R: 2.4,
  GRENADE_R: 0.15,

  INVESTIGATE_PAUSE: 70,    // ticks an enemy inspects a noise source
  GROUP_MAX: 9,
  CELL: 4.0,                // broadphase grid cell size (m)

  RIVER_MUL: 0.45,          // player speed multiplier while wading
  TRENCH_DEPTH: 0.9,        // trenches are real pits dug this deep
  RIVER_DEPTH: 0.6,         // river channels are shallower pits full of water
  CROUCH_H: 1.0,            // crouched height (sneak inside a trench)
  CROUCH_CHEST: 0.55,       // crouched chest height (aim/LOS target)
  TRENCH_STANDOFF: 12.0,    // soldiers besiege an entrenched player from here
  TRENCH_PROBE: 8.0,        // investigators won't walk closer to a hole than this
  SLIDE_SPEED: 6.0,         // rockslide boulder speed
  SLIDE_LEN: 22.0,          // how far boulders roll
  SLIDE_CRUSH_R: 1.3,       // crush radius around each boulder
  SCOPE_MAX: 5,             // scoped shots available per stage (then the scope is dry)
  SCOPE_CD: 300,            // recharge between scoped shots (6 s) — standard equipment
  SCOPE_RANGE: 90.0,
  SCOPE_DMG: 3,
  SCOPE_YAW_RATE: 175,      // manual aim TOP speed (angle units/tick) while scoped
  SCOPE_PITCH_RATE: 100,
  SCOPE_RAMP_TICKS: 32,     // held-steer ramp: a tap creeps for fine aim, hold winds up
  // rendered humanoid head sits at ~1.1×h, above the h-tall body hit cylinder;
  // the scoped shot extends its cylinder by this fraction so a crosshair-on-head
  // shot connects. Scoped-shot only — bullet/grenade/AI collision unchanged.
  SCOPE_HEAD_FRAC: 0.18,
  JAMMER_R: 12.0,           // default jammer field radius

  SQUAD_RADIO_R: 30.0,      // squad doctrine: radio-link range between units
  SQUAD_FAN_R: 6.0,         // fan-clearing offset around a shared last-known position
  SQUAD_FLANK_R: 9.0,       // RED flankers swing this wide of the direct line
  LAMP_R: 6.0,              // default lamp light-pool radius
  SEARCH_R: 18.0,           // default searchlight beam reach
  SEARCH_HALF: 1300         // beam half-width in angle units (~7 deg)
};

function tacQ(v) { return Math.fround(v); }

// --- deterministic trig ------------------------------------------------------
// sin of an integer angle (65536 units per turn). Fixed-order Taylor polynomial:
// only * - + on doubles, so the result is bit-identical on every JS engine and
// portable to C# with the same statement order.
var TAC_RAD_PER_UNIT = 9.587379924285257e-5; // 2*pi / 65536

function tacSinQ(q) {
  var a = q & 65535;
  var neg = false;
  if (a >= 32768) { a = a - 32768; neg = true; }
  if (a > 16384) { a = 32768 - a; }
  var x = a * TAC_RAD_PER_UNIT;
  var x2 = x * x;
  var t9 = x2 / 72.0;
  var p7 = 1.0 - t9;
  var t7 = x2 * p7;
  var t7b = t7 / 42.0;
  var p5 = 1.0 - t7b;
  var t5 = x2 * p5;
  var t5b = t5 / 20.0;
  var p3 = 1.0 - t5b;
  var t3 = x2 * p3;
  var t3b = t3 / 6.0;
  var p1 = 1.0 - t3b;
  var s = x * p1;
  if (neg) { return -s; }
  return s;
}

function tacCosQ(q) { return tacSinQ(q + 16384); }

// --- replay codec ------------------------------------------------------------
// Per-tick input record (6 bytes):
//   [0] buttons: bit0 jump (edge), bit1 fire (held), bit2 sneak (held),
//       bit3 drone launch (edge), bit4 grenade (edge), bit5 scope toggle (edge)
//   [1] moveDir: 0..127 camera-relative direction (units of 512 angle units),
//       255 = no movement
//   [2..3] yawQ  uint16 LE (absolute view yaw, angle units)
//   [4..5] pitchQ int16 LE (view pitch, angle units, clamped +-PITCH_MAX)
// Stream = repeated [record(6B) count(uint16 LE)]. Records with an edge bit
// (jump/drone) set never have count > 1 (edges must not repeat). Encoded as
// base64 in the replay certificate: { v:'t1', ticks, data }.

var TAC_B64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

function tacB64Encode(bytes) {
  var out = '';
  var i = 0;
  while (i + 2 < bytes.length) {
    var n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
    out += TAC_B64[(n >> 18) & 63] + TAC_B64[(n >> 12) & 63] + TAC_B64[(n >> 6) & 63] + TAC_B64[n & 63];
    i += 3;
  }
  var rest = bytes.length - i;
  if (rest === 1) {
    var n1 = bytes[i] << 16;
    out += TAC_B64[(n1 >> 18) & 63] + TAC_B64[(n1 >> 12) & 63] + '==';
  } else if (rest === 2) {
    var n2 = (bytes[i] << 16) | (bytes[i + 1] << 8);
    out += TAC_B64[(n2 >> 18) & 63] + TAC_B64[(n2 >> 12) & 63] + TAC_B64[(n2 >> 6) & 63] + '=';
  }
  return out;
}

function tacB64Decode(str) {
  if (typeof str !== 'string' || str.length % 4 !== 0) return null;
  var lut = tacB64Decode._lut;
  if (!lut) {
    lut = {};
    for (var c = 0; c < 64; c++) lut[TAC_B64[c]] = c;
    tacB64Decode._lut = lut;
  }
  var pad = 0;
  if (str.length >= 2) {
    if (str[str.length - 1] === '=') pad++;
    if (str[str.length - 2] === '=') pad++;
  }
  var outLen = (str.length / 4) * 3 - pad;
  var bytes = new Uint8Array(outLen);
  var o = 0;
  for (var i = 0; i < str.length; i += 4) {
    var a = lut[str[i]], b = lut[str[i + 1]];
    var c2 = str[i + 2] === '=' ? 0 : lut[str[i + 2]];
    var d = str[i + 3] === '=' ? 0 : lut[str[i + 3]];
    if (a === undefined || b === undefined || c2 === undefined || d === undefined) return null;
    var n = (a << 18) | (b << 12) | (c2 << 6) | d;
    if (o < outLen) bytes[o++] = (n >> 16) & 255;
    if (o < outLen) bytes[o++] = (n >> 8) & 255;
    if (o < outLen) bytes[o++] = n & 255;
  }
  return bytes;
}

// recs: array of per-tick {b, m, yawQ, pitchQ}. Returns base64 stream.
function tacEncodeTrace(recs) {
  var bytes = [];
  var i = 0;
  while (i < recs.length) {
    var r = recs[i];
    var count = 1;
    if ((r.b & 57) === 0) {
      while (i + count < recs.length && count < 65535) {
        var s = recs[i + count];
        if (s.b !== r.b || s.m !== r.m || s.yawQ !== r.yawQ || s.pitchQ !== r.pitchQ) break;
        count++;
      }
    }
    var p = r.pitchQ & 65535;
    bytes.push(r.b & 255, r.m & 255, r.yawQ & 255, (r.yawQ >> 8) & 255, p & 255, (p >> 8) & 255, count & 255, (count >> 8) & 255);
    i += count;
  }
  return tacB64Encode(bytes);
}

// Returns array of {b, m, yawQ, pitchQ} (jump bit only on the first tick of a
// run), or null if malformed / longer than maxTicks.
function tacDecodeTrace(data, maxTicks) {
  var bytes = tacB64Decode(data);
  if (!bytes || bytes.length % 8 !== 0) return null;
  var recs = [];
  for (var i = 0; i < bytes.length; i += 8) {
    var b = bytes[i];
    var m = bytes[i + 1];
    if (b > 63) return null;
    if (m > 127 && m !== 255) return null;
    var yawQ = bytes[i + 2] | (bytes[i + 3] << 8);
    var pu = bytes[i + 4] | (bytes[i + 5] << 8);
    var pitchQ = pu >= 32768 ? pu - 65536 : pu;
    if (pitchQ > TAC.PITCH_MAX || pitchQ < TAC.PITCH_MIN) return null;
    var count = bytes[i + 6] | (bytes[i + 7] << 8);
    if (count < 1) return null;
    if ((b & 57) !== 0 && count !== 1) return null;
    if (recs.length + count > maxTicks) return null;
    for (var n = 0; n < count; n++) {
      recs.push({ b: n === 0 ? b : (b & 6), m: m, yawQ: yawQ, pitchQ: pitchQ });
    }
  }
  return recs;
}

// --- world -------------------------------------------------------------------

function TacWorld(stage) {
  var w = this;
  w.arenaW = tacQ(stage.arena.w);
  w.arenaD = tacQ(stage.arena.d);
  w.timeLimit = tacQ(stage.timeLimit || 600);
  w.maxTicks = Math.floor(w.timeLimit / TAC.TICK);
  w.tick = 0;
  // per-stage auto step-up: opt-in via stage.stepUp, clamped [0.35, 0.55]
  w.stepUp = tacQ(Math.min(0.55, Math.max(0.35, stage.stepUp === undefined ? TAC.STEP_UP : stage.stepUp)));

  w.px = tacQ(stage.playerStart.x);
  w.pz = tacQ(stage.playerStart.z);
  w.py = 0.0;
  w.playerStartY = (stage.playerStart.y === undefined || stage.playerStart.y === null) ? NaN : tacQ(stage.playerStart.y);
  w.vy = 0.0;
  var yawDeg = tacQ(stage.playerStart.yaw || 0);
  w.yawQ = Math.floor(yawDeg * 182.04444444444445) & 65535; // deg -> units
  w.faceQ = w.yawQ;         // character facing = last movement direction; aim follows this
  w.pitchQ = 0;
  w.onGround = true;
  w.moveT = 0;              // consecutive moving ticks (drives the speed ramp)
  w.fireCd = 0;
  w.hp = stage.lives ? Math.floor(stage.lives) : 1;
  w.maxHp = w.hp;
  w.ammo = (stage.ammo === undefined || stage.ammo === 0) ? -1 : Math.floor(stage.ammo); // -1 = infinite
  w.hurtCd = 0;             // brief post-hit invulnerability
  w.lockTarget = -1;        // index the auto-aim is locked onto (-1 none)
  w.lockKind = 0;           // 0 = enemy index, 1 = barrel index
  w.droneUses = 0;          // captured drone charges (one per operator killed)
  w.pilot = null;           // active player-piloted drone {x,y,z,battery}
  w.scopeSteerT = 0;        // scoped-aim ramp counter (precision tap -> fast sweep)
  w.grenadeCd = 0;          // grenade recharge (0 = ready to throw)
  w.grenades = [];          // live grenades {x,y,z,vx,vy,vz,alive}
  w.prevB = 255;            // all buttons "held" at spawn: a button already down on (re)load/retry is NOT a fresh edge — must be released and re-pressed to fire (prevents scope/drone/grenade misfire that roots the body)
  w.fireGate = false;       // true while piloting/scoping — blocks held-fire spillover into body fire until FIRE is released
  w.dead = false;
  w.clearedFlag = false;
  w.timedOutFlag = false;

  w.boxes = [];             // {x0,z0,x1,z1,h,kind} kind: 0 rock,1 wall,2 platform
  w.slopes = [];            // {x0,z0,x1,z1,h,dir,ux,uz} dir 0:+z 1:+x 2:-z 3:-x
  w.barrels = [];           // {x,z,y,state,fuse,dx,dz,alive} state 0 idle,1 rolling
  w.mines = [];             // {x,z,y,fuse,alive} fuse<0 = armed and waiting
  w.medkits = [];           // {x,z,y,alive} heals 1 hp on touch (only if hurt)
  w.rivers = [];            // {x0,z0,x1,z1} slow water; ground enemies refuse to enter
  w.trenches = [];          // {x0,z0,x1,z1} entrenched cover (see bullet rules)
  w.slides = [];            // rockslides {pileX,pileZ,w,d,dx,dz,postX,postZ,postY,triggered,boulders:[]}
  w.switches = [];          // {x,z,y,r,alive} EMP switches: each projects its own dome
  w.scopeCd = 0;            // scoped-shot recharge (standard equipment)
  w.scopeShots = TAC.SCOPE_MAX; // scoped shots left this stage
  w.crouched = false;       // auto-crouch below grade; firing pops you up briefly
  w.fireFlash = 0;          // ticks of standing after a shot from a pit
  w.scoped = false;
  w.aimYawQ = 0;
  w.aimPitchQ = 0;
  w.playerJammed = false;
  w.enemies = [];           // see addEnemy
  w.bullets = [];           // {x,y,z,vx,vy,vz,ttl,fromPlayer,alive}
  w.bombs = [];             // bomber satchels {sx,sy,sz,x,y,z,t,fuse,state 0 fly/1 armed/2 done}
  w.noises = [];            // this tick: {x,z,r}
  w.events = {};
  w.enemiesLeft = 0;
  w.shotsFired = 0;
  w.night = !!stage.night;  // night ops: enemies see HALF as far unless the player is LIT
  w.squad = !!stage.squad;  // opt-in: units share intel by radio (see squad* below)
  w.pitDefs = [];           // generic dug-out pits {x0,z0,x1,z1,depth}
  // render-only theme palette: {ground, sky, water} hex colors
  w.palette = (stage.palette && typeof stage.palette === 'object') ? stage.palette : null;
  w.lamps = [];             // {x,z,r} static light pools
  w.lights = [];            // searchlights {x,z,r,angQ,speed}
  w.playerLit = false;
  // goal: 0 = eliminate every hostile (default), 1 = extract — collect ALL
  // intel items, then reach the exit zone. Kills optional (ghost runs valid).
  w.goalType = stage.goal === 'extract' ? 1 : 0;
  w.intels = [];            // {x,z,y,alive}
  w.exitZone = null;        // {x0,z0,x1,z1}
  w.intelLeft = 0;

  var parts = stage.parts || [];
  for (var i = 0; i < parts.length; i++) {
    var p = parts[i];
    if (p.type === 'rock') { w.addBox(p.x, p.z, p.w, p.d, p.h === undefined ? 1.4 : p.h, 0); if (p.tint) w.boxes[w.boxes.length - 1].tint = String(p.tint); }
    else if (p.type === 'wall') { w.addBox(p.x, p.z, p.w, p.d, p.h === undefined ? 3.0 : p.h, 1); if (p.tint) w.boxes[w.boxes.length - 1].tint = String(p.tint); }
    else if (p.type === 'platform') { w.addBox(p.x, p.z, p.w, p.d, p.h === undefined ? 2.0 : p.h, 2); if (p.tint) w.boxes[w.boxes.length - 1].tint = String(p.tint); }
    else if (p.type === 'crackedWall') { w.addBox(p.x, p.z, p.w, p.d, p.h === undefined ? 3.0 : p.h, 3, p.y0 || 0); if (p.tint) w.boxes[w.boxes.length - 1].tint = String(p.tint); }
    else if (p.type === 'slope') { w.addSlope(p.x, p.z, p.w, p.d, p.h === undefined ? 2.0 : p.h, p.dir || 0, p.y0 || 0); if (p.tint) w.slopes[w.slopes.length - 1].tint = String(p.tint); }
    else if (p.type === 'barrel') w.addBarrel(p.x, p.z);
    else if (p.type === 'mine') w.mines.push({ x: tacQ(p.x), z: tacQ(p.z), y: 0.0, fuse: -1, alive: true });
    else if (p.type === 'medkit') w.medkits.push({ x: tacQ(p.x), z: tacQ(p.z), y: 0.0, alive: true });
    else if (p.type === 'block') {
      // freeform cuboid: any size, any height, optionally FLOATING (y0) —
      // castles, arches, bridges, islands. tint colors the render only.
      w.addBox(p.x, p.z, p.w, p.d, p.h === undefined ? 1.0 : p.h, 4, p.y0 || 0);
      if (p.tint) w.boxes[w.boxes.length - 1].tint = String(p.tint);
    }
    else if (p.type === 'pit') {
      var phw = tacQ(p.w) / 2.0;
      var phd = tacQ(p.d) / 2.0;
      var pcx = tacQ(p.x);
      var pcz = tacQ(p.z);
      w.pitDefs.push({ x0: pcx - phw, z0: pcz - phd, x1: pcx + phw, z1: pcz + phd,
        depth: tacQ(p.depth === undefined ? 1.5 : p.depth) });
    }
    else if (p.type === 'river' || p.type === 'trench') {
      var fhw = tacQ(p.w) / 2.0;
      var fhd = tacQ(p.d) / 2.0;
      var fcx = tacQ(p.x);
      var fcz = tacQ(p.z);
      var rect = { x0: fcx - fhw, z0: fcz - fhd, x1: fcx + fhw, z1: fcz + fhd };
      if (p.type === 'river') w.rivers.push(rect); else w.trenches.push(rect);
    }
    else if (p.type === 'rockslide') {
      var sd = p.dir & 3;
      var sdx = sd === 1 ? 1.0 : (sd === 3 ? -1.0 : 0.0);
      var sdz = sd === 0 ? 1.0 : (sd === 2 ? -1.0 : 0.0);
      var pw = tacQ(p.w === undefined ? 6 : p.w);
      var pdep = tacQ(p.d === undefined ? 3 : p.d);
      var px2 = tacQ(p.x);
      var pz2 = tacQ(p.z);
      var poff = pdep / 2.0 + 0.6;
      w.slides.push({
        pileX: px2, pileZ: pz2, w: pw, d: pdep, dx: sdx, dz: sdz,
        postX: px2 + sdx * poff, postZ: pz2 + sdz * poff, postY: 0.0,
        triggered: false, boulders: []
      });
    }
    else if (p.type === 'switch') w.switches.push({ x: tacQ(p.x), z: tacQ(p.z), y: 0.0, r: tacQ(p.r === undefined ? TAC.JAMMER_R : p.r), alive: true });
    else if (p.type === 'intel') w.intels.push({ x: tacQ(p.x), z: tacQ(p.z), y: 0.0, alive: true });
    else if (p.type === 'lamp') w.lamps.push({ x: tacQ(p.x), z: tacQ(p.z), r: tacQ(p.r === undefined ? TAC.LAMP_R : p.r) });
    else if (p.type === 'searchlight') {
      var slDeg = tacQ(p.yaw || 0);
      var slBase = Math.floor(slDeg * 182.04444444444445) & 65535;
      var slPeriod = p.period === undefined ? 12 : p.period;
      var slSpeed = Math.floor(65536 / (slPeriod * 50));
      if (slSpeed < 1) slSpeed = 1;
      w.lights.push({ x: tacQ(p.x), z: tacQ(p.z), r: tacQ(p.r === undefined ? TAC.SEARCH_R : p.r), angQ: slBase, speed: slSpeed });
    }
    else if (p.type === 'exit') {
      var xhw = tacQ(p.w === undefined ? 4 : p.w) / 2.0;
      var xhd = tacQ(p.d === undefined ? 4 : p.d) / 2.0;
      var xcx = tacQ(p.x);
      var xcz = tacQ(p.z);
      w.exitZone = { x0: xcx - xhw, z0: xcz - xhd, x1: xcx + xhw, z1: xcz + xhd };
    }
  }
  var enemies = stage.enemies || [];
  for (var e = 0; e < enemies.length; e++) w.addEnemy(enemies[e]);
  // drone <-> operator uplink: operators control the drones sharing their
  // group number; when a group's operators are ALL dead, its drones crash.
  w.opGroups = {};
  for (var oe = 0; oe < w.enemies.length; oe++) {
    var oen = w.enemies[oe];
    if (oen.type === 4 && oen.group > 0) {
      if (!w.opGroups[oen.group]) w.opGroups[oen.group] = [];
      w.opGroups[oen.group].push(oe);
    }
  }

  // pits: real depressions in the ground (trenches deep, river channels shallow)
  w.pits = [];
  for (var pt = 0; pt < w.trenches.length; pt++) {
    var tt = w.trenches[pt];
    w.pits.push({ x0: tt.x0, z0: tt.z0, x1: tt.x1, z1: tt.z1, depth: TAC.TRENCH_DEPTH });
  }
  for (var pr = 0; pr < w.rivers.length; pr++) {
    var rr = w.rivers[pr];
    w.pits.push({ x0: rr.x0, z0: rr.z0, x1: rr.x1, z1: rr.z1, depth: TAC.RIVER_DEPTH });
  }
  for (var gp = 0; gp < w.pitDefs.length; gp++) {
    var gpd = w.pitDefs[gp];
    w.pits.push({ x0: gpd.x0, z0: gpd.z0, x1: gpd.x1, z1: gpd.z1, depth: gpd.depth });
  }
  w.buildGrid();
  // settle entities onto terrain
  // playerStart.y (optional): the floor the player spawns on. Like enemy spawnY,
  // it settles to the surface at that height instead of the tallest — needed to
  // start a player INSIDE a roofed room. Omitted (NaN) keeps the old behavior.
  w.py = w.groundY(w.px, w.pz, isNaN(w.playerStartY) ? 1000.0 : w.playerStartY, TAC.PLAYER_R);
  for (var k = 0; k < w.enemies.length; k++) {
    var en = w.enemies[k];
    // With spawnY set, resolve the floor at THAT height (groundY returns the
    // tallest surface not above refY+stepUp, so refY=spawnY lands on the floor
    // the author meant, never the roof above it). Without it, refY=1000 keeps
    // the original "tallest surface" behavior — existing stages unchanged.
    en.y = w.groundY(en.x, en.z, isNaN(en.spawnY) ? 1000.0 : en.spawnY, en.r);
    if (en.type === 3) en.y = en.y + TAC.DRONE_FLY_Y; // drones hover above their spawn ground
    en.homeY = en.y;
  }
  for (var b2 = 0; b2 < w.barrels.length; b2++) {
    var ba = w.barrels[b2];
    ba.y = w.groundY(ba.x, ba.z, 1000.0, TAC.BARREL_R);
  }
  for (var m2 = 0; m2 < w.mines.length; m2++) {
    var mi = w.mines[m2];
    mi.y = w.groundY(mi.x, mi.z, 1000.0, TAC.MINE_R);
  }
  for (var h2 = 0; h2 < w.medkits.length; h2++) {
    var mk = w.medkits[h2];
    mk.y = w.groundY(mk.x, mk.z, 1000.0, 0.3);
  }
  for (var it2 = 0; it2 < w.intels.length; it2++) {
    var itm = w.intels[it2];
    itm.y = w.groundY(itm.x, itm.z, 1000.0, 0.3);
  }
  w.intelLeft = w.intels.length;
  for (var sw2 = 0; sw2 < w.switches.length; sw2++) {
    var swo = w.switches[sw2];
    swo.y = w.groundY(swo.x, swo.z, 1000.0, 0.3);
  }
  for (var sl2 = 0; sl2 < w.slides.length; sl2++) {
    var so2 = w.slides[sl2];
    so2.postY = w.groundY(so2.postX, so2.postZ, 1000.0, 0.3);
  }
}

// area helpers -----------------------------------------------------------
TacWorld.prototype.inRiver = function (x, z) {
  for (var i = 0; i < this.rivers.length; i++) {
    var r = this.rivers[i];
    if (x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1) return true;
  }
  return false;
};
TacWorld.prototype.trenchAt = function (x, z) {
  for (var i = 0; i < this.trenches.length; i++) {
    var t = this.trenches[i];
    if (x >= t.x0 && x <= t.x1 && z >= t.z0 && z <= t.z1) return i;
  }
  return -1;
};
TacWorld.prototype.inActiveJammer = function (x, z) {
  for (var i = 0; i < this.switches.length; i++) {
    var j = this.switches[i];
    if (!j.alive) continue;
    var dx = x - j.x;
    var dz = z - j.z;
    if (dx * dx + dz * dz <= j.r * j.r) return true;
  }
  return false;
};
// (trench cover is now pure geometry: pits + pitRimBlocked, no special rule)

TacWorld.prototype.addBox = function (x, z, bw, bd, h, kind, yb) {
  var hw = tacQ(bw) / 2.0;
  var hd = tacQ(bd) / 2.0;
  var cx = tacQ(x);
  var cz = tacQ(z);
  var b0 = tacQ(yb || 0);
  // yb = bottom, h = ABSOLUTE top. Ground-based parts keep yb 0 => identical.
  this.boxes.push({ x0: cx - hw, z0: cz - hd, x1: cx + hw, z1: cz + hd, yb: b0, h: b0 + tacQ(h), kind: kind | 0, alive: true, hp: (kind | 0) === 3 ? TAC.CRACKED_HP : 0, tint: null });
};

TacWorld.prototype.addSlope = function (x, z, bw, bd, h, dir, y0) {
  var hw = tacQ(bw) / 2.0;
  var hd = tacQ(bd) / 2.0;
  var cx = tacQ(x);
  var cz = tacQ(z);
  var d = dir & 3;
  var ux = 0.0, uz = 0.0;
  if (d === 0) uz = 1.0;
  else if (d === 1) ux = 1.0;
  else if (d === 2) uz = -1.0;
  else ux = -1.0;
  var qh = tacQ(h);
  var nSteps = Math.ceil(qh / 0.32);
  if (nSteps < 2) nSteps = 2;
  // y0 = the height the LOW edge starts at (default 0 = ground). A raised
  // staircase (y0 > 0) climbs from y0 to y0+h, so a slope can bridge floor N to
  // floor N+1 of a multi-story build. slopeYAt adds y0 to every tread.
  this.slopes.push({ x0: cx - hw, z0: cz - hd, x1: cx + hw, z1: cz + hd, h: qh, y0: tacQ(y0 || 0), dir: d, ux: ux, uz: uz, steps: nSteps, rise: tacQ(qh / nSteps) });
};

TacWorld.prototype.addBarrel = function (x, z) {
  this.barrels.push({ x: tacQ(x), z: tacQ(z), y: 0.0, state: 0, fuse: -1, dx: 0.0, dz: 0.0, alive: true });
};

TacWorld.prototype.addEnemy = function (spec) {
  var type = spec.type === 'gatling' ? 1 : (spec.type === 'sniper' ? 2 : (spec.type === 'drone' ? 3 : (spec.type === 'operator' ? 4 : (spec.type === 'bomber' ? 5 : (spec.type === 'shield' ? 6 : (spec.type === 'apc' ? 7 : 0))))));
  var defHp = type === 1 ? TAC.GATLING_HP : (type === 2 ? TAC.SNIPER_HP : (type === 3 ? TAC.DRONE_HP : (type === 4 ? TAC.OPERATOR_HP : (type === 5 ? TAC.BOMBER_HP : (type === 6 ? TAC.SHIELD_HP : (type === 7 ? TAC.APC_HP : TAC.SOLDIER_HP))))));
  var hp = spec.hp ? Math.floor(spec.hp) : defHp;
  var yawDeg = tacQ(spec.yaw || 0);
  var yawQ = Math.floor(yawDeg * 182.04444444444445) & 65535;
  var hasPatrol = spec.patrolX !== undefined && spec.patrolZ !== undefined;
  var en = {
    type: type,                     // 0 soldier, 1 gatling, 2 sniper, 3 drone, 4 operator, 5 bomber, 6 shield, 7 apc
    x: tacQ(spec.x), z: tacQ(spec.z), y: 0.0, homeY: 0.0,
    // spawnY = the floor this unit starts on for a MULTI-STORY build. When set,
    // the settle step snaps to the surface nearest this height instead of the
    // tallest one under (x,z) — otherwise a unit placed on floor 1 gets sucked
    // up to the roof. NaN sentinel = not specified (default: tallest surface,
    // the pre-existing behavior, so old stages are byte-identical).
    spawnY: (spec.y === undefined || spec.y === null) ? NaN : tacQ(spec.y),
    r: type === 1 ? TAC.GATLING_R : (type === 2 ? TAC.SNIPER_R : (type === 3 ? TAC.DRONE_R : (type === 4 ? TAC.OPERATOR_R : (type === 5 ? TAC.BOMBER_R : (type === 6 ? TAC.SHIELD_R : (type === 7 ? TAC.APC_R : TAC.SOLDIER_R)))))),
    h: type === 1 ? TAC.GATLING_H : (type === 2 ? TAC.SNIPER_H : (type === 3 ? TAC.DRONE_H : (type === 4 ? TAC.OPERATOR_H : (type === 5 ? TAC.BOMBER_H : (type === 6 ? TAC.SHIELD_H : (type === 7 ? TAC.APC_H : TAC.SOLDIER_H)))))),
    yawQ: yawQ, baseYawQ: yawQ,
    hp: hp, alive: true,
    group: spec.group ? Math.floor(spec.group) : 0,
    state: 0,                       // 0 patrol/idle, 1 suspicious, 2 alert
    gauge: 0.0,
    tx: 0.0, tz: 0.0,               // investigate / last-known-player target
    homeX: tacQ(spec.x), homeZ: tacQ(spec.z),
    patX: hasPatrol ? tacQ(spec.patrolX) : 0.0,
    patZ: hasPatrol ? tacQ(spec.patrolZ) : 0.0,
    hasPatrol: hasPatrol,
    patToB: true,
    pauseT: 0,
    attackCd: 0,
    bombCd: 0,                      // bomber lob cooldown
    rifleCd: 0,                     // soldier aimed-shot cooldown
    aimT: 0,                        // rifle telegraph build-up (shot fires at RIFLE_AIM)
    fireFlash: 0,                   // ticks of staying up after firing (trench pop-up)
    crouched: false,
    cycleT: 0,                      // gatling fire cycle
    warnT: 0,                       // sniper laser build-up
    diving: false,                  // drone kamikaze dive engaged
    crashing: false,                // drone lost its operator and is falling
    idx: this.enemies.length,       // stable spawn index (squad role assignment)
    corpseSpotted: false,           // this body has already been discovered
    shieldStagT: 0,                 // blast stagger: the wall drops for a while
    entrench: !!spec.entrench,      // opt-in: dash to the nearest trench on gunfire
    seekCover: false,               // currently sprinting for a trench
    dugIn: false,                   // reached cover — never abandons it again
    seesPlayer: false
  };
  this.enemies.push(en);
  this.enemiesLeft++;
};

// --- broadphase grid over static boxes ----------------------------------------

TacWorld.prototype.buildGrid = function () {
  var w = this;
  w.gridW = Math.floor(w.arenaW / TAC.CELL) + 1;
  w.gridD = Math.floor(w.arenaD / TAC.CELL) + 1;
  var cells = new Array(w.gridW * w.gridD);
  for (var i = 0; i < cells.length; i++) cells[i] = null;
  for (var b = 0; b < w.boxes.length; b++) {
    var box = w.boxes[b];
    var cx0 = Math.floor(box.x0 / TAC.CELL); if (cx0 < 0) cx0 = 0;
    var cz0 = Math.floor(box.z0 / TAC.CELL); if (cz0 < 0) cz0 = 0;
    var cx1 = Math.floor(box.x1 / TAC.CELL); if (cx1 >= w.gridW) cx1 = w.gridW - 1;
    var cz1 = Math.floor(box.z1 / TAC.CELL); if (cz1 >= w.gridD) cz1 = w.gridD - 1;
    for (var gz = cz0; gz <= cz1; gz++) {
      for (var gx = cx0; gx <= cx1; gx++) {
        var idx = gz * w.gridW + gx;
        if (!cells[idx]) cells[idx] = [];
        cells[idx].push(b);
      }
    }
  }
  w.grid = cells;
};

// visit box indices whose cells intersect the aabb (x0,z0)-(x1,z1); calls fn(boxIndex).
// Uses a per-query stamp so a box spanning cells is visited once.
TacWorld.prototype.forBoxesIn = function (x0, z0, x1, z1, fn) {
  var w = this;
  if (!w._stamp) { w._stamp = 1; w._stamps = new Array(w.boxes.length); }
  var stamp = ++w._stamp;
  var cx0 = Math.floor(x0 / TAC.CELL); if (cx0 < 0) cx0 = 0;
  var cz0 = Math.floor(z0 / TAC.CELL); if (cz0 < 0) cz0 = 0;
  var cx1 = Math.floor(x1 / TAC.CELL); if (cx1 >= w.gridW) cx1 = w.gridW - 1;
  var cz1 = Math.floor(z1 / TAC.CELL); if (cz1 >= w.gridD) cz1 = w.gridD - 1;
  for (var gz = cz0; gz <= cz1; gz++) {
    for (var gx = cx0; gx <= cx1; gx++) {
      var cell = w.grid[gz * w.gridW + gx];
      if (!cell) continue;
      for (var i = 0; i < cell.length; i++) {
        var bi = cell[i];
        if (w._stamps[bi] === stamp) continue;
        w._stamps[bi] = stamp;
        fn(bi);
      }
    }
  }
};

// --- terrain queries -----------------------------------------------------------

// stair tread height at (x,z): the 'slope' part is a staircase — discrete
// treads from the low edge up to h. Every riser is <= 0.32 m (under STEP_UP)
// so walking onto the next tread ALWAYS succeeds, from any approach angle.
TacWorld.prototype.slopeYAt = function (s, x, z) {
  var t;
  if (s.dir === 0) t = (z - s.z0) / (s.z1 - s.z0);
  else if (s.dir === 1) t = (x - s.x0) / (s.x1 - s.x0);
  else if (s.dir === 2) t = (s.z1 - z) / (s.z1 - s.z0);
  else t = (s.x1 - x) / (s.x1 - s.x0);
  if (t < 0.0) t = 0.0;
  if (t > 1.0) t = 1.0;
  var idx = Math.floor(t * s.steps);
  if (idx >= s.steps) idx = s.steps - 1;
  return (s.y0 || 0) + s.rise * (idx + 1);
};

// highest standable surface at (x,z) not above refY + STEP_UP, for a circle of radius r
TacWorld.prototype.groundY = function (x, z, refY, r) {
  var w = this;
  var best = 0.0;
  for (var pi = 0; pi < w.pits.length; pi++) {
    var pp = w.pits[pi];
    if (x >= pp.x0 && x <= pp.x1 && z >= pp.z0 && z <= pp.z1) {
      var pf = -pp.depth;
      if (pf < best) best = pf;
    }
  }
  var lim = refY + w.stepUp;
  w.forBoxesIn(x - r, z - r, x + r, z + r, function (bi) {
    var b = w.boxes[bi];
    if (!b.alive) return;
    if (b.h > lim) return;
    if (refY < b.yb) return; // beneath a floating block: its top is not your floor
    if (x + r <= b.x0 || x - r >= b.x1 || z + r <= b.z0 || z - r >= b.z1) return;
    if (b.h > best) best = b.h;
  });
  for (var s = 0; s < w.slopes.length; s++) {
    var sl = w.slopes[s];
    if (x < sl.x0 || x > sl.x1 || z < sl.z0 || z > sl.z1) continue;
    var sy = w.slopeYAt(sl, x, z);
    if (sy <= lim && sy > best) best = sy;
  }
  for (var bi2 = 0; bi2 < w.barrels.length; bi2++) {
    var ba = w.barrels[bi2];
    if (!ba.alive || ba.state !== 0) continue;
    var ddx = x - ba.x;
    var ddz = z - ba.z;
    var rr = r + TAC.BARREL_R;
    var d2 = ddx * ddx + ddz * ddz;
    if (d2 >= rr * rr) continue;
    var top = ba.y + TAC.BARREL_H;
    if (top <= lim && top > best) best = top;
  }
  return best;
};

// first cracked wall (kind 3, alive) the segment hits, or -1. Only gatling
// rounds and explosions damage these — rifle and scope fire just thuds.
TacWorld.prototype.segCrackedHit = function (x0, y0, z0, x1, y1, z1) {
  var w = this;
  var dx = x1 - x0;
  var dy = y1 - y0;
  var dz = z1 - z0;
  for (var i = 0; i < w.boxes.length; i++) {
    var b = w.boxes[i];
    if (b.kind !== 3) continue;
    if (!b.alive) continue;
    var t0 = 0.0, t1 = 1.0;
    if (dx > 0.000001 || dx < -0.000001) {
      var txa = (b.x0 - x0) / dx;
      var txb = (b.x1 - x0) / dx;
      var txmin = txa < txb ? txa : txb;
      var txmax = txa < txb ? txb : txa;
      if (txmin > t0) t0 = txmin;
      if (txmax < t1) t1 = txmax;
    } else if (x0 <= b.x0 || x0 >= b.x1) continue;
    if (dz > 0.000001 || dz < -0.000001) {
      var tza = (b.z0 - z0) / dz;
      var tzb = (b.z1 - z0) / dz;
      var tzmin = tza < tzb ? tza : tzb;
      var tzmax = tza < tzb ? tzb : tza;
      if (tzmin > t0) t0 = tzmin;
      if (tzmax < t1) t1 = tzmax;
    } else if (z0 <= b.z0 || z0 >= b.z1) continue;
    if (dy > 0.000001 || dy < -0.000001) {
      var tya = (b.yb - y0) / dy;
      var tyb = (b.h - y0) / dy;
      var tymin = tya < tyb ? tya : tyb;
      var tymax = tya < tyb ? tyb : tya;
      if (tymin > t0) t0 = tymin;
      if (tymax < t1) t1 = tymax;
    } else if (y0 <= b.yb || y0 >= b.h) continue;
    if (t0 <= t1) return i;
  }
  return -1;
};

TacWorld.prototype.breakWall = function (bi) {
  var w = this;
  var b = w.boxes[bi];
  b.alive = false;
  if (!w.events.wallBreaks) w.events.wallBreaks = [];
  w.events.wallBreaks.push({ i: bi, x: (b.x0 + b.x1) / 2.0, y: b.h / 2.0, z: (b.z0 + b.z1) / 2.0 });
};

// the slope the circle stands on at (x,z,y), or null
TacWorld.prototype.slopeUnder = function (x, z, y) {
  for (var s = 0; s < this.slopes.length; s++) {
    var sl = this.slopes[s];
    if (x < sl.x0 || x > sl.x1 || z < sl.z0 || z > sl.z1) continue;
    var sy = this.slopeYAt(sl, x, z);
    var dy = y - sy;
    if (dy > -0.1 && dy < 0.12) return sl;
  }
  return null;
};

// resolve horizontal movement of a circle (radius r, feet y, height h) against
// boxes + idle barrels + arena bounds. Moves the full step, then iteratively
// pushes the circle out along each contact normal: faces give clean wall
// slides, corners deflect smoothly around (no snagging). Returns {x, z}.
TacWorld.prototype.moveCircle = function (x, z, y, r, h, dx, dz) {
  var w = this;
  var blockAbove = y + w.stepUp; // a box blocks if its top is above this and its base (0) is below head
  var nx = x + dx;
  var nz = z + dz;
  for (var iter = 0; iter < 3; iter++) {
    var pushed = { any: false };
    w.forBoxesIn(nx - r, nz - r, nx + r, nz + r, function (bi) {
      var b = w.boxes[bi];
      if (!b.alive) return;
      if (b.h <= blockAbove) return;
      if (b.yb >= y + h) return; // floating span is above the mover: pass beneath
      var cx = nx < b.x0 ? b.x0 : (nx > b.x1 ? b.x1 : nx);
      var cz = nz < b.z0 ? b.z0 : (nz > b.z1 ? b.z1 : nz);
      var ox = nx - cx;
      var oz = nz - cz;
      var d2 = ox * ox + oz * oz;
      if (d2 >= r * r) return;
      if (d2 > 0.000001) {
        var d = Math.sqrt(d2);
        var push = r - d;
        var pux = ox / d;
        var puz = oz / d;
        nx = nx + pux * push;
        nz = nz + puz * push;
      } else {
        // center is inside the box: escape along the shallowest face
        var lx = nx - b.x0;
        var rx = b.x1 - nx;
        var lz = nz - b.z0;
        var rz = b.z1 - nz;
        if (lx <= rx && lx <= lz && lx <= rz) nx = b.x0 - r;
        else if (rx <= lz && rx <= rz) nx = b.x1 + r;
        else if (lz <= rz) nz = b.z0 - r;
        else nz = b.z1 + r;
      }
      pushed.any = true;
    });
    // staircases (slopes) are solid where a tread is too tall to step onto: the
    // raised sides and back become walls so nobody walks INTO the stairs and ends
    // up buried under a tread (unshootable). The low ramp entry — treads within
    // STEP_UP of the feet — stays open, so climbing works from any angle as before.
    for (var si = 0; si < w.slopes.length; si++) {
      var sl2 = w.slopes[si];
      // nearest point on the slope footprint to the circle center
      var scx = nx < sl2.x0 ? sl2.x0 : (nx > sl2.x1 ? sl2.x1 : nx);
      var scz = nz < sl2.z0 ? sl2.z0 : (nz > sl2.z1 ? sl2.z1 : nz);
      var sox = nx - scx;
      var soz = nz - scz;
      var sd2 = sox * sox + soz * soz;
      if (sd2 >= r * r) continue; // not touching the footprint
      // tread top at the contact point; if we can step onto it, don't block
      var tread = w.slopeYAt(sl2, scx, scz);
      if (tread <= blockAbove) continue;
      if (y >= tread) continue; // already standing on/above this tread
      if (sd2 > 0.000001) {
        var sdd = Math.sqrt(sd2);
        var spush = r - sdd;
        nx = nx + (sox / sdd) * spush;
        nz = nz + (soz / sdd) * spush;
      } else {
        // center inside the footprint: escape along the shallowest face
        var slx = nx - sl2.x0;
        var srx = sl2.x1 - nx;
        var slz = nz - sl2.z0;
        var srz = sl2.z1 - nz;
        if (slx <= srx && slx <= slz && slx <= srz) nx = sl2.x0 - r;
        else if (srx <= slz && srx <= srz) nx = sl2.x1 + r;
        else if (slz <= srz) nz = sl2.z0 - r;
        else nz = sl2.z1 + r;
      }
      pushed.any = true;
    }
    // idle barrels are solid cylinders (also blockable when their top is too high to step on)
    for (var i = 0; i < w.barrels.length; i++) {
      var ba = w.barrels[i];
      if (!ba.alive || ba.state !== 0) continue;
      var top = ba.y + TAC.BARREL_H;
      if (top <= blockAbove) continue;
      if (y >= top) continue;
      var ddx = nx - ba.x;
      var ddz = nz - ba.z;
      var rr = r + TAC.BARREL_R;
      var d2b = ddx * ddx + ddz * ddz;
      if (d2b < rr * rr && d2b > 0.000001) {
        var d3 = Math.sqrt(d2b);
        var push2 = rr - d3;
        var pux2 = ddx / d3;
        var puz2 = ddz / d3;
        nx = nx + pux2 * push2;
        nz = nz + puz2 * push2;
        pushed.any = true;
      }
    }
    if (!pushed.any) break;
  }
  if (nx < r) nx = r;
  if (nx > w.arenaW - r) nx = w.arenaW - r;
  if (nz < r) nz = r;
  if (nz > w.arenaD - r) nz = w.arenaD - r;
  return { x: nx, z: nz };
};

// lowest box UNDERSIDE above fromY over the footprint (jump head-bump)
TacWorld.prototype.ceilingY = function (x, z, r, fromY) {
  var w = this;
  var best = 1.0e9;
  w.forBoxesIn(x - r, z - r, x + r, z + r, function (bi) {
    var b = w.boxes[bi];
    if (!b.alive) return;
    if (b.yb < fromY + 0.2) return;
    if (x + r <= b.x0 || x - r >= b.x1 || z + r <= b.z0 || z - r >= b.z1) return;
    if (b.yb < best) best = b.yb;
  });
  return best;
};

// segment (x0,y0,z0)->(x1,y1,z1) vs static boxes; true if blocked.
TacWorld.prototype.segBlocked = function (x0, y0, z0, x1, y1, z1) {
  var w = this;
  var lo = { hit: false };
  var minX = x0 < x1 ? x0 : x1;
  var maxX = x0 < x1 ? x1 : x0;
  var minZ = z0 < z1 ? z0 : z1;
  var maxZ = z0 < z1 ? z1 : z0;
  var dx = x1 - x0;
  var dy = y1 - y0;
  var dz = z1 - z0;
  if (w.pits.length && w.pitRimBlocked(x0, y0, z0, x1, y1, z1)) return true;
  w.forBoxesIn(minX, minZ, maxX, maxZ, function (bi) {
    if (lo.hit) return;
    var b = w.boxes[bi];
    if (!b.alive) return;
    // slab test on x, z, and y (0..b.h)
    var t0 = 0.0, t1 = 1.0;
    if (dx > 0.000001 || dx < -0.000001) {
      var txa = (b.x0 - x0) / dx;
      var txb = (b.x1 - x0) / dx;
      var txmin = txa < txb ? txa : txb;
      var txmax = txa < txb ? txb : txa;
      if (txmin > t0) t0 = txmin;
      if (txmax < t1) t1 = txmax;
    } else if (x0 <= b.x0 || x0 >= b.x1) return;
    if (dz > 0.000001 || dz < -0.000001) {
      var tza = (b.z0 - z0) / dz;
      var tzb = (b.z1 - z0) / dz;
      var tzmin = tza < tzb ? tza : tzb;
      var tzmax = tza < tzb ? tzb : tza;
      if (tzmin > t0) t0 = tzmin;
      if (tzmax < t1) t1 = tzmax;
    } else if (z0 <= b.z0 || z0 >= b.z1) return;
    if (dy > 0.000001 || dy < -0.000001) {
      var tya = (b.yb - y0) / dy;
      var tyb = (b.h - y0) / dy;
      var tymin = tya < tyb ? tya : tyb;
      var tymax = tya < tyb ? tyb : tya;
      if (tymin > t0) t0 = tymin;
      if (tymax < t1) t1 = tymax;
    } else if (y0 <= b.yb || y0 >= b.h) return;
    if (t0 <= t1) lo.hit = true;
  });
  return lo.hit;
};

// pit rim test: does the segment cross a pit boundary while below grade
// (y < 0)? That is the dirt wall of the trench/channel. Returns true if any
// crossing of any pit's rectangle happens underground.
TacWorld.prototype.pitRimBlocked = function (x0, y0, z0, x1, y1, z1) {
  var w = this;
  var dx = x1 - x0;
  var dy = y1 - y0;
  var dz = z1 - z0;
  for (var i = 0; i < w.pits.length; i++) {
    var pp = w.pits[i];
    var t0 = 0.0, t1 = 1.0;
    if (dx > 0.000001 || dx < -0.000001) {
      var ta = (pp.x0 - x0) / dx;
      var tb = (pp.x1 - x0) / dx;
      var mn = ta < tb ? ta : tb;
      var mx = ta < tb ? tb : ta;
      if (mn > t0) t0 = mn;
      if (mx < t1) t1 = mx;
    } else if (x0 <= pp.x0 || x0 >= pp.x1) continue;
    if (dz > 0.000001 || dz < -0.000001) {
      var tc = (pp.z0 - z0) / dz;
      var td = (pp.z1 - z0) / dz;
      var mn2 = tc < td ? tc : td;
      var mx2 = tc < td ? td : tc;
      if (mn2 > t0) t0 = mn2;
      if (mx2 < t1) t1 = mx2;
    } else if (z0 <= pp.z0 || z0 >= pp.z1) continue;
    if (t0 > t1) continue;
    // entering the pit's footprint below grade = hitting the near wall;
    // leaving it below grade = hitting the far wall
    if (t0 > 0.0 && t0 < 1.0) {
      var yEnter = y0 + dy * t0;
      if (yEnter < 0.0 && yEnter > -pp.depth - 3.0) return true;
    }
    if (t1 > 0.0 && t1 < 1.0) {
      var yExit = y0 + dy * t1;
      if (yExit < 0.0 && yExit > -pp.depth - 3.0) return true;
    }
  }
  return false;
};

// segment vs vertical cylinder (cx,cz, radius cr, feet cy, height ch).
// Returns t in [0,1] of first hit, or -1.
function tacSegCylinder(x0, y0, z0, x1, y1, z1, cx, cy, cz, cr, ch) {
  var dx = x1 - x0;
  var dz = z1 - z0;
  var fx = x0 - cx;
  var fz = z0 - cz;
  var a = dx * dx + dz * dz;
  var b = 2.0 * (fx * dx + fz * dz);
  var c = fx * fx + fz * fz - cr * cr;
  // [tin, tout] = the parameter interval the ray spends inside the infinite
  // vertical cylinder (XZ circle), then intersect with the height slab. The old
  // code only tested the wall-entry height, so a shot whose entry point grazed
  // above the head was rejected even when the body was hit further along the ray
  // — the "aimed dead-on but nothing dies" bug.
  var tin, tout;
  if (a < 0.0000001) {
    if (c > 0.0) return -1.0;
    tin = 0.0; tout = 1.0;
  } else {
    var disc = b * b - 4.0 * a * c;
    if (disc < 0.0) return -1.0;
    var sq = Math.sqrt(disc);
    tin = (-b - sq) / (2.0 * a);
    tout = (-b + sq) / (2.0 * a);
  }
  if (tin < 0.0) tin = 0.0;
  if (tout > 1.0) tout = 1.0;
  if (tin > tout) return -1.0;
  var dy = y1 - y0;
  var loY = cy, hiY = cy + ch;
  if (dy > 0.0000001 || dy < -0.0000001) {
    var ta = (loY - y0) / dy;
    var tb = (hiY - y0) / dy;
    var tlo = ta < tb ? ta : tb;
    var thi = ta < tb ? tb : ta;
    if (tlo > tin) tin = tlo;
    if (thi < tout) tout = thi;
    if (tin > tout) return -1.0;
  } else {
    if (y0 < loY || y0 > hiY) return -1.0;
  }
  return tin < 0.0 ? 0.0 : tin;
}

TacWorld.prototype.addNoise = function (x, z, r) {
  this.noises.push({ x: x, z: z, r: r });
  if (!this.events.noises) this.events.noises = [];
  this.events.noises.push({ x: x, z: z, r: r });
};

// --- step --------------------------------------------------------------------

TacWorld.prototype.step = function (input) {
  var w = this;
  w.events = {};
  w.noises = [];
  if (w.dead || w.clearedFlag || w.timedOutFlag) return w.events;
  w.tick++;
  w.sneaking = (input.b & 4) !== 0;

  w.stepPlayer(input);
  w.stepBullets();
  w.stepBombs();
  w.stepGrenades();
  w.stepBarrels();
  w.stepMines();
  w.stepSlides();
  w.stepMedkits();
  w.stepIntel();
  w.stepLights();
  w.stepEnemies();

  var goalMet = false;
  if (w.goalType === 1) {
    if (w.intelLeft <= 0 && w.exitZone !== null &&
        w.px >= w.exitZone.x0 && w.px <= w.exitZone.x1 &&
        w.pz >= w.exitZone.z0 && w.pz <= w.exitZone.z1 &&
        w.py > -0.5 && w.py < 1.0) goalMet = true;
  } else {
    if (w.enemiesLeft <= 0) goalMet = true;
  }
  if (goalMet && !w.dead) {
    w.clearedFlag = true;
    w.events.cleared = true;
  } else if (w.tick >= w.maxTicks) {
    w.timedOutFlag = true;
    w.events.timedOut = true;
  }
  return w.events;
};

TacWorld.prototype.stepPlayer = function (input) {
  var w = this;
  w.yawQ = input.yawQ & 65535;
  var p = input.pitchQ;
  if (p > TAC.PITCH_MAX) p = TAC.PITCH_MAX;
  if (p < TAC.PITCH_MIN) p = TAC.PITCH_MIN;
  w.pitchQ = p;
  if (w.fireCd > 0) w.fireCd--;
  if (w.hurtCd > 0) w.hurtCd--;
  if (w.fireFlash > 0) w.fireFlash--;
  if (w.scopeCd > 0) w.scopeCd--;

  // --- captured drone piloting -------------------------------------------
  // While piloting, the BODY stands frozen (and stays vulnerable!); movement
  // keys steer the drone, FIRE detonates it, the drone key recalls it.
  var droneEdge = (input.b & 8) !== 0 && (w.prevB & 8) === 0;
  var fireEdge = (input.b & 2) !== 0 && (w.prevB & 2) === 0;
  var grenEdge = (input.b & 16) !== 0 && (w.prevB & 16) === 0;
  var scopeEdge = (input.b & 32) !== 0 && (w.prevB & 32) === 0;
  w.prevB = input.b;

  // Fire gate: once piloting/scoping ends, the still-held FIRE must be released
  // before the body will shoot again — otherwise the detonation/scope press
  // spills straight into held-fire auto-shooting. Released FIRE clears the gate.
  if ((input.b & 2) === 0) w.fireGate = false;
  if (w.grenadeCd > 0) w.grenadeCd--;
  w.playerJammed = w.inActiveJammer(w.px, w.pz);

  // --- scoped sniping: manual aim, no auto lock, 3 shots per pickup ------
  if (scopeEdge && !w.pilot) {
    if (w.scoped) {
      w.scoped = false;
      w.events.scopeOff = true;
    } else if (w.onGround) {
      w.scoped = true;
      // start the scope from where the player is already looking: yaw = current
      // facing, pitch = the live camera pitch (carried in the recorded input, so
      // this stays replay-deterministic)
      w.aimYawQ = w.faceQ;
      w.aimPitchQ = input.pitchQ;
      if (w.aimPitchQ > TAC.PITCH_MAX) w.aimPitchQ = TAC.PITCH_MAX;
      if (w.aimPitchQ < TAC.PITCH_MIN) w.aimPitchQ = TAC.PITCH_MIN;
      w.events.scopeOn = true;
    }
  }
  if (w.scoped) {
    // Direct-drag aiming: the reticle follows the camera angle carried in the
    // recorded input (yawQ/pitchQ), so dragging ANYWHERE on screen moves the aim
    // 1:1 like a standard FPS sniper — no directional stick. Absolute angles keep
    // this fully replay-deterministic.
    w.aimYawQ = input.yawQ & 65535;
    w.aimPitchQ = input.pitchQ;
    if (w.aimPitchQ > TAC.PITCH_MAX) w.aimPitchQ = TAC.PITCH_MAX;
    if (w.aimPitchQ < TAC.PITCH_MIN) w.aimPitchQ = TAC.PITCH_MIN;
    if (fireEdge && w.scopeShots > 0) {
      w.fireScopedShot();
      w.scopeShots--;
    }
    if ((input.b & 2) !== 0) w.fireGate = true; // held FIRE while scoped must not spill into body fire on scope-off
    w.faceQ = w.aimYawQ;
    // periscope style: in a pit you stay crouched and rest the scope on the
    // lip — hidden while sniping; the shot's NOISE is what gives you away
    w.crouched = w.py < -0.45;
    w.lockTarget = -1;
    return; // aiming down the scope roots the body
  }
  if (droneEdge) {
    if (!w.pilot && w.droneUses > 0 && w.onGround) {
      w.droneUses--;
      w.pilot = { x: w.px, y: w.py + 1.2, z: w.pz, battery: TAC.PILOT_BATTERY, dive: -1, yawQ: w.faceQ };
      w.events.droneLaunch = true;
    }
  }
  if (w.pilot) {
    var pd = w.pilot;
    if ((input.b & 2) !== 0) w.fireGate = true; // held FIRE while piloting must not spill into body fire on recall/detonate
    // the EMP veil fries YOUR drone exactly like an enemy's — symmetric. It
    // dies and detonates the moment it crosses in (no homing payoff); shoot
    // the switch console first if you need that airspace.
    if (w.inActiveJammer(pd.x, pd.z)) {
      w.events.jamZap = { x: pd.x, y: pd.y, z: pd.z };
      w.explodeAt(pd.x, pd.y, pd.z, TAC.PILOT_BLAST_R, 2);
      w.events.droneDead = true;
      w.pilot = null;
      w.lockTarget = -1;
      return;
    }
    // committed homing dive: the drone chases its marked target on its own
    if (pd.dive >= 0) {
      var den = w.enemies[pd.dive];
      var tx3 = pd.x, ty3 = 0.4, tz3 = pd.z;
      if (den && den.alive) {
        tx3 = den.x;
        ty3 = den.y + den.h * 0.5;
        tz3 = den.z;
      }
      var ddx3 = tx3 - pd.x;
      var ddy3 = ty3 - pd.y;
      var ddz3 = tz3 - pd.z;
      var dl3 = Math.sqrt(ddx3 * ddx3 + ddy3 * ddy3 + ddz3 * ddz3);
      var dstep3 = TAC.PILOT_DIVE_SPEED * TAC.TICK;
      if (dl3 <= 1.0 || dl3 <= dstep3) {
        w.explodeAt(tx3, ty3, tz3, TAC.PILOT_BLAST_R, 2);
        w.events.droneDetonate = true;
        w.pilot = null;
      } else {
        pd.x = pd.x + (ddx3 / dl3) * dstep3;
        pd.y = pd.y + (ddy3 / dl3) * dstep3;
        pd.z = pd.z + (ddz3 / dl3) * dstep3;
      }
      w.lockTarget = -1;
      return;
    }
    if (input.m !== 255) {
      var pq = (w.yawQ + input.m * 512) & 65535;
      var pvx = tacSinQ(pq);
      var pvz = tacCosQ(pq);
      pd.yawQ = pq; // the drone points where it flies
      var pstep = TAC.PILOT_SPEED * TAC.TICK;
      var pres = w.moveCircle(pd.x, pd.z, pd.y, 0.5, 0.5, pvx * pstep, pvz * pstep);
      pd.x = pres.x;
      pd.z = pres.z;
    }
    var pg = w.groundY(pd.x, pd.z, 1000.0, 0.5);
    var pWant = pg + TAC.PILOT_ALT;
    var pRise = 3.0 * TAC.TICK;
    if (pd.y < pWant - pRise) pd.y = pd.y + pRise;
    else if (pd.y > pWant + pRise) pd.y = pd.y - pRise;
    else pd.y = pWant;
    pd.battery--;
    // homing lock: nearest living enemy within the lock radius (horizontal)
    var pBest = -1;
    var pBest2 = TAC.PILOT_LOCK_R * TAC.PILOT_LOCK_R;
    for (var pe = 0; pe < w.enemies.length; pe++) {
      var pen = w.enemies[pe];
      if (!pen.alive) continue;
      var phx = pen.x - pd.x;
      var phz = pen.z - pd.z;
      var ph2 = phx * phx + phz * phz;
      if (ph2 < pBest2) { pBest2 = ph2; pBest = pe; }
    }
    if (fireEdge) {
      if (pBest >= 0) {
        pd.dive = pBest; // homing dive onto the marked target
        w.events.droneDive = true;
      } else {
        w.explodeAt(pd.x, pg + 0.9, pd.z, TAC.PILOT_BLAST_R, 2);
        w.events.droneDetonate = true;
        w.pilot = null;
      }
    } else if (pd.battery <= 0) {
      w.pilot = null;
      w.events.droneDead = true;
    }
    // the regular lock marker shows the homing target while piloting
    w.lockTarget = pBest;
    w.lockKind = 0;
    return; // the body does nothing while piloting
  }

  // grenade: standard equipment, recharge-gated. Tossed along the facing in a
  // flat arc; explodes on impact (ground, wall, or enemy).
  if (grenEdge && w.grenadeCd === 0) {
    if (w.py < -0.45) w.fireFlash = 15; // the throwing motion exposes you briefly
    var ggx = tacSinQ(w.faceQ);
    var ggz = tacCosQ(w.faceQ);
    w.grenades.push({
      x: w.px + ggx * 0.5, y: w.py + TAC.CHEST_H, z: w.pz + ggz * 0.5,
      vx: ggx * TAC.GRENADE_SPEED_H, vy: TAC.GRENADE_SPEED_V, vz: ggz * TAC.GRENADE_SPEED_H,
      alive: true
    });
    w.grenadeCd = TAC.GRENADE_CD;
    w.events.grenadeThrow = true;
  }

  var sneak = (input.b & 4) !== 0;
  // trenches auto-crouch you the moment you're down in them; firing pops you
  // up for a moment (fireFlash). Sneak-crouching manually works in any pit
  // (e.g. submerging in a river).
  var inPitLow = w.py < -0.45;
  var autoCrouch = inPitLow && w.trenchAt(w.px, w.pz) >= 0;
  var fireHeld = (input.b & 2) !== 0;
  // crouch: automatic in a trench, OR held via SNEAK anywhere (incl. flat
  // ground). Firing stands you up briefly (fireFlash); in a pit, holding FIRE
  // also stands you to shoot over the rim.
  w.crouched = (autoCrouch || sneak) && w.fireFlash === 0 && !(fireHeld && inPitLow);
  if (w.crouched) sneak = true; // crouched movement is a quiet crawl
  var moving = input.m !== 255;
  var dx = 0.0, dz = 0.0;

  // speed ramp: a tapped key barely nudges; a held key winds up to full speed
  if (moving) { if (w.moveT < TAC.MOVE_RAMP_TICKS) w.moveT++; } else { w.moveT = 0; }
  var rampFrac = w.moveT / TAC.MOVE_RAMP_TICKS;
  var rampSpan = 1.0 - TAC.MOVE_RAMP_MIN;
  var ramp = TAC.MOVE_RAMP_MIN + rampSpan * rampFrac;

  if (moving) {
    var mq2 = (w.yawQ + input.m * 512) & 65535;
    w.faceQ = mq2; // the character turns to face where it moves; aim follows facing
    var mvx2 = tacSinQ(mq2);
    var mvz2 = tacCosQ(mq2);
    var spd2 = sneak ? TAC.WALK_SPEED : TAC.RUN_SPEED;
    var spd2R = spd2 * ramp;
    var stepd2 = spd2R * TAC.TICK;
    dx = mvx2 * stepd2;
    dz = mvz2 * stepd2;
    if (!sneak && w.onGround && (w.tick % TAC.NOISE_RUN_EVERY) === 0) w.addNoise(w.px, w.pz, TAC.NOISE_RUN_R);
  }

  if (dx !== 0.0 || dz !== 0.0) {
    // wading slows you ONLY when you are actually down in the water — walking
    // a deck or bridge built OVER a channel is dry land
    if (w.py < -0.2 && w.inRiver(w.px, w.pz)) {
      dx = dx * TAC.RIVER_MUL;
      dz = dz * TAC.RIVER_MUL;
    }
    var res = w.moveCircle(w.px, w.pz, w.py, TAC.PLAYER_R, TAC.PLAYER_H, dx, dz);
    w.px = res.x;
    w.pz = res.z;
  }

  // jump (edge)
  if ((input.b & 1) !== 0 && w.onGround) {
    w.vy = TAC.JUMP_V;
    w.onGround = false;
    w.events.jumped = true;
  }

  // vertical
  var dv = TAC.GRAVITY * TAC.TICK;
  w.vy = w.vy - dv;
  var dyy = w.vy * TAC.TICK;
  w.py = w.py + dyy;
  if (w.vy > 0.0) {
    var ceil = w.ceilingY(w.px, w.pz, TAC.PLAYER_R, w.py - dyy);
    if (w.py + TAC.PLAYER_H > ceil) {
      w.py = ceil - TAC.PLAYER_H;
      w.vy = 0.0;
    }
  }
  var g = w.groundY(w.px, w.pz, w.onGround ? w.py + 0.01 : w.py, TAC.PLAYER_R);
  if (w.py <= g && w.vy <= 0.0) {
    if (!w.onGround && w.vy < -6.0) {
      w.addNoise(w.px, w.pz, TAC.NOISE_LAND_R);
      w.events.landed = true;
    }
    w.py = g;
    w.vy = 0.0;
    w.onGround = true;
  } else if (w.py > g + 0.02) {
    w.onGround = false;
  }

  // auto lock-on: pick the enemy closest to the view center, within the lock
  // cone/range and with clear line of sight. Fully deterministic (sim state +
  // recorded view angles only), so replays reproduce every locked shot.
  w.updateLock();

  // fire (held) — rounds leave the actual gun muzzle (forward-right of the
  // body, at the gun's height), not the body center
  if (!w.fireGate && (input.b & 2) !== 0 && w.fireCd === 0 && w.ammo !== 0) {
    if (w.py < -0.45) w.fireFlash = 8; // tiny settle-back tail; holding FIRE keeps you up
    var mfx = tacSinQ(w.faceQ);
    var mfz = tacCosQ(w.faceQ);
    var mrx = mfz;
    var mrz = -mfx;
    var muzX = w.px + mfx * 0.7 + mrx * 0.25;
    var muzY = w.py + (w.crouched && w.fireFlash === 0 ? 0.6 : 1.05);
    var muzZ = w.pz + mfz * 0.7 + mrz * 0.25;
    var dirx, diry, dirz;
    if (w.lockTarget >= 0) {
      var ltx, lty, ltz;
      if (w.lockKind === 1) {
        var lb = w.barrels[w.lockTarget];
        ltx = lb.x;
        lty = lb.y + 0.5;
        ltz = lb.z;
      } else if (w.lockKind === 2) {
        var lm = w.mines[w.lockTarget];
        ltx = lm.x;
        lty = lm.y + 0.1;
        ltz = lm.z;
      } else if (w.lockKind === 3) {
        var lsd = w.slides[w.lockTarget];
        ltx = lsd.postX;
        lty = lsd.postY + 0.6;
        ltz = lsd.postZ;
      } else if (w.lockKind === 4) {
        var lsw = w.switches[w.lockTarget];
        ltx = lsw.x;
        lty = lsw.y + 0.6;
        ltz = lsw.z;
      } else {
        var lt = w.enemies[w.lockTarget];
        ltx = lt.x;
        lty = lt.y + lt.h * 0.6;
        ltz = lt.z;
      }
      var tdx = ltx - muzX;
      var tdy = lty - muzY;
      var tdz = ltz - muzZ;
      var tl = Math.sqrt(tdx * tdx + tdy * tdy + tdz * tdz);
      dirx = tdx / tl;
      diry = tdy / tl;
      dirz = tdz / tl;
    } else {
      // no lock: fire level along the character's facing
      dirx = mfx;
      diry = 0.0;
      dirz = mfz;
    }
    // the muzzle (~1.05 m) sits below a standard rock (1.4 m), so cover
    // blocks outgoing fire exactly like it blocks vision; jump to shoot over
    w.bullets.push({
      x: muzX, y: muzY, z: muzZ,
      sx: muzX, sy: muzY, sz: muzZ,
      vx: dirx * TAC.BULLET_SPEED, vy: diry * TAC.BULLET_SPEED, vz: dirz * TAC.BULLET_SPEED,
      ttl: TAC.BULLET_TTL, fromPlayer: true, alive: true
    });
    w.fireCd = TAC.FIRE_CD;
    if (w.ammo > 0) w.ammo--;
    w.shotsFired++;
    w.addNoise(w.px, w.pz, TAC.NOISE_SHOT_R);
    w.events.shot = true;
  }
};

// Auto lock-on considers enemies, explosive barrels and mines: whichever is
// closest to the CHARACTER'S FACING direction (horizontal cone, 3D range,
// line of sight) wins. lockKind: 0 = enemy, 1 = barrel, 2 = mine.
TacWorld.prototype.updateLock = function () {
  var w = this;
  var fx = tacSinQ(w.faceQ);
  var fz = tacCosQ(w.faceQ);
  var ex = w.px;
  // lock LOS runs along the actual bullet path (muzzle at chest height), so a
  // lock is only offered where the shot can really connect
  var ey = w.py + TAC.CHEST_H;
  var ez = w.pz;
  // priority: an ENEMY inside the aim cone always wins; among candidates of the
  // same class the CLOSEST one is chosen (was: tightest angle). tier 0 = enemy,
  // tier 1 = objects (barrel/mine/slide-post/switch).
  var best = -1;
  var bestKind = 0;
  var bestTier = 99;
  var bestD2 = 1.0e18;
  var cos = TAC.LOCK_COS;
  var range2 = TAC.LOCK_RANGE * TAC.LOCK_RANGE;
  var consider = function (i, kind, tier, cx, cy, cz) {
    if (tier > bestTier) return; // a worse class can never beat a better one
    var tx = cx - ex;
    var ty = cy - ey;
    var tz = cz - ez;
    var d2 = tx * tx + ty * ty + tz * tz;
    if (d2 > range2 || d2 < 0.0001) return;
    var dh2 = tx * tx + tz * tz;
    if (dh2 < 0.0001) return;
    var dh = Math.sqrt(dh2);
    var dot = (fx * tx + fz * tz) / dh;
    if (dot <= cos) return; // outside the aim cone
    if (tier === bestTier && d2 >= bestD2) return; // farther than the current pick
    if (w.segBlocked(ex, ey, ez, cx, cy, cz)) return;
    best = i;
    bestKind = kind;
    bestTier = tier;
    bestD2 = d2;
  };
  for (var i = 0; i < w.enemies.length; i++) {
    var en = w.enemies[i];
    if (!en.alive) continue;
    consider(i, 0, 0, en.x, en.y + tacEnemyH(en) * 0.6, en.z);
  }
  for (var b = 0; b < w.barrels.length; b++) {
    var ba = w.barrels[b];
    if (!ba.alive) continue;
    consider(b, 1, 1, ba.x, ba.y + 0.5, ba.z);
  }
  for (var m = 0; m < w.mines.length; m++) {
    var mi = w.mines[m];
    if (!mi.alive) continue;
    consider(m, 2, 1, mi.x, mi.y + 0.1, mi.z);
  }
  for (var sl = 0; sl < w.slides.length; sl++) {
    var sld2 = w.slides[sl];
    if (sld2.triggered) continue;
    consider(sl, 3, 1, sld2.postX, sld2.postY + 0.6, sld2.postZ);
  }
  for (var sw = 0; sw < w.switches.length; sw++) {
    var swc = w.switches[sw];
    if (!swc.alive) continue;
    consider(sw, 4, 1, swc.x, swc.y + 0.6, swc.z);
  }
  w.lockTarget = best;
  w.lockKind = bestKind;
};

// earliest t where the segment enters the box (2.0 = no hit)
function tacSegBoxT(x0, y0, z0, x1, y1, z1, b) {
  var dx = x1 - x0;
  var dy = y1 - y0;
  var dz = z1 - z0;
  var t0 = 0.0, t1 = 1.0;
  if (dx > 0.000001 || dx < -0.000001) {
    var ta = (b.x0 - x0) / dx;
    var tb = (b.x1 - x0) / dx;
    var mn = ta < tb ? ta : tb;
    var mx = ta < tb ? tb : ta;
    if (mn > t0) t0 = mn;
    if (mx < t1) t1 = mx;
  } else if (x0 <= b.x0 || x0 >= b.x1) return 2.0;
  if (dz > 0.000001 || dz < -0.000001) {
    var tc = (b.z0 - z0) / dz;
    var td = (b.z1 - z0) / dz;
    var mn2 = tc < td ? tc : td;
    var mx2 = tc < td ? td : tc;
    if (mn2 > t0) t0 = mn2;
    if (mx2 < t1) t1 = mx2;
  } else if (z0 <= b.z0 || z0 >= b.z1) return 2.0;
  if (dy > 0.000001 || dy < -0.000001) {
    var te = (b.yb - y0) / dy;
    var tf = (b.h - y0) / dy;
    var mn3 = te < tf ? te : tf;
    var mx3 = te < tf ? tf : te;
    if (mn3 > t0) t0 = mn3;
    if (mx3 < t1) t1 = mx3;
  } else if (y0 <= b.yb || y0 >= b.h) return 2.0;
  if (t0 <= t1) return t0;
  return 2.0;
}

// manual long-range shot: first thing hit along the reticle wins
TacWorld.prototype.fireScopedShot = function () {
  var w = this;
  var cp = tacCosQ(w.aimPitchQ);
  var sp = tacSinQ(w.aimPitchQ);
  var sy = tacSinQ(w.aimYawQ);
  var cy = tacCosQ(w.aimYawQ);
  var ox = w.px;
  var oy = w.py + (w.crouched ? 1.05 : TAC.EYE_H); // crouched: barrel just over the lip
  var oz = w.pz;
  var ex = ox + sy * cp * TAC.SCOPE_RANGE;
  var ey = oy + sp * TAC.SCOPE_RANGE;
  var ez = oz + cy * cp * TAC.SCOPE_RANGE;
  var bestT = 2.0;
  var minX = ox < ex ? ox : ex;
  var maxX = ox < ex ? ex : ox;
  var minZ = oz < ez ? oz : ez;
  var maxZ = oz < ez ? ez : oz;
  w.forBoxesIn(minX, minZ, maxX, maxZ, function (bi) {
    if (!w.boxes[bi].alive) return;
    var t = tacSegBoxT(ox, oy, oz, ex, ey, ez, w.boxes[bi]);
    if (t < bestT) bestT = t;
  });
  if (w.pits.length && w.pitRimBlocked(ox, oy, oz, ex, ey, ez)) {
    // conservatively treat a rim hit as a full stop just past the rim: we
    // cannot get its exact t cheaply, so scoped shots never cross pit walls
    bestT = -1.0;
  }
  if (bestT === -1.0) { w.addNoise(w.px, w.pz, TAC.NOISE_SHOT_R); w.events.scopeShot = true; return; }
  var hitEnemy = -1;
  for (var e = 0; e < w.enemies.length; e++) {
    var en = w.enemies[e];
    if (!en.alive) continue;
    if (w.shieldUp(en)) {
      var sfx2 = tacSinQ(en.yawQ);
      var sfz2 = tacCosQ(en.yawQ);
      if ((ex - ox) * sfx2 + (ez - oz) * sfz2 < 0.0) {
        var ts = tacSegCylinder(ox, oy, oz, ex, ey, ez, en.x + sfx2 * 0.45, en.y, en.z + sfz2 * 0.45, TAC.SHIELD_BLOCK_R, TAC.SHIELD_H);
        if (ts >= 0.0 && ts < bestT) { bestT = ts; hitEnemy = -1; w.events.shieldBlock = true; continue; }
      }
    }
    // extend the cylinder up to the rendered head so a crosshair-on-head shot
    // connects (see SCOPE_HEAD_FRAC)
    var hitH = tacEnemyH(en) * (1.0 + TAC.SCOPE_HEAD_FRAC);
    var te = tacSegCylinder(ox, oy, oz, ex, ey, ez, en.x, en.y, en.z, en.r, hitH);
    if (te >= 0.0 && te < bestT) { bestT = te; hitEnemy = e; }
  }
  w.addNoise(w.px, w.pz, TAC.NOISE_SHOT_R);
  w.events.scopeShot = true;
  if (hitEnemy >= 0) w.damageEnemy(w.enemies[hitEnemy], TAC.SCOPE_DMG, ox, oz);
};

TacWorld.prototype.hurtPlayer = function (dmg, kx, kz) {
  var w = this;
  if (w.dead || w.hurtCd > 0) return;
  w.hp -= dmg;
  w.hurtCd = 40;
  w.events.playerHit = true;
  if (kx !== 0.0 || kz !== 0.0) {
    var res = w.moveCircle(w.px, w.pz, w.py, TAC.PLAYER_R, TAC.PLAYER_H, kx, kz);
    w.px = res.x;
    w.pz = res.z;
  }
  if (w.hp <= 0) {
    w.dead = true;
    w.events.playerDead = true;
  }
};

TacWorld.prototype.damageEnemy = function (en, dmg, srcX, srcZ) {
  var w = this;
  if (!en.alive) return;
  // APC armour: small-arms fire from within the front/side arc bounces off.
  // srcX/srcZ is the shot's origin; when omitted (explosions) armour is
  // ignored, so grenades and barrels always punch through. Rear hits land.
  if (en.type === 7 && srcX !== undefined) {
    var afx = tacSinQ(en.yawQ);
    var afz = tacCosQ(en.yawQ);
    var adx = srcX - en.x;
    var adz = srcZ - en.z;
    var al = Math.sqrt(adx * adx + adz * adz);
    if (al > 0.0001) {
      var adot = (adx / al) * afx + (adz / al) * afz;
      if (adot >= TAC.APC_ARMOR_COS) { // shot came from the frontal/side arc
        w.events.armorBlock = true;
        if (!w.events.armorBlocks) w.events.armorBlocks = [];
        w.events.armorBlocks.push({ x: en.x, y: en.y + en.h * 0.6, z: en.z });
        return;
      }
    }
  }
  en.hp -= dmg;
  w.events.enemyHit = true;
  if (!w.events.hits) w.events.hits = [];
  w.events.hits.push({ x: en.x, y: en.y + en.h * 0.6, z: en.z });
  if (en.hp <= 0) {
    en.alive = false;
    w.enemiesLeft--;
    if (!w.events.kills) w.events.kills = [];
    w.events.kills.push({ x: en.x, y: en.y, z: en.z, type: en.type });
    if (en.type === 4) {
      // captured uplink: the player can now pilot a drone of their own
      w.droneUses++;
      w.events.droneGranted = true;
    }
  } else if (en.type === 0) {
    // getting shot alerts a soldier (and its group) to the shooter
    w.alertEnemy(en, w.px, w.pz);
  }
};

// tower shield: raised while alive, in the CLOSED phase of the world-wide
// formation drill, and not blast-staggered. The whole wall opens and closes
// in perfect sync — the gatling behind fires through the open windows, and so
// can you.
TacWorld.prototype.shieldUp = function (en) {
  if (en.type !== 6 || !en.alive || en.shieldStagT > 0) return false;
  return (this.tick % TAC.SHIELD_CYCLE) < (TAC.SHIELD_CYCLE - TAC.SHIELD_OPEN);
};

// does the segment slam into a raised shield coming from its FRONT?
TacWorld.prototype.segShieldBlocked = function (x0, y0, z0, x1, y1, z1) {
  var w = this;
  for (var i = 0; i < w.enemies.length; i++) {
    var en = w.enemies[i];
    if (!w.shieldUp(en)) continue;
    var fx = tacSinQ(en.yawQ);
    var fz = tacCosQ(en.yawQ);
    // moving INTO the shield face, not out from behind it
    if ((x1 - x0) * fx + (z1 - z0) * fz >= 0.0) continue;
    var scx = en.x + fx * 0.45;
    var scz = en.z + fz * 0.45;
    var t = tacSegCylinder(x0, y0, z0, x1, y1, z1, scx, en.y, scz, TAC.SHIELD_BLOCK_R, TAC.SHIELD_H);
    if (t >= 0.0) return true;
  }
  return false;
};

// shield bearer: no weapon — he IS the wall. Faces the threat and holds.
TacWorld.prototype.stepShield = function (en, range2) {
  var w = this;
  w.visionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);
  if (en.state === 2) {
    en.yawQ = tacTurnToward(en.yawQ, tacYawFor(w.px - en.x, w.pz - en.z), TAC.SHIELD_TURN);
  } else {
    en.yawQ = tacTurnToward(en.yawQ, en.baseYawQ, TAC.SHIELD_TURN);
  }
};

// squad doctrine (stage "squad": true): units radio what they know. Nearby
// mobile units converge on a shared last-known position, fanning out so they
// arrive from different bearings instead of a single-file conga line.
TacWorld.prototype.squadShareYellow = function (x, z, exceptEn) {
  var w = this;
  if (!w.squad) return;
  var linked = 0;
  for (var i = 0; i < w.enemies.length && linked < 3; i++) {
    var o = w.enemies[i];
    if (!o.alive || o === exceptEn || o.state >= 2) continue;
    if (o.type !== 0 && o.type !== 4 && o.type !== 5) continue; // mobile ground units only
    var dx = o.x - x;
    var dz = o.z - z;
    if (dx * dx + dz * dz > TAC.SQUAD_RADIO_R * TAC.SQUAD_RADIO_R) continue;
    var dirQ = tacYawFor(dx, dz);
    var off = linked === 0 ? 0 : (linked === 1 ? 10923 : -10923); // 0 / +60 / -60 deg
    var fq = (dirQ + off) & 65535;
    var fx = x + tacSinQ(fq) * TAC.SQUAD_FAN_R;
    var fz = z + tacCosQ(fq) * TAC.SQUAD_FAN_R;
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
};

// corpse discovery: a patrolling unit that lays eyes on a fallen comrade goes
// suspicious at the body and radios the position out
TacWorld.prototype.squadCorpseCheck = function (en) {
  var w = this;
  for (var i = 0; i < w.enemies.length; i++) {
    var c = w.enemies[i];
    if (c.alive || c.corpseSpotted || c.type === 3) continue;
    var dx = c.x - en.x;
    var dz = c.z - en.z;
    var d2 = dx * dx + dz * dz;
    if (d2 > TAC.VISION_RANGE * TAC.VISION_RANGE || d2 < 0.01) continue;
    var fx = tacSinQ(en.yawQ);
    var fz = tacCosQ(en.yawQ);
    var dot = fx * dx + fz * dz;
    if (dot <= 0.0) continue;
    if (dot * dot < TAC.VISION_COS2 * d2) continue;
    if (w.segBlocked(en.x, en.y + tacEnemyH(en) - 0.2, en.z, c.x, c.y + 0.4, c.z)) continue;
    c.corpseSpotted = true;
    en.state = 1;
    en.tx = c.x;
    en.tz = c.z;
    en.pauseT = 0;
    w.events.corpseFound = true;
    w.squadShareYellow(c.x, c.z, en);
    return;
  }
};

// a spotting drone is a flying searchlight: while it has eyes on the player it
// keeps every radio-linked unit's target pinned to the player's LIVE position
TacWorld.prototype.squadIlluminate = function (drone) {
  var w = this;
  var any = false;
  for (var i = 0; i < w.enemies.length; i++) {
    var o = w.enemies[i];
    if (!o.alive || o === drone || o.type === 3) continue;
    var dx = o.x - drone.x;
    var dz = o.z - drone.z;
    if (dx * dx + dz * dz > TAC.SQUAD_RADIO_R * TAC.SQUAD_RADIO_R) continue;
    if (o.state === 0) { o.state = 1; o.pauseT = 0; }
    o.tx = w.px;
    o.tz = w.pz;
    any = true;
  }
  if (any) w.events.radio = true;
};

TacWorld.prototype.alertEnemy = function (en, x, z) {
  var w = this;
  var wasAlert = en.state === 2;
  if (!wasAlert) en.rifleCd = TAC.RIFLE_REACT; // reaction delay before the first shot
  en.state = 2;
  en.tx = x;
  en.tz = z;
  en.gauge = TAC.GAUGE_MAX;
  if (en.group > 0) {
    for (var i = 0; i < w.enemies.length; i++) {
      var o = w.enemies[i];
      if (!o.alive || o.group !== en.group || o.state === 2) continue;
      o.state = 2;
      o.rifleCd = TAC.RIFLE_REACT;
      o.tx = x;
      o.tz = z;
      o.gauge = TAC.GAUGE_MAX;
    }
    w.events.groupAlert = true;
  }
  if (!wasAlert) w.squadShareYellow(x, z, en); // share on the TRANSITION only
};

TacWorld.prototype.stepBullets = function () {
  var w = this;
  for (var i = 0; i < w.bullets.length; i++) {
    var bu = w.bullets[i];
    if (!bu.alive) continue;
    bu.ttl--;
    if (bu.ttl <= 0) { bu.alive = false; continue; }
    var nx = bu.x + bu.vx * TAC.TICK;
    var ny = bu.y + bu.vy * TAC.TICK;
    var nz = bu.z + bu.vz * TAC.TICK;

    // out of arena / into the LOCAL floor (inside a pit the floor is sunken —
    // you can fire from the bottom of a moat, and shots can dive into one)
    if (nx < 0.0 || nx > w.arenaW || nz < 0.0 || nz > w.arenaD) {
      bu.alive = false;
      continue;
    }
    var floorY = 0.0;
    for (var fp = 0; fp < w.pits.length; fp++) {
      var fpp = w.pits[fp];
      if (nx >= fpp.x0 && nx <= fpp.x1 && nz >= fpp.z0 && nz <= fpp.z1) {
        if (-fpp.depth < floorY) floorY = -fpp.depth;
      }
    }
    if (ny <= floorY) {
      bu.alive = false;
      continue;
    }
    // cracked walls: any bullet stops on them, only GATLING rounds chip them
    var ck = w.segCrackedHit(bu.x, bu.y, bu.z, nx, ny, nz);
    if (ck >= 0) {
      if (bu.gat) {
        var cb = w.boxes[ck];
        cb.hp--;
        if (cb.hp <= 0) w.breakWall(ck);
      }
      bu.alive = false;
      w.events.bulletWall = true;
      continue;
    }
    // static geometry
    if (w.segBlocked(bu.x, bu.y, bu.z, nx, ny, nz)) {
      bu.alive = false;
      w.events.bulletWall = true;
      continue;
    }
    // slopes: below surface at endpoint
    var sl = w.slopeUnder(nx, nz, ny);
    if (!sl) {
      for (var s = 0; s < w.slopes.length; s++) {
        var so = w.slopes[s];
        if (nx < so.x0 || nx > so.x1 || nz < so.z0 || nz > so.z1) continue;
        if (ny < w.slopeYAt(so, nx, nz)) { sl = so; break; }
      }
    }
    if (sl && ny < w.slopeYAt(sl, nx, nz)) {
      bu.alive = false;
      continue;
    }
    // barrels
    var hitBarrel = false;
    for (var bi = 0; bi < w.barrels.length; bi++) {
      var ba = w.barrels[bi];
      if (!ba.alive) continue;
      var t = tacSegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, ba.x, ba.y, ba.z, TAC.BARREL_R, TAC.BARREL_H);
      if (t >= 0.0) {
        w.igniteBarrel(ba, bu.vx, bu.vz, TAC.BARREL_FUSE);
        bu.alive = false;
        hitBarrel = true;
        break;
      }
    }
    if (hitBarrel) continue;
    // mines: a bullet detonates them remotely (short fuse)
    var hitMine = false;
    for (var mj = 0; mj < w.mines.length; mj++) {
      var mi = w.mines[mj];
      if (!mi.alive) continue;
      var tm = tacSegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, mi.x, mi.y, mi.z, TAC.MINE_R, 0.25);
      if (tm >= 0.0) {
        if (mi.fuse < 0 || mi.fuse > TAC.MINE_SHOT_FUSE) mi.fuse = TAC.MINE_SHOT_FUSE;
        w.events.mineHit = true;
        bu.alive = false;
        hitMine = true;
        break;
      }
    }
    if (hitMine) continue;
    // jammer switches: one bullet kills the console and drops the field
    var hitObj = false;
    for (var sw3 = 0; sw3 < w.switches.length; sw3++) {
      var swb = w.switches[sw3];
      if (!swb.alive) continue;
      var tsw = tacSegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, swb.x, swb.y, swb.z, 0.4, 1.2);
      if (tsw >= 0.0) {
        w.killSwitch(swb);
        bu.alive = false;
        hitObj = true;
        break;
      }
    }
    if (hitObj) continue;
    // rockslide support posts
    for (var sl3 = 0; sl3 < w.slides.length; sl3++) {
      var sld = w.slides[sl3];
      if (sld.triggered) continue;
      var tps = tacSegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, sld.postX, sld.postY, sld.postZ, 0.3, 1.2);
      if (tps >= 0.0) {
        w.triggerSlide(sld);
        bu.alive = false;
        hitObj = true;
        break;
      }
    }
    if (hitObj) continue;

    if (bu.fromPlayer && w.segShieldBlocked(bu.x, bu.y, bu.z, nx, ny, nz)) {
      bu.alive = false;
      w.events.shieldBlock = true;
      continue;
    }
    if (bu.fromPlayer) {
      var hitEnemy = false;
      for (var e = 0; e < w.enemies.length; e++) {
        var en = w.enemies[e];
        if (!en.alive) continue;
        var te = tacSegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, en.x, en.y, en.z, en.r, tacEnemyH(en));
        if (te < 0.0) continue;
        w.damageEnemy(en, 1, bu.x, bu.z);
        bu.alive = false;
        hitEnemy = true;
        break;
      }
      if (hitEnemy) continue;
    } else {
      var tp = tacSegCylinder(bu.x, bu.y, bu.z, nx, ny, nz, w.px, w.py, w.pz, TAC.PLAYER_R, w.playerHitH());
      if (tp >= 0.0) {
        bu.alive = false;
        w.hurtPlayer(1, 0.0, 0.0);
        continue;
      }
    }
    bu.x = nx;
    bu.y = ny;
    bu.z = nz;
  }
  // compact the list occasionally to bound memory (deterministic: fixed cadence)
  if ((w.tick % 100) === 0 && w.bullets.length > 64) {
    var live = [];
    for (var k = 0; k < w.bullets.length; k++) if (w.bullets[k].alive) live.push(w.bullets[k]);
    w.bullets = live;
  }
};

TacWorld.prototype.stepGrenades = function () {
  var w = this;
  for (var i = 0; i < w.grenades.length; i++) {
    var g = w.grenades[i];
    if (!g.alive) continue;
    var dv = TAC.GRAVITY * TAC.TICK;
    g.vy = g.vy - dv;
    var nx = g.x + g.vx * TAC.TICK;
    var ny = g.y + g.vy * TAC.TICK;
    var nz = g.z + g.vz * TAC.TICK;
    var boom = false;
    var bx = nx, by = ny, bz = nz;
    if (nx < 0.0 || nx > w.arenaW || nz < 0.0 || nz > w.arenaD) {
      boom = true;
      bx = g.x; by = g.y; bz = g.z;
    } else if (w.segBlocked(g.x, g.y, g.z, nx, ny, nz)) {
      boom = true;
      bx = g.x; by = g.y; bz = g.z; // detonate on the near side of the wall
    } else {
      var gy = w.groundY(nx, nz, g.y, TAC.GRENADE_R);
      if (ny <= gy + 0.05) {
        boom = true;
        by = gy + 0.3;
      } else {
        for (var e = 0; e < w.enemies.length; e++) {
          var en = w.enemies[e];
          if (!en.alive) continue;
          var te = tacSegCylinder(g.x, g.y, g.z, nx, ny, nz, en.x, en.y, en.z, en.r + TAC.GRENADE_R, en.h);
          if (te >= 0.0) { boom = true; break; }
        }
      }
    }
    if (boom) {
      g.alive = false;
      w.explodeAt(bx, by, bz, TAC.GRENADE_BLAST_R, 2);
    } else {
      g.x = nx;
      g.y = ny;
      g.z = nz;
    }
  }
  if ((w.tick % 100) === 0 && w.grenades.length > 16) {
    var live = [];
    for (var k = 0; k < w.grenades.length; k++) if (w.grenades[k].alive) live.push(w.grenades[k]);
    w.grenades = live;
  }
};

TacWorld.prototype.igniteBarrel = function (ba, vx, vz, fuse) {
  var w = this;
  if (!ba.alive) return;
  if (ba.fuse < 0) ba.fuse = fuse;
  if (ba.state === 0) {
    var hl = Math.sqrt(vx * vx + vz * vz);
    if (hl > 0.0001) {
      ba.dx = vx / hl;
      ba.dz = vz / hl;
      ba.state = 1;
    }
    w.events.barrelHit = true;
  }
};

TacWorld.prototype.stepBarrels = function () {
  var w = this;
  for (var i = 0; i < w.barrels.length; i++) {
    var ba = w.barrels[i];
    if (!ba.alive) continue;
    if (ba.state === 1) {
      var stepd = TAC.BARREL_ROLL * TAC.TICK;
      var dx = ba.dx * stepd;
      var dz = ba.dz * stepd;
      var res = w.moveCircle(ba.x, ba.z, ba.y, TAC.BARREL_R, TAC.BARREL_H, dx, dz);
      var movedX = res.x - ba.x;
      var movedZ = res.z - ba.z;
      ba.x = res.x;
      ba.z = res.z;
      var want2 = dx * dx + dz * dz;
      var got2 = movedX * movedX + movedZ * movedZ;
      if (got2 < want2 * 0.25) ba.state = 2; // wedged against something: stop rolling
      var g = w.groundY(ba.x, ba.z, ba.y + 0.01, TAC.BARREL_R);
      if (g < ba.y - 0.01) ba.y = g; // rolls off ledges straight down (kept simple)
    }
    if (ba.fuse >= 0) {
      ba.fuse--;
      if (ba.fuse <= 0) w.explodeBarrel(ba);
    }
  }
};

// shared blast logic: damages everything in radius, chain-ignites barrels and
// mines. Used by barrels, mines and kamikaze drones.
TacWorld.prototype.explodeAt = function (cx, cy, cz, radius, playerDmg) {
  var w = this;
  if (!w.events.explosions) w.events.explosions = [];
  w.events.explosions.push({ x: cx, y: cy, z: cz, r: radius });
  w.addNoise(cx, cz, TAC.NOISE_BLAST_R);
  var r2 = radius * radius;
  for (var e = 0; e < w.enemies.length; e++) {
    var en = w.enemies[e];
    if (!en.alive) continue;
    var ex = en.x - cx;
    var ey = en.y + en.h * 0.5 - cy;
    var ez = en.z - cz;
    var d2 = ex * ex + ey * ey + ez * ez;
    if (d2 <= r2) {
      if (en.type === 6) {
        en.shieldStagT = TAC.SHIELD_STAGGER;
        w.damageEnemy(en, TAC.SHIELD_BLAST_DMG);
      } else {
        w.damageEnemy(en, TAC.BLAST_DMG);
      }
    }
  }
  var px = w.px - cx;
  var pyy = w.py + TAC.CHEST_H - cy;
  var pz = w.pz - cz;
  var pd2 = px * px + pyy * pyy + pz * pz;
  if (pd2 <= r2) w.hurtPlayer(playerDmg, 0.0, 0.0);
  for (var b = 0; b < w.barrels.length; b++) {
    var o = w.barrels[b];
    if (!o.alive) continue;
    var ox = o.x - cx;
    var oy = o.y + 0.5 - cy;
    var oz = o.z - cz;
    var od2 = ox * ox + oy * oy + oz * oz;
    if (od2 <= r2 && o.fuse < 0) w.igniteBarrel(o, ox, oz, TAC.BARREL_CHAIN_FUSE);
  }
  for (var cw = 0; cw < w.boxes.length; cw++) {
    var cbx = w.boxes[cw];
    if (cbx.kind !== 3) continue;
    if (!cbx.alive) continue;
    var qx = cx < cbx.x0 ? cbx.x0 : (cx > cbx.x1 ? cbx.x1 : cx);
    var qy = cy < 0.0 ? 0.0 : (cy > cbx.h ? cbx.h : cy);
    var qz = cz < cbx.z0 ? cbx.z0 : (cz > cbx.z1 ? cbx.z1 : cz);
    var wx = cx - qx;
    var wy = cy - qy;
    var wz = cz - qz;
    if (wx * wx + wy * wy + wz * wz <= r2) w.breakWall(cw);
  }
  for (var m = 0; m < w.mines.length; m++) {
    var mi = w.mines[m];
    if (!mi.alive) continue;
    var mx = mi.x - cx;
    var my = mi.y - cy;
    var mz = mi.z - cz;
    var md2 = mx * mx + my * my + mz * mz;
    if (md2 <= r2 && (mi.fuse < 0 || mi.fuse > TAC.MINE_SHOT_FUSE)) mi.fuse = TAC.MINE_SHOT_FUSE;
  }
  for (var sw4 = 0; sw4 < w.switches.length; sw4++) {
    var swx = w.switches[sw4];
    if (!swx.alive) continue;
    var swdx = swx.x - cx;
    var swdz = swx.z - cz;
    if (swdx * swdx + swdz * swdz <= r2) w.killSwitch(swx);
  }
  for (var sl4 = 0; sl4 < w.slides.length; sl4++) {
    var slx = w.slides[sl4];
    if (slx.triggered) continue;
    var sldx = slx.postX - cx;
    var sldz = slx.postZ - cz;
    if (sldx * sldx + sldz * sldz <= r2) w.triggerSlide(slx);
  }
};

TacWorld.prototype.explodeBarrel = function (ba) {
  var w = this;
  if (!ba.alive) return;
  ba.alive = false;
  w.explodeAt(ba.x, ba.y + 0.5, ba.z, TAC.BLAST_R, TAC.BLAST_PLAYER_DMG);
};

TacWorld.prototype.killSwitch = function (sw) {
  var w = this;
  if (!sw.alive) return;
  sw.alive = false; // the veil it projects dies with it
  w.events.switchDown = true;
};

TacWorld.prototype.triggerSlide = function (sld) {
  var w = this;
  if (sld.triggered) return;
  sld.triggered = true;
  w.events.slideStart = true;
  w.addNoise(sld.pileX, sld.pileZ, TAC.NOISE_BLAST_R);
  // boulders spawn in a line across the pile's width, perpendicular to dir
  var perpX = sld.dz;
  var perpZ = sld.dx;
  var n = Math.floor(sld.w / 1.4);
  if (n < 3) n = 3;
  if (n > 8) n = 8;
  for (var i = 0; i < n; i++) {
    var frac = n === 1 ? 0.0 : (i / (n - 1)) - 0.5;
    var off = frac * (sld.w - 1.2);
    var bx = sld.pileX + perpX * off;
    var bz = sld.pileZ + perpZ * off;
    var by = w.groundY(bx, bz, 1000.0, 0.6);
    sld.boulders.push({ x: bx, y: by, z: bz, traveled: 0.0, alive: true });
  }
};

TacWorld.prototype.stepSlides = function () {
  var w = this;
  for (var i = 0; i < w.slides.length; i++) {
    var sld = w.slides[i];
    if (!sld.triggered) continue;
    for (var b = 0; b < sld.boulders.length; b++) {
      var bo = sld.boulders[b];
      if (!bo.alive) continue;
      var stepd = TAC.SLIDE_SPEED * TAC.TICK;
      var res = w.moveCircle(bo.x, bo.z, bo.y, 0.6, 1.2, sld.dx * stepd, sld.dz * stepd);
      var got = (res.x - bo.x) * sld.dx + (res.z - bo.z) * sld.dz;
      bo.x = res.x;
      bo.z = res.z;
      bo.traveled += stepd;
      var g = w.groundY(bo.x, bo.z, bo.y + 0.01, 0.6);
      if (g < bo.y - 0.01) bo.y = bo.y - 8.0 * TAC.TICK;
      if (bo.y < g) bo.y = g;
      if (got < stepd * 0.2 || bo.traveled >= TAC.SLIDE_LEN) { bo.alive = false; continue; }
      // crush everything in the boulder's path
      var r2 = TAC.SLIDE_CRUSH_R * TAC.SLIDE_CRUSH_R;
      for (var e = 0; e < w.enemies.length; e++) {
        var en = w.enemies[e];
        if (!en.alive || en.type === 3) continue; // fliers are above the flow
        var ex = en.x - bo.x;
        var ez = en.z - bo.z;
        var eyd = en.y - bo.y;
        if (eyd > -1.0 && eyd < 1.6 && ex * ex + ez * ez <= r2) {
          w.damageEnemy(en, TAC.BLAST_DMG);
          if (!w.events.crushed) w.events.crushed = 0;
          w.events.crushed++;
        }
      }
      var pxd = w.px - bo.x;
      var pzd = w.pz - bo.z;
      var pyd = w.py - bo.y;
      if (pyd > -1.0 && pyd < 1.6 && pxd * pxd + pzd * pzd <= r2) w.hurtPlayer(2, 0.0, 0.0);
    }
  }
};

TacWorld.prototype.stepMines = function () {
  var w = this;
  for (var i = 0; i < w.mines.length; i++) {
    var mi = w.mines[i];
    if (!mi.alive) continue;
    if (mi.fuse < 0) {
      var tr2 = TAC.MINE_TRIGGER_R * TAC.MINE_TRIGGER_R;
      var pdx = w.px - mi.x;
      var pdz = w.pz - mi.z;
      var pdy = w.py - mi.y;
      if (pdy > -0.6 && pdy < 0.6 && pdx * pdx + pdz * pdz <= tr2) {
        mi.fuse = TAC.MINE_FUSE;
        w.events.mineArmed = true;
      } else {
        for (var e = 0; e < w.enemies.length; e++) {
          var en = w.enemies[e];
          if (!en.alive || en.type === 1 || en.type === 2) continue; // only units that move on the ground
          var edy = en.y - mi.y;
          if (edy < -0.6 || edy > 0.6) continue;
          var edx = en.x - mi.x;
          var edz = en.z - mi.z;
          if (edx * edx + edz * edz <= tr2) {
            mi.fuse = TAC.MINE_FUSE;
            w.events.mineArmed = true;
            break;
          }
        }
      }
    } else {
      mi.fuse--;
      if (mi.fuse <= 0) {
        mi.alive = false;
        w.explodeAt(mi.x, mi.y + 0.1, mi.z, TAC.MINE_BLAST_R, 2);
      }
    }
  }
};

// turn en.yawQ toward target angle by at most rate units (shortest way)
function tacTurnToward(cur, target, rate) {
  var diff = (target - cur) & 65535;
  if (diff > 32768) diff -= 65536;
  if (diff > rate) diff = rate;
  if (diff < -rate) diff = -rate;
  return (cur + diff) & 65535;
}

// integer yaw whose sin/cos best matches direction (dx,dz): binary search on the
// quantized circle (no atan). 17 iterations over 65536 units.
function tacYawFor(dx, dz) {
  var best = 0;
  var bestDot = -2.0;
  // coarse-to-fine deterministic search: 64 coarse samples then refine
  var i, q, s, c, len, dot;
  len = Math.sqrt(dx * dx + dz * dz);
  if (len < 0.000001) return 0;
  var nx = dx / len;
  var nz = dz / len;
  for (i = 0; i < 64; i++) {
    q = i * 1024;
    s = tacSinQ(q);
    c = tacCosQ(q);
    dot = s * nx + c * nz;
    if (dot > bestDot) { bestDot = dot; best = q; }
  }
  var lo2 = best - 512;
  for (i = 0; i < 33; i++) {
    q = (lo2 + i * 32) & 65535;
    s = tacSinQ(q);
    c = tacCosQ(q);
    dot = s * nx + c * nz;
    if (dot > bestDot) { bestDot = dot; best = q; }
  }
  return best & 65535;
}

TacWorld.prototype.playerChestY = function () {
  return this.py + (this.crouched ? TAC.CROUCH_CHEST : TAC.CHEST_H);
};
TacWorld.prototype.playerHitH = function () {
  return this.crouched ? TAC.CROUCH_H : TAC.PLAYER_H;
};

// effective enemy height: trench soldiers duck between shots
function tacEnemyH(en) {
  return en.crouched ? TAC.CROUCH_H : en.h;
}

TacWorld.prototype.playerVisibleFrom = function (en, range2, cos2) {
  var w = this;
  var dx = w.px - en.x;
  var dz = w.pz - en.z;
  var d2 = dx * dx + dz * dz;
  if (d2 > range2) return -1.0;
  if (d2 < 0.0001) return 0.0;
  var fx = tacSinQ(en.yawQ);
  var fz = tacCosQ(en.yawQ);
  var dot = fx * dx + fz * dz;
  if (dot <= 0.0) return -1.0;
  var dd = dot * dot;
  var lim = cos2 * d2;
  if (dd < lim) return -1.0;
  var eyeY = en.y + tacEnemyH(en) - 0.2;
  var chestY = w.playerChestY();
  if (w.segBlocked(en.x, eyeY, en.z, w.px, chestY, w.pz)) return -1.0;
  return d2;
};

TacWorld.prototype.moveEnemy = function (en, txx, tzz, speed, fly) {
  var w = this;
  var dx = txx - en.x;
  var dz = tzz - en.z;
  var d2 = dx * dx + dz * dz;
  if (d2 < 0.01) return true;
  var d = Math.sqrt(d2);
  var stepd = speed * TAC.TICK;
  if (stepd > d) stepd = d;
  var mx = (dx / d) * stepd;
  var mz = (dz / d) * stepd;
  var res = w.moveCircle(en.x, en.z, en.y, en.r, en.h, mx, mz);
  if (!fly && en.y < 0.5 && w.inRiver(res.x, res.z) && !w.inRiver(en.x, en.z)) {
    // ground units refuse to wade in — they hold the bank
    en.yawQ = tacTurnToward(en.yawQ, tacYawFor(dx, dz), TAC.ENEMY_TURN_RATE);
    return false;
  }
  en.x = res.x;
  en.z = res.z;
  if (!fly) {
    var g = w.groundY(en.x, en.z, en.y + 0.01, en.r);
    en.y = g;
  }
  en.yawQ = tacTurnToward(en.yawQ, tacYawFor(dx, dz), TAC.ENEMY_TURN_RATE);
  return d < 0.35;
};

TacWorld.prototype.stepEnemies = function () {
  var w = this;
  var range2 = TAC.VISION_RANGE * TAC.VISION_RANGE;
  var snipRange2 = TAC.SNIPER_RANGE * TAC.SNIPER_RANGE;
  var gatRange2 = TAC.GATLING_VISION * TAC.GATLING_VISION;
  var apcRange2 = TAC.APC_VISION * TAC.APC_VISION;

  for (var i = 0; i < w.enemies.length; i++) {
    var en = w.enemies[i];
    if (!en.alive) continue;
    if (en.attackCd > 0) en.attackCd--;

    // hearing: idle/suspicious enemies investigate noises in range
    if (en.state < 2) {
      for (var n = 0; n < w.noises.length; n++) {
        var no = w.noises[n];
        var ndx = no.x - en.x;
        var ndz = no.z - en.z;
        var nd2 = ndx * ndx + ndz * ndz;
        if (nd2 <= no.r * no.r) {
          en.state = 1;
          en.tx = no.x;
          en.tz = no.z;
          en.pauseT = 0;
          w.events.heard = true;
          // entrench doctrine: gunfire sends him sprinting for the nearest
          // trench, not toward the sound
          if (en.entrench && en.type === 0 && !en.dugIn && w.trenches.length > 0) {
            var bd2 = 1.0e30;
            for (var tr = 0; tr < w.trenches.length; tr++) {
              var trc = w.trenches[tr];
              var cxp = en.x < trc.x0 + 0.7 ? trc.x0 + 0.7 : (en.x > trc.x1 - 0.7 ? trc.x1 - 0.7 : en.x);
              var czp = en.z < trc.z0 + 0.7 ? trc.z0 + 0.7 : (en.z > trc.z1 - 0.7 ? trc.z1 - 0.7 : en.z);
              var cdx = cxp - en.x;
              var cdz = czp - en.z;
              var cd2 = cdx * cdx + cdz * cdz;
              if (cd2 < bd2) { bd2 = cd2; en.tx = cxp; en.tz = czp; }
            }
            en.seekCover = true;
          }
        }
      }
    }

    if (w.squad && en.state < 2 && en.type !== 3) w.squadCorpseCheck(en);

    if (en.shieldStagT > 0) en.shieldStagT--;

    if (en.type === 0) w.stepSoldier(en, range2);
    else if (en.type === 1) w.stepGatling(en, gatRange2);
    else if (en.type === 2) w.stepSniper(en, snipRange2);
    else if (en.type === 3) w.stepDrone(en, range2);
    else if (en.type === 5) w.stepBomber(en, range2);
    else if (en.type === 6) w.stepShield(en, range2);
    else if (en.type === 7) w.stepApc(en, apcRange2);
    else w.stepOperator(en, range2);
  }
};

TacWorld.prototype.visionGauge = function (en, range2, cos2, sneakMul) {
  var w = this;
  var d2 = w.playerVisibleFrom(en, range2, cos2);
  en.seesPlayer = d2 >= 0.0;
  if (d2 >= 0.0) {
    var d = Math.sqrt(d2);
    var range = Math.sqrt(range2);
    var near = 1.0 - d / range;
    var fill = 2.0 + 9.0 * near;
    // sneaking players build the gauge slower; entrenched players even slower
    fill = fill * sneakMul;
    en.gauge += fill;
    if (en.gauge >= TAC.GAUGE_MAX) {
      en.gauge = TAC.GAUGE_MAX;
      if (en.state !== 2) w.events.spotted = true;
      w.alertEnemy(en, w.px, w.pz);
    } else if (en.gauge >= TAC.SUSPICIOUS_AT && en.state === 0) {
      en.state = 1;
      en.tx = w.px;
      en.tz = w.pz;
      en.pauseT = 0;
    }
    if (en.state === 2) { en.tx = w.px; en.tz = w.pz; }
  } else {
    en.gauge -= TAC.GAUGE_DECAY;
    if (en.gauge < 0.0) en.gauge = 0.0;
  }
};

// fires one aimed, visible bullet from an enemy at the player's chest
TacWorld.prototype.fireEnemyBullet = function (en) {
  var w = this;
  var dx = w.px - en.x;
  var dz = w.pz - en.z;
  var d = Math.sqrt(dx * dx + dz * dz);
  if (d < 0.2) return;
  var ux = dx / d;
  var uz = dz / d;
  var muzY = en.y + en.h - 0.5;
  var spd = en.type === 2 ? TAC.SNIPER_BULLET_SPEED : TAC.ENEMY_BULLET_SPEED;
  var t = d / spd;
  var wantY = w.playerChestY();
  var vy = (wantY - muzY) / t;
  var vmax = spd * 0.5;
  if (vy > vmax) vy = vmax;
  if (vy < -vmax) vy = -vmax;
  w.bullets.push({
    x: en.x + ux * 0.5, y: muzY, z: en.z + uz * 0.5,
    sx: en.x, sy: muzY, sz: en.z,
    vx: ux * spd, vy: vy, vz: uz * spd,
    ttl: TAC.ENEMY_BULLET_TTL, fromPlayer: false, alive: true
  });
  w.events.rifleShot = true;
  if (!w.events.eshots) w.events.eshots = [];
  w.events.eshots.push({ x: en.x, z: en.z });
};

// blind suppressive round: fired toward a bearing (angle units), aimed at
// standing-chest height around the last known position — deliberately sloppy
TacWorld.prototype.fireSuppressive = function (en, aimQ, dist) {
  var w = this;
  var ux = tacSinQ(aimQ);
  var uz = tacCosQ(aimQ);
  var muzY = en.y + tacEnemyH(en) - 0.5;
  var t = dist / TAC.ENEMY_BULLET_SPEED;
  var vy = (1.1 - muzY) / t;
  var vmax = TAC.ENEMY_BULLET_SPEED * 0.35;
  if (vy > vmax) vy = vmax;
  if (vy < -vmax) vy = -vmax;
  w.bullets.push({
    x: en.x + ux * 0.5, y: muzY, z: en.z + uz * 0.5,
    sx: en.x, sy: muzY, sz: en.z,
    vx: ux * TAC.ENEMY_BULLET_SPEED, vy: vy, vz: uz * TAC.ENEMY_BULLET_SPEED,
    ttl: TAC.ENEMY_BULLET_TTL, fromPlayer: false, alive: true
  });
  w.events.rifleShot = true;
  if (!w.events.eshots) w.events.eshots = [];
  w.events.eshots.push({ x: en.x, z: en.z });
};

TacWorld.prototype.stepSoldier = function (en, range2) {
  var w = this;
  // trench discipline: a soldier in a pit fights as a pop-up target — it
  // ducks below grade, periodically rises to scan (or, when engaged, rises on
  // a combat rhythm to aim and fire), and never abandons its trench.
  if (en.fireFlash > 0) en.fireFlash--;
  var inTrench = w.trenchAt(en.x, en.z) >= 0;
  if (en.entrench && !en.dugIn && inTrench) { en.dugIn = true; en.seekCover = false; }
  var peek = true;
  if (inTrench) {
    // engaged trench troops fight nearly upright — the moment they know where
    // you are, they rise and shoot; they only bob down briefly to reload
    if (en.state === 2) peek = (w.tick % 90) < 70;
    else peek = (w.tick % 130) < 35;
  }
  en.crouched = inTrench && !peek && en.aimT === 0 && en.fireFlash === 0;
  en.holdTrench = inTrench;
  // NOTE: sneak state is read from the player's last input; stash it on the world
  w.visionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);
  if (en.rifleCd > 0) en.rifleCd--;

  if (en.state === 2) {
    var dx = w.px - en.x;
    var dz = w.pz - en.z;
    var d2 = dx * dx + dz * dz;
    var engage = TAC.RIFLE_RANGE * TAC.RIFLE_RANGE;
    if (en.seesPlayer && d2 <= engage) {
      // stand, telegraph the aim (yellow line client-side), then shoot
      en.yawQ = tacTurnToward(en.yawQ, tacYawFor(dx, dz), TAC.ENEMY_TURN_RATE);
      if (en.rifleCd === 0 && !en.crouched) {
        en.aimT++;
        if (en.aimT >= TAC.RIFLE_AIM) {
          w.fireEnemyBullet(en);
          en.rifleCd = TAC.RIFLE_CD;
          en.aimT = 0;
          en.fireFlash = 50; // a full second exposed after firing, same as the player
        }
      }
    } else if (en.holdTrench) {
      // entrenched + alerted but no eyes on: keep the pressure up anyway —
      // sloppy suppressive fire sprayed around the last known position
      en.aimT = 0;
      var spx = en.tx - en.x;
      var spz = en.tz - en.z;
      var spd2 = spx * spx + spz * spz;
      if (!en.crouched && en.rifleCd === 0 && spd2 > 9.0 && spd2 <= engage) {
        en.suppressT = (en.suppressT || 0) + 1;
        var wob = ((w.tick * 13 + en.suppressT * 29) % 9) - 4; // deterministic spray
        var sprayQ = (tacYawFor(spx, spz) + wob * 700) & 65535;
        en.yawQ = tacTurnToward(en.yawQ, sprayQ, TAC.ENEMY_TURN_RATE);
        w.fireSuppressive(en, sprayQ, Math.sqrt(spd2));
        en.rifleCd = TAC.RIFLE_SUPPRESS_CD;
        en.fireFlash = 50; // a full second exposed after firing, same as the player
      } else {
        en.yawQ = tacTurnToward(en.yawQ, tacYawFor(spx, spz), TAC.ENEMY_TURN_RATE);
      }
    } else if (w.trenchAt(en.tx, en.tz) >= 0 && w.trenchAt(en.x, en.z) < 0 &&
               dx * dx + dz * dz < TAC.TRENCH_STANDOFF * TAC.TRENCH_STANDOFF) {
      // the target went to ground in a trench: don't blunder up to the lip —
      // hold the standoff ring, stay aimed, and wait for a head to pop up
      en.aimT = 0;
      en.yawQ = tacTurnToward(en.yawQ, tacYawFor(dx, dz), TAC.ENEMY_TURN_RATE);
    } else {
      en.aimT = 0;
      var fx2 = en.tx;
      var fz2 = en.tz;
      if (w.squad && (en.idx & 1) === 1) {
        // odd-numbered soldiers are the flankers: while the pinning fire keeps
        // the player busy, they swing wide and come in from the side
        var fdx = en.x - en.tx;
        var fdz = en.z - en.tz;
        if (fdx * fdx + fdz * fdz > TAC.SQUAD_FLANK_R * TAC.SQUAD_FLANK_R) {
          var dirQ2 = tacYawFor(fdx, fdz);
          var side = (en.idx & 2) === 0 ? 16384 : 49152;
          var fq2 = (dirQ2 + side) & 65535;
          fx2 = en.tx + tacSinQ(fq2) * TAC.SQUAD_FLANK_R;
          fz2 = en.tz + tacCosQ(fq2) * TAC.SQUAD_FLANK_R;
          if (fx2 < 0.8) fx2 = 0.8;
          if (fx2 > w.arenaW - 0.8) fx2 = w.arenaW - 0.8;
          if (fz2 < 0.8) fz2 = 0.8;
          if (fz2 > w.arenaD - 0.8) fz2 = w.arenaD - 0.8;
        }
      }
      w.moveEnemy(en, fx2, fz2, TAC.SOLDIER_CHASE_SPEED);
      // lost track and reached last known position: cool down to suspicious
      if (!en.seesPlayer) {
        var ldx = en.tx - en.x;
        var ldz = en.tz - en.z;
        if (ldx * ldx + ldz * ldz < 0.25) {
          en.state = 1;
          en.pauseT = 0;
        }
      }
    }
  } else if (en.state === 1) {
    var arrived;
    if (en.dugIn) {
      arrived = true; // dug-in: scan from the trench, never leave it
    } else if (en.seekCover) {
      arrived = w.moveEnemy(en, en.tx, en.tz, TAC.SOLDIER_CHASE_SPEED);
    } else if (w.trenchAt(en.tx, en.tz) >= 0 && w.trenchAt(en.x, en.z) < 0) {
      var idx2 = en.tx - en.x;
      var idz2 = en.tz - en.z;
      if (idx2 * idx2 + idz2 * idz2 < TAC.TRENCH_PROBE * TAC.TRENCH_PROBE) {
        // close enough — peer at the hole from a safe distance
        en.yawQ = tacTurnToward(en.yawQ, tacYawFor(idx2, idz2), TAC.ENEMY_TURN_RATE);
        arrived = true;
      } else {
        arrived = w.moveEnemy(en, en.tx, en.tz, TAC.SOLDIER_INVESTIGATE_SPEED);
      }
    } else {
      arrived = w.moveEnemy(en, en.tx, en.tz, TAC.SOLDIER_INVESTIGATE_SPEED);
    }
    if (arrived) {
      en.pauseT++;
      en.yawQ = (en.yawQ + 300) & 65535; // scan around
      if (en.pauseT >= TAC.INVESTIGATE_PAUSE) {
        en.state = 0;
        en.gauge = 0.0;
        en.pauseT = 0;
      }
    }
  } else {
    if (en.hasPatrol && !en.dugIn) {
      var gx = en.patToB ? en.patX : en.homeX;
      var gz = en.patToB ? en.patZ : en.homeZ;
      var got = w.moveEnemy(en, gx, gz, TAC.SOLDIER_PATROL_SPEED);
      if (got) en.patToB = !en.patToB;
    } else {
      en.yawQ = tacTurnToward(en.yawQ, en.baseYawQ, 80);
    }
  }
};

TacWorld.prototype.stepGatling = function (en, range2) {
  var w = this;
  w.visionGauge(en, range2, TAC.VISION_COS2, 1.0);

  var aimQ;
  if (en.state === 2) {
    var dx = w.px - en.x;
    var dz = w.pz - en.z;
    en.yawQ = tacTurnToward(en.yawQ, tacYawFor(dx, dz), TAC.GATLING_ALERT_TURN);
    aimQ = en.yawQ;
    if (!en.seesPlayer) {
      en.gauge -= TAC.GAUGE_DECAY;
      if (en.gauge <= 0.0) { en.state = 0; en.gauge = 0.0; }
    }
  } else {
    // area suppression: sweep a triangle wave around base yaw, fire on cycle
    var cyc = en.cycleT % 200; // 4 s sweep period
    var ph = cyc < 100 ? cyc : 200 - cyc;
    var off = Math.floor((ph * TAC.GATLING_SWEEP * 2) / 100) - TAC.GATLING_SWEEP;
    aimQ = (en.baseYawQ + off) & 65535;
    en.yawQ = aimQ;
  }
  en.cycleT++;

  var fireCyc = en.cycleT % (TAC.GATLING_SPRAY + TAC.GATLING_RELOAD);
  if (fireCyc < TAC.GATLING_SPRAY && (fireCyc % TAC.GATLING_SHOT_EVERY) === 0) {
    var s = tacSinQ(aimQ);
    var c = tacCosQ(aimQ);
    var muzY = en.y + en.h - 0.5;
    // the gunner depresses the barrel toward the player's chest whenever it
    // can SEE them (alerted or not) — a player standing in a trench is lower
    // than grade, so level fire would sail overhead otherwise
    var vy = 0.0;
    if (en.state === 2 || en.seesPlayer) {
      var tdx = w.px - en.x;
      var tdz = w.pz - en.z;
      var td = Math.sqrt(tdx * tdx + tdz * tdz);
      if (td > 1.0) {
        var wantY = w.playerChestY();
        vy = (wantY - muzY) / (td / TAC.ENEMY_BULLET_SPEED);
        var vmax = TAC.ENEMY_BULLET_SPEED * 0.5;
        if (vy > vmax) vy = vmax;
        if (vy < -vmax) vy = -vmax;
      }
    }
    w.bullets.push({
      x: en.x + s * 0.7, y: muzY, z: en.z + c * 0.7,
      sx: en.x, sy: muzY, sz: en.z,
      vx: s * TAC.ENEMY_BULLET_SPEED, vy: vy, vz: c * TAC.ENEMY_BULLET_SPEED,
      ttl: TAC.ENEMY_BULLET_TTL, fromPlayer: false, alive: true, gat: (en.state === 2 || en.seesPlayer)
    });
    w.events.gatlingShot = true;
    if (!w.events.eshots) w.events.eshots = [];
    w.events.eshots.push({ x: en.x, z: en.z, gat: true });
  }
};

// APC / light armored vehicle: a slow rolling gun with a high HP pool and a
// frontal/side armour arc (handled in damageEnemy). It grinds toward the
// player, keeps the hull pointed at them (slow yaw — flank the REAR), and
// suppresses with a hull machine gun in bursts. Explosives ignore its armour.
TacWorld.prototype.stepApc = function (en, range2) {
  var w = this;
  w.visionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);

  if (en.state === 2) {
    var dx = w.px - en.x;
    var dz = w.pz - en.z;
    var dist = Math.sqrt(dx * dx + dz * dz);
    // hull turns slowly toward the player — the whole reason a flanker can
    // stay on its blind rear quarter
    en.yawQ = tacTurnToward(en.yawQ, tacYawFor(dx, dz), TAC.APC_TURN);
    // grind forward until inside firing range, then hold and shoot
    if (dist > TAC.APC_RANGE * 0.8) {
      w.moveEnemy(en, w.px, w.pz, TAC.APC_ADVANCE_SPEED);
    }
    if (!en.seesPlayer) {
      en.gauge -= TAC.GAUGE_DECAY;
      if (en.gauge <= 0.0) { en.state = 0; en.gauge = 0.0; }
    }
    // hull machine gun: burst / reload cadence, only when it can see the target
    en.cycleT++;
    var fireCyc = en.cycleT % (TAC.APC_GUN_BURST + TAC.APC_GUN_RELOAD);
    if ((en.state === 2 && en.seesPlayer) && fireCyc < TAC.APC_GUN_BURST &&
        (fireCyc % TAC.APC_GUN_CD) === 0 && dist <= TAC.APC_RANGE) {
      var aq = en.yawQ;
      var s = tacSinQ(aq);
      var c = tacCosQ(aq);
      var muzY = en.y + en.h - 0.4;
      var vy = 0.0;
      if (dist > 1.0) {
        var wantY = w.playerChestY();
        vy = (wantY - muzY) / (dist / TAC.ENEMY_BULLET_SPEED);
        var vmax = TAC.ENEMY_BULLET_SPEED * 0.5;
        if (vy > vmax) vy = vmax;
        if (vy < -vmax) vy = -vmax;
      }
      w.bullets.push({
        x: en.x + s * 0.9, y: muzY, z: en.z + c * 0.9,
        sx: en.x, sy: muzY, sz: en.z,
        vx: s * TAC.ENEMY_BULLET_SPEED, vy: vy, vz: c * TAC.ENEMY_BULLET_SPEED,
        ttl: TAC.ENEMY_BULLET_TTL, fromPlayer: false, alive: true, gat: true
      });
      w.events.gatlingShot = true;
      if (!w.events.eshots) w.events.eshots = [];
      w.events.eshots.push({ x: en.x, z: en.z, gat: true });
    }
  } else if (en.state === 1) {
    // investigating a noise: creep toward it, hull leading
    var ax = en.tx - en.x, az = en.tz - en.z;
    en.yawQ = tacTurnToward(en.yawQ, tacYawFor(ax, az), TAC.APC_TURN);
    var arr = w.moveEnemy(en, en.tx, en.tz, TAC.APC_PATROL_SPEED);
    if (arr) {
      en.pauseT++;
      if (en.pauseT >= TAC.INVESTIGATE_PAUSE) { en.state = 0; en.gauge = 0.0; en.pauseT = 0; }
    }
  } else {
    // idle patrol: roll between waypoints if given, else hold the spawn bearing
    if (en.hasPatrol) {
      var gx = en.patToB ? en.patX : en.homeX;
      var gz = en.patToB ? en.patZ : en.homeZ;
      var gdx = gx - en.x, gdz = gz - en.z;
      en.yawQ = tacTurnToward(en.yawQ, tacYawFor(gdx, gdz), TAC.APC_TURN);
      var got = w.moveEnemy(en, gx, gz, TAC.APC_PATROL_SPEED);
      if (got) en.patToB = !en.patToB;
    } else {
      en.yawQ = tacTurnToward(en.yawQ, en.baseYawQ, TAC.APC_TURN);
    }
  }
};

// kamikaze scout drone: hovers above cover (its eye sees over rocks), patrols,
// and once alerted flies at the player and self-destructs. 1 hp — shoot it down.
// night ops (cosmetic): rotate the searchlights and track whether the player
// is LIT (lamp pool, or caught in a beam with a clear line from the light).
// Night is atmosphere only — vision is identical day or night; playerLit is
// kept deterministic for rendering and future use.
TacWorld.prototype.stepLights = function () {
  var w = this;
  for (var i = 0; i < w.lights.length; i++) {
    var li = w.lights[i];
    li.angQ = (li.angQ + li.speed) & 65535;
  }
  if (!w.night) { w.playerLit = true; return; }
  w.playerLit = false;
  for (var l = 0; l < w.lamps.length; l++) {
    var la = w.lamps[l];
    var ldx = w.px - la.x;
    var ldz = w.pz - la.z;
    if (ldx * ldx + ldz * ldz <= la.r * la.r) { w.playerLit = true; return; }
  }
  for (var s2 = 0; s2 < w.lights.length; s2++) {
    var sl = w.lights[s2];
    var sdx = w.px - sl.x;
    var sdz = w.pz - sl.z;
    var sd2 = sdx * sdx + sdz * sdz;
    if (sd2 > sl.r * sl.r || sd2 < 0.04) continue;
    var toQ = tacYawFor(sdx, sdz);
    var diff = (toQ - sl.angQ) & 65535;
    if (diff > 32768) diff = 65536 - diff;
    if (diff > TAC.SEARCH_HALF) continue;
    if (w.segBlocked(sl.x, 3.0, sl.z, w.px, w.playerChestY(), w.pz)) continue;
    w.playerLit = true;
    return;
  }
};

// intel pickups (extract goal): grab them all, then reach the exit
TacWorld.prototype.stepIntel = function () {
  var w = this;
  if (w.goalType !== 1) return;
  for (var i = 0; i < w.intels.length; i++) {
    var it = w.intels[i];
    if (!it.alive) continue;
    var dx = w.px - it.x;
    var dz = w.pz - it.z;
    var dy = w.py - it.y;
    if (dy > -0.6 && dy < 1.2 && dx * dx + dz * dz <= 0.81) {
      it.alive = false;
      w.intelLeft--;
      w.events.intelPick = { left: w.intelLeft };
      if (w.intelLeft <= 0) w.events.intelAll = true;
    }
  }
};

// satchel bomber: no rifle — it lobs a time bomb in a HIGH overhead arc that
// clears walls and trench rims. The bomb lands where you were standing and
// gives a full 10 seconds of beeping warning: run, or pay. Not shootable.
TacWorld.prototype.stepBomber = function (en, range2) {
  var w = this;
  w.visionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);
  if (en.bombCd > 0) en.bombCd--;
  if (en.state === 2) {
    var dx = w.px - en.x;
    var dz = w.pz - en.z;
    var d2 = dx * dx + dz * dz;
    var tx = en.seesPlayer ? w.px : en.tx;
    var tz = en.seesPlayer ? w.pz : en.tz;
    var tdx = tx - en.x;
    var tdz = tz - en.z;
    var td2 = tdx * tdx + tdz * tdz;
    en.yawQ = tacTurnToward(en.yawQ, tacYawFor(tdx, tdz), TAC.ENEMY_TURN_RATE);
    if (en.bombCd === 0 && td2 >= TAC.BOMBER_MIN * TAC.BOMBER_MIN && td2 <= TAC.BOMBER_RANGE * TAC.BOMBER_RANGE) {
      w.throwBomb(en, tx, tz);
      en.bombCd = TAC.BOMBER_CD;
    } else if (td2 > TAC.BOMBER_RANGE * TAC.BOMBER_RANGE) {
      w.moveEnemy(en, tx, tz, TAC.SOLDIER_CHASE_SPEED);
    } else if (en.seesPlayer && d2 < TAC.BOMBER_MIN * TAC.BOMBER_MIN) {
      // too close to lob safely: back off to throwing distance
      w.moveEnemy(en, en.x - dx, en.z - dz, TAC.SOLDIER_CHASE_SPEED);
    }
    if (!en.seesPlayer) {
      var ldx = en.tx - en.x;
      var ldz = en.tz - en.z;
      if (ldx * ldx + ldz * ldz < 0.25) {
        en.state = 1;
        en.pauseT = 0;
      }
    }
  } else if (en.state === 1) {
    var arrived = w.moveEnemy(en, en.tx, en.tz, TAC.SOLDIER_INVESTIGATE_SPEED);
    if (arrived) {
      en.pauseT++;
      en.yawQ = (en.yawQ + 300) & 65535;
      if (en.pauseT >= TAC.INVESTIGATE_PAUSE) {
        en.state = 0;
        en.gauge = 0.0;
        en.pauseT = 0;
      }
    }
  } else {
    if (en.hasPatrol) {
      var gx = en.patToB ? en.patX : en.homeX;
      var gz = en.patToB ? en.patZ : en.homeZ;
      var got = w.moveEnemy(en, gx, gz, TAC.SOLDIER_INVESTIGATE_SPEED);
      if (got) en.patToB = !en.patToB;
    } else {
      en.yawQ = tacTurnToward(en.yawQ, en.baseYawQ, 80);
    }
  }
};

TacWorld.prototype.throwBomb = function (en, tx, tz) {
  var w = this;
  var ty = w.groundY(tx, tz, 1000.0, 0.35);
  w.bombs.push({ sx: en.x, sy: en.y + 1.4, sz: en.z, x: tx, y: ty, z: tz, t: 0, fuse: TAC.BOMB_FUSE, state: 0 });
  w.events.bomberThrow = { x: en.x, z: en.z };
};

TacWorld.prototype.stepBombs = function () {
  var w = this;
  for (var i = 0; i < w.bombs.length; i++) {
    var bo = w.bombs[i];
    if (bo.state === 2) continue;
    if (bo.state === 0) {
      bo.t++;
      if (bo.t >= TAC.BOMB_FLIGHT) {
        bo.state = 1;
        w.events.bombLand = { x: bo.x, z: bo.z };
      }
    } else {
      bo.fuse--;
      if (bo.fuse <= 0) {
        bo.state = 2;
        w.explodeAt(bo.x, bo.y + 0.2, bo.z, TAC.BOMB_BLAST_R, 2);
      }
    }
  }
};

// medkits heal 1 hp on touch; ignored while at full health (stay for later)
TacWorld.prototype.stepMedkits = function () {
  var w = this;
  if (w.hp >= w.maxHp) return;
  for (var i = 0; i < w.medkits.length; i++) {
    var mk = w.medkits[i];
    if (!mk.alive) continue;
    var dx = w.px - mk.x;
    var dz = w.pz - mk.z;
    var dy = w.py - mk.y;
    if (dy > -0.5 && dy < 1.0 && dx * dx + dz * dz <= 0.81) {
      mk.alive = false;
      w.hp++;
      w.events.medkit = true;
    }
  }
};

// the EMP veil FRIES a drone on contact: it dies and detonates on the spot
// (the player's piloted drone dies the same way). Checked at tick start AND
// again right after a dive step, so a kamikaze can NEVER detonate from
// inside the veil — the fry always wins over the self-destruct.
TacWorld.prototype.fryDrone = function (en) {
  var w = this;
  en.alive = false;
  w.enemiesLeft--;
  if (!w.events.kills) w.events.kills = [];
  w.events.kills.push({ x: en.x, y: en.y, z: en.z, type: en.type });
  w.events.jamZap = { x: en.x, y: en.y, z: en.z };
  w.explodeAt(en.x, en.y + 0.25, en.z, TAC.DRONE_BLAST_R, 2);
};

TacWorld.prototype.stepDrone = function (en, range2) {
  var w = this;
  if (w.squad && en.seesPlayer && en.alive && !en.crashing) w.squadIlluminate(en);
  // enemy drones are UNAFFECTED by EMP veils (the veil only denies the PLAYER's
  // captured drone that airspace). They fly through freely.

  // uplink check: if this drone's group has operators and none is alive, it
  // loses control and crashes (exploding where it lands).
  if (!en.crashing && en.group > 0 && w.opGroups[en.group]) {
    var ops = w.opGroups[en.group];
    var anyAlive = false;
    for (var oi = 0; oi < ops.length; oi++) {
      if (w.enemies[ops[oi]].alive) { anyAlive = true; break; }
    }
    if (!anyAlive) {
      en.crashing = true;
      w.events.droneCrash = true;
    }
  }
  if (en.crashing) {
    var fallStep = TAC.DRONE_CRASH_SPEED * TAC.TICK;
    en.y = en.y - fallStep;
    var gy = w.groundY(en.x, en.z, en.y, en.r);
    if (en.y <= gy + 0.15) {
      en.alive = false;
      w.enemiesLeft--;
      if (!w.events.kills) w.events.kills = [];
      w.events.kills.push({ x: en.x, y: en.y, z: en.z, type: en.type });
      w.explodeAt(en.x, en.y + 0.25, en.z, TAC.DRONE_BLAST_R, 2);
    }
    return;
  }
  w.visionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);

  if (en.state === 2) {
    var dx = w.px - en.x;
    var dz = w.pz - en.z;
    var dh2 = dx * dx + dz * dz;
    if (!en.diving && dh2 <= TAC.DRONE_DIVE_AT * TAC.DRONE_DIVE_AT) {
      en.diving = true;
      w.events.droneDive = true;
    }
    if (en.diving) {
      w.moveEnemy(en, w.px, w.pz, TAC.DRONE_CHASE_SPEED, true);
      var targetY = w.py + TAC.CHEST_H * 0.5; // aim at the body's middle (low when crouched in a pit)
      var fall = TAC.DRONE_DIVE_SPEED * TAC.TICK;
      if (en.y > targetY + fall) en.y = en.y - fall;
      else if (en.y < targetY - fall) en.y = en.y + fall;
      else en.y = targetY;
      var pcy = w.py + w.playerHitH() * 0.5; // true body center
      var dy = en.y - pcy;
      var d3 = dh2 + dy * dy;
      if (d3 <= TAC.DRONE_BOOM_AT * TAC.DRONE_BOOM_AT) {
        en.alive = false;
        w.enemiesLeft--;
        if (!w.events.kills) w.events.kills = [];
        w.events.kills.push({ x: en.x, y: en.y, z: en.z, type: en.type });
        w.explodeAt(en.x, en.y + 0.25, en.z, TAC.DRONE_BLAST_R, 2);
      }
    } else {
      w.moveEnemy(en, en.tx, en.tz, TAC.DRONE_CHASE_SPEED, true);
      // alerted: drop to the player's eye line FIRST and chase there — the
      // hover-then-vertical-drop was an unavoidable overhead kill on touch
      // controls; at aim height the drone can be intercepted (2026-07-20)
      var egy = w.groundY(en.x, en.z, en.y, en.r) + TAC.DRONE_ENGAGE_Y;
      var drop = TAC.DRONE_DESCEND * TAC.TICK;
      if (en.y > egy + drop) en.y = en.y - drop;
      else if (en.y < egy - drop) en.y = en.y + drop;
      else en.y = egy;
      if (!en.seesPlayer) {
        var ldx = en.tx - en.x;
        var ldz = en.tz - en.z;
        if (ldx * ldx + ldz * ldz < 0.25) {
          en.state = 1;
          en.pauseT = 0;
        }
      }
    }
  } else if (en.state === 1) {
    var arrived = w.moveEnemy(en, en.tx, en.tz, TAC.DRONE_PATROL_SPEED, true);
    if (arrived) {
      en.pauseT++;
      en.yawQ = (en.yawQ + 300) & 65535;
      if (en.pauseT >= TAC.INVESTIGATE_PAUSE) {
        en.state = 0;
        en.gauge = 0.0;
        en.pauseT = 0;
      }
    }
  } else {
    if (en.hasPatrol) {
      var gx = en.patToB ? en.patX : en.homeX;
      var gz = en.patToB ? en.patZ : en.homeZ;
      var got = w.moveEnemy(en, gx, gz, TAC.DRONE_PATROL_SPEED, true);
      if (got) en.patToB = !en.patToB;
    } else {
      en.yawQ = tacTurnToward(en.yawQ, en.baseYawQ, 80);
    }
    // drift back up to hover height if it dipped (e.g. cancelled dive)
    if (en.y < en.homeY) {
      var rise = 2.0 * TAC.TICK;
      en.y = en.y + rise;
      if (en.y > en.homeY) en.y = en.homeY;
    }
  }
};

// drone operator: unarmed support. Spots the player like any watcher (which
// alerts its whole group — it IS the radio), then runs away. Killing it cuts
// the uplink of every drone in its group.
TacWorld.prototype.stepOperator = function (en, range2) {
  var w = this;
  w.visionGauge(en, range2, TAC.VISION_COS2, w.sneaking ? 0.5 : 1.0);

  if (en.state === 2) {
    var dx = en.x - w.px;
    var dz = en.z - w.pz;
    var d2 = dx * dx + dz * dz;
    if (d2 > 0.01) {
      var d = Math.sqrt(d2);
      var fx = en.x + (dx / d) * 4.0;
      var fz = en.z + (dz / d) * 4.0;
      w.moveEnemy(en, fx, fz, TAC.OPERATOR_FLEE_SPEED);
    }
    if (!en.seesPlayer) {
      en.gauge -= TAC.GAUGE_DECAY;
      if (en.gauge <= 0.0) {
        en.gauge = 0.0;
        en.state = 1;
        en.pauseT = 0;
      }
    }
  } else if (en.state === 1) {
    en.pauseT++;
    en.yawQ = (en.yawQ + 300) & 65535; // nervous scan
    if (en.pauseT >= TAC.INVESTIGATE_PAUSE) {
      en.state = 0;
      en.gauge = 0.0;
      en.pauseT = 0;
    }
  } else {
    en.yawQ = tacTurnToward(en.yawQ, en.baseYawQ, 80);
  }
};

TacWorld.prototype.stepSniper = function (en, range2) {
  var w = this;
  if (en.attackCd > 0) {
    en.warnT = 0;
    en.seesPlayer = false;
    return;
  }
  var d2 = w.playerVisibleFrom(en, range2, TAC.SNIPER_COS2);
  en.seesPlayer = d2 >= 0.0;
  if (!en.seesPlayer) {
    // no target: sweep the scope across the arc instead of staring down one lane
    var cyc = en.cycleT % 600; // 12 s out-and-back
    var ph = cyc < 300 ? cyc : 600 - cyc;
    var off = Math.floor((ph * TAC.SNIPER_SWEEP * 2) / 300) - TAC.SNIPER_SWEEP;
    en.yawQ = tacTurnToward(en.yawQ, (en.baseYawQ + off) & 65535, TAC.SNIPER_SCAN_TURN);
    en.cycleT++;
  }
  if (en.seesPlayer) {
    en.yawQ = tacTurnToward(en.yawQ, tacYawFor(w.px - en.x, w.pz - en.z), TAC.SNIPER_TRACK_TURN);
    en.warnT++;
    en.tx = w.px;
    en.tz = w.pz;
    if (en.warnT === 1) w.events.sniperAim = true;
    if (en.warnT >= TAC.SNIPER_WARN) {
      // the shot: a real travelling bullet (same speed as any enemy round), so
      // from long range you SEE the tracer coming and can still break LOS / move
      w.fireEnemyBullet(en);
      w.events.sniperShot = true;
      en.warnT = 0;
      en.attackCd = TAC.SNIPER_COOLDOWN;
      w.addNoise(en.x, en.z, TAC.NOISE_SHOT_R);
    }
  } else {
    en.warnT -= TAC.SNIPER_WARN_DECAY;
    if (en.warnT < 0) en.warnT = 0;
  }
};

// --- replay verification --------------------------------------------------------

// Verifies a tac replay certificate against a stage. Returns
// { cleared, ticks, error? }. hardTickCap bounds worker CPU.
function tacRunReplay(stageData, replay, hardTickCap) {
  if (!replay || replay.v !== 't1' || typeof replay.data !== 'string') {
    return { cleared: false, ticks: 0, error: 'bad replay' };
  }
  if (replay.data.length > 900000) {
    return { cleared: false, ticks: 0, error: 'replay too large' };
  }
  var world;
  try {
    world = new TacWorld(stageData);
  } catch (e) {
    return { cleared: false, ticks: 0, error: 'bad stage' };
  }
  var cap = world.maxTicks < hardTickCap ? world.maxTicks : hardTickCap;
  var recs = tacDecodeTrace(replay.data, cap);
  if (!recs) return { cleared: false, ticks: 0, error: 'bad trace' };
  for (var i = 0; i < recs.length; i++) {
    var r = recs[i];
    world.step(r);
    if (world.clearedFlag) return { cleared: true, ticks: world.tick };
    if (world.dead || world.timedOutFlag) return { cleared: false, ticks: world.tick };
  }
  return { cleared: false, ticks: world.tick };
}
