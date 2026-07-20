// TAC sim headless test. Run from repo root:
//   cat server/tacsim.js scripts/tac-test.js | node -
// Verifies: basic mechanics, determinism (two runs bit-identical), replay codec
// round-trip, and tacRunReplay acceptance of a scripted clear.

'use strict';

let failures = 0;
function check(name, cond) {
  if (cond) { console.log('  ok  ' + name); }
  else { failures++; console.log('FAIL  ' + name); }
}

function hex(v) {
  const buf = new ArrayBuffer(8);
  new DataView(buf).setFloat64(0, v);
  return new DataView(buf).getBigUint64(0).toString(16).padStart(16, '0');
}

const STAGE = {
  schemaVersion: '0.3', game: 'tac', name: 'test arena',
  timeLimit: 120, lives: 3, ammo: 0,
  arena: { w: 40, d: 60 },
  playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [
    { type: 'rock', x: 20, z: 15, w: 3, d: 1.5, h: 1.4 },
    { type: 'wall', x: 10, z: 25, w: 8, d: 1, h: 3 },
    { type: 'platform', x: 32, z: 40, w: 8, d: 8, h: 2 },
    { type: 'slope', x: 32, z: 33, w: 4, d: 6, h: 2, dir: 0 },
    { type: 'barrel', x: 25, z: 30 },
    { type: 'barrel', x: 26.2, z: 30 }
  ],
  enemies: [
    { type: 'soldier', x: 20, z: 35, yaw: 180, patrolX: 15, patrolZ: 35, group: 1 },
    { type: 'gatling', x: 5, z: 50, yaw: 180 },
    { type: 'sniper', x: 32, z: 41, yaw: 180, group: 1 }
  ]
};

const IDLE = { b: 0, m: 255, yawQ: 0, pitchQ: 0 };

// --- trig sanity ---
check('sin(0)=0', tacSinQ(0) === 0);
check('sin(quarter)=~1', Math.abs(tacSinQ(16384) - 1.0) < 1e-5);
check('cos(quarter)=~0', Math.abs(tacCosQ(16384)) < 1e-5);
check('sin(-q) = -sin(q)', tacSinQ(-8192) === -tacSinQ(8192));

// --- world construction ---
const w = new TacWorld(STAGE);
check('3 enemies alive', w.enemiesLeft === 3);
check('player at start', Math.abs(w.px - 20) < 1e-9 && Math.abs(w.pz - 5) < 1e-9);
check('player on floor', w.py === 0);
check('infinite ammo', w.ammo === -1);
check('lives 3', w.hp === 3);

// --- movement: run forward (north, +z) for 2s ---
for (let t = 0; t < 100; t++) w.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 });
check('ran ~9.2m north', w.pz > 13.0 && w.pz < 14.5 && Math.abs(w.px - 20) < 0.01);

// rock at z=15 (z0=14.25) should block at pz = 14.25 - 0.4 = 13.85
for (let t = 0; t < 50; t++) w.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 });
check('blocked by rock', w.pz <= 13.851 && w.pz > 13.7);

// --- jump ---
const wj = new TacWorld(STAGE);
wj.step({ b: 1, m: 255, yawQ: 0, pitchQ: 0 });
let apex = 0;
for (let t = 0; t < 60; t++) { wj.step(IDLE); if (wj.py > apex) apex = wj.py; }
check('jump apex ~1.2m', apex > 1.0 && apex < 1.4);
check('landed', wj.py === 0 && wj.onGround);

// --- soldier mechanics on a minimal duel stage ---
// stationary soldier at (20,14) FACING the player (yaw 180 = south).
const DUEL_FRONT = {
  name: 'duel-front', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [
    { type: 'soldier', x: 20, z: 14, yaw: 180, group: 1 },
    { type: 'sniper', x: 38, z: 38, yaw: 180, group: 1 }
  ]
};
const ws = new TacWorld(DUEL_FRONT);
let rifleShots = 0;
for (let t = 0; t < 400; t++) {
  const ev = ws.step(IDLE);
  if (ev.rifleShot) rifleShots++;
}
check('soldier spots the player and opens fire', rifleShots > 0);
check('rifle bullets hit the player in the open', ws.hp < 5);
check('group link alerted the sniper too', ws.enemies[1].state === 2);

// soldier dies to frontal fire (no shield mechanic)
const wsf = new TacWorld(DUEL_FRONT);
for (let t = 0; t < 200 && wsf.enemies[0].alive; t++) wsf.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
check('soldier takes damage from the front and dies', !wsf.enemies[0].alive);

// soldier facing away is a clean stealth kill -> single-enemy stage clears
const DUEL_BACK = {
  name: 'duel-back', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [{ type: 'soldier', x: 20, z: 12, yaw: 0 }]
};
const wk = new TacWorld(DUEL_BACK);
const killRecs = [];
let clearedDuel = false;
for (let t = 0; t < 400 && !clearedDuel; t++) {
  const rec = { b: 2, m: 255, yawQ: 0, pitchQ: 0 };
  killRecs.push(rec);
  const ev = wk.step(rec);
  if (ev.cleared) clearedDuel = true;
}
check('soldier dies to shots from behind (stage cleared)', clearedDuel === true);
check('unaware soldier never fired back', wk.hp === 5);

// replay certificate round trip through the verifier
const duelReplay = { v: 't1', ticks: wk.tick, data: tacEncodeTrace(killRecs) };
const duelVerdict = tacRunReplay(DUEL_BACK, duelReplay, 100000);
check('tacRunReplay verifies the clear', duelVerdict.cleared === true);
check('verified tick count matches', duelVerdict.ticks === wk.tick);
const truncated = { v: 't1', ticks: 10, data: tacEncodeTrace(killRecs.slice(0, 10)) };
check('truncated replay does not clear', tacRunReplay(DUEL_BACK, truncated, 100000).cleared === false);
check('garbage replay rejected', tacRunReplay(DUEL_BACK, { v: 't1', ticks: 5, data: '!!!' }, 100000).cleared === false);

// --- barrel: shoot it; it rolls away north and chains into the second barrel on its path ---
// one far-off enemy well OUTSIDE the aim cone (behind the player) keeps the
// stage from auto-clearing; with enemy-priority auto-lock the barrel ahead is
// still the target because no hostile sits in the forward cone
const CHAIN_STAGE = { ...STAGE, enemies: [{ type: 'soldier', x: 25, z: 2, yaw: 0, hp: 10 }], parts: STAGE.parts.slice(0, 4).concat([{ type: 'barrel', x: 25, z: 30 }, { type: 'barrel', x: 25, z: 40 }]) };
const wb = new TacWorld(CHAIN_STAGE);
wb.px = 25.0; wb.pz = 20.0; // (direct state poke is fine outside replay tests)
let boomTick = -1, boomCount = 0, hitTick = -1;
for (let t = 0; t < 400; t++) {
  const ev = wb.step({ b: t === 5 ? 2 : 0, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.barrelHit && hitTick < 0) hitTick = wb.tick;
  if (ev.explosions) { if (boomTick < 0) boomTick = wb.tick; boomCount += ev.explosions.length; }
}
check('barrel was hit', hitTick > 0);
check('explosion ~150 ticks after hit', boomTick >= hitTick + 140 && boomTick <= hitTick + 160);
check('chain reaction: 2 booms', boomCount === 2);

// --- auto-lock priority: enemy first, then nearest ---
const LOCK_STAGE = { name: 'lock', timeLimit: 120, lives: 5, ammo: 0, arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'barrel', x: 20, z: 12 }],
  enemies: [{ type: 'soldier', x: 20, z: 22, yaw: 180, hp: 10 }, { type: 'soldier', x: 20, z: 34, yaw: 180, hp: 10 }] };
const wlk = new TacWorld(LOCK_STAGE);
wlk.px = 20; wlk.pz = 8;
wlk.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 });
// a barrel sits closer (z12) than either enemy, but an enemy in the cone wins
check('enemy beats a nearer barrel', wlk.lockKind === 0);
check('and it is the NEAREST enemy', wlk.lockTarget === 0);
// once both enemies are gone, the barrel (only remaining candidate) locks
wlk.enemies[0].alive = false; wlk.enemies[0].hp = 0;
wlk.enemies[1].alive = false; wlk.enemies[1].hp = 0;
wlk.enemiesLeft = 1; // keep sim alive for the check
wlk.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 });
check('barrel locks when no enemy in cone', wlk.lockKind === 1);
// two enemies both in the cone: the closer one is chosen
const NEAR_STAGE = { name: 'near', timeLimit: 120, lives: 5, ammo: 0, arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [], enemies: [{ type: 'soldier', x: 20, z: 30, yaw: 180, hp: 10 }, { type: 'soldier', x: 20, z: 16, yaw: 180, hp: 10 }] };
const wnr = new TacWorld(NEAR_STAGE);
wnr.px = 20; wnr.pz = 8;
wnr.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 });
check('nearest of two cone enemies is locked', wnr.lockKind === 0 && wnr.lockTarget === 1);

