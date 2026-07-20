// =============================================================================
// PromptSim — deterministic gameplay simulation (JS side).
// This file is a LINE-FOR-LINE mirror of
// PromptWorld/Assets/Scripts/Sim/PromptSim.cs. Any change there MUST be
// applied here identically, or replay verification will break.
//
// Determinism rules (both sides):
// - all state is double; every stage-JSON value is quantized to float32
//   (C#: (double)(float)v, JS: Math.fround(v)) before entering the sim
// - only + - * / and comparisons; no transcendental functions
// - at most one floating-point operation per statement (prevents FMA fusion)
// - fixed step 0.02s (50 Hz), tick counters are integers
//
// NOTE: this file has no imports/exports — the deploy script concatenates it
// in front of worker.js to form _worker.js.
// =============================================================================

const SIM = {
  TICK: 0.02,
  GRAVITY: 58.0,
  MOVE_SPEED: 8.0,
  JUMP_SPEED: 19.65,
  PLAYER_HALF: 0.5,
  BULLET_HALF: 0.22,
  BOOST_KICK: 4.0,
  COYOTE_TICKS: 5,
  BUFFER_TICKS: 6,
  CORNER_NUDGE: 0.28,
  BOOST_LOCK_TICKS: 30,
  FLIP_COOLDOWN_TICKS: 35,
  CRUMBLE_DELAY_TICKS: 25,
  CRUMBLE_RESPAWN_TICKS: 125,
  KILL_MARGIN_BELOW: 8.0,
  KILL_MARGIN_ABOVE: 12.0,
  GROUND_PROBE: 0.06,
  FALLER_FALL_SPEED: 8.0,
  FALLER_TELEGRAPH_TICKS: 18,
  FALLER_RISE_SPEED: 3.0,
  FALLER_WAIT_TICKS: 25,
  FALLER_MARGIN: 0.6,
  FALLER_RIDE_TOLERANCE: 0.35,
  AIR_DAMPING: 0.86,
  // Switch gate: how slowly it eases shut after you step off (ticks to go from
  // fully open back to fully closed). 90 ticks = 1.8s of grace.
  SWITCH_GATE_CLOSE_TICKS: 90,
  SWITCH_GATE_OPEN_TICKS: 6, // near-instant open when pressed
  ORBIT_STEPS: 24,
  ENEMY_STOMP_BOUNCE: 15.44,
  ENEMY_STOMP_JUMP_BOUNCE: 23.87,
  ENEMY_HIT_FLASH_TICKS: 10,
  ENEMY_STOMP_MARGIN: 0.30,
  ENEMY_STOMP_GRACE_TICKS: 8,
  ENEMY_WALK_SPEED: 3.5,
  ENEMY_JUMP_PERIOD_TICKS: 55,
  ENEMY_JUMP_SPEED: 15.44,
  ENEMY_GROUND_PROBE_AHEAD: 0.35,
  BOSS_JUMP_PERIOD_TICKS: 90,
  BOSS_JUMP_SPEED: 16.85,
  BOSS_FIRE_PERIOD_TICKS: 70,
  BOSS_FIRE_SPEED: 9.0,
  BOSS_FIRE_MAX_DIST: 18.0,
  FIREBALL_HALF: 0.28,
};

// Baked 24-step unit circle for the rotating hazard. Written as identical
// decimal literals in PromptWorld/Assets/Scripts/Sim/PromptSim.cs so both sides
// orbit byte-for-byte the same (no trig at runtime, which would diverge).
const ORBIT_COS = [
  1, 0.9659258127212524, 0.8660253882408142, 0.7071067690849304, 0.5, 0.258819043636322, 0, -0.258819043636322, -0.5, -0.7071067690849304, -0.8660253882408142, -0.9659258127212524, -1, -0.9659258127212524, -0.8660253882408142, -0.7071067690849304, -0.5, -0.258819043636322, 0, 0.258819043636322, 0.5, 0.7071067690849304, 0.8660253882408142, 0.9659258127212524
];
const ORBIT_SIN = [
  0, 0.258819043636322, 0.5, 0.7071067690849304, 0.8660253882408142, 0.9659258127212524, 1, 0.9659258127212524, 0.8660253882408142, 0.7071067690849304, 0.5, 0.258819043636322, 0, -0.258819043636322, -0.5, -0.7071067690849304, -0.8660253882408142, -0.9659258127212524, -1, -0.9659258127212524, -0.8660253882408142, -0.7071067690849304, -0.5, -0.258819043636322
];

function simQ(v) { return Math.fround(v); }

class SimWorld {
  constructor(startX, startY, timeLimit) {
    this.solids = [];
    this.movers = [];
    this.crumbles = [];
    this.triggers = [];
    this.fallers = [];
    this.conveyors = [];
    this.gates = [];
    this.keys = [];
    this.doors = [];
    this.cannons = [];
    this.rotors = [];
    this.waves = [];
    this.teleporters = [];
    this.fans = [];
    this.switches = [];
    this.switchGates = [];
    this.enemies = [];
    this.bossDoors = [];
    this.keysCollected = 0;

    this.startX = simQ(startX);
    this.startY = simQ(startY);
    this.killBottom = -12.0;
    this.killTop = 15.0;
    this.px = this.startX;
    this.py = this.startY;
    this.vx = 0.0;
    this.vy = 0.0;
    this.gravityDir = 1.0;
    this.lockTicks = 0;
    this.lastGroundedTick = -1000000;
    this.jumpPressedTick = -1000000;
    this.groundMover = -1;
    this.groundConveyor = -1;
    this.ridingFaller = -1;
    this.airCarryVx = 0.0;
    this.tickCount = 0;
    this.clearedFlag = false;
    this.timedOutFlag = false;
    this.maxLives = 5;
    this.livesLeft = 5;
    this.livesOut = false;

    const limit = simQ(timeLimit);
    const ticks = limit / SIM.TICK;
    this.maxTicks = Math.trunc(ticks);
  }

