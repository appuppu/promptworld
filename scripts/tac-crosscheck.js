// TAC crosscheck (JS side): node twin of scripts/tac-crosscheck/Program.cs.
// Run: cat server/tacsim.js scripts/tac-crosscheck.js | node -
'use strict';
var fs = require('fs');
var path = require('path');

var seed = 12345;
function lcg() {
  seed = ((Math.imul(seed, 1103515245) + 12345) | 0) & 0x3fffffff;
  return seed;
}
function rnd(lo, hi) {
  return lo + lcg() * ((hi - lo) / 1073741824.0);
}
var _buf = new ArrayBuffer(8);
var _dv = new DataView(_buf);
function bits(v) {
  _dv.setFloat64(0, v);
  return _dv.getBigUint64(0).toString(16).padStart(16, '0');
}

var heading = 255, yaw = 0, pitch = 0;
function makeInput(t) {
  if (t % 37 === 0) heading = (lcg() & 255) > 200 ? 255 : (lcg() & 127);
  if (t % 61 === 0) yaw = lcg() & 65535;
  if (t % 53 === 0) pitch = (lcg() % 20000) - 10000;
  var b = 0;
  if ((t % 97) < 40) b |= 2;
  if ((t % 131) < 50) b |= 4;
  if (t % 149 === 0) b |= 1;
  if (t % 211 === 0) b |= 16;
  if (t % 387 === 0) b |= 32;
  if (t % 501 === 0) b |= 8;
  return { b: b, m: heading, yawQ: yaw, pitchQ: pitch };
}

var out = [];
// ---- kernel trace ----
for (var q = -70000; q < 140000; q += 17) {
  out.push('sin ' + q + ' ' + bits(tacSinQ(q)));
  out.push('cos ' + q + ' ' + bits(tacCosQ(q)));
}
for (var i = 0; i < 2000; i++) {
  var v = rnd(-1000.0, 1000.0);
  out.push('q ' + bits(tacQ(v)));
}
for (var i2 = 0; i2 < 2000; i2++) {
  var cur = lcg() & 65535;
  var tgt = lcg() & 65535;
  var rate = (lcg() % 900) + 1;
  out.push('turn ' + tacTurnToward(cur, tgt, rate));
}
for (var i3 = 0; i3 < 2000; i3++) {
  var dx = rnd(-10.0, 10.0);
  var dz = rnd(-10.0, 10.0);
  out.push('yaw ' + tacYawFor(dx, dz));
}
for (var i4 = 0; i4 < 3000; i4++) {
  var x0 = rnd(-5.0, 5.0), y0 = rnd(0.0, 3.0), z0 = rnd(-5.0, 5.0);
  var x1 = rnd(-5.0, 5.0), y1 = rnd(0.0, 3.0), z1 = rnd(-5.0, 5.0);
  var cx = rnd(-3.0, 3.0), cz = rnd(-3.0, 3.0);
  var cr = rnd(0.1, 2.0), ch = rnd(0.2, 3.0);
  out.push('segcyl ' + bits(tacSegCylinder(x0, y0, z0, x1, y1, z1, cx, 0.0, cz, cr, ch)));
}

// ---- full-stage simulation traces ----
var dir = path.join(process.cwd(), 'scripts', 'tac-crosscheck', 'stages');
var names = ['kitchen', 'breach', 'expiry', 'extract', 'castle', 'steps', 'armor'];
for (var sIdx = 0; sIdx < names.length; sIdx++) {
  var json = fs.readFileSync(path.join(dir, names[sIdx] + '.json'), 'utf8');
  var stage = JSON.parse(json);
  var w = new TacWorld(stage);
  seed = 1000 + sIdx;
  heading = 255; yaw = 0; pitch = 0;
  var recs = [];
  out.push('STAGE ' + names[sIdx] + ' ' + bits(w.px) + ' ' + bits(w.py) + ' ' + bits(w.pz) + ' ' + w.enemiesLeft);
  for (var t = 0; t < 2600; t++) {
    var inp = makeInput(t);
    recs.push(inp);
    w.step(inp);
    var liveB = 0;
    for (var bi = 0; bi < w.bullets.length; bi++) if (w.bullets[bi].alive) liveB++;
    var liveG = 0;
    for (var gi = 0; gi < w.grenades.length; gi++) if (w.grenades[gi].alive) liveG++;
    var actBo = 0;
    for (var oi = 0; oi < w.bombs.length; oi++) if (w.bombs[oi].state !== 2) actBo++;
    out.push('P ' + w.tick + ' ' + bits(w.px) + ' ' + bits(w.py) + ' ' + bits(w.pz) + ' ' + bits(w.vy) +
      ' ' + w.hp + ' ' + w.lockTarget + ' ' + w.lockKind + ' ' + w.enemiesLeft +
      ' ' + w.shotsFired + ' ' + w.scopeCd + ' ' + w.grenadeCd + ' ' + w.droneUses +
      ' ' + (w.pilot ? 1 : 0) + ' ' + (w.crouched ? 1 : 0) + ' ' + (w.scoped ? 1 : 0) +
      ' ' + liveB + ' ' + liveG + ' ' + actBo + ' ' + w.intelLeft + ' ' + (w.playerLit ? 1 : 0));
    if ((t % 25) === 0) {
      for (var e = 0; e < w.enemies.length; e++) {
        var en = w.enemies[e];
        out.push('E ' + w.tick + ' ' + e + ' ' + bits(en.x) + ' ' + bits(en.z) + ' ' + bits(en.y) +
          ' ' + en.yawQ + ' ' + en.state + ' ' + en.hp + ' ' + (en.alive ? 1 : 0) +
          ' ' + bits(en.gauge));
      }
    }
    if (w.dead || w.clearedFlag || w.timedOutFlag) break;
  }
  out.push('END ' + names[sIdx] + ' ' + w.tick + ' ' + (w.dead ? 1 : 0) + ' ' + (w.clearedFlag ? 1 : 0) + ' ' + (w.timedOutFlag ? 1 : 0) + ' ' + w.hp + ' ' + w.enemiesLeft);
  var enc = tacEncodeTrace(recs);
  var dec = tacDecodeTrace(enc, 100000);
  var match = dec !== null && dec.length === recs.length;
  if (match) {
    for (var ri = 0; ri < recs.length; ri++) {
      var dr = dec[ri];
      var rr0 = recs[ri];
      var wantB = ri === 0 ? rr0.b : rr0.b; // decode re-applies edge masking; compare below accounts for it
      if (dr.m !== rr0.m || dr.yawQ !== rr0.yawQ || dr.pitchQ !== rr0.pitchQ) { match = false; break; }
      if (dr.b !== rr0.b) { match = false; break; }
    }
  }
  var rr = tacRunReplay(JSON.parse(json), { v: 't1', ticks: recs.length, data: enc }, 100000);
  out.push('CODEC ' + names[sIdx] + ' ' + enc.length + ' ' + (match ? 1 : 0) + ' ' + (rr.cleared ? 1 : 0) + ' ' + rr.ticks);
}
process.stdout.write(out.join('\n') + '\n');