// --- freeform blocks: floating cuboids you can walk under, stand on, bump into ---
const BLK_STAGE = { name: 'blk', timeLimit: 300, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 5, z: 20, yaw: 0 },
  parts: [
    { type: 'block', x: 12, z: 20, w: 3, d: 6, h: 2.5 },                 // grounded pillar
    { type: 'block', x: 20, z: 20, w: 4, d: 8, h: 0.6, y0: 2.4 },       // bridge overhead
    { type: 'block', x: 27, z: 20, w: 3, d: 3, h: 0.35 },               // curb-height step
    { type: 'pit', x: 33, z: 20, w: 4, d: 4, depth: 2.0 },
  ],
  enemies: [{ type: 'soldier', x: 5, z: 38, yaw: 0, hp: 10 }] };
const EAST = { b: 0, m: 32, yawQ: 0, pitchQ: 0 };
const wbk = new TacWorld(BLK_STAGE);
for (let t = 0; t < 100; t++) wbk.step(EAST);
check('grounded block stops the player', wbk.px < 10.7 && wbk.py === 0);
const wbk2 = new TacWorld(BLK_STAGE);
wbk2.px = 15; // past the pillar
let underOk = false, steppedUp = false, fellIn = false;
for (let t = 0; t < 500; t++) {
  wbk2.step(EAST);
  if (wbk2.px > 18 && wbk2.px < 22 && wbk2.py === 0) underOk = true;    // walked UNDER the bridge
  if (wbk2.px > 25.6 && wbk2.px < 28.4 && wbk2.py > 0.3) steppedUp = true; // stood ON the curb
  if (wbk2.px > 31 && wbk2.py < -1.0) { fellIn = true; break; }         // dropped into the pit
}
check('player walks under a floating bridge', underOk);
check('player steps up onto a low block', steppedUp);
check('generic pit is a real hole', fellIn);
// jump under the bridge: head stops at its underside
const wbk3 = new TacWorld(BLK_STAGE);
wbk3.px = 20; wbk3.pz = 20;
let blkApex = 0;
for (let t = 0; t < 60; t++) {
  wbk3.step(t === 2 ? { b: 1, m: 255, yawQ: 0, pitchQ: 0 } : IDLE);
  if (wbk3.py > blkApex) blkApex = wbk3.py;
}
check('jump head-bumps the bridge underside', blkApex > 0.1 && blkApex <= 2.4 - 1.7 + 0.001);
// bullets: blocked by the bridge span, free underneath it
const wbk4 = new TacWorld(BLK_STAGE);
check('shot under the bridge flies clear', !wbk4.segBlocked(16, 1.2, 20, 24, 1.2, 20));
check('shot through the bridge span is blocked', wbk4.segBlocked(16, 2.7, 20, 24, 2.7, 20));

// --- decks over water are dry land: no wading slowdown up there ---
const DECK_STAGE = { name: 'deck', timeLimit: 300, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 6, z: 20, yaw: 0 },
  parts: [{ type: 'river', x: 24, z: 20, w: 20, d: 12 }, { type: 'block', x: 24, z: 20, w: 20, d: 4, h: 2.0 }],
  enemies: [{ type: 'soldier', x: 5, z: 38, yaw: 0, hp: 10 }] };
const wdk = new TacWorld(DECK_STAGE);
wdk.px = 20; wdk.pz = 20; wdk.py = 2.0; wdk.onGround = true; // on the deck over the river
const dk0 = wdk.px;
for (let t = 0; t < 50; t++) wdk.step({ b: 0, m: 32, yawQ: 0, pitchQ: 0 });
const deckDist = wdk.px - dk0;
const wdk2 = new TacWorld(DECK_STAGE);
wdk2.px = 20; wdk2.pz = 26; wdk2.py = -0.6; wdk2.onGround = true; // wading in the channel beside the deck
const wd0 = wdk2.px;
for (let t = 0; t < 50; t++) wdk2.step({ b: 0, m: 32, yawQ: 0, pitchQ: 0 });
check('deck over water walks at full speed; wading is slower', deckDist > (wdk2.px - wd0) * 1.5 && deckDist > 3.5);

// --- firing from inside a pit: bullets live below grade ---
const PITF_STAGE = { name: 'pitf', timeLimit: 300, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 10, z: 20, yaw: 0 },
  parts: [{ type: 'pit', x: 20, z: 20, w: 16, d: 10, depth: 2.5 }],
  enemies: [{ type: 'soldier', x: 5, z: 38, yaw: 0, hp: 10 }] };
const wpf = new TacWorld(PITF_STAGE);
wpf.px = 20; wpf.pz = 20; wpf.py = -2.5; wpf.onGround = true; // standing on the moat floor
wpf.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
let pitShot = null;
for (let i = 0; i < wpf.bullets.length; i++) if (wpf.bullets[i].fromPlayer) pitShot = wpf.bullets[i];
wpf.step(IDLE); // 1 tick: faster bullet, still inside the pit span (z<25)
check('a shot fired inside the moat flies (does not die at y=0)', pitShot !== null && pitShot.alive && pitShot.y < 0 && pitShot.z < 25);
for (let t = 0; t < 6; t++) wpf.step(IDLE);
check('the below-grade shot stops at the moat wall', !pitShot.alive);

// --- shield bearers: a synced wall that opens on the drill and staggers on blasts ---
const SH_STAGE = { name: 'sh', timeLimit: 300, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 6, yaw: 0 },
  enemies: [
    { type: 'shield', x: 20, z: 16, yaw: 180 },
    { type: 'gatling', x: 20, z: 20, yaw: 180, hp: 6 },
    { type: 'soldier', x: 5, z: 58, yaw: 0, hp: 10 }] };
const FIRE1 = { b: 2, m: 255, yawQ: 0, pitchQ: 0 };
// CLOSED phase: bullets die on the wall — bearer AND the gatling behind it are safe
const wsh = new TacWorld(SH_STAGE);
let blocked = false;
for (let t = 0; t < 60; t++) {
  const ev = wsh.step(t % 20 === 5 ? FIRE1 : IDLE);
  if (ev.shieldBlock) blocked = true;
}
check('closed shield wall eats player bullets', blocked && wsh.enemies[0].hp === 4 && wsh.enemies[1].hp === 6);
// OPEN phase: the drill window exposes them
const wsh2 = new TacWorld(SH_STAGE);
for (let t = 0; t < 210; t++) wsh2.step(IDLE); // tick 210 is inside the OPEN window (200-299)
let openHit = false;
for (let t = 0; t < 60 && !openHit; t++) {
  wsh2.step(t % 20 === 5 ? FIRE1 : IDLE);
  if (wsh2.enemies[0].hp < 4 || wsh2.enemies[1].hp < 6) openHit = true;
}
check('open drill window lets rounds through', openHit);
// blast stagger: a grenade forces the wall open outside the drill
const wsh3 = new TacWorld(SH_STAGE);
wsh3.explodeAt(20, 1, 15, 3.0, 0);
check('a nearby blast staggers the bearer (soaked damage, wall down)',
  wsh3.enemies[0].shieldStagT > 0 && wsh3.enemies[0].hp === 2 && wsh3.shieldUp(wsh3.enemies[0]) === false);
let stagHit = false;
for (let t = 0; t < 60 && !stagHit; t++) {
  wsh3.step(t % 20 === 5 ? FIRE1 : IDLE);
  if (wsh3.enemies[0].hp < 2) stagHit = true;
}
check('staggered wall can be shot even in the closed phase', stagHit);

// --- sniper scanning: idle snipers sweep their scope instead of staring ---
const SCAN_STAGE = { name: 'scan', timeLimit: 300, lives: 5, ammo: 0,
  arena: { w: 60, d: 80 }, playerStart: { x: 5, z: 3, yaw: 0 },
  parts: [{ type: 'wall', x: 10, z: 10, w: 16, d: 1, h: 4 }],
  enemies: [{ type: 'sniper', x: 30, z: 70, yaw: 180, hp: 10 }, { type: 'soldier', x: 55, z: 78, yaw: 0, hp: 10 }] };
const wscan = new TacWorld(SCAN_STAGE);
const scanSn = wscan.enemies[0];
const scanBase = scanSn.yawQ;
let scanMaxDev = 0;
for (let t = 0; t < 500; t++) {
  wscan.step(IDLE); // player hides behind the wall: sniper never sees them
  let dv = (scanSn.yawQ - scanBase) & 65535;
  if (dv > 32768) dv = 65536 - dv;
  if (dv > scanMaxDev) scanMaxDev = dv;
}
check('idle sniper sweeps its scope across the arc', scanMaxDev > 5000);
check('sniper never spotted the hidden player during the scan test', scanSn.warnT === 0);

// --- squad doctrine (opt-in): corpses, radio links, illumination, flanking ---
const SQ_BASE = { name: 'sq', timeLimit: 300, lives: 5, ammo: 0, squad: true,
  arena: { w: 60, d: 60 }, playerStart: { x: 5, z: 3, yaw: 0 },
  enemies: [
    { type: 'soldier', x: 30, z: 40, yaw: 180, hp: 10 },
    { type: 'soldier', x: 30, z: 26, yaw: 0, hp: 10 },
    { type: 'soldier', x: 44, z: 40, yaw: 180, hp: 10 },
    { type: 'soldier', x: 5, z: 58, yaw: 0, hp: 10 }] };