  addSolid(x, y, w, h) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    this.solids.push({ x: simQ(x), y: simQ(y), halfW: hw, halfH: hh });
  }

  addMover(x, y, w, h, dx, dy, period) {
    let p = simQ(period);
    if (p <= 0.0) p = 4.0;
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    const m = {
      baseX: simQ(x), baseY: simQ(y), halfW: hw, halfH: hh,
      dx: simQ(dx), dy: simQ(dy), period: p,
      x: 0.0, y: 0.0, prevX: 0.0, prevY: 0.0, deltaX: 0.0, deltaY: 0.0,
    };
    m.x = m.baseX;
    m.y = m.baseY;
    m.prevX = m.baseX;
    m.prevY = m.baseY;
    this.movers.push(m);
  }

  addCrumble(x, y, w, h) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    this.crumbles.push({ x: simQ(x), y: simQ(y), halfW: hw, halfH: hh, touchedTick: -1 });
  }

  addFaller(x, y, w, h, dy) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    let fall = simQ(dy);
    if (fall <= 0.0) fall = 4.0;
    this.fallers.push({
      x: simQ(x), y: simQ(y), halfW: hw, halfH: hh,
      baseY: simQ(y), dy: fall, state: 0, offset: 0.0, waitLeft: 0,
      prevY: simQ(y), deltaY: 0.0,
    });
  }

  // Ground-collision for crushers: a crusher may not sink through a solid
  // floor. Clamp each crusher's slam distance so its BOTTOM edge comes to rest
  // flush on the highest solid directly beneath it, instead of sinking until
  // its top edge lines up with the floor. Runs after all parts load.
  resolveFallerLandings() {
    for (const f of this.fallers) {
      const bottom = f.y - f.halfH;
      const cLeft = f.x - f.halfW;
      const cRight = f.x + f.halfW;
      let maxFall = f.dy;
      for (const s of this.solids) {
        const sLeft = s.x - s.halfW;
        const sRight = s.x + s.halfW;
        if (cRight <= sLeft) continue;
        if (cLeft >= sRight) continue;
        const sTop = s.y + s.halfH;
        if (sTop > bottom) continue;
        const gap = bottom - sTop;
        if (gap < maxFall) maxFall = gap;
      }
      if (maxFall < 0.0) maxFall = 0.0;
      f.dy = maxFall;
    }
  }

  // Precompute each cannon's firing range: distance from muzzle to the first
  // solid in the bullet's path. The bullet flies that far (until it hits a
  // wall) instead of vanishing at an arbitrary per-period distance.
  resolveCannonRanges() {
    for (const c of this.cannons) {
      const halfDir = c.dir * c.halfW;
      const muzzle = c.x + halfDir;
      const by = c.y;
      let best = 1200.0;
      for (const s of this.solids) {
        const sLeft = s.x - s.halfW;
        const sRight = s.x + s.halfW;
        const sTop = s.y + s.halfH;
        const sBot = s.y - s.halfH;
        if (by + SIM.BULLET_HALF <= sBot) continue;
        if (by - SIM.BULLET_HALF >= sTop) continue;
        if (c.dir > 0.0) {
          const face = sLeft - SIM.BULLET_HALF;
          const dist = face - muzzle;
          if (dist < 0.0) continue;
          if (dist < best) best = dist;
        } else {
          const face = sRight + SIM.BULLET_HALF;
          const dist = muzzle - face;
          if (dist < 0.0) continue;
          if (dist < best) best = dist;
        }
      }
      if (best < 0.0) best = 0.0;
      c.wallDist = best;
    }
  }

  addConveyor(x, y, w, h, dirX, power) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    let speed = simQ(power);
    if (speed <= 0.0) speed = 3.0;
    let dir = 1.0;
    if (simQ(dirX) < 0.0) dir = -1.0;
    this.conveyors.push({ x: simQ(x), y: simQ(y), halfW: hw, halfH: hh, dir, speed });
  }

  addGate(x, y, w, h, period, phase) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    let p = simQ(period);
    if (p <= 0.0) p = 2.0;
    const pt = p / SIM.TICK;
    const ph = simQ(phase);
    const pht = ph / SIM.TICK;
    const gate = {
      x: simQ(x), y: simQ(y), halfW: hw, halfH: hh,
      periodTicks: Math.trunc(pt),
      phaseTicks: Math.trunc(pht),
      onTicks: 0,
    };
    gate.onTicks = Math.trunc(gate.periodTicks / 2);
    if (gate.periodTicks < 2) gate.periodTicks = 2;
    if (gate.onTicks < 1) gate.onTicks = 1;
    this.gates.push(gate);
  }

  addKey(x, y, w, h) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    this.keys.push({ x: simQ(x), y: simQ(y), halfW: hw, halfH: hh, collected: false });
  }

  addDoor(x, y, w, h) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    this.doors.push({ x: simQ(x), y: simQ(y), halfW: hw, halfH: hh });
  }

  addCannon(x, y, w, h, dirX, power, period, phase) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    let speed = simQ(power);
    if (speed <= 0.0) speed = 7.0;
    let dir = 1.0;
    if (simQ(dirX) < 0.0) dir = -1.0;
    let p = simQ(period);
    if (p <= 0.0) p = 2.0;
    const pt = p / SIM.TICK;
    const ph = simQ(phase);
    const pht = ph / SIM.TICK;
    const cannon = {
      x: simQ(x), y: simQ(y), halfW: hw, halfH: hh,
      dir, speed, wallDist: 0.0,
      periodTicks: Math.trunc(pt), phaseTicks: Math.trunc(pht),
      bulletActive: false, bulletX: 0.0, bulletY: 0.0,
    };
    if (cannon.periodTicks < 2) cannon.periodTicks = 2;
    this.cannons.push(cannon);
  }

  // Rotating hazard: a spike head orbiting the block's center. w/h set the
  // orbit diameter; power = seconds per revolution; dirX<0 spins the other way;
  // dx = head size (defaults small).
  addRotor(x, y, w, h, power, dirX, head) {
    const rw = simQ(w) / 2.0;
    const rh = simQ(h) / 2.0;
    let radius = rw;
    if (rh > radius) radius = rh;
    let spin = 1.0;
    if (simQ(dirX) < 0.0) spin = -1.0;
    let p = simQ(power);
    if (p <= 0.0) p = 2.0;
    const pt = p / SIM.TICK;
    let hh = simQ(head);
    if (hh <= 0.0) hh = 0.35;
    const r = {
      x: simQ(x), y: simQ(y), halfW: rw, halfH: rh,
      radius, spinDir: spin,
      periodTicks: Math.trunc(pt), phaseTicks: 0,
      headHalf: hh, headX: simQ(x), headY: simQ(y),
    };
    if (r.periodTicks < SIM.ORBIT_STEPS) r.periodTicks = SIM.ORBIT_STEPS;
    this.rotors.push(r);
  }

  // Sweeping wall of death. power = speed (units/sec); dirX/dirY pick the sweep
  // direction (left->right = dirX 1; down->up = dirY 1; up->down = dirY -1);
  // period = seconds to wait before it starts moving.
  addWave(x, y, w, h, power, dirX, dirY, period) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    let spd = simQ(power);
    if (spd <= 0.0) spd = 8.0;
    const perTick = spd * SIM.TICK;
    const ddx = simQ(dirX);
    const ddy = simQ(dirY);
    let ux = 0.0;
    let uy = 0.0;
    if (ddx > 0.0) ux = 1.0;
    else if (ddx < 0.0) ux = -1.0;
    else if (ddy > 0.0) uy = 1.0;
    else if (ddy < 0.0) uy = -1.0;
    else ux = 1.0;
    let delay = simQ(period);
    if (delay < 0.0) delay = 0.0;
    const startTick = Math.trunc(delay / SIM.TICK);
    const wx = simQ(x);
    const wy = simQ(y);
    const offX = wx - this.startX;
    const offY = wy - this.startY;
    const wv = {
      x: wx, y: wy, halfW: hw, halfH: hh,
      speed: perTick, dirX: ux, dirY: uy, delayTicks: startTick,
      offsetX: offX, offsetY: offY,
      anchorX: wx, anchorY: wy, restartTick: startTick,
      curX: wx, curY: wy,
    };
    this.waves.push(wv);
  }

  // Teleporter endpoint. dirX>=0 marks an entry, dirX<0 marks an exit; period
  // carries the pair id (integer) so an entry warps to the exit sharing it.
  addTeleporter(x, y, w, h, dirX, pairId) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    const isEntry = simQ(dirX) >= 0.0;
    const pid = Math.trunc(simQ(pairId));
    this.teleporters.push({
      x: simQ(x), y: simQ(y), halfW: hw, halfH: hh,
      pairId: pid, isEntry, exitIndex: -1, wasOverlapping: false,
    });
  }

  // Fan: a wind zone that pushes the player while overlapping. dirX/power set
  // the horizontal component, dy the vertical (default: straight up).
  addFan(x, y, w, h, dirX, power, dy) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    const px = simQ(dirX);
    let py = simQ(dy);
    if (px === 0.0 && py === 0.0) py = 1.0;
    let spd = simQ(power);
    if (spd <= 0.0) spd = 12.0;
    this.fans.push({
      x: simQ(x), y: simQ(y), halfW: hw, halfH: hh,
      dirX: px, dirY: py, power: spd,
    });
  }

  // Pressure switch. period carries the gate-group id it toggles.
  addSwitch(x, y, w, h, gateId) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    const gid = Math.trunc(simQ(gateId));
    this.switches.push({ x: simQ(x), y: simQ(y), halfW: hw, halfH: hh, gateId: gid });
  }

  // Switch-linked gate. period carries the gate-group id. Starts closed (solid).
  addSwitchGate(x, y, w, h, gateId) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    const gid = Math.trunc(simQ(gateId));
    const cy = simQ(y);
    const top = cy + hh;
    this.switchGates.push({
      x: simQ(x), y: cy, halfW: hw, halfH: hh, gateId: gid, openTicks: 0,
      fullHalfH: hh, top,
    });
  }

  // An enemy. mode (dirX): 0 glide, 1 chaser, 2 patrol-walk, 3 jumper. dx/period
  // = patrol span & round-trip seconds; power = HP (default 1); dyFlag>0 = BOSS.
  addEnemy(x, y, w, h, dx, period, power, dyFlag, mode) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    let hp = Math.trunc(simQ(power));
    if (hp < 1) hp = 1;
    let p = simQ(period);
    if (p <= 0.0) p = 4.0;
    let md = Math.trunc(simQ(mode));
    if (md < 0 || md > 3) md = 0;
    const e = {
      baseX: simQ(x), baseY: simQ(y), halfW: hw, halfH: hh,
      dx: simQ(dx), period: p,
      hp, maxHp: hp,
      isBoss: simQ(dyFlag) > 0.0,
      dead: false, hitTick: -1000000, facing: 1,
      x: 0.0, y: 0.0,
      vyJump: 0.0, groundY: simQ(y), inAir: false,
      fireActive: false, fireX: 0.0, fireY: 0.0, fireDir: 1.0, fireStartTick: -1000000,
      fireLaunchX: 0.0, fireLaunchY: 0.0,
      mode: md, walkDir: 1, speed: SIM.ENEMY_WALK_SPEED,
    };
    e.x = e.baseX;
    e.y = e.baseY;
    this.enemies.push(e);
  }

  // A boss door: solid wall that stays shut while any boss enemy is alive and
  // vanishes once every boss is defeated (opening the way to the goal).
  addBossDoor(x, y, w, h) {
    const hw = simQ(w) / 2.0;
    const hh = simQ(h) / 2.0;
    this.bossDoors.push({ x: simQ(x), y: simQ(y), halfW: hw, halfH: hh });
  }

  // True once every boss enemy has been defeated (or there are none).
  bossDoorsOpen() {
    for (const e of this.enemies) {
      if (e.isBoss && !e.dead) return false;
    }
    return true;
  }

  // Does a solid box block the enemy's BODY at x = nx (a wall it can't walk
  // through)? Ignores the floor it stands on (bodyBot lifted by 0.1).
  enemyBlockedBy(en, nx, b) {
    const sTop = b.y + b.halfH;
    const sBot = b.y - b.halfH;
    const sLeft = b.x - b.halfW;
    const sRight = b.x + b.halfW;
    const foot = en.baseY - en.halfH;
    const bodyTop = en.baseY + en.halfH;
    const bodyBot = foot + 0.1;
    return (nx + en.halfW > sLeft && nx - en.halfW < sRight &&
            bodyTop > sBot && bodyBot < sTop);
  }

  // Can a walking enemy stand at x = nx? True when ground supports its feet just
  // ahead (won't walk off a ledge) and no solid wall blocks its body there — this
  // includes plain solids, closed doors, closed switch gates and shut boss doors,
  // so a monster can't stroll through a wall or a locked exit.
  enemyCanStand(en, nx) {
    const foot = en.baseY - en.halfH;
    const dir = nx >= en.x ? 1.0 : -1.0;
    const edgeX = nx + dir * en.halfW;
    let groundAhead = false;
    for (const s of this.solids) {
      const sTop = s.y + s.halfH;
      const sLeft = s.x - s.halfW;
      const sRight = s.x + s.halfW;
      let gap = foot - sTop;
      if (gap < 0.0) gap = -gap;
      if (gap <= SIM.ENEMY_GROUND_PROBE_AHEAD && edgeX > sLeft && edgeX < sRight) {
        groundAhead = true;
      }
      if (this.enemyBlockedBy(en, nx, s)) return false;
    }
    // Walls the enemy also can't pass: shut boss doors, closed key doors, and
    // solid switch gates.
    if (!this.bossDoorsOpen()) {
      for (const d of this.bossDoors) if (this.enemyBlockedBy(en, nx, d)) return false;
    }
    if (!this.doorsOpen()) {
      for (const d of this.doors) if (this.enemyBlockedBy(en, nx, d)) return false;
    }
    for (const sg of this.switchGates) {
      if (this.switchGateSolid(sg) && this.enemyBlockedBy(en, nx, sg)) return false;
    }
    return groundAhead;
  }

  addTrigger(kind, x, y, w, h, power, dirX) {
    let hw;
    let hh;
    let cy = simQ(y);
    if (kind === 'hazard') {
      hw = simQ(w) * 0.35;
      hh = simQ(h) * 0.35;
    } else if (kind === 'pad') {
      hw = simQ(w) / 2.0;
      const grown = simQ(h) + 0.5;
      hh = grown / 2.0;
      cy = cy + 0.25;
    } else {
      hw = simQ(w) / 2.0;
      hh = simQ(h) / 2.0;
    }
    this.triggers.push({
      kind, x: simQ(x), y: cy, halfW: hw, halfH: hh,
      power: simQ(power), dirX: simQ(dirX),
      lastFlipTick: -1000000, wasOverlapping: false,
    });
  }

  crumbleActive(c) {
    if (c.touchedTick < 0) return true;
    const vanishAt = c.touchedTick + SIM.CRUMBLE_DELAY_TICKS;
    return this.tickCount < vanishAt;
  }

  gateActive(g) {
    const phase = this.tickCount + g.phaseTicks;
    const m = phase % g.periodTicks;
    return m < g.onTicks;
  }

  doorsOpen() {
    if (this.keys.length === 0) return true;
    return this.keysCollected >= this.keys.length;
  }

  // Recompute a switch gate's LIVE collision box from openTicks: the gate hangs
  // from its FIXED TOP edge and its height shrinks from fullHalfH*2 (closed,
  // reaching the floor) toward ~0 (open, raised up). The BOTTOM edge rises as it
  // opens and descends as it closes — a portcullis. Returns true while it still
  // has enough height to collide. openTicks is constant within a tick so the
  // mutation is idempotent (deterministic).
  switchGateSolid(g) {
    const frac = g.openTicks / SIM.SWITCH_GATE_CLOSE_TICKS;
    const remain = 1.0 - frac;
    const fullH = g.fullHalfH + g.fullHalfH;
    const liveH = fullH * remain;
    const liveHalfH = liveH / 2.0;
    g.halfH = liveHalfH;
    g.y = g.top - liveHalfH; // hang from the fixed top; bottom rises as it opens
    return liveHalfH > 0.02;
  }

  static overlaps(ax, ay, ahw, ahh, b) {
    let dx = ax - b.x;
    if (dx < 0.0) dx = -dx;
    let dy = ay - b.y;
    if (dy < 0.0) dy = -dy;
    const limX = ahw + b.halfW;
    const limY = ahh + b.halfH;
    if (dx >= limX) return false;
    if (dy >= limY) return false;
    return true;
  }

  // Highest solid/gate/door surface top strictly below `fromY` that overlaps
  // the player's horizontal span. Returns a very low value if none — used by
  // the crusher to decide whether a slam squishes the player against a floor.
  surfaceTopBelow(fromY) {
    let best = -1000000.0;
    for (const s of this.solids) {
      if (s.x - s.halfW >= this.px + SIM.PLAYER_HALF) continue;
      if (s.x + s.halfW <= this.px - SIM.PLAYER_HALF) continue;
      const top = s.y + s.halfH;
      if (top <= fromY && top > best) best = top;
    }
    for (const g of this.gates) {
      if (!this.gateActive(g)) continue;
      if (g.x - g.halfW >= this.px + SIM.PLAYER_HALF) continue;
      if (g.x + g.halfW <= this.px - SIM.PLAYER_HALF) continue;
      const top = g.y + g.halfH;
      if (top <= fromY && top > best) best = top;
    }
    if (!this.doorsOpen()) {
      for (const d of this.doors) {
        if (d.x - d.halfW >= this.px + SIM.PLAYER_HALF) continue;
        if (d.x + d.halfW <= this.px - SIM.PLAYER_HALF) continue;
        const top = d.y + d.halfH;
        if (top <= fromY && top > best) best = top;
      }
    }
    if (!this.bossDoorsOpen()) {
      for (const d of this.bossDoors) {
        if (d.x - d.halfW >= this.px + SIM.PLAYER_HALF) continue;
        if (d.x + d.halfW <= this.px - SIM.PLAYER_HALF) continue;
        const top = d.y + d.halfH;
        if (top <= fromY && top > best) best = top;
      }
    }
    for (const sg of this.switchGates) {
      if (!this.switchGateSolid(sg)) continue;
      if (sg.x - sg.halfW >= this.px + SIM.PLAYER_HALF) continue;
      if (sg.x + sg.halfW <= this.px - SIM.PLAYER_HALF) continue;
      const top = sg.y + sg.halfH;
      if (top <= fromY && top > best) best = top;
    }
    return best;
  }

  probeGround() {
    const shift = SIM.GROUND_PROBE * this.gravityDir;
    const probeY = this.py - shift;
    for (const s of this.solids) {
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, s)) return true;
    }
    for (const m of this.movers) {
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, m)) return true;
    }
    for (const c of this.crumbles) {
      if (!this.crumbleActive(c)) continue;
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, c)) return true;
    }
    for (const cv of this.conveyors) {
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, cv)) return true;
    }
    for (const g of this.gates) {
      if (!this.gateActive(g)) continue;
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, g)) return true;
    }
    if (!this.doorsOpen()) {
      for (const d of this.doors) {
        if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, d)) return true;
      }
    }
    if (!this.bossDoorsOpen()) {
      for (const d of this.bossDoors) {
        if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, d)) return true;
      }
    }
    for (const f of this.fallers) {
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, f)) return true;
    }
    for (const cn of this.cannons) {
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, cn)) return true;
    }
    for (const sg of this.switchGates) {
      if (!this.switchGateSolid(sg)) continue;
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, sg)) return true;
    }
    return false;
  }

  resolveAxis(xAxis) {
    for (const s of this.solids) this.resolveAgainst(s, xAxis, null);
    // The platform a player is riding must not shove them HORIZONTALLY: while
    // standing on it there is always a sub-pixel vertical overlap, so an x-axis
    // resolve would nudge the player toward the platform's nearer edge every
    // tick and slide them off (the "drifts left off the lift" bug). Vertical
    // resolution still runs so the platform holds them up.
    for (let i = 0; i < this.movers.length; i++) {
      if (xAxis && i === this.groundMover) continue;
      this.resolveAgainst(this.movers[i], xAxis, null);
    }
    for (const c of this.crumbles) {
      if (!this.crumbleActive(c)) continue;
      this.resolveAgainst(c, xAxis, c);
    }
    for (const cv of this.conveyors) this.resolveAgainst(cv, xAxis, null);
    for (const g of this.gates) {
      if (!this.gateActive(g)) continue;
      this.resolveAgainst(g, xAxis, null);
    }
    if (!this.doorsOpen()) {
      for (const d of this.doors) this.resolveAgainst(d, xAxis, null);
    }
    if (!this.bossDoorsOpen()) {
      for (const d of this.bossDoors) this.resolveAgainst(d, xAxis, null);
    }
    // The crusher a player is riding must not shove them sideways out of its
    // box; every other crusher still collides normally.
    for (let i = 0; i < this.fallers.length; i++) {
      if (i === this.ridingFaller) continue;
      this.resolveAgainst(this.fallers[i], xAxis, null);
    }
    for (const cn of this.cannons) this.resolveAgainst(cn, xAxis, null);
    for (const sg of this.switchGates) {
      if (!this.switchGateSolid(sg)) continue;
      this.resolveAgainst(sg, xAxis, null);
    }
  }

  overlapsAnySolid() {
    for (const s of this.solids) {
      if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, s)) return true;
    }
    for (const m of this.movers) {
      if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, m)) return true;
    }
    for (const cv of this.conveyors) {
      if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, cv)) return true;
    }
    for (const g of this.gates) {
      if (!this.gateActive(g)) continue;
      if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, g)) return true;
    }
    if (!this.doorsOpen()) {
      for (const d of this.doors) {
        if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, d)) return true;
      }
    }
    if (!this.bossDoorsOpen()) {
      for (const d of this.bossDoors) {
        if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, d)) return true;
      }
    }
    for (const cn of this.cannons) {
      if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, cn)) return true;
    }
    for (const sg of this.switchGates) {
      if (!this.switchGateSolid(sg)) continue;
      if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, sg)) return true;
    }
    return false;
  }

  resolveAgainst(b, xAxis, crumble) {
    if (!SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, b)) return;

    if (crumble !== null && crumble.touchedTick < 0) {
      crumble.touchedTick = this.tickCount;
      this.events.crumbled = true;
    }

    if (xAxis) {
      const lim = SIM.PLAYER_HALF + b.halfW;
      if (this.px < b.x) {
        this.px = b.x - lim;
      } else {
        this.px = b.x + lim;
      }
      this.vx = 0.0;
    } else {
      const lim = SIM.PLAYER_HALF + b.halfH;
      const hitFromBelow = this.py < b.y; // moving up into a ceiling (normal gravity)
      if (hitFromBelow) {
        // CORNER CORRECTION: if the player only barely clips this ceiling's
        // corner, slide them sideways past the edge so the jump isn't stopped
        // dead. Compute how far they overlap each vertical edge.
        const pRight = this.px + SIM.PLAYER_HALF;
        const bLeft = b.x - b.halfW;
        const overlapL = pRight - bLeft;   // player's right past block's left edge
        const pLeft = this.px - SIM.PLAYER_HALF;
        const bRight = b.x + b.halfW;
        const overlapR = bRight - pLeft;   // block's right past player's left edge
        if (overlapL > 0.0 && overlapL <= SIM.CORNER_NUDGE && overlapR > overlapL) {
          // Clipping the LEFT corner: nudge left, slip past, keep rising.
          const nx = bLeft - SIM.PLAYER_HALF;
          this.px = nx;
          return;
        }
        if (overlapR > 0.0 && overlapR <= SIM.CORNER_NUDGE && overlapL > overlapR) {
          // Clipping the RIGHT corner: nudge right, slip past, keep rising.
          const nx2 = bRight + SIM.PLAYER_HALF;
          this.px = nx2;
          return;
        }
        this.py = b.y - lim;
      } else {
        this.py = b.y + lim;
      }
      this.vy = 0.0;
      // If a launched/boosted player slams into a ceiling, release the control
      // lock so they immediately free-fall under their own steering.
      const intoCeiling = this.gravityDir > 0.0 ? hitFromBelow : !hitFromBelow;
      if (intoCeiling && this.lockTicks > 0) this.lockTicks = 0;
    }
  }

  // True when the player was standing on top of this crusher at the START of
  // the tick (feet on its PRE-MOVE top edge). Tested against prevY so a fast
  // slam that drops away from the feet still counts as riding.
  ridingFallerTop(f) {
    const left = f.x - f.halfW;
    const right = f.x + f.halfW;
    if (this.px + SIM.PLAYER_HALF <= left) return false;
    if (this.px - SIM.PLAYER_HALF >= right) return false;
    // Not "riding" while jumping/rising AWAY from the top: a player moving up
    // (against gravity) is leaping off, not standing. Without this the glue
    // re-captures them ~0.35u after a jump and yanks them back down, so jumps
    // off a crusher barely leave the surface.
    const vUp = this.vy * this.gravityDir;
    if (vUp > 0.0) return false;
    const prevTop = f.prevY + f.halfH;
    const feet = this.py - SIM.PLAYER_HALF;
    let gap = feet - prevTop;
    if (gap < 0.0) gap = -gap;
    return gap <= SIM.FALLER_RIDE_TOLERANCE;
  }

  // Glue a riding player's feet exactly to the crusher's current top.
  stickToFallerTop(f) {
    const top = f.y + f.halfH;
    this.py = top + SIM.PLAYER_HALF;
    if (this.vy < 0.0) this.vy = 0.0;
  }

  respawn() {
    this.px = this.startX;
    this.py = this.startY;
    this.vx = 0.0;
    this.vy = 0.0;
    this.gravityDir = 1.0;
    this.lockTicks = 0;
    this.groundMover = -1;
    this.groundConveyor = -1;
    this.airCarryVx = 0.0;
    this.lastGroundedTick = -1000000;
    this.jumpPressedTick = -1000000;
    // Dying returns collected keys to the stage: you must re-collect them,
    // so a death after grabbing a key can't leave doors permanently open
    // (which would let you skip the whole key challenge).
    if (this.keysCollected > 0) {
      for (const k of this.keys) k.collected = false;
      this.keysCollected = 0;
    }
    // Dying also RESURRECTS every enemy you defeated — a death is a full retry,
    // so enemies return to their spawn state (same idea as keys returning).
    for (const en of this.enemies) {
      en.dead = false;
      en.hp = en.maxHp;
      en.hitTick = -1000000;
      en.facing = 1;
      en.x = en.baseX;
      en.y = en.baseY;
      en.groundY = en.baseY;
      en.inAir = false;
      en.vyJump = 0.0;
      en.fireActive = false;
      en.fireStartTick = -1000000;
      en.walkDir = 1;
    }
    // Waves also restart: re-anchor behind the (possibly checkpoint-moved) respawn
    // point and re-arm the delay, so you don't respawn straight into a wave that
    // kept sweeping while you were dead.
    for (const wv of this.waves) {
      wv.anchorX = this.startX + wv.offsetX;
      wv.anchorY = this.startY + wv.offsetY;
      wv.restartTick = this.tickCount;
      wv.curX = wv.anchorX;
      wv.curY = wv.anchorY;
    }
    this.events.respawned = true;

    // Each death costs a life; running out ends the run (game over), same
    // terminal state as a time-out but for a different reason.
    if (this.livesLeft > 0) this.livesLeft = this.livesLeft - 1;
    if (this.livesLeft <= 0) {
      this.livesOut = true;
      this.timedOutFlag = true;
      this.events.timedOut = true;
    }
  }

  step(input) {
    this.events = {};
    if (this.clearedFlag || this.timedOutFlag) return this.events;

    this.tickCount = this.tickCount + 1;

    // 1. movers
    for (const m of this.movers) {
      const tt = this.tickCount * SIM.TICK;
      const s = tt / m.period;
      const fl = Math.floor(s);
      const f = s - fl;
      let k;
      if (f < 0.5) {
        k = f * 2.0;
      } else {
        const u = 1.0 - f;
        k = u * 2.0;
      }
      const ox = m.dx * k;
      const oy = m.dy * k;
      const nx = m.baseX + ox;
      const ny = m.baseY + oy;
      m.prevX = m.x;
      m.prevY = m.y;
      m.x = nx;
      m.y = ny;
      m.deltaX = m.x - m.prevX;
      m.deltaY = m.y - m.prevY;
    }

    // 1b. fallers (crushers): trigger when the player passes below, slam
    // down, wait, rise back. Contact while falling crushes (respawn).
    this.ridingFaller = -1;
    for (let fi = 0; fi < this.fallers.length; fi++) {
      const f = this.fallers[fi];
      f.prevY = f.y;
      const wasOnTop = this.ridingFallerTop(f);
      if (wasOnTop) this.ridingFaller = fi;
      if (f.state === 0) {
        let lo = f.x - f.halfW;
        lo = lo - SIM.FALLER_MARGIN;
        let hi = f.x + f.halfW;
        hi = hi + SIM.FALLER_MARGIN;
        const bottom = f.y - f.halfH;
        if (this.px > lo && this.px < hi && this.py < bottom) {
          f.state = 4; // telegraph before slamming
          f.waitLeft = SIM.FALLER_TELEGRAPH_TICKS;
        }
      } else if (f.state === 4) {
        f.waitLeft = f.waitLeft - 1;
        if (f.waitLeft <= 0) f.state = 1;
      } else if (f.state === 1) {
        const d = SIM.FALLER_FALL_SPEED * SIM.TICK;
        f.offset = f.offset + d;
        if (f.offset >= f.dy) {
          f.offset = f.dy;
          f.state = 2;
          f.waitLeft = SIM.FALLER_WAIT_TICKS;
          this.events.slammed = true;
        }
        f.y = f.baseY - f.offset;
        f.deltaY = f.y - f.prevY;
        // A rider on top rides the slam down (glued later in 9c). A player
        // caught UNDER the descending crusher is not killed on mere touch: the
        // crusher pushes them down like a heavy ceiling. They are only crushed
        // if pinned against a surface below with less room than their own body.
        if (!wasOnTop && SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, f)) {
          const fallerBottom = f.y - f.halfH;
          const pushedPy = fallerBottom - SIM.PLAYER_HALF;
          const surfaceTop = this.surfaceTopBelow(fallerBottom);
          const room = fallerBottom - surfaceTop;
          if (room < SIM.PLAYER_HALF + SIM.PLAYER_HALF) {
            this.respawn();
          } else if (pushedPy < this.py) {
            this.py = pushedPy;
            if (this.vy > 0.0) this.vy = 0.0;
          }
        }
        continue;
      } else if (f.state === 2) {
        f.waitLeft = f.waitLeft - 1;
        if (f.waitLeft <= 0) f.state = 3;
      } else {
        const d = SIM.FALLER_RISE_SPEED * SIM.TICK;
        f.offset = f.offset - d;
        if (f.offset <= 0.0) {
          f.offset = 0.0;
          f.state = 0;
        }
      }
      f.y = f.baseY - f.offset;
      f.deltaY = f.y - f.prevY;
      // (Riding is applied in section 9c, after player physics.)
    }

    // 1c. rotating hazards: advance each spike head around its orbit using
    // the baked unit-circle table (no trig). Kill check happens post-physics.
    for (const r of this.rotors) {
      const phase = this.tickCount + r.phaseTicks;
      const frac = phase / r.periodTicks;
      const flr = Math.floor(frac);
      const intoRev = frac - flr;
      const stepF = intoRev * SIM.ORBIT_STEPS;
      let idx = Math.trunc(stepF);
      if (idx >= SIM.ORBIT_STEPS) idx = idx - SIM.ORBIT_STEPS;
      if (r.spinDir < 0.0) {
        idx = SIM.ORBIT_STEPS - idx;
        if (idx >= SIM.ORBIT_STEPS) idx = idx - SIM.ORBIT_STEPS;
      }
      const ox = ORBIT_COS[idx] * r.radius;
      const oy = ORBIT_SIN[idx] * r.radius;
      r.headX = r.x + ox;
      r.headY = r.y + oy;
    }

    // 1c3. waves sweep steadily from their anchor once the delay has passed.
    // Anchor + restart tick reset on respawn so the wave re-chases from behind
    // the respawn point. Position is linear in elapsed ticks (byte-identical).
    for (const wv of this.waves) {
      const since = this.tickCount - wv.restartTick;
      let elapsed = since - wv.delayTicks;
      if (elapsed < 0) elapsed = 0;
      const dist = elapsed * wv.speed;
      const ax = dist * wv.dirX;
      const ay = dist * wv.dirY;
      wv.curX = wv.anchorX + ax;
      wv.curY = wv.anchorY + ay;
    }

    // 1c2. enemies move per their mode. Defeated enemies stop.
    for (const en of this.enemies) {
      if (en.dead) continue;
      const prevX = en.x;
      if (en.mode === 0) {
        const ett = this.tickCount * SIM.TICK;
        const es = ett / en.period;
        const efl = Math.floor(es);
        const ef = es - efl;
        let ek;
        if (ef < 0.5) ek = ef * 2.0;
        else { const eu = 1.0 - ef; ek = eu * 2.0; }
        const eox = en.dx * ek;
        en.x = en.baseX + eox;
      } else if (en.mode === 1) {
        // CHASER: step toward the player. If that way is blocked (ledge or wall,
        // e.g. the player is across a gap), PACE the other way instead of freezing
        // so it always looks alive; flip pace direction when that end is blocked too.
        const want = this.px >= en.x ? 1 : -1;
        const towardX = en.x + en.speed * SIM.TICK * want;
        if (this.enemyCanStand(en, towardX)) { en.x = towardX; en.walkDir = want; }
        else {
          const paceX = en.x + en.speed * SIM.TICK * en.walkDir;
          if (this.enemyCanStand(en, paceX)) en.x = paceX;
          else en.walkDir = -en.walkDir;
        }
      } else if (en.mode === 2) {
        // PATROL
        const stepx = en.speed * SIM.TICK * en.walkDir;
        const nx = en.x + stepx;
        if (this.enemyCanStand(en, nx)) en.x = nx;
        else en.walkDir = -en.walkDir;
      } else {
        // JUMPER: hop toward the player (a lively hopping chaser). Drifts in the
        // player's direction each tick but won't hop off a ledge or into a wall.
        const want = this.px >= en.x ? 1 : -1;
        en.walkDir = want;
        const stepx = en.speed * 0.6 * SIM.TICK * want;
        const nx = en.x + stepx;
        if (this.enemyCanStand(en, nx)) en.x = nx;
      }
      const move = en.x - prevX;
      if (move > 0.0) en.facing = 1;
      else if (move < 0.0) en.facing = -1;

      // JUMPER vertical hop (non-boss).
      if (!en.isBoss && en.mode === 3) {
        const jp = this.tickCount % SIM.ENEMY_JUMP_PERIOD_TICKS;
        if (jp === 0 && !en.inAir) {
          en.inAir = true;
          en.vyJump = SIM.ENEMY_JUMP_SPEED;
        }
        if (en.inAir) {
          const dvj = SIM.GRAVITY * SIM.TICK;
          en.vyJump = en.vyJump - dvj;
          const dyj = en.vyJump * SIM.TICK;
          en.y = en.y + dyj;
          if (en.y <= en.groundY) {
            en.y = en.groundY;
            en.inAir = false;
            en.vyJump = 0.0;
          }
        }
      }

      if (en.isBoss) {
        // Boss HOP.
        const jphase = this.tickCount % SIM.BOSS_JUMP_PERIOD_TICKS;
        if (jphase === 0 && !en.inAir) {
          en.inAir = true;
          en.vyJump = SIM.BOSS_JUMP_SPEED;
        }
        if (en.inAir) {
          const dvj = SIM.GRAVITY * SIM.TICK;
          en.vyJump = en.vyJump - dvj;
          const dyj = en.vyJump * SIM.TICK;
          en.y = en.y + dyj;
          if (en.y <= en.groundY) {
            en.y = en.groundY;
            en.inAir = false;
            en.vyJump = 0.0;
          }
        } else {
          en.y = en.groundY;
        }

        // Boss FIRE. When it fires, snapshot the MOUTH position (the boss's live
        // x at that instant, plus its facing edge) and fly the fireball out from
        // THERE — not from the patrol anchor, which drifts as the boss moves.
        // The fireball flies until it hits a solid wall (same rule as cannon
        // bullets), with a far safety cap so an open-air shot still expires.
        if (en.fireActive) {
          const flived = this.tickCount - en.fireStartTick;
          const travel = flived * SIM.BOSS_FIRE_SPEED * SIM.TICK;
          const signed = en.fireDir * travel;
          en.fireX = en.fireLaunchX + signed;
          en.fireY = en.fireLaunchY;
          if (travel >= SIM.BOSS_FIRE_MAX_DIST) {
            en.fireActive = false;
          } else {
            for (const s of this.solids) {
              if (SimWorld.overlaps(en.fireX, en.fireY, SIM.FIREBALL_HALF, SIM.FIREBALL_HALF, s)) {
                en.fireActive = false;
                break;
              }
            }
          }
        }
        if (!en.fireActive) {
          const fphase = this.tickCount % SIM.BOSS_FIRE_PERIOD_TICKS;
          if (fphase === 0) {
            en.fireActive = true;
            en.fireStartTick = this.tickCount;
            // Always breathe fire TOWARD the player (aim at whichever side they're
            // on), and turn to face them so the mouth lines up with the shot.
            en.fireDir = this.px >= en.x ? 1.0 : -1.0;
            en.facing = en.fireDir >= 0 ? 1 : -1;
            en.fireLaunchX = en.x + en.fireDir * (en.halfW + SIM.FIREBALL_HALF);
            en.fireLaunchY = en.y;
            en.fireX = en.fireLaunchX;
            en.fireY = en.fireLaunchY;
          }
        }
      } else if (en.mode !== 3) {
        en.y = en.baseY;
      }
    }

    // 1d. switch gates: any switch of a group held down pushes its gates
    // toward OPEN; released groups ease shut slowly (grace window). Uses the
    // PREVIOUS tick's player position — the switch overlap is evaluated here.
    for (const sg of this.switchGates) {
      let pressed = false;
      for (const sw of this.switches) {
        if (sw.gateId !== sg.gateId) continue;
        if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, sw)) { pressed = true; break; }
      }
      if (pressed) {
        const add = Math.trunc(SIM.SWITCH_GATE_CLOSE_TICKS / SIM.SWITCH_GATE_OPEN_TICKS);
        sg.openTicks = sg.openTicks + add;
        if (sg.openTicks > SIM.SWITCH_GATE_CLOSE_TICKS) sg.openTicks = SIM.SWITCH_GATE_CLOSE_TICKS;
      } else if (sg.openTicks > 0) {
        sg.openTicks = sg.openTicks - 1;
      }

      // A CLOSING (descending) gate that catches the player against the floor
      // crushes them — same rule as a faller: if the space between the gate's
      // descended bottom and the surface below is smaller than the player's
      // body, they are squished. Otherwise the bottom shoves them down.
      if (this.switchGateSolid(sg) &&
          SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, sg)) {
        const gateBottom = sg.y - sg.halfH;
        const pushedPy = gateBottom - SIM.PLAYER_HALF;
        const floor = this.surfaceTopBelow(gateBottom);
        const room = gateBottom - floor;
        if (room < SIM.PLAYER_HALF + SIM.PLAYER_HALF) {
          this.respawn();
        } else if (pushedPy < this.py) {
          this.py = pushedPy;
          if (this.vy > 0.0) this.vy = 0.0;
        }
      }
    }

    // 2. crumble reset
    for (const c of this.crumbles) {
      if (c.touchedTick < 0) continue;
      const returnAt = c.touchedTick + SIM.CRUMBLE_DELAY_TICKS + SIM.CRUMBLE_RESPAWN_TICKS;
      if (this.tickCount >= returnAt) c.touchedTick = -1;
    }

    // 3. carry by ground mover / conveyor drift
    if (this.groundMover >= 0) {
      const gm = this.movers[this.groundMover];
      this.px = this.px + gm.deltaX;
      this.py = this.py + gm.deltaY;
    }
    if (this.groundConveyor >= 0) {
      const gc = this.conveyors[this.groundConveyor];
      const push = gc.speed * SIM.TICK;
      const pd = push * gc.dir;
      this.px = this.px + pd;
    }

    // 4. input
    if (input.jump) this.jumpPressedTick = this.tickCount;
    let axis = 0.0;
    if (input.right) axis = axis + 1.0;
    if (input.left) axis = axis - 1.0;

    // 5. grounded
    if (this.probeGround()) this.lastGroundedTick = this.tickCount;

    // 6. control: direct while steering; releasing decays air speed
    // quickly (controllable), but ride-inherited velocity persists so
    // moving floors don't leave you behind
    const groundedNow = this.lastGroundedTick === this.tickCount;
    if (groundedNow) this.airCarryVx = 0.0;
    if (this.lockTicks > 0) {
      this.lockTicks = this.lockTicks - 1;
    } else if (axis !== 0.0) {
      const steer = axis * SIM.MOVE_SPEED;
      this.vx = steer + this.airCarryVx;
    } else if (groundedNow) {
      this.vx = 0.0;
    } else {
      const rel = this.vx - this.airCarryVx;
      const damped = rel * SIM.AIR_DAMPING;
      this.vx = this.airCarryVx + damped;
    }

    // 7. jump (coyote + buffer); inherits the velocity of whatever you
    // were riding so platform jumps feel glued
    const sincePress = this.tickCount - this.jumpPressedTick;
    const sinceGround = this.tickCount - this.lastGroundedTick;
    if (sincePress <= SIM.BUFFER_TICKS && sinceGround <= SIM.COYOTE_TICKS) {
      this.vy = SIM.JUMP_SPEED * this.gravityDir;
      // Moving platforms carry you: jumping inherits their velocity so you are
      // not left behind. A conveyor only slides your feet — jumping releases
      // you cleanly with your own steering velocity, not the belt's.
      if (this.groundMover >= 0) {
        const jm = this.movers[this.groundMover];
        const mv = jm.deltaX / SIM.TICK;
        this.airCarryVx = mv;
        this.vx = this.vx + mv;
      }
      // Releasing from a crusher you were riding: cancel the ride so 9c does
      // not re-glue your feet to its top and swallow the jump.
      this.ridingFaller = -1;
      this.jumpPressedTick = -1000000;
      this.lastGroundedTick = -1000000;
      this.events.jumped = true;
    }

    // 8. gravity
    const dv = SIM.GRAVITY * SIM.TICK;
    const dvg = dv * this.gravityDir;
    this.vy = this.vy - dvg;

    // 8b. fans: while the player is inside a wind zone, ease their velocity
    // toward the fan's target velocity along its direction. An upward fan
    // can hold the player aloft against gravity (updraft/hover).
    for (const fan of this.fans) {
      if (!SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, fan)) continue;
      const tvx = fan.dirX * fan.power;
      const tvy = fan.dirY * fan.power;
      if (fan.dirX !== 0.0) {
        const relx = tvx - this.vx;
        const addx = relx * 0.25;
        this.vx = this.vx + addx;
      }
      if (fan.dirY !== 0.0) {
        const rely = tvy - this.vy;
        const addy = rely * 0.25;
        this.vy = this.vy + addy;
      }
    }

    // 9. integrate + collide, axis separated
    const mx = this.vx * SIM.TICK;
    this.px = this.px + mx;
    this.resolveAxis(true);
    const my = this.vy * SIM.TICK;
    this.py = this.py + my;
    this.resolveAxis(false);

    // 9b. anti-wedge safety net: if the player is still overlapping a solid
    // after resolution (squeezed between two blocks), lift them out (against
    // gravity) until clear so they can never get permanently stuck.
    const lift = 0.12 * -this.gravityDir;
    for (let guard = 0; guard < 16; guard++) {
      if (!this.overlapsAnySolid()) break;
      this.py = this.py + lift;
      this.vy = 0.0;
    }

    // 9c. faller ride: if the player was standing on a crusher at the start of
    // the tick, glue their feet to its (post-move) top now — AFTER their own
    // gravity/collision — so even a fast slam can't drop out from under them.
    if (this.ridingFaller >= 0) {
      this.stickToFallerTop(this.fallers[this.ridingFaller]);
    }

    // 10. triggers (edge-based, stage order within kind order)
    let respawned = false;
    for (const tr of this.triggers) {
      const overlap = SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, tr);
      const fire = overlap && !tr.wasOverlapping;
      tr.wasOverlapping = overlap;
      if (!fire) continue;

      if (tr.kind === 'hazard') {
        this.respawn();
        respawned = true;
        break;
      }
      if (tr.kind === 'pad') {
        let p = tr.power;
        if (p <= 0.0) p = 22.0;
        // Snap the player's feet to the pad surface first, so the bounce
        // launches from true contact instead of mid-overlap. Under normal
        // gravity that's the pad's top; when flipped, its bottom.
        if (this.gravityDir > 0.0) {
          const padTop = tr.y + tr.halfH;
          this.py = padTop + SIM.PLAYER_HALF;
        } else {
          const padBot = tr.y - tr.halfH;
          this.py = padBot - SIM.PLAYER_HALF;
        }
        this.vy = p * this.gravityDir;
        this.events.bounced = true;
      } else if (tr.kind === 'boost') {
        let p = tr.power;
        if (p <= 0.0) p = 10.0;
        let d = 1.0;
        if (tr.dirX < 0.0) d = -1.0;
        this.vx = d * p;
        this.vy = SIM.BOOST_KICK * this.gravityDir;
        this.lockTicks = SIM.BOOST_LOCK_TICKS;
        this.events.boosted = true;
      } else if (tr.kind === 'launcher') {
        // Flings the player straight up hard; they sail off the top
        // of the world and respawn. A deadly floating trap.
        let p = tr.power;
        if (p <= 0.0) p = 40.0;
        this.vx = 0.0;
        this.vy = p * this.gravityDir;
        this.lockTicks = SIM.BOOST_LOCK_TICKS * 3;
        this.events.boosted = true;
      } else if (tr.kind === 'flip') {
        const since = this.tickCount - tr.lastFlipTick;
        if (since >= SIM.FLIP_COOLDOWN_TICKS) {
          this.gravityDir = -this.gravityDir;
          tr.lastFlipTick = this.tickCount;
          this.events.flipped = true;
        }
      } else if (tr.kind === 'gravset') {
        // Sets gravity to this block's FIXED direction (dir<0 = up, else
        // down). Idempotent: touching it again does nothing, so the arrow it
        // shows always matches the resulting gravity.
        const want = tr.dirX < 0.0 ? -1.0 : 1.0;
        if (this.gravityDir !== want) {
          this.gravityDir = want;
          this.events.flipped = true;
        }
      } else if (tr.kind === 'checkpoint') {
        // Reaching a checkpoint moves the respawn point here. Snap the saved
        // spot to the checkpoint's top so a death drops you onto it, not inside
        // it. Only ever moves forward (edge-fire once).
        const cpTop = tr.y + tr.halfH;
        this.startX = tr.x;
        this.startY = cpTop + SIM.PLAYER_HALF;
        this.events.key = true; // reuse the pickup chime
      } else if (tr.kind === 'goal') {
        this.clearedFlag = true;
        this.events.cleared = true;
        return this.events;
      }
    }

    // 10b. keys unlock the doors once all are collected
    if (!respawned) {
      for (const k of this.keys) {
        if (k.collected) continue;
        if (!SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, k)) continue;
        k.collected = true;
        this.keysCollected = this.keysCollected + 1;
        this.events.key = true;
        if (this.keysCollected >= this.keys.length) this.events.door = true;
      }
    }

    // 10c. cannons: each fires one bullet per period; the bullet flies
    // straight until it hits a solid or leaves the world. Position is
    // derived from ticks-since-fire (no per-bullet objects).
    if (!respawned) {
      for (const c of this.cannons) {
        const perTick = c.speed * SIM.TICK;
        const halfDir = c.dir * c.halfW;
        const muzzle = c.x + halfDir;
        const by = c.y;

        // A bullet must fly the WHOLE way to its wall before the next fires,
        // so the effective fire cadence is at least the flight time.
        const ticksToWall = Math.trunc(c.wallDist / perTick) + 1;
        let cycle = c.periodTicks;
        if (ticksToWall > cycle) cycle = ticksToWall;

        const phase = this.tickCount + c.phaseTicks;
        const intoCycle = phase % cycle;
        const traveled = intoCycle * perTick;

        const reachedWall = traveled >= c.wallDist;
        const signedTravel = c.dir * traveled;
        const bx = muzzle + signedTravel;
        c.bulletActive = !reachedWall;
        c.bulletX = bx;
        c.bulletY = by;

        if (c.bulletActive &&
            SimWorld.overlaps(bx, by, SIM.BULLET_HALF, SIM.BULLET_HALF,
              { x: this.px, y: this.py, halfW: SIM.PLAYER_HALF, halfH: SIM.PLAYER_HALF })) {
          this.respawn();
          respawned = true;
          break;
        }
      }
    }

    // 10d. rotating hazards: die on contact with the orbiting spike head.
    if (!respawned) {
      for (const r of this.rotors) {
        const head = { x: r.headX, y: r.headY, halfW: r.headHalf, halfH: r.headHalf };
        if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, head)) {
          this.respawn();
          respawned = true;
          break;
        }
      }
    }

    // 10d2. waves: touching the sweeping wall kills you. Its live box is at
    // (curX,curY) with the wave's half-extents.
    if (!respawned) {
      for (const wv of this.waves) {
        const box = { x: wv.curX, y: wv.curY, halfW: wv.halfW, halfH: wv.halfH };
        if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, box)) {
          this.respawn();
          respawned = true;
          break;
        }
      }
    }

    // 10e. teleporters: entering EITHER endpoint warps you to its partner,
    // preserving velocity. Two-way — you can go back the way you came.
    // Edge-based, and the destination is marked overlapping so you don't
    // instantly bounce back.
    if (!respawned) {
      for (const tp of this.teleporters) {
        const overlap = SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, tp);
        const fire = overlap && !tp.wasOverlapping;
        tp.wasOverlapping = overlap;
        if (!fire) continue;
        if (tp.exitIndex < 0) continue;
        const exit = this.teleporters[tp.exitIndex];
        this.px = exit.x;
        this.py = exit.y;
        exit.wasOverlapping = true;
        this.events.flipped = true; // reuse the warp/whoosh chime
        break;
      }
    }

    // 10f. enemies: STOMP from above (player descending, feet over the enemy's
    // upper half) damages the enemy and bounces the player; any other contact
    // kills the PLAYER. Defeated bosses may open the boss doors.
    if (!respawned) {
      for (const en of this.enemies) {
        if (en.dead) continue;
        if (!SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, en)) continue;
        // Just bounced off this enemy? The player is still overlapping it for a
        // tick or two while rising away — don't read that as a side-hit kill.
        const sinceHit = this.tickCount - en.hitTick;
        if (sinceHit >= 0 && sinceHit < SIM.ENEMY_STOMP_GRACE_TICKS) continue;
        const feet = this.py - SIM.PLAYER_HALF * this.gravityDir;
        const descending = this.vy * this.gravityDir;
        const overTop = (feet - en.y) * this.gravityDir;
        // Favour the STOMP two ways: (1) the player is falling onto the enemy's
        // upper half (a normal stomp), OR (2) the player's feet are clearly ABOVE
        // the enemy's centre — they're on top of it, no matter their own velocity.
        // Case (2) covers a boss that JUMPS UP into a player standing/landing on
        // its head: contact from above is always a stomp, never a cheap hit.
        const stompFalling = descending < 0.0 && overTop > -SIM.ENEMY_STOMP_MARGIN;
        const onTop = overTop > SIM.ENEMY_STOMP_MARGIN;
        const stomp = stompFalling || onTop;
        // A jumping enemy that is RISING up and away, overhead of the player, is
        // leaving — don't let its underside deal a cheap graze. But if the player
        // is genuinely UNDER a boss (it's descending onto them, or they ran into
        // its underside), that MUST still hit. So only pass through when the enemy
        // is BOTH mostly overhead AND currently moving up (its jump is ascending).
        const enemyAbovePlayer = (en.y - this.py) * this.gravityDir > 0.0;
        const enemyRising = en.inAir && (en.vyJump * this.gravityDir) > 0.0;
        const passOverhead = enemyAbovePlayer && enemyRising;
        if (stomp) {
          en.hp = en.hp - 1;
          en.hitTick = this.tickCount;
          // Mario-style: holding/pressing JUMP as you land the stomp gives a much
          // higher bounce; a plain stomp gives the small pop.
          const sincePress = this.tickCount - this.jumpPressedTick;
          const jumpHeld = sincePress <= SIM.BUFFER_TICKS;
          const bounce = jumpHeld ? SIM.ENEMY_STOMP_JUMP_BOUNCE : SIM.ENEMY_STOMP_BOUNCE;
          this.vy = bounce * this.gravityDir;
          if (jumpHeld) {
            this.jumpPressedTick = -1000000;
            this.events.jumped = true;
          }
          this.events.stomped = true;
          if (en.hp <= 0) {
            en.dead = true;
            this.events.enemyDown = true;
          }
        } else if (passOverhead) {
          // enemy is jumping up and away overhead — a graze, not a hit
        } else {
          this.respawn();
          respawned = true;
          break;
        }
      }
    }

    // 10g. boss fireballs: touching a live fireball kills the player.
    if (!respawned) {
      for (const en of this.enemies) {
        if (en.dead || !en.isBoss || !en.fireActive) continue;
        const fb = { x: en.fireX, y: en.fireY, halfW: SIM.FIREBALL_HALF, halfH: SIM.FIREBALL_HALF };
        if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, fb)) {
          this.respawn();
          respawned = true;
          break;
        }
      }
    }

    // 11. kill bounds (derived from stage geometry — tall stages welcome)
    if (!respawned) {
      if (this.py < this.killBottom || this.py > this.killTop) this.respawn();
    }

    // 12. ground mover / conveyor for next tick's carry
    this.groundMover = -1;
    const gShift = SIM.GROUND_PROBE * this.gravityDir;
    const gProbeY = this.py - gShift;
    for (let i = 0; i < this.movers.length; i++) {
      if (SimWorld.overlaps(this.px, gProbeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, this.movers[i])) {
        this.groundMover = i;
        break;
      }
    }
    this.groundConveyor = -1;
    for (let i = 0; i < this.conveyors.length; i++) {
      if (SimWorld.overlaps(this.px, gProbeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, this.conveyors[i])) {
        this.groundConveyor = i;
        break;
      }
    }

    // 13. time limit
    if (this.tickCount >= this.maxTicks) {
      this.timedOutFlag = true;
      this.events.timedOut = true;
    }
    return this.events;
  }

  static fromStage(data) {
    const world = new SimWorld(data.playerStart.x, data.playerStart.y, data.timeLimit);
    // Lives are FIXED at 5 for every stage — a deliberate whole-game rule, so a
    // stage's own "lives" field (whatever a creator set, old or new) is ignored.
    // Keeps difficulty consistent across the catalog.
    for (const p of data.parts) {
      switch (p.type) {
        case 'solid': world.addSolid(p.x, p.y, p.w, p.h); break;
        case 'movingPlatform': world.addMover(p.x, p.y, p.w, p.h, p.dx || 0, p.dy || 0, p.period || 0); break;
        case 'crumble': world.addCrumble(p.x, p.y, p.w, p.h); break;
        case 'faller': world.addFaller(p.x, p.y, p.w, p.h, p.dy || 0); break;
        case 'conveyor': world.addConveyor(p.x, p.y, p.w, p.h, p.dirX || 0, p.power || 0); break;
        case 'timedGate': world.addGate(p.x, p.y, p.w, p.h, p.period || 0, p.dx || 0); break;
        case 'key': world.addKey(p.x, p.y, p.w, p.h); break;
        case 'door': world.addDoor(p.x, p.y, p.w, p.h); break;
        case 'cannon': world.addCannon(p.x, p.y, p.w, p.h, p.dirX || 0, p.power || 0, p.period || 0, p.dx || 0); break;
        case 'hazard': world.addTrigger('hazard', p.x, p.y, p.w, p.h, 0, 0); break;
        case 'jumpPad': world.addTrigger('pad', p.x, p.y, p.w, p.h, p.power || 0, 0); break;
        case 'boost': world.addTrigger('boost', p.x, p.y, p.w, p.h, p.power || 0, p.dirX || 0); break;
        case 'launcher': world.addTrigger('launcher', p.x, p.y, p.w, p.h, p.power || 0, 0); break;
        case 'gravityFlip': world.addTrigger('flip', p.x, p.y, p.w, p.h, 0, 0); break;
        case 'gravitySet': world.addTrigger('gravset', p.x, p.y, p.w, p.h, 0, p.dirX || 0); break;
        case 'checkpoint': world.addTrigger('checkpoint', p.x, p.y, p.w, p.h, 0, 0); break;
        case 'rotatingHazard': world.addRotor(p.x, p.y, p.w, p.h, p.power || 0, p.dirX || 0, p.dx || 0); break;
        case 'wave': world.addWave(p.x, p.y, p.w, p.h, p.power || 0, p.dirX || 0, p.dy || 0, p.period || 0); break;
        case 'teleporter': world.addTeleporter(p.x, p.y, p.w, p.h, p.dirX || 0, p.period || 0); break;
        case 'fan': world.addFan(p.x, p.y, p.w, p.h, p.dirX || 0, p.power || 0, p.dy || 0); break;
        case 'switch': world.addSwitch(p.x, p.y, p.w, p.h, p.period || 0); break;
        case 'switchGate': world.addSwitchGate(p.x, p.y, p.w, p.h, p.period || 0); break;
        case 'enemy': world.addEnemy(p.x, p.y, p.w, p.h, p.dx || 0, p.period || 0, p.power || 0, p.dy || 0, p.dirX || 0); break;
        case 'bossDoor': world.addBossDoor(p.x, p.y, p.w, p.h); break;
      }
    }
    world.addTrigger('goal', data.goal.x, data.goal.y, data.goal.w, data.goal.h, 0, 0);
    world.resolveFallerLandings();
    world.resolveCannonRanges();
    world.resolveTeleporterPairs();
    world.computeKillBounds();
    return world;
  }

  computeKillBounds() {
    let minY = this.startY;
    let maxY = this.startY;
    const consider = (b) => {
      const lo = b.y - b.halfH;
      const hi = b.y + b.halfH;
      if (lo < minY) minY = lo;
      if (hi > maxY) maxY = hi;
    };
    for (const s of this.solids) consider(s);
    for (const m of this.movers) consider(m);
    for (const c of this.crumbles) consider(c);
    for (const t of this.triggers) consider(t);
    for (const f of this.fallers) consider(f);
    for (const cv of this.conveyors) consider(cv);
    for (const g of this.gates) consider(g);
    for (const k of this.keys) consider(k);
    for (const d of this.doors) consider(d);
    for (const c of this.cannons) consider(c);
    for (const r of this.rotors) consider(r);
    for (const tp of this.teleporters) consider(tp);
    for (const fn of this.fans) consider(fn);
    for (const sw of this.switches) consider(sw);
    for (const sg of this.switchGates) consider(sg);
    for (const en of this.enemies) consider(en);
    for (const bd of this.bossDoors) consider(bd);
    this.killBottom = minY - SIM.KILL_MARGIN_BELOW;
    this.killTop = maxY + SIM.KILL_MARGIN_ABOVE;
  }

  // Pair each teleporter entry with the FIRST exit sharing its pairId (and
  // vice-versa for a two-way pair). Unpaired endpoints simply never fire.
  resolveTeleporterPairs() {
    for (let i = 0; i < this.teleporters.length; i++) {
      const a = this.teleporters[i];
      a.exitIndex = -1;
      for (let j = 0; j < this.teleporters.length; j++) {
        if (j === i) continue;
        const b = this.teleporters[j];
        if (b.pairId !== a.pairId) continue; // partner = the other endpoint with the same pair id
        a.exitIndex = j;
        break;
      }
    }
  }
}

