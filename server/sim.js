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
  GRAVITY: 29.43,
  MOVE_SPEED: 8.0,
  JUMP_SPEED: 14.0,
  PLAYER_HALF: 0.5,
  BULLET_HALF: 0.22,
  BOOST_KICK: 4.0,
  COYOTE_TICKS: 5,
  BUFFER_TICKS: 6,
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
  AIR_DAMPING: 0.86,
};

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
    this.airCarryVx = 0.0;
    this.tickCount = 0;
    this.clearedFlag = false;
    this.timedOutFlag = false;

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
    });
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
      dir, speed,
      periodTicks: Math.trunc(pt), phaseTicks: Math.trunc(pht),
      bulletActive: false, bulletX: 0.0, bulletY: 0.0,
    };
    if (cannon.periodTicks < 2) cannon.periodTicks = 2;
    this.cannons.push(cannon);
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
    for (const f of this.fallers) {
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, f)) return true;
    }
    for (const cn of this.cannons) {
      if (SimWorld.overlaps(this.px, probeY, SIM.PLAYER_HALF, SIM.PLAYER_HALF, cn)) return true;
    }
    return false;
  }

  resolveAxis(xAxis) {
    for (const s of this.solids) this.resolveAgainst(s, xAxis, null);
    for (const m of this.movers) this.resolveAgainst(m, xAxis, null);
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
    for (const f of this.fallers) this.resolveAgainst(f, xAxis, null);
    for (const cn of this.cannons) this.resolveAgainst(cn, xAxis, null);
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
    for (const cn of this.cannons) {
      if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, cn)) return true;
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
      if (this.py < b.y) {
        this.py = b.y - lim;
      } else {
        this.py = b.y + lim;
      }
      this.vy = 0.0;
    }
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
    this.events.respawned = true;
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
    for (const f of this.fallers) {
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
        if (SimWorld.overlaps(this.px, this.py, SIM.PLAYER_HALF, SIM.PLAYER_HALF, f)) {
          this.respawn();
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
      this.jumpPressedTick = -1000000;
      this.lastGroundedTick = -1000000;
      this.events.jumped = true;
    }

    // 8. gravity
    const dv = SIM.GRAVITY * SIM.TICK;
    const dvg = dv * this.gravityDir;
    this.vy = this.vy - dvg;

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
        const phase = this.tickCount + c.phaseTicks;
        const intoCycle = phase % c.periodTicks;
        const perTick = c.speed * SIM.TICK;
        const traveled = intoCycle * perTick;
        const signedTravel = c.dir * traveled;
        const halfDir = c.dir * c.halfW;
        const muzzle = c.x + halfDir;
        const bx = muzzle + signedTravel;
        const by = c.y;

        let blocked = false;
        for (const s of this.solids) {
          if (SimWorld.overlaps(bx, by, SIM.BULLET_HALF, SIM.BULLET_HALF, s)) { blocked = true; break; }
        }
        const offWorld = bx < -600.0 || bx > 600.0;
        c.bulletActive = !blocked && !offWorld;
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
      }
    }
    world.addTrigger('goal', data.goal.x, data.goal.y, data.goal.w, data.goal.h, 0, 0);
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
    this.killBottom = minY - SIM.KILL_MARGIN_BELOW;
    this.killTop = maxY + SIM.KILL_MARGIN_ABOVE;
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