const wsq = new TacWorld(SQ_BASE);
wsq.enemies[0].alive = false; wsq.enemiesLeft--; // comrade down at (30,40)
for (let t = 0; t < 10; t++) wsq.step(IDLE);
const disc = wsq.enemies[1], link = wsq.enemies[2];
check('patrol discovers the corpse and goes YELLOW at the body',
  disc.state === 1 && Math.abs(disc.tx - 30) < 1 && Math.abs(disc.tz - 40) < 1);
check('radio link fans a nearby unit onto the corpse position',
  link.state === 1 && Math.hypot(link.tx - 30, link.tz - 40) < 8);
const SQ_OFF = JSON.parse(JSON.stringify(SQ_BASE)); delete SQ_OFF.squad;
const wsq2 = new TacWorld(SQ_OFF);
wsq2.enemies[0].alive = false; wsq2.enemiesLeft--;
for (let t = 0; t < 10; t++) wsq2.step(IDLE);
check('without squad flag nobody notices the corpse', wsq2.enemies[1].state === 0 && wsq2.enemies[2].state === 0);

// drone illumination: while a drone has eyes on you, linked units track you live
const SQ_DRONE = { name: 'sqd', timeLimit: 300, lives: 5, ammo: 0, squad: true,
  arena: { w: 60, d: 60 }, playerStart: { x: 30, z: 10, yaw: 0 },
  parts: [{ type: 'wall', x: 14, z: 16, w: 1, d: 20, h: 3 }],
  enemies: [
    { type: 'drone', x: 30, z: 20, yaw: 180, hp: 10 },
    { type: 'soldier', x: 8, z: 22, yaw: 0, hp: 10 },
    { type: 'soldier', x: 5, z: 58, yaw: 0, hp: 10 }] };
const wsqd = new TacWorld(SQ_DRONE);
for (let t = 0; t < 30; t++) wsqd.step(IDLE);
const lit = wsqd.enemies[1];
check('drone spotlight feeds the player position to linked units',
  wsqd.enemies[0].seesPlayer && lit.state >= 1 && Math.hypot(lit.tx - wsqd.px, lit.tz - wsqd.pz) < 1);

// flanking: an odd-indexed alerted soldier without eyes swings wide of the line
const SQ_FLANK = { name: 'sqf', timeLimit: 300, lives: 5, ammo: 0, squad: true,
  arena: { w: 60, d: 60 }, playerStart: { x: 30, z: 3, yaw: 0 },
  parts: [{ type: 'wall', x: 30, z: 10, w: 10, d: 1, h: 4 }],
  enemies: [
    { type: 'soldier', x: 5, z: 58, yaw: 0, hp: 10 },
    { type: 'soldier', x: 30, z: 45, yaw: 180, hp: 10 }] };
const wsqf = new TacWorld(SQ_FLANK);
const flk = wsqf.enemies[1]; // idx 1 = flanker
flk.state = 2; flk.gauge = 100; flk.tx = 30; flk.tz = 12; // LKP behind the wall, no eyes
let maxDev = 0;
for (let t = 0; t < 400; t++) {
  wsqf.step(IDLE);
  const dev = Math.abs(flk.x - 30);
  if (dev > maxDev) maxDev = dev;
}
check('squad flanker swings wide of the direct line', maxDev > 3.5);

// --- entrench doctrine (opt-in): gunfire sends him diving for the nearest trench ---
const ENT_STAGE = { name: 'ent', timeLimit: 300, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 4, yaw: 0 },
  parts: [{ type: 'trench', x: 30, z: 30, w: 6, d: 6 }],
  enemies: [
    { type: 'soldier', x: 24, z: 26, yaw: 0, entrench: true, patrolX: 24, patrolZ: 14, hp: 10 },
    { type: 'soldier', x: 16, z: 26, yaw: 0, hp: 10 },
    { type: 'soldier', x: 5, z: 58, yaw: 0, hp: 10 }] };
const went = new TacWorld(ENT_STAGE);
for (let t = 0; t < 400; t++) {
  went.step(t === 10 ? { b: 2, m: 255, yawQ: 0, pitchQ: 0 } : IDLE); // one shot = noise
}
const entS = went.enemies[0], ctlS = went.enemies[1];
check('entrench soldier dives into the trench on gunfire', went.trenchAt(entS.x, entS.z) >= 0 && entS.dugIn);
check('un-flagged soldier investigates the noise instead', went.trenchAt(ctlS.x, ctlS.z) < 0);
for (let t = 0; t < 400; t++) went.step(IDLE); // long after the scare: still dug in, patrol abandoned
check('dug-in soldier never abandons his trench', went.trenchAt(went.enemies[0].x, went.enemies[0].z) >= 0);

// --- sniper rounds are near-hitscan: the counter is the laser warn, not footwork ---
const SNSPD_STAGE = { name: 'snspd', timeLimit: 300, lives: 5, ammo: 0,
  arena: { w: 40, d: 80 }, playerStart: { x: 20, z: 10, yaw: 0 },
  enemies: [{ type: 'sniper', x: 20, z: 70, yaw: 180, hp: 10 }, { type: 'soldier', x: 5, z: 78, yaw: 0, hp: 10 }] };
const wsnspd = new TacWorld(SNSPD_STAGE);
let snFireTick = -1, snHitTick = -1;
for (let t = 0; t < 400; t++) {
  const ev = wsnspd.step(IDLE);
  if (ev.sniperShot && snFireTick < 0) snFireTick = t;
  if (snFireTick >= 0 && wsnspd.hp < wsnspd.maxHp) { snHitTick = t; break; }
}
check('sniper round covers 60 m in under 0.8 s', snFireTick >= 0 && snHitTick >= 0 && (snHitTick - snFireTick) <= 40);

// --- night ops: cosmetic darkness — vision is identical day or night; ---
// --- playerLit stays deterministic for rendering (lamp pools + beams)  ---
const NIGHT_STAGE = { name: 'night', timeLimit: 300, lives: 5, ammo: 0, night: true,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'lamp', x: 20, z: 20, r: 5 }],
  enemies: [{ type: 'soldier', x: 20, z: 35, yaw: 180, hp: 10 }, { type: 'soldier', x: 5, z: 55, yaw: 0, hp: 10 }] };
const wng = new TacWorld(NIGHT_STAGE);
wng.px = 20; wng.pz = 21; // standing in the lamp pool, 14 m from the watcher
for (let t = 0; t < 40; t++) wng.step(IDLE);
check('lamp pool marks the player LIT', wng.playerLit === true && wng.enemies[0].gauge > 0);
const wng2 = new TacWorld(NIGHT_STAGE);
wng2.px = 28; wng2.pz = 22; // dark spot ~15 m out, in the cone — night is cosmetic, still seen normally
for (let t = 0; t < 150; t++) wng2.step(IDLE);
check('night vision equals day vision (unlit player still seen)', wng2.playerLit === false && wng2.enemies[0].gauge > 0);

// searchlight: the rotating beam lights the player once per revolution
const SL_STAGE = { name: 'sl', timeLimit: 300, lives: 5, ammo: 0, night: true,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'searchlight', x: 20, z: 30, r: 16, period: 6 }],
  enemies: [{ type: 'soldier', x: 5, z: 55, yaw: 0, hp: 10 }] };
const wsl2 = new TacWorld(SL_STAGE);
wsl2.px = 20; wsl2.pz = 18; // 12 m south of the light, inside its reach
let litTicks = 0, darkTicks = 0;
for (let t = 0; t < 6 * 50; t++) { // one full revolution
  wsl2.step(IDLE);
  if (wsl2.playerLit) litTicks++; else darkTicks++;
}
check('searchlight sweeps over the player once a revolution', litTicks > 5 && litTicks < 60 && darkTicks > 200);

// --- extract goal: collect all intel, reach the exit — no kill required ---
const EXT_STAGE = { name: 'ext', timeLimit: 300, lives: 5, ammo: 0, goal: 'extract',
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [
    { type: 'intel', x: 20, z: 20 }, { type: 'intel', x: 10, z: 30 },
    { type: 'exit', x: 35, z: 8, w: 6, d: 6 }
  ],
  enemies: [{ type: 'soldier', x: 38, z: 55, yaw: 0, hp: 10 }] };
function walkTo(wx, tx, tz, maxT) {
  for (let t = 0; t < maxT; t++) {
    const dx = tx - wx.px, dz = tz - wx.pz;
    if (dx * dx + dz * dz < 0.4) return true;
    wx.step({ b: 0, m: 0, yawQ: tacYawFor(dx, dz), pitchQ: 0 });
    if (wx.dead || wx.clearedFlag) return false;
  }
  return false;
}
const wex = new TacWorld(EXT_STAGE);
check('extract stage counts its intel', wex.goalType === 1 && wex.intelLeft === 2);
walkTo(wex, 35, 8, 400); // exit first: must NOT clear without the intel
for (let t = 0; t < 10; t++) wex.step(IDLE);
check('exit alone does not clear', wex.clearedFlag === false);
walkTo(wex, 20, 20, 600);
check('first intel picked', wex.intelLeft === 1);
walkTo(wex, 10, 30, 600);
check('second intel picked', wex.intelLeft === 0);
walkTo(wex, 35, 8, 900);
for (let t = 0; t < 5 && !wex.clearedFlag; t++) wex.step(IDLE);
check('all intel + exit clears WITHOUT killing anyone', wex.clearedFlag === true && wex.enemiesLeft === 1);