/// Runs an RLE-encoded replay ([code,count,...]) against a stage.
/// Returns { cleared, ticks } — ticks is the tick at which the goal was hit.
///
/// Hardened against CPU-exhaustion: the total simulated tick count is bounded
/// up front (a replay can never run longer than the stage's own time limit),
/// every element is validated, and empty (count===0) padding is rejected — so
/// the amount of work is provably capped no matter what an attacker sends.
function simRunReplay(stageData, rle, hardTickCap) {
  if (rle.length % 2 !== 0) {
    return { cleared: false, ticks: 0, error: 'Malformed replay.' };
  }
  // Pre-flight: validate every element and bound the total work before we
  // build a world or run a single step.
  let totalTicks = 0;
  for (let i = 0; i < rle.length; i += 2) {
    const code = rle[i];
    const count = rle[i + 1];
    if (!Number.isInteger(code) || code < 0 || code > 7) {
      return { cleared: false, ticks: 0, error: 'Malformed replay.' };
    }
    if (!Number.isInteger(count) || count < 1) {
      return { cleared: false, ticks: 0, error: 'Malformed replay.' };
    }
    totalTicks += count;
    if (totalTicks > hardTickCap) {
      return { cleared: false, ticks: 0, error: 'Replay exceeds time limit.' };
    }
  }

  const world = SimWorld.fromStage(stageData);
  for (let i = 0; i < rle.length; i += 2) {
    const code = rle[i];
    const count = rle[i + 1];
    const input = {
      left: (code & 1) !== 0,
      right: (code & 2) !== 0,
      jump: (code & 4) !== 0,
    };
    for (let n = 0; n < count; n++) {
      // jump is an edge: only the first tick of a run-length carries the press
      const stepInput = n === 0 ? input : { left: input.left, right: input.right, jump: false };
      const ev = world.step(stepInput);
      if (ev.cleared) return { cleared: true, ticks: world.tickCount };
      if (ev.timedOut) return { cleared: false, ticks: world.tickCount, error: 'Timed out.' };
    }
  }
  return { cleared: false, ticks: world.tickCount, error: 'Replay ended before reaching the goal.' };
}
