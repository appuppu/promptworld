// Stochastic solver: proves a stage is clearable BEFORE it gets created.
// Run: cat server/sim.js server/solve-stage.js | node -
// Writes the winning replay to /tmp-ish scratch for the clear submission.

const fs = require('fs');

const stage = {
  schemaVersion: '0.3',
  name: 'Crossfire',
  timeLimit: 40,
  playerStart: { x: -14, y: -2.5 },
  goal: { x: 23, y: -2.3, w: 1.4, h: 2.6 },
  parts: [
    { type: 'solid', x: -13, y: -4, w: 6, h: 1 },

    { type: 'crumble', x: -8, y: -4, w: 1.2, h: 1 },
    { type: 'crumble', x: -5.5, y: -4, w: 1.2, h: 1 },

    { type: 'movingPlatform', x: -2, y: -3.5, w: 1.2, h: 0.5, dx: 4, dy: 0, period: 2.6 },

    { type: 'solid', x: 5.5, y: -4, w: 2.4, h: 1 },
    { type: 'hazard', x: 4.7, y: -3.1, w: 0.8, h: 0.8 },
    { type: 'hazard', x: 6.3, y: -3.1, w: 0.8, h: 0.8 },

    { type: 'gravityFlip', x: 5.5, y: -1.6, w: 1.2, h: 1.2 },
    { type: 'solid', x: 7, y: 2, w: 6, h: 1 },
    { type: 'hazard', x: 8.5, y: 0.9, w: 0.7, h: 0.7 },
    { type: 'gravityFlip', x: 9.5, y: 0.2, w: 1.2, h: 1.2 },

    { type: 'solid', x: 11.5, y: -4, w: 2, h: 1 },
    { type: 'solid', x: 14.5, y: -4, w: 2, h: 1 },
    { type: 'boost', x: 15, y: -2.6, w: 0.4, h: 1.8, dirX: 1, power: 16 },

    { type: 'solid', x: 21.5, y: -4, w: 5, h: 1 },
    { type: 'hazard', x: 21.2, y: -3.1, w: 0.8, h: 0.8 },
  ],
};

function mulberry32(seed) {
  return function () {
    seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const maxTicks = stage.timeLimit * 50;
let best = -999;
let solution = null;

for (let seed = 1; seed <= 40000 && !solution; seed++) {
  const rnd = mulberry32(seed);
  const world = SimWorld.fromStage(stage);
  const codes = [];
  for (let t = 0; t < maxTicks; t++) {
    const right = rnd() < 0.92;
    const jump = rnd() < 0.12;
    let code = 0;
    if (right) code |= 2;
    if (jump) code |= 4;
    codes.push(code);
    const ev = world.step({ left: false, right, jump });
    if (world.px > best) best = world.px;
    if (ev.cleared) { solution = { codes: codes, ticks: world.tickCount, seed }; break; }
    if (ev.timedOut) break;
  }
}

if (!solution) {
  console.log(`UNSOLVED after 40000 attempts. Best x reached: ${best.toFixed(2)}`);
  process.exit(1);
}

// RLE encode (jump ticks never merged — edge semantics)
const rle = [];
let i = 0;
while (i < solution.codes.length) {
  const code = solution.codes[i];
  let count = 1;
  if ((code & 4) === 0) {
    while (i + count < solution.codes.length && solution.codes[i + count] === code) count++;
  }
  rle.push(code, count);
  i += count;
}

// verify the encoded replay round-trips
const check = simRunReplay(stage, rle, maxTicks);
if (!check.cleared) { console.log('RLE round-trip failed!'); process.exit(1); }

console.log(`SOLVED: seed ${solution.seed}, ${solution.ticks} ticks = ${(solution.ticks * 20 / 1000).toFixed(1)}s (verified)`);
fs.writeFileSync(process.env.OUT || 'solution.json', JSON.stringify({
  stage,
  clearTimeMs: check.ticks * 20,
  replay: { v: 1, ticks: check.ticks, rle },
}));
console.log('solution written');