// --- flat-ground crouch: SNEAK held on open ground crouches you ---
const FLAT_STAGE = { name: 'flat', timeLimit: 120, lives: 5, ammo: 0, arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [], enemies: [{ type: 'soldier', x: 20, z: 35, yaw: 180, hp: 10 }] };
const wf = new TacWorld(FLAT_STAGE);
wf.step({ b: 4, m: 255, yawQ: 0, pitchQ: 0 }); // SNEAK held, standing on flat ground
check('crouches on flat ground with SNEAK', wf.crouched === true && wf.py >= -0.01);
check('crouched hit height is lower', wf.playerHitH() < TAC.PLAYER_H);
wf.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 }); // release
check('stands back up when SNEAK released', wf.crouched === false);

// --- stairs: walk up (dir 0 = +z ascending) at full speed, reach platform top ---
const wsl = new TacWorld(STAGE);
wsl.px = 32.0; wsl.pz = 28.0;
for (let t = 0; t < 80; t++) wsl.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 }); // faster run: 80 ticks lands mid-platform (further overshoots the far edge)
check('climbed the stairs to the platform', wsl.py > 1.9 && wsl.pz > 36.0 && wsl.pz < 44.0);
// mid-staircase heights sit on discrete treads (multiples of the riser)
const wst = new TacWorld(STAGE);
wst.px = 32.0; wst.pz = 28.0;
for (let t = 0; t < 45; t++) wst.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 });
const rise = wst.slopes[0].rise;
const treads = Math.round(wst.py / rise);
check('standing on a discrete tread', wst.py > 0 && Math.abs(wst.py - treads * rise) < 0.001);
// diagonal approach climbs too (the old ramp forced a slide here)
const wdg = new TacWorld(STAGE);
wdg.px = 30.5; wdg.pz = 28.5;
let dgMax = 0;
for (let t = 0; t < 160; t++) { wdg.step({ b: 0, m: 16, yawQ: 0, pitchQ: 0 }); if (wdg.py > dgMax) dgMax = wdg.py; } // 45° NE across the stairs
check('diagonal approach still climbs', dgMax > 0.5);
// walking back down is plain walking now — no forced slide
for (let t = 0; t < 90; t++) wsl.step({ b: 0, m: 64, yawQ: 0, pitchQ: 0 }); // faster walk: 90 ticks reaches ground past the stair base
check('walked back down to ground level', wsl.py < 0.5 && wsl.pz < 33.0 && wsl.pz > 20.0);

// --- stealth: sneaking near an idle enemy fills gauge slower than running ---
function gaugeAfter(sneak, ticks) {
  const g = new TacWorld(STAGE);
  // stand in front of the shield soldier's patrol, within cone
  g.px = 20.0; g.pz = 30.0; // 5m in front, enemy faces 180 (south, toward player)
  for (let t = 0; t < ticks; t++) g.step({ b: sneak ? 4 : 0, m: 255, yawQ: 0, pitchQ: 0 });
  return g.enemies[0].gauge;
}
const gRun = gaugeAfter(false, 6);
const gSneak = gaugeAfter(true, 6);
check('vision gauge fills', gRun > 0);
check('sneaking halves gauge fill', gSneak < gRun && gSneak > 0);

// standing behind the rock blocks vision
const gc = new TacWorld(STAGE);
gc.px = 20.0; gc.pz = 14.6; // rock (z 14.25..15.75, h1.4) between player chest (1.1)? chest 1.1 < 1.4 => blocked
for (let t = 0; t < 100; t++) gc.step(IDLE);
check('cover blocks vision gauge', gc.enemies[0].gauge === 0);

// --- gatling fires without provocation ---
const wg = new TacWorld(STAGE);
let gShots = 0;
for (let t = 0; t < 300; t++) { const ev = wg.step(IDLE); if (ev.gatlingShot) gShots++; }
check('gatling area suppression fires', gShots >= 15);

// --- sniper laser: stand in its cone with LOS (dedicated stage, no interference) ---
const SNIPE_STAGE = {
  name: 'snipe', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 10, yaw: 0 },
  parts: [],
  enemies: [{ type: 'sniper', x: 20, z: 30, yaw: 180 }]
};
const wsn = new TacWorld(SNIPE_STAGE);
let aimEv = false, shotEv = false;
for (let t = 0; t < 200; t++) { const ev = wsn.step(IDLE); if (ev.sniperAim) aimEv = true; if (ev.sniperShot) shotEv = true; }
check('sniper acquired target', aimEv === true);
check('sniper fired after warn', shotEv === true);
check('player lost 1 hp', wsn.hp === 4);

// --- determinism: identical scripted run twice, compare full state fingerprints ---
function scripted(seedYaw) {
  const world = new TacWorld(STAGE);
  const recs = [];
  let yaw = seedYaw, pitch = 0;
  for (let t = 0; t < 1500; t++) {
    yaw = (yaw + ((t % 7) === 0 ? 300 : 40)) & 65535;
    pitch = ((t % 90) < 45 ? pitch + 30 : pitch - 30);
    const b = ((t % 37) === 0 ? 1 : 0) | ((t % 100) < 60 ? 2 : 0) | ((t % 50) > 40 ? 4 : 0);
    const m = (t % 11) === 0 ? 255 : ((t * 3) % 128);
    const rec = { b, m, yawQ: yaw, pitchQ: pitch };
    recs.push(rec);
    world.step(rec);
  }
  let fp = hex(world.px) + hex(world.py) + hex(world.pz) + hex(world.vy) + world.enemiesLeft + '|' + world.hp;
  for (const en of world.enemies) fp += hex(en.x) + hex(en.z) + en.state + '|' + hex(en.gauge);
  for (const ba of world.barrels) fp += hex(ba.x) + hex(ba.z) + ba.state;
  return { fp, recs, world };
}
const r1 = scripted(100);
const r2 = scripted(100);
check('determinism: bit-identical state after 1500 mixed ticks', r1.fp === r2.fp);

// --- replay codec round trip ---
const encoded = tacEncodeTrace(r1.recs);
const decoded = tacDecodeTrace(encoded, 100000);
check('codec: same length', decoded && decoded.length === r1.recs.length);
let same = true;
for (let i = 0; i < r1.recs.length; i++) {
  const a = r1.recs[i], d = decoded[i];
  // jump bit only survives on the first tick of a run — but our encoder never merges jump ticks,
  // so all fields must match exactly.
  if (a.b !== d.b || a.m !== d.m || a.yawQ !== d.yawQ || a.pitchQ !== d.pitchQ) { same = false; break; }
}
check('codec: lossless round trip', same);
console.log('  (encoded size for 1500 ticks: ' + encoded.length + ' chars)');

// --- auto lock-on (facing-based) ---
// moving ~14 deg off the enemy direction still locks (25-deg cone) and clears
const wl = new TacWorld(DUEL_BACK);
let lockSeen = false, clearedLock = false;
for (let t = 0; t < 500 && !clearedLock; t++) {
  const ev = wl.step({ b: 2, m: 5, yawQ: 0, pitchQ: 0 }); // m=5 -> facing ~14 deg east of north
  if (wl.lockTarget === 0 && wl.lockKind === 0) lockSeen = true;
  if (ev.cleared) clearedLock = true;
}
check('auto lock-on: acquires target near facing', lockSeen);
check('auto lock-on: off-center facing still clears', clearedLock);

// beyond the 24 m lock range there is NO lock — closing in is mandatory
const FAR_STAGE = {
  name: 'far', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [{ type: 'sniper', x: 20, z: 45, yaw: 0 }] // 40 m ahead, facing away
};
const wfar = new TacWorld(FAR_STAGE);
wfar.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 }); // face north
check('no lock beyond 24 m', wfar.lockTarget === -1);
// run north until inside lock range
let gotFarLock = false;
for (let t = 0; t < 1200 && !gotFarLock; t++) {
  wfar.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 });
  if (wfar.lockTarget === 0) gotFarLock = true;
}
check('lock acquires once player closes in', gotFarLock);

// no lock through cover: wall between player and enemy
const DUEL_WALLED = {
  name: 'duel-walled', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'wall', x: 20, z: 9, w: 6, d: 1, h: 4 }],
  enemies: [{ type: 'soldier', x: 20, z: 14, yaw: 0 }]
};
const ww = new TacWorld(DUEL_WALLED);
let lockedThroughWall = false, wallHits = 0;
for (let t = 0; t < 120; t++) {
  const ev = ww.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
  if (ww.lockTarget >= 0) lockedThroughWall = true;
  if (ev.bulletWall) wallHits++;
}
check('no lock through walls', lockedThroughWall === false);
check('unlocked shots fly straight into the wall', wallHits > 0);
check('walled enemy untouched', ww.enemies[0].hp === TAC.SOLDIER_HP);

