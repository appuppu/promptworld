// Design-time solvability checker (NOT for publishing — human clear stays the
// publish gate). Loads server/sim.js, then runs a stochastic beam/greedy search
// over per-tick inputs to see if a clear route exists. Reports solved + a coarse
// difficulty (clear rate over N random-ish episodes).
//
// Usage: node solvecheck.js <stage.json> [episodes]

const fs = require('fs');
const path = require('path');
const vm = require('vm');

// Resolve sim.js robustly.
function loadSim() {
  const candidates = [
    path.resolve(process.cwd(), 'server/sim.js'),
    path.resolve(__dirname, '../../../../server/sim.js'),
    ''+require('path').resolve(__dirname,'../../server/sim.js')+'',
  ];
  for (const c of candidates) {
    try { const src = fs.readFileSync(c, 'utf8'); const ctx = { console }; vm.createContext(ctx);
      vm.runInContext(src + '\n;this.__SimWorld = SimWorld;', ctx); return ctx.__SimWorld; } catch (e) {}
  }
  throw new Error('sim.js not found');
}
const SimWorld = loadSim();

const stagePath = process.argv[2];
const EPISODES = parseInt(process.argv[3] || '4000', 10);
const stage = JSON.parse(fs.readFileSync(stagePath, 'utf8'));

// A tiny xorshift RNG so runs are reproducible without Math.random.
let seed = 123456789;
function rnd() { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return ((seed >>> 0) / 4294967296); }

function runEpisode(maxTicks) {
  const w = SimWorld.fromStage(stage);
  const goalX = stage.goal.x;
  // Policy: mostly move toward the goal-x, occasionally reverse, jump on a
  // Poisson-ish cadence, with random "commit" streaks so it can climb/ride.
  let dir = 1;                 // preferred horizontal
  let jumpCooldown = 0;
  let bestDist = Infinity;
  let stuck = 0, lastX = w.px, lastY = w.py;
  for (let t = 0; t < maxTicks; t++) {
    // choose horizontal: bias toward reducing |px-goalX| but explore
    const towardGoal = (goalX - w.px) >= 0 ? 1 : -1;
    let horiz = towardGoal;
    if (rnd() < 0.18) horiz = -towardGoal;      // occasional backtrack
    if (rnd() < 0.06) horiz = 0;                // occasional pause
    // jump: cadence + extra when not making progress
    let jump = false;
    if (jumpCooldown <= 0) {
      const p = 0.10 + (stuck > 30 ? 0.25 : 0);
      if (rnd() < p) { jump = true; jumpCooldown = 6 + Math.floor(rnd() * 10); }
    } else jumpCooldown--;

    const ev = w.step({ left: horiz < 0, right: horiz > 0, jump });
    if (ev.cleared) return { cleared: true, ticks: w.tickCount };
    if (w.timedOutFlag) return { cleared: false, ticks: w.tickCount };

    const d = Math.abs(goalX - w.px) + Math.abs(stage.goal.y - w.py) * 0.5;
    if (d < bestDist) bestDist = d;
    // stuck detection (little net movement)
    if (Math.abs(w.px - lastX) < 0.05 && Math.abs(w.py - lastY) < 0.05) stuck++; else stuck = 0;
    lastX = w.px; lastY = w.py;
  }
  return { cleared: false, ticks: maxTicks, bestDist };
}

const maxTicks = Math.min(stage.timeLimit * 50, 9000);
let clears = 0, minTicks = Infinity, bestDist = Infinity;
for (let e = 0; e < EPISODES; e++) {
  seed = 0x9e3779b9 ^ (e * 2654435761);     // vary seed per episode
  const r = runEpisode(maxTicks);
  if (r.cleared) { clears++; if (r.ticks < minTicks) minTicks = r.ticks; }
  else if (r.bestDist < bestDist) bestDist = r.bestDist;
}
const rate = clears / EPISODES;
console.log(JSON.stringify({
  stage: stage.name,
  episodes: EPISODES,
  clears,
  clearRate: +(rate * 100).toFixed(2) + '%',
  solvable: clears > 0,
  fastestClearSec: minTicks === Infinity ? null : +(minTicks * 0.02).toFixed(2),
  closestApproachWhenFailing: clears > 0 ? null : +bestDist.toFixed(1),
}, null, 2));
