// TAC reachability analyzer — shared by the create/update gate in worker.js
// (concatenated after tacsim.js, same no-exports pattern) and by the CLI
// scripts/tac-reach.js. Requires TacWorld from tacsim.js to be defined first.
//
// Builds a 0.5 m multi-level walk graph over the stage (each standable surface
// in a cell is its own node: the ground under a bridge and the bridge deck are
// different nodes), then BFS from the spawn twice:
//   WALK  — steps up to +0.42, any descent
//   +JUMP — additionally steps up to +1.35
// PASS rule: every intel, the exit zone and every medkit must be reachable by
// WALK alone (jumps are for optional shortcuts, per design policy).
//
// tacAnalyzeReachability(stage) -> {
//   pass:     boolean            — false when an objective is not walk-reachable
//   failures: [string]           — UNREACHABLE / JUMP-ONLY objectives (gate on these)
//   warnings: [string]           — advisory: elevated tops nothing can reach
//   jumpTops: n, deadTops: n     — counts for reporting
//   skipped:  boolean            — true when the stage exceeds the work budget
// }
'use strict';

function tacAnalyzeReachability(stage) {
  var CELL = 0.5;
  var STEP = 0.42;   // auto step-up (sim STEP_UP 0.4 + slack)
  var JUMP = 1.35;   // jump-assisted step
  var HEAD = 1.6;    // headroom needed to stand

  var w = new TacWorld(stage);
  var W = Math.ceil(w.arenaW / CELL), D = Math.ceil(w.arenaD / CELL);

  // ---- work budget ----
  // The worker runs this on every tac create/update, so the cost must be
  // bounded against adversarial stages. Rasterizing slopes/pits costs one
  // entry per covered cell; refuse pathological totals instead of burning CPU.
  var raster = 0;
  var i;
  for (i = 0; i < w.slopes.length; i++) {
    raster += (Math.ceil(w.slopes[i].x1 / CELL) - Math.floor(w.slopes[i].x0 / CELL) + 1) *
              (Math.ceil(w.slopes[i].z1 / CELL) - Math.floor(w.slopes[i].z0 / CELL) + 1);
  }
  for (i = 0; i < w.pits.length; i++) {
    raster += (Math.ceil(w.pits[i].x1 / CELL) - Math.floor(w.pits[i].x0 / CELL) + 1) *
              (Math.ceil(w.pits[i].z1 / CELL) - Math.floor(w.pits[i].z0 / CELL) + 1);
  }
  if (W * D > 200000 || raster > 2000000) {
    return { pass: true, failures: [], warnings: ['reachability analysis skipped: stage too large for the work budget'], jumpTops: 0, deadTops: 0, skipped: true };
  }

  // ---- per-cell slope / pit index (point queries below stay O(local)) ----
  var slopeIdx = new Array(W * D), pitIdx = new Array(W * D);
  function rasterize(list, idx) {
    for (var li = 0; li < list.length; li++) {
      var r = list[li];
      var cx0 = Math.max(0, Math.floor(r.x0 / CELL)), cx1 = Math.min(W - 1, Math.floor(r.x1 / CELL));
      var cz0 = Math.max(0, Math.floor(r.z0 / CELL)), cz1 = Math.min(D - 1, Math.floor(r.z1 / CELL));
      for (var gz = cz0; gz <= cz1; gz++) for (var gx = cx0; gx <= cx1; gx++) {
        var k = gz * W + gx;
        if (!idx[k]) idx[k] = [];
        idx[k].push(li);
      }
    }
  }
  rasterize(w.slopes, slopeIdx);
  rasterize(w.pits, pitIdx);

  // ---- standable surfaces per cell ----
  // surfaces[ci] = sorted array of heights (each is a walk-graph node level)
  var surfaces = new Array(W * D);
  function baseFloor(cx, cz, x, z) {
    var f = 0.0, lst = pitIdx[cz * W + cx];
    if (lst) for (var pi = 0; pi < lst.length; pi++) {
      var p = w.pits[lst[pi]];
      if (x >= p.x0 && x <= p.x1 && z >= p.z0 && z <= p.z1 && -p.depth < f) f = -p.depth;
    }
    return f;
  }
  function headroomOk(x, z, y) {
    var ok = true;
    w.forBoxesIn(x, z, x, z, function (bi) {
      var b = w.boxes[bi];
      if (!b.alive) return;
      if (x < b.x0 || x > b.x1 || z < b.z0 || z > b.z1) return;
      if (b.yb < y + HEAD && b.h > y + 0.05) ok = false; // box occupies the body space
    });
    return ok;
  }
  var cz, cx;
  for (cz = 0; cz < D; cz++) for (cx = 0; cx < W; cx++) {
    var x = (cx + 0.5) * CELL, z = (cz + 0.5) * CELL;
    var cands = [baseFloor(cx, cz, x, z)];
    w.forBoxesIn(x, z, x, z, function (bi) {
      var b = w.boxes[bi];
      if (!b.alive) return;
      if (x < b.x0 || x > b.x1 || z < b.z0 || z > b.z1) return;
      cands.push(b.h);
    });
    var sl = slopeIdx[cz * W + cx];
    if (sl) for (var si = 0; si < sl.length; si++) {
      var s = w.slopes[sl[si]];
      if (x < s.x0 || x > s.x1 || z < s.z0 || z > s.z1) continue;
      cands.push(w.slopeYAt(s, x, z));
    }
    cands.sort(function (a, b2) { return a - b2; });
    var ok = [];
    for (var ci2 = 0; ci2 < cands.length; ci2++) {
      var y = cands[ci2];
      if (ok.length && Math.abs(ok[ok.length - 1] - y) < 0.05) continue;
      if (headroomOk(x, z, y)) ok.push(y);
    }
    surfaces[cz * W + cx] = ok;
  }

  // ---- BFS over (cell, level) nodes ----
  function bfs(maxUp) {
    var seen = new Array(W * D);
    for (var si2 = 0; si2 < W * D; si2++) seen[si2] = [];
    var sx = Math.min(W - 1, Math.max(0, Math.floor(w.px / CELL)));
    var sz = Math.min(D - 1, Math.max(0, Math.floor(w.pz / CELL)));
    var s0 = surfaces[sz * W + sx];
    if (!s0.length) return seen;
    // start on the surface closest to the actual spawn height
    var best = 0;
    for (var k = 1; k < s0.length; k++) if (Math.abs(s0[k] - w.py) < Math.abs(s0[best] - w.py)) best = k;
    var q = [[sx, sz, best]];
    seen[sz * W + sx][best] = true;
    var DIRS = [[1, 0], [-1, 0], [0, 1], [0, -1]];
    while (q.length) {
      var cur = q.pop();
      var cx3 = cur[0], cz3 = cur[1], li = cur[2];
      var y = surfaces[cz3 * W + cx3][li];
      for (var d = 0; d < 4; d++) {
        var nx = cx3 + DIRS[d][0], nz = cz3 + DIRS[d][1];
        if (nx < 0 || nx >= W || nz < 0 || nz >= D) continue;
        var ns = surfaces[nz * W + nx];
        // land on the HIGHEST surface not more than maxUp above the feet
        var pick = -1;
        for (var k2 = ns.length - 1; k2 >= 0; k2--) {
          if (ns[k2] <= y + maxUp) { pick = k2; break; }
        }
        if (pick < 0) continue;
        if (!seen[nz * W + nx][pick]) {
          seen[nz * W + nx][pick] = true;
          q.push([nx, nz, pick]);
        }
      }
    }
    return seen;
  }
  function reachableAt(seen, x, z, y) {
    var cx4 = Math.min(W - 1, Math.max(0, Math.floor(x / CELL)));
    var cz4 = Math.min(D - 1, Math.max(0, Math.floor(z / CELL)));
    var ss = surfaces[cz4 * W + cx4];
    for (var k = 0; k < ss.length; k++) {
      if (seen[cz4 * W + cx4][k] && Math.abs(ss[k] - y) < 0.9) return true;
    }
    return false;
  }

  var walk = bfs(STEP);
  var jump = bfs(JUMP);

  // ---- objectives: must be walk-reachable, or the stage is broken ----
  var failures = [];
  function checkPoint(kind, x, z, y) {
    if (reachableAt(walk, x, z, y)) return;
    if (reachableAt(jump, x, z, y)) failures.push('JUMP-ONLY ' + kind + ' at (' + x + ',' + z + ') y' + y.toFixed(1));
    else failures.push('UNREACHABLE ' + kind + ' at (' + x + ',' + z + ') y' + y.toFixed(1));
  }
  w.intels.forEach(function (it) { checkPoint('intel', it.x, it.z, it.y); });
  w.medkits.forEach(function (mk) { checkPoint('medkit', mk.x, mk.z, mk.y); });
  if (w.exitZone) {
    var ex = (w.exitZone.x0 + w.exitZone.x1) / 2, ez = (w.exitZone.z0 + w.exitZone.z1) / 2;
    checkPoint('exit', ex, ez, w.groundY(ex, ez, 1000, 0.3));
  }

  // ---- advisory: elevated standable tops of meaningful size nothing can reach ----
  var warnings = [], deadTops = 0, jumpTops = 0;
  for (var bi2 = 0; bi2 < w.boxes.length; bi2++) {
    var b2 = w.boxes[bi2];
    if (!b2.alive || b2.h < 0.9) continue;
    if ((b2.x1 - b2.x0) < 1.2 || (b2.z1 - b2.z0) < 1.2) continue; // decor: poles, merlons, banners
    var mx = (b2.x0 + b2.x1) / 2, mz = (b2.z0 + b2.z1) / 2;
    if (!headroomOk(mx, mz, b2.h)) continue; // buried under another block: not a play surface
    if (reachableAt(walk, mx, mz, b2.h)) continue;
    if (reachableAt(jump, mx, mz, b2.h)) { jumpTops++; continue; }
    deadTops++;
    if (warnings.length < 30) warnings.push('unreachable top at (' + mx.toFixed(0) + ',' + mz.toFixed(0) + ') y' + b2.h.toFixed(1));
  }

  return { pass: failures.length === 0, failures: failures, warnings: warnings, jumpTops: jumpTops, deadTops: deadTops, skipped: false };
}