// lock ignores enemies outside the facing cone (enemy 90 deg to the right)
const DUEL_SIDE = {
  name: 'duel-side', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 5, z: 20, yaw: 0 },
  parts: [],
  enemies: [{ type: 'soldier', x: 25, z: 20, yaw: 270 }]
};
const wsd = new TacWorld(DUEL_SIDE);
wsd.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 }); // facing +z (spawn yaw), enemy is at +x
check('no lock outside the 25-deg cone', wsd.lockTarget === -1);
wsd.step({ b: 0, m: 32, yawQ: 0, pitchQ: 0 }); // step east -> character turns east
check('lock acquired after turning to face the enemy', wsd.lockTarget === 0);

// --- mines ---
const MINE_STAGE = {
  name: 'mines', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'mine', x: 20, z: 10 }, { type: 'mine', x: 20, z: 11.5 }],
  enemies: [{ type: 'sniper', x: 38, z: 38, yaw: 0 }]
};
// walking onto a mine arms it, then it explodes and chains into the neighbor
const wm = new TacWorld(MINE_STAGE);
let armed = false, mineBooms = 0;
for (let t = 0; t < 300; t++) {
  const ev = wm.step({ b: 0, m: t < 52 ? 0 : 255, yawQ: 0, pitchQ: 0 }); // run north (ramp-up included), stop on the mine
  if (ev.mineArmed) armed = true;
  if (ev.explosions) mineBooms += ev.explosions.length;
}
check('mine arms on proximity', armed);
check('mine explodes and chains the second mine', mineBooms === 2);
check('standing in the blast hurt the player', wm.hp < 5);

// shooting a mine detonates it remotely (facing lock picks it up)
// mines sit ~7 m out: the faster bullet's aim ray needs a shallower descent
// than at 5 m so a discrete tick samples inside the low (0.25 m) mine cylinder.
const MINE_SHOOT_STAGE = {
  name: 'mine-shoot', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'mine', x: 20, z: 12 }, { type: 'mine', x: 20, z: 13.5 }],
  enemies: [{ type: 'sniper', x: 38, z: 38, yaw: 0 }]
};
const wm2 = new TacWorld(MINE_SHOOT_STAGE);
let shotBoom = false, mineLocked = false;
for (let t = 0; t < 120 && !shotBoom; t++) {
  const ev = wm2.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 }); // fire from spawn, ~7 m short of the mine
  if (wm2.lockKind === 2 && wm2.lockTarget >= 0) mineLocked = true;
  if (ev.explosions) shotBoom = true;
}
check('mine is lockable and detonated by gunfire', mineLocked && shotBoom);
check('remote detonation from 5 m leaves the player unhurt', wm2.hp === 5);

// a patrolling soldier walks onto a mine and dies
const MINE_TRAP = {
  name: 'trap', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'mine', x: 30, z: 30 }],
  enemies: [{ type: 'soldier', x: 24, z: 30, yaw: 90, patrolX: 36, patrolZ: 30 }]
};
const wt = new TacWorld(MINE_TRAP);
let trapCleared = false;
for (let t = 0; t < 600 && !trapCleared; t++) {
  const ev = wt.step(IDLE);
  if (ev.cleared) trapCleared = true;
}
check('patrolling soldier steps on the mine (stage cleared)', trapCleared);

// --- drone ---
const DRONE_STAGE = {
  name: 'drone', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'rock', x: 20, z: 12, w: 4, d: 1.6, h: 1.4 }],
  enemies: [{ type: 'drone', x: 20, z: 24, yaw: 180 }]
};
// drone hovers, sees over the rock, dives and self-destructs on the player
const wd = new TacWorld(DRONE_STAGE);
check('drone hovers at fly height', wd.enemies[0].y > 2.5);
let dived = false, droneBoom = false, droneCleared = false;
for (let t = 0; t < 800 && !droneCleared; t++) {
  const ev = wd.step(IDLE);
  if (ev.droneDive) dived = true;
  if (ev.explosions) droneBoom = true;
  if (ev.cleared) droneCleared = true;
}
check('drone spots the player over cover and dives', dived);
check('kamikaze blast hurt the player', droneBoom && wd.hp < 5);
check('self-destructed drone counts as defeated', droneCleared);

// a drone can be shot down before it connects
const wd2 = new TacWorld(DRONE_STAGE);
let shotDown = false;
for (let t = 0; t < 400 && !shotDown; t++) {
  const ev = wd2.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.cleared) shotDown = true;
}
check('drone is shot down (1 hp) before reaching the player', shotDown && wd2.hp === 5);

// --- chest-height muzzle: rocks block outgoing fire (no one-way cover abuse) ---
const ROCK_DUEL = {
  name: 'rock-duel', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'rock', x: 20, z: 10, w: 4, d: 1.6, h: 1.4 }],
  enemies: [{ type: 'soldier', x: 20, z: 14, yaw: 0 }]
};
const wr = new TacWorld(ROCK_DUEL);
let rockWall = 0;
for (let t = 0; t < 100; t++) {
  const ev = wr.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.bulletWall) rockWall++;
}
check('no lock through a rock at chest height', wr.lockTarget === -1);
check('grounded shots slam into the rock', rockWall > 0);
check('enemy behind the rock is untouched', wr.enemies[0].hp === TAC.SOLDIER_HP);
// ...but jumping raises the muzzle over the rock: jump + fire kills it
const wr2 = new TacWorld(ROCK_DUEL);
let overRockKill = false;
for (let t = 0; t < 600 && !overRockKill; t++) {
  const b = ((t % 40) === 0 ? 1 : 0) | 2;
  const ev = wr2.step({ b: b, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.cleared) overRockKill = true;
}
check('jump-firing over the rock kills the enemy', overRockKill);

// --- operator: killing the pilot crashes its drones ---
const OP_STAGE = {
  name: 'op', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [
    { type: 'operator', x: 20, z: 22, yaw: 0, group: 5 },
    { type: 'drone', x: 32, z: 30, yaw: 270, patrolX: 8, patrolZ: 30, group: 5 }
  ]
};
const wop = new TacWorld(OP_STAGE);
let crashSeen = false, opCleared = false;
for (let t = 0; t < 600 && !opCleared; t++) {
  const ev = wop.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 }); // shoot the operator ahead
  if (ev.droneCrash) crashSeen = true;
  if (ev.cleared) opCleared = true;
}
check('operator dies to one shot', !wop.enemies[0].alive);
check('its drone lost uplink and crashed', crashSeen);
check('crashed drone counts as defeated (cleared)', opCleared);

// an alerted operator runs away from the player
const OP_FLEE = {
  name: 'op-flee', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [{ type: 'operator', x: 20, z: 14, yaw: 180 }, { type: 'sniper', x: 38, z: 55, yaw: 0 }]
};
const wof = new TacWorld(OP_FLEE);
for (let t = 0; t < 250; t++) wof.step(IDLE);
check('spotted operator flees away', wof.enemies[0].z > 16.5);

// --- grenade: standard equipment with a recharge gate ---
const GREN_STAGE = {
  name: 'gren', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [{ type: 'soldier', x: 20, z: 13.5, yaw: 0 }] // ~8.5 m ahead, facing away
};
const wgr = new TacWorld(GREN_STAGE);
let thrown = false, grenBoom = false, grenCleared = false;
wgr.step(IDLE); // release first: a held button at spawn is not a fresh edge
wgr.step({ b: 16, m: 255, yawQ: 0, pitchQ: 0 });
thrown = wgr.grenades.length === 1 && wgr.grenadeCd > 0;
check('grenade throws on the bomb button', thrown);
wgr.step({ b: 16, m: 255, yawQ: 0, pitchQ: 0 }); // spam attempt during cooldown
check('cooldown blocks back-to-back throws', wgr.grenades.length === 1);
for (let t = 0; t < 120 && !grenCleared; t++) {
  const ev = wgr.step(IDLE);
  if (ev.explosions) grenBoom = true;
  if (ev.cleared) grenCleared = true;
}
check('grenade explodes on impact and kills the soldier', grenBoom && grenCleared);
// recharge: ready again after 8 s (enemy far away so the stage doesn't clear)
const wgr2 = new TacWorld({ ...GREN_STAGE, enemies: [{ type: 'sniper', x: 38, z: 38, yaw: 0 }] });
wgr2.step(IDLE); // release first: a held button at spawn is not a fresh edge
wgr2.step({ b: 16, m: 255, yawQ: 0, pitchQ: 0 });
for (let t = 0; t < TAC.GRENADE_CD; t++) wgr2.step(IDLE);
wgr2.step({ b: 16, m: 255, yawQ: 0, pitchQ: 0 });
check('grenade recharges and throws again', wgr2.grenades.length === 2);

