// Determinism cross-check, node twin of SimCrossCheck.cs.
// Run with: cat server/sim.js server/crosscheck.js | node - (from repo root)
// Produces simcheck_js.txt which must be byte-identical to simcheck_cs.txt.

const fs = require('fs');
const path = require('path');

const RUN_TICKS = 3000;
const SAMPLE_EVERY = 20;

function hex(v) {
  const buf = new ArrayBuffer(8);
  const dv = new DataView(buf);
  dv.setFloat64(0, v);
  return dv.getBigUint64(0).toString(16).padStart(16, '0');
}

function eventBits(ev) {
  let bits = 0;
  if (ev.jumped) bits += 1;
  if (ev.bounced) bits += 2;
  if (ev.boosted) bits += 4;
  if (ev.flipped) bits += 8;
  if (ev.respawned) bits += 16;
  if (ev.crumbled) bits += 32;
  if (ev.cleared) bits += 64;
  if (ev.timedOut) bits += 128;
  return bits;
}

const dir = path.join('PromptWorld', 'Assets', 'StreamingAssets', 'Stages');
const files = ['stage-001.json', 'stage-002.json', 'stage-003.json'];
const lines = [];

for (const file of files) {
  const data = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
  const world = SimWorld.fromStage(data);

  for (let t = 0; t < RUN_TICKS; t++) {
    const input = {
      right: (t % 100) < 85,
      left: (t % 213) < 20,
      jump: (t % 37) === 0,
    };
    const ev = world.step(input);
    const bits = eventBits(ev);

    if (world.tickCount % SAMPLE_EVERY === 0 || bits !== 0) {
      lines.push(`${file} ${world.tickCount} ${hex(world.px)} ${hex(world.py)} ${hex(world.vx)} ${hex(world.vy)} ${world.gravityDir | 0} ${bits}`);
    }
    if (world.clearedFlag || world.timedOutFlag) break;
  }
  const cleared = world.clearedFlag ? 'True' : 'False';
  const timedOut = world.timedOutFlag ? 'True' : 'False';
  lines.push(`${file} END cleared=${cleared} timedOut=${timedOut} ticks=${world.tickCount}`);
}

fs.writeFileSync('simcheck_js.txt', lines.join('\n') + '\n');
console.log('simcheck_js.txt written');