// periscope sniping: scoped in a trench stays crouched — hidden while shooting
const PERI_STAGE = {
  name: 'peri', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 80 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'trench', x: 20, z: 10, w: 8, d: 4 }],
  enemies: [
    { type: 'soldier', x: 20, z: 26, yaw: 180, hp: 10 },
    { type: 'sniper', x: 38, z: 76, yaw: 0 }
  ]
};
const wpe = new TacWorld(PERI_STAGE);
wpe.px = 20; wpe.pz = 10; wpe.py = -0.9;
wpe.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 }); // release first: a held button at spawn is not a fresh edge
wpe.step({ b: 32, m: 255, yawQ: 0, pitchQ: 0 }); // scope up, in the pit
check('scoped in a pit stays crouched (periscope)', wpe.scoped === true && wpe.crouched === true);
let periHurt = false, periHits = 0;
for (let t = 0; t < 1400; t++) {
  const b = (t % 320) === 0 ? 2 : 0; // one scoped shot per recharge
  const ev = wpe.step({ b: b, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.playerHit) periHurt = true;
  if (ev.enemyHit) periHits++;
}
check('periscope shots land on the target', periHits >= 3);
check('crouched sniper is never hit back', !periHurt);

// --- captured drone: operator kill grants a pilotable kamikaze drone ---
const CAP_STAGE = {
  name: 'cap', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [
    { type: 'operator', x: 20, z: 15, yaw: 0, group: 6 },
    { type: 'gatling', x: 20, z: 45, yaw: 0 } // faces away; hard target for the drone
  ]
};
const wc = new TacWorld(CAP_STAGE);
let granted = false;
for (let t = 0; t < 200 && !granted; t++) {
  const ev = wc.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.droneGranted) granted = true;
}
check('operator kill grants a drone use', granted && wc.droneUses === 1);
// launch (edge), fly north toward the gatling, detonate on top of it
let launched = false;
wc.step({ b: 8, m: 255, yawQ: 0, pitchQ: 0 });
launched = !!wc.pilot;
check('drone launches with the drone button', launched && wc.droneUses === 0);
let flew = 0;
while (wc.pilot && wc.pilot.z < 44.8 && flew < 700) { wc.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 }); flew++; }
check('piloted drone flies while the body stays put', wc.pilot && wc.pz < 10);
wc.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 }); // FIRE = start the homing dive
let boomCleared = false;
for (let t = 0; t < 150 && !boomCleared; t++) { const ev = wc.step(IDLE); if (ev.cleared) boomCleared = true; }
check('homing dive destroys the gatling (cleared)', boomCleared);
check('drone is spent after detonating', wc.pilot === null && wc.droneUses === 0);
// battery: a fresh grant powers down after 15 s of flight
const wc2 = new TacWorld(CAP_STAGE);
wc2.droneUses = 1; // grant directly for the battery test
wc2.step(IDLE); // release first: a held button at spawn is not a fresh edge
wc2.step({ b: 8, m: 255, yawQ: 0, pitchQ: 0 });
let fizzled = false;
for (let t = 0; t < 1700 && !fizzled; t++) { const ev = wc2.step(IDLE); if (ev.droneDead) fizzled = true; }
check('drone battery expires after ~30 s', fizzled && wc2.pilot === null);

// --- medkit ---
const MED_STAGE = {
  name: 'med', timeLimit: 120, lives: 3, ammo: 0,
  arena: { w: 40, d: 40 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'mine', x: 20, z: 10 }, { type: 'medkit', x: 20, z: 14 }],
  enemies: [{ type: 'sniper', x: 38, z: 38, yaw: 0 }]
};
const wmed = new TacWorld(MED_STAGE);
let healed = false, medBoomed = false;
for (let t = 0; t < 300; t++) {
  // run up to the mine, then STOP on it (faster running would otherwise clear
  // the 2 m blast in the 0.5 s fuse) to eat the blast, then walk on to the kit
  const m = medBoomed ? 0 : (wmed.pz < 9.4 ? 0 : 255);
  const ev = wmed.step({ b: 0, m, yawQ: 0, pitchQ: 0 });
  if (ev.explosions) medBoomed = true;
  if (ev.medkit) healed = true;
}
check('mine blast hurt, medkit healed 1 back', healed && wmed.hp === 2);
check('used medkit is consumed', wmed.medkits[0].alive === false);
// at full health the kit is ignored and stays
const wmed2 = new TacWorld({ ...MED_STAGE, parts: [{ type: 'medkit', x: 20, z: 9 }] });
for (let t = 0; t < 120; t++) wmed2.step({ b: 0, m: t < 60 ? 0 : 255, yawQ: 0, pitchQ: 0 });
check('full-health player leaves the medkit', wmed2.hp === 3 && wmed2.medkits[0].alive === true);

// --- river: slows the player; ground enemies refuse to cross ---
const RIVER_STAGE = {
  name: 'river', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 11, yaw: 0 },
  parts: [{ type: 'river', x: 20, z: 16, w: 40, d: 8 }],
  enemies: [{ type: 'soldier', x: 20, z: 30, yaw: 180 }]
};
const wrv = new TacWorld(RIVER_STAGE);
for (let t = 0; t < 60; t++) wrv.step({ b: 0, m: 0, yawQ: 0, pitchQ: 0 });
// 60 ticks of running leaves the player mid-channel (z=12..20): at full run speed
// the ~45% wading slowdown is what keeps them here — without it they'd already be
// out the far bank (pz>20, back at grade).
check('river slows wading (~45% speed)', wrv.pz > 14.5 && wrv.pz < 17.5 && wrv.py < -0.4);
check('river is a sunken channel (player wades below grade)', wrv.py < -0.4);
// alerted soldier chases but holds the river bank
const wrv2 = new TacWorld(RIVER_STAGE);
wrv2.px = 20; wrv2.pz = 24; // north of the river, in the open
for (let t = 0; t < 60; t++) wrv2.step(IDLE); // get spotted
wrv2.px = 20; wrv2.pz = 8;  // hop south of the river (test poke)
for (let t = 0; t < 300; t++) wrv2.step(IDLE);
check('ground enemy refuses to enter the river', wrv2.enemies[0].z >= 19.9);

// --- trench: a REAL pit — crouch to vanish, stand to fight ---
const TRENCH_STAGE = {
  name: 'trench', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'trench', x: 20, z: 10, w: 8, d: 4 }],
  enemies: [{ type: 'soldier', x: 20, z: 28, yaw: 180 }] // 18 m: too flat an angle to see into the pit
};
const wtr = new TacWorld(TRENCH_STAGE);
wtr.px = 20; wtr.pz = 10; wtr.py = -0.9; // already down in the pit (test poke)
for (let t = 0; t < 30; t++) wtr.step(IDLE);
check('trench is a real pit (player drops ~0.9m)', wtr.py < -0.7 && wtr.trenchAt(wtr.px, wtr.pz) >= 0);
check('crouch engages automatically in a trench', wtr.crouched === true);
// auto-crouched below grade: at 18 m the rifleman can neither see nor hit
let crHurt = false;
for (let t = 0; t < 500; t++) { const ev = wtr.step(IDLE); if (ev.playerHit) crHurt = true; }
check('crouched in the pit: never spotted, never hit', !crHurt && wtr.enemies[0].state === 0);
// firing pops you up for a moment: now the fight is on
let upSpotted = false;
for (let t = 0; t < 600; t++) {
  wtr.step({ b: (t % 40) === 0 ? 2 : 0, m: 255, yawQ: 0, pitchQ: 0 });
  if (wtr.enemies[0].state === 2) { upSpotted = true; break; }
}
check('firing from the pit pops you up and gets you spotted', upSpotted);
// and the player can shoot out of the pit
const wtr2 = new TacWorld(TRENCH_STAGE);
wtr2.px = 20; wtr2.pz = 10;
for (let t = 0; t < 30; t++) wtr2.step(IDLE); // drop to the pit floor
let outKill = false;
for (let t = 0; t < 300 && !outKill; t++) { const ev = wtr2.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 }); if (ev.cleared) outKill = true; }
check('shooting out of a trench works (cleared)', outKill);
// pop-firing from a trench = standing: the besieger's normal shots land
const POP_STAGE = {
  name: 'pop', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'trench', x: 20, z: 10, w: 8, d: 4 }],
  enemies: [{ type: 'soldier', x: 20, z: 24, yaw: 180, hp: 10 }] // tanky so the duel lasts
};
const wpp = new TacWorld(POP_STAGE);
wpp.px = 20; wpp.pz = 10; wpp.py = -0.9;
wpp.faceQ = 32768; // firing away to the south — still fully exposed while the trigger is held
let popHit = false;
for (let t = 0; t < 900 && !popHit; t++) {
  const ev = wpp.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 }); // trigger held the whole time
  if (ev.playerHit) popHit = true;
}
check('holding fire from a trench eats normal enemy shots', popHit);
// release the trigger: back under cover almost immediately
let reHidden = false;
for (let t = 0; t < 20; t++) { wpp.step(IDLE); if (wpp.crouched) { reHidden = true; break; } }
check('releasing fire re-crouches within ~0.2s', reHidden);

// a gatling that can see a trench-standing player depresses its barrel and hits
const GATR_STAGE = {
  name: 'gatr', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'trench', x: 20, z: 10, w: 8, d: 4 }],
  enemies: [{ type: 'gatling', x: 20, z: 36, yaw: 180, hp: 10 }] // 26 m out — beyond old soldier vision
};
const wgt = new TacWorld(GATR_STAGE);
wgt.px = 20; wgt.pz = 10; wgt.py = -0.9;
wgt.faceQ = 32768; // firing south, standing exposed in the pit
let gatHit = false;
for (let t = 0; t < 1200 && !gatHit; t++) {
  const ev = wgt.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.playerHit) gatHit = true;
}
check('gatling stream connects with a trench-standing player at 26m', gatHit);

// suppressive fire: an alerted trench soldier sprays at the last known spot
const SUPP_STAGE = {
  name: 'supp', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [
    { type: 'trench', x: 20, z: 24, w: 8, d: 3 },
    { type: 'rock', x: 20, z: 14, w: 4, d: 1.6, h: 1.4 }
  ],
  enemies: [{ type: 'soldier', x: 20, z: 24, yaw: 180 }]
};
const wsp = new TacWorld(SUPP_STAGE);
wsp.px = 20; wsp.pz = 11; // hidden behind the rock (no line of sight)
wsp.alertEnemy(wsp.enemies[0], wsp.px, wsp.pz); // it knows roughly where you are
let suppShots = 0, seenDuring = false;
for (let t = 0; t < 900; t++) {
  const ev = wsp.step({ b: 4, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.rifleShot) { suppShots++; if (wsp.enemies[0].seesPlayer) seenDuring = true; }
}
check('entrenched soldier sprays suppressive fire while blind', suppShots >= 5 && !seenDuring);

// siege AI: soldiers hold a standoff ring instead of walking onto the lip
const SIEGE_STAGE = {
  name: 'siege', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'trench', x: 20, z: 10, w: 8, d: 4 }],
  enemies: [{ type: 'soldier', x: 20, z: 28, yaw: 180 }]
};
const wsg = new TacWorld(SIEGE_STAGE);
wsg.px = 20; wsg.pz = 10; wsg.py = -0.9;
for (let t = 0; t < 20; t++) wsg.step(IDLE);
wsg.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 }); // one pop-up shot: it knows you're there
for (let t = 0; t < 900; t++) wsg.step(IDLE); // back under; auto-crouch hides you
const sgD = Math.sqrt((wsg.enemies[0].x - 20) ** 2 + (wsg.enemies[0].z - 10) ** 2);
check('alerted soldier besieges the trench from ~12m (no blundering in)', wsg.enemies[0].state === 2 && sgD > 10.5);

// enemy soldiers in trenches duck between shots (pop-up targets)
const TRENCH_DUEL = {
  name: 'tduel', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'trench', x: 20, z: 24, w: 8, d: 3 }],
  enemies: [{ type: 'soldier', x: 20, z: 24, yaw: 180 }]
};
const wtd = new TacWorld(TRENCH_DUEL);
let sawCrouch = false, sawPop = false;
for (let t = 0; t < 600; t++) {
  wtd.step(IDLE);
  if (wtd.enemies[0].crouched) sawCrouch = true;
  if (!wtd.enemies[0].crouched && wtd.enemies[0].aimT > 0) sawPop = true;
}
check('trench soldier ducks and pops up to fire', sawCrouch && sawPop);

// --- rockslide: shoot the post, boulders crush the patrol lane ---
const SLIDE_STAGE = {
  name: 'slide', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [{ type: 'rockslide', x: 20, z: 30, w: 6, d: 3, dir: 2 }], // rolls -z toward the lane
  enemies: [{ type: 'soldier', x: 20, z: 22, yaw: 90, patrolX: 20, patrolZ: 22 }]
};
const wsd2 = new TacWorld(SLIDE_STAGE);
// post sits at z=30-2.1=27.9 facing us; fire north from spawn (lock will find it? enemy nearer center) —
// aim by facing north: enemy at z22 is closer to the reticle; shoot enemy dead first? Instead poke the trigger directly:
wsd2.triggerSlide(wsd2.slides[0]);
let crushed = false;
for (let t = 0; t < 300 && !crushed; t++) { const ev = wsd2.step(IDLE); if (ev.cleared) crushed = true; }
check('rockslide boulders crush the soldier (cleared)', crushed);

// --- scope: pickup, manual aim, 3 shots ---
const SCOPE_STAGE = {
  name: 'scope', timeLimit: 120, lives: 5, ammo: 0,
  arena: { w: 40, d: 100 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [],
  enemies: [{ type: 'gatling', x: 20, z: 80, yaw: 0, hp: 20 }] // tanky target far beyond lock range (survives all 5 scoped shots)
};
const wsc = new TacWorld(SCOPE_STAGE);
wsc.step(IDLE); // release first: a button already held at spawn is not a fresh edge
wsc.step({ b: 32, m: 255, yawQ: 0, pitchQ: 0 }); // standard equipment: scope anytime
check('scope mode engaged, no auto-lock', wsc.scoped === true && wsc.lockTarget === -1);
// gatling dead ahead: NO recharge wait anymore — each tap fires (5-shot cap is the limit)
let scShots = 0;
for (let k = 0; k < 2; k++) {
  for (let t = 0; t < 3; t++) wsc.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 });
  const ev = wsc.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.scopeShot) scShots++;
}
check('back-to-back scoped taps both fire (no recharge)', scShots === 2 && wsc.enemies[0].hp === 14);
// keep tapping: the remaining 3 shots also fire freely, then the cap stops it
let scMore = 0;
for (let k2 = 0; k2 < 5; k2++) {
  wsc.step({ b: 0, m: 255, yawQ: 0, pitchQ: 0 });
  const evM = wsc.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
  if (evM.scopeShot) scMore++;
}
check('remaining shots fire until the 5-cap is hit', scMore === 3 && wsc.scopeShots === 0);

// --- scope: only SCOPE_MAX (5) shots per stage, then it's dry ---
// two far, walled-off enemies keep the stage un-cleared but can't shoot the
// player (a tall wall breaks line of sight); the scope fires into the wall.
const SCAP_STAGE = { name: 'scap', timeLimit: 300, lives: 5, ammo: 0, arena: { w: 30, d: 80 }, playerStart: { x: 15, z: 10, yaw: 0 },
  parts: [{ type: 'wall', x: 15, z: 20, w: 30, d: 2, h: 4 }],
  enemies: [{ type: 'soldier', x: 15, z: 60, yaw: 180, hp: 20 }, { type: 'soldier', x: 5, z: 60, yaw: 180, hp: 20 }] };
const wscap = new TacWorld(SCAP_STAGE);
wscap.step(IDLE); // release first: a button already held at spawn is not a fresh edge
wscap.step({ b: 32, m: 255, yawQ: 0, pitchQ: 0 }); // scope up
let capShots = 0;
// tap fire repeatedly (edge each time) — no recharge wait now, but still capped
for (let t = 0; t < 40; t++) {
  const fire = (t % 2 === 0) ? 2 : 0; // release between shots so each is an edge
  const ev = wscap.step({ b: fire, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.scopeShot) capShots++;
}
check('scope fires at most SCOPE_MAX times', capShots === TAC.SCOPE_MAX);
check('scopeShots counter is depleted', wscap.scopeShots === 0);
check('scope stays engaged between shots', wsc.scoped === true);

// --- jammer + switch: enemy drones stall, player drone immune, switch kills field ---
const JAM_STAGE = {
  name: 'jam', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 80 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [
    { type: 'switch', x: 20, z: 12, r: 14 }
  ],
  enemies: [
    { type: 'drone', x: 20, z: 18, yaw: 0, patrolX: 20, patrolZ: 60 },
    { type: 'operator', x: 36, z: 76, yaw: 180, group: 8 }
  ]
};
const wjm = new TacWorld(JAM_STAGE);
let enemyFried = false;
for (let t = 0; t < 120; t++) { const ev = wjm.step(IDLE); if (ev.jamZap) enemyFried = true; }
check('ENEMY drone is NOT harmed by the veil', !enemyFried && wjm.enemies[0].alive);
wjm.px = 20; wjm.pz = 15; // walk into the veil
wjm.step(IDLE);
check('player map is jammed inside the field', wjm.playerJammed === true);
// --- satchel bomber: 10-second time bomb with full warning ---
const BOMB_STAGE = {
  name: 'bomb', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 10, yaw: 0 },
  parts: [],
  enemies: [
    { type: 'bomber', x: 20, z: 24, yaw: 180 },
    { type: 'soldier', x: 5, z: 55, yaw: 0, hp: 10 }
  ]
};
const wbm = new TacWorld(BOMB_STAGE);
let bmThrow = -1, bmLand = -1, bmBoom = -1;
for (let t = 0; t < 1200; t++) {
  const ev = wbm.step(IDLE);
  if (ev.bomberThrow && bmThrow < 0) bmThrow = t;
  if (ev.bombLand && bmLand < 0) bmLand = t;
  if (ev.explosions && bmBoom < 0) bmBoom = t;
}
check('bomber lobs at a seen player', bmThrow >= 0);
check('bomb lands 1.8 s after the throw', bmLand === bmThrow + 90);
check('bomb blows exactly 5 s after landing', bmBoom === bmLand + 250);
check('a camper who ignores 5 s of beeping eats the blast', wbm.hp < wbm.maxHp);

// walking away during the fuse is always enough
const wbe = new TacWorld(BOMB_STAGE);
let bmLand2 = -1, bmDone = false;
for (let t = 0; t < 1500 && !bmDone; t++) {
  const ev = wbe.step(bmLand2 < 0 ? IDLE : { b: 0, m: 64, yawQ: 0, pitchQ: 0 }); // flee south once it lands
  if (ev.bombLand && bmLand2 < 0) bmLand2 = t;
  if (bmLand2 >= 0 && wbe.bombs.length && wbe.bombs[0].state === 2) bmDone = true;
}
check('5 s is enough to walk clear of the bomb', bmDone && wbe.hp === wbe.maxHp);

// --- cracked wall: falls only to explosives and gatling fire ---
const CW_STAGE = {
  name: 'cw', timeLimit: 180, lives: 5, ammo: 0,
  // shooter set back to z=3 (~8 m from the foot mine): the faster bullet needs a
  // flatter aim than at ~5 m to sample inside the low mine cylinder and set it off.
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 3, yaw: 0 },
  parts: [
    { type: 'crackedWall', x: 20, z: 12, w: 6, d: 1, h: 3 },
    { type: 'mine', x: 20, z: 10.8 }
  ],
  enemies: [
    { type: 'soldier', x: 20, z: 18, yaw: 180, hp: 10 },
    { type: 'soldier', x: 5, z: 55, yaw: 0, hp: 10 }
  ]
};
const wcw = new TacWorld(CW_STAGE);
wcw.step(IDLE);
check('cracked wall blocks sight while intact', wcw.enemies[0].seesPlayer === false);
// rifle fire thuds off without chipping
const wcr = new TacWorld(CW_STAGE);
wcr.mines[0].alive = false; // just the wall
for (let t = 0; t < 300; t++) wcr.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 });
const cwBox = wcr.boxes.find(b => b.kind === 3);
check('rifle fire never chips a cracked wall', cwBox.alive === true && cwBox.hp === TAC.CRACKED_HP);
// one nearby explosion razes it (shoot the mine at its foot)
let cwBroke = -1;
for (let t = 0; t < 300 && cwBroke < 0; t++) {
  const ev = wcw.step({ b: t === 2 ? 2 : 0, m: 255, yawQ: 0, pitchQ: 0 });
  if (ev.wallBreaks) cwBroke = t;
}
check('an explosion razes the cracked wall', cwBroke >= 0 && wcw.boxes.find(b => b.kind === 3).alive === false);
wcw.step(IDLE);
check('sightlines open through the razed wall', wcw.enemies[0].seesPlayer === true);
const cwPass = wcw.moveCircle(20, 11, 0, 0.4, 1.8, 0, 3.0);
check('the breach is walkable', cwPass.z > 12.5);
// sustained gatling fire chews through
const wcg = new TacWorld(CW_STAGE);
wcg.mines[0].alive = false;
let gatBroke = false;
for (let t = 0; t < 200 && !gatBroke; t++) {
  if (t < TAC.CRACKED_HP) wcg.bullets.push({ x: 20, y: 1.5, z: 8, vx: 0, vy: 0, vz: 30, ttl: 50, fromPlayer: false, alive: true, gat: true });
  const ev = wcg.step(IDLE);
  if (ev.wallBreaks) gatBroke = true;
}
check('gatling fire chews through the cracked wall', gatBroke);
// an IDLE gatling's suppression sweep must never demolish geometry
const CWI = {
  name: 'cwi', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 60 }, playerStart: { x: 20, z: 3, yaw: 0 },
  parts: [{ type: 'crackedWall', x: 20, z: 20, w: 8, d: 1, h: 3 }],
  enemies: [
    { type: 'gatling', x: 20, z: 28, yaw: 180 },
    { type: 'soldier', x: 5, z: 55, yaw: 0, hp: 10 }
  ]
};
const wci = new TacWorld(CWI);
for (let t = 0; t < 500; t++) wci.step(IDLE); // it sprays the wall the whole time, unalerted
const cwiBox = wci.boxes.find(b => b.kind === 3);
check('idle gatling sweep never chews the wall', cwiBox.alive === true && cwiBox.hp === TAC.CRACKED_HP);

// enemy drones IGNORE the veil now: a drone dives THROUGH the field and reaches
// the player inside it (the veil no longer stops or destroys enemy drones)
const RIM_STAGE = {
  name: 'rim', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 60, d: 60 }, playerStart: { x: 30, z: 30, yaw: 0 },
  parts: [{ type: 'switch', x: 30, z: 22, r: 12 }],
  enemies: [
    { type: 'drone', x: 30, z: 44, yaw: 180 },
    { type: 'operator', x: 4, z: 4, yaw: 0, group: 9 }
  ]
};
const wrm = new TacWorld(RIM_STAGE);
wrm.px = 30; wrm.pz = 32.4;
let rimZap = false, rimHurt = false;
for (let t = 0; t < 3000; t++) {
  const ev = wrm.step({ b: 0, m: 255, yawQ: 32768, pitchQ: 0 });
  if (ev.jamZap) rimZap = true;
  if (wrm.hp < wrm.maxHp) rimHurt = true;
  if (!wrm.enemies[0].alive) break;
}
check('enemy drone is never fried by the veil', !rimZap);
check('enemy drone reaches the player through the veil', rimHurt);

// symmetric denial: launching your drone INSIDE the veil kills it instantly
wjm.droneUses = 1;
wjm.px = 20; wjm.pz = 8; // inside the veil (r 14 around 20,12)
const evJL = wjm.step({ b: 8, m: 255, yawQ: 0, pitchQ: 0 });
check('own drone launched in the veil dies at launch', !!(evJL.jamZap && evJL.droneDead) && wjm.pilot === null);
// barrier: a drone OUTSIDE the field cannot chase the player into it
const JAM_BARRIER = {
  name: 'jam2', timeLimit: 180, lives: 5, ammo: 0,
  arena: { w: 40, d: 80 }, playerStart: { x: 20, z: 5, yaw: 0 },
  parts: [
    { type: 'switch', x: 20, z: 20, r: 12 }
  ],
  enemies: [
    { type: 'drone', x: 20, z: 38, yaw: 180, patrolX: 20, patrolZ: 46 },
    { type: 'sniper', x: 4, z: 76, yaw: 0 }
  ]
};
const wjb = new TacWorld(JAM_BARRIER);
wjb.px = 20; wjb.pz = 24; // inside the dome, near its northern rim — bait
let enemyZapped = false, playerTouched = false;
for (let t = 0; t < 900; t++) { const ev = wjb.step(IDLE); if (ev.jamZap) enemyZapped = true; if (wjb.hp < wjb.maxHp) playerTouched = true; }
check('a drone chasing you into the veil is NOT fried', !enemyZapped);
check('and it reaches the player through the veil', playerTouched);

// the veil is symmetric: the PLAYER's piloted drone also fries on contact
const wjp = new TacWorld(JAM_BARRIER);
wjp.px = 20; wjp.pz = 38; // outside the dome (r 12 around 20,20), facing it
wjp.enemies.forEach(e => { e.alive = false; wjp.enemiesLeft--; });
wjp.enemiesLeft++; // keep one "left" so the sim doesn't clear-stop
wjp.droneUses = 1;
wjp.step({ b: 0, m: 255, yawQ: 32768, pitchQ: 0 }); // release first: a held button at spawn is not a fresh edge
wjp.step({ b: 8, m: 255, yawQ: 32768, pitchQ: 0 }); // launch, looking -z
check('own drone launched', wjp.pilot !== null);
let ownFried = false;
for (let t = 0; t < 400 && !ownFried; t++) {
  const ev = wjp.step({ b: 0, m: 0, yawQ: 32768, pitchQ: 0 }); // fly toward the dome
  if (ev.jamZap && ev.droneDead) ownFried = true;
}
check('own drone fries the instant it enters the veil', ownFried && wjp.pilot === null);

// destroy the switch -> field drops -> drone wakes up
const wjm2 = new TacWorld(JAM_STAGE);
let swDown = false;
for (let t = 0; t < 120 && !swDown; t++) {
  const ev = wjm2.step({ b: 2, m: 255, yawQ: 0, pitchQ: 0 }); // console dead ahead at z12, veil is not solid
  if (ev.switchDown) swDown = true;
}
check('shooting the console through its own veil kills the field', swDown && wjm2.switches[0].alive === false);
check('map clears once the veil is down', (wjm2.px = 20, wjm2.pz = 15, wjm2.step(IDLE), wjm2.playerJammed === false));

console.log(failures === 0 ? '\nALL PASS' : '\n' + failures + ' FAILURES');
process.exit(failures === 0 ? 0 : 1);
