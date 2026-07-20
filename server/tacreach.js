// TAC reachability analyzer — shared by the create/update gate in worker.js
// (concatenated after tacsim.js, same no-exports pattern) and by the CLI
// scripts/tac-reach.js. Requires TacWorld from tacsim.js to be defined first.
//
// Builds a 0.5 m multi-level walk graph over the stage (each standable surface
// in a cell is its own node: the ground under a bridge and the bridge deck are
// different nodes), then BFS from the spawn twice:
//   WALK  — steps up to +0.42, any descent
//   +JUMP — additionally steps up to +1.35
// PASS rule: every intel, the exit zone and every medkit must be reachable ON
// FOOT — walking OR jumping. A jump-only objective is allowed (surfaced as a
// warning); only a spot NO jump can reach fails the gate. (Design policy: the
// player is expected to jump for elevated objectives.)
//
// tacAnalyzeReachability(stage) -> {
//   pass:     boolean            — false only when an objective is reachable by neither walk nor jump
//   failures: [string]           — UNREACHABLE objectives (gate on these)
//   warnings: [string]           — JUMP-ONLY objectives + elevated tops nothing can reach
//   jumpTops: n, deadTops: n     — counts for reporting
//   skipped:  boolean            — true when the stage exceeds the work budget
// }
'use strict';

function tacAnalyzeReachability(stage) {
  var CELL = 0.5;
  var JUMP = 1.35;   // jump-assisted step
  var HEAD = 1.6;    // headroom needed to stand

  var w = new TacWorld(stage);
  var STEP = w.stepUp + 0.07;   // auto step-up (sim stepUp, default 0.35, + slack)
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

  // ---- radius-aware edge test ----------------------------------------------
  // The walk graph steps between 0.5 m cell CENTERS, but the real player is a
  // PLAYER_R (0.4 m) circle resolved by moveCircle. A point flood-fill leaks
  // through gaps and thin walls the circle can't actually pass:
  //   * a wall thinner than a cell can sit BETWEEN two sample centers, so no
  //     cell ever samples it and the graph walks straight through;
  //   * an L-notch or slot narrower than 2*PLAYER_R is "open" to a point but
  //     impassable to the circle.
  // edgeBlocked() rejects a move A->B when a solid box (top above what the feet
  // can step onto) intrudes within PLAYER_R of the segment between the two cell
  // centers — mirroring moveCircle's blockAbove = feetY + STEP_UP rule. R is
  // trimmed by a hair (EDGE_SLACK) so a legitimately tight-but-passable opening
  // the sim lets you slide through isn't falsely sealed.
  var PLAYER_R = 0.4;
  var EDGE_SLACK = 0.06;      // clearance the sim's slide-resolution buys back
  var R = PLAYER_R - EDGE_SLACK;
  // shortest distance from point (px,pz) to the axis-aligned box [x0,x1]x[z0,z1]
  function distPtBox(px, pz, x0, z0, x1, z1) {
    var ddx = px < x0 ? x0 - px : (px > x1 ? px - x1 : 0.0);
    var ddz = pz < z0 ? z0 - pz : (pz > z1 ? pz - z1 : 0.0);
    return Math.sqrt(ddx * ddx + ddz * ddz);
  }
  // Push a circle centered at (px,pz) out of every wall taller than `lim` that it
  // overlaps — a faithful mini-copy of moveCircle's push loop. Walls that ARE the
  // feet's own floor (top == footY, center over them) are skipped: you stand ON
  // them, they don't push you. Returns the settled {x,z}. Used to answer "can the
  // circle actually occupy this spot, and where does it end up?".
  function settleCircle(px, pz, footY) {
    for (var iter = 0; iter < 4; iter++) {
      var moved = false;
      var res = w.forBoxesIn ? null : null; // (keep closure shape; iterate manually below)
      // manual scan of nearby boxes
      var cands = [];
      w.forBoxesIn(px - R, pz - R, px + R, pz + R, function (bi) { cands.push(bi); });
      for (var ci = 0; ci < cands.length; ci++) {
        var b = w.boxes[cands[ci]];
        if (!b.alive) continue;
        if (b.h <= footY + STEP) continue;                 // steppable: not a wall
        if (b.yb >= footY + HEAD) continue;                // clears the body: pass under
        var cxp = px < b.x0 ? b.x0 : (px > b.x1 ? b.x1 : px);
        var czp = pz < b.z0 ? b.z0 : (pz > b.z1 ? b.z1 : pz);
        var ox = px - cxp, oz = pz - czp, d2 = ox * ox + oz * oz;
        if (d2 >= R * R) continue;
        if (d2 > 1e-6) {
          var d = Math.sqrt(d2), push = R - d;
          px += (ox / d) * push; pz += (oz / d) * push; moved = true;
        } else {
          var lxx = px - b.x0, rxx = b.x1 - px, lzz = pz - b.z0, rzz = b.z1 - pz;
          if (lxx <= rxx && lxx <= lzz && lxx <= rzz) px = b.x0 - R;
          else if (rxx <= lzz && rxx <= rzz) px = b.x1 + R;
          else if (lzz <= rzz) pz = b.z0 - R; else pz = b.z1 + R;
          moved = true;
        }
      }
      if (!moved) break;
    }
    return { x: px, z: pz };
  }
  // true when the circle cannot travel from cell A (feet yA) to adjacent cell B
  // (feet yB). Faithful to moveCircle: we try to settle the circle at B's center
  // against every wall too tall to step onto; if the settle pushes it more than a
  // cell away from B (it can't fit near B), the move is blocked. A thin wall or a
  // sub-radius slot between the cells pushes the circle right back; a wall the
  // player merely walks the edge of only nudges it a little and the move stands.
  function edgeBlocked(cxA, czA, yA, cxB, czB, yB) {
    var bx = (cxB + 0.5) * CELL, bz = (czB + 0.5) * CELL;
    var s = settleCircle(bx, bz, yB);
    // If, after being pushed out of walls, the circle's center leaves B's own cell
    // by more than a slack margin, it cannot actually stand at B: blocked. (Half a
    // cell + slack keeps honest edge-walking — a small nudge — passable.)
    var dx = s.x - bx, dz = s.z - bz;
    if (dx * dx + dz * dz > (CELL * 0.5 + EDGE_SLACK) * (CELL * 0.5 + EDGE_SLACK)) return true;
    // Also require the MIDPOINT between A and B to admit the circle — catches a
    // thin wall sitting exactly on the shared cell boundary (both centers clear,
    // but the circle can't pass through the middle).
    var ax = (cxA + 0.5) * CELL, az = (czA + 0.5) * CELL;
    var mx = (ax + bx) / 2, mz = (az + bz) / 2;
    var sm = settleCircle(mx, mz, Math.max(yA, yB));
    var mdx = sm.x - mx, mdz = sm.z - mz;
    if (mdx * mdx + mdz * mdz > (CELL * 0.5 + EDGE_SLACK) * (CELL * 0.5 + EDGE_SLACK)) return true;
    return false;
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
        if (edgeBlocked(cx3, cz3, y, nx, nz, ns[pick])) continue; // circle can't fit past a wall/slot
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

  // ---- objectives: must be reachable ON FOOT, walking OR jumping ----
  // Design policy (per the creator): every objective has to be attainable by a
  // player who is willing to jump — a jump-only approach is fine, a spot no jump
  // can reach is a broken stage. So JUMP-ONLY is an advisory NOTE (surfaced as a
  // warning), and only a truly UNREACHABLE objective (not walk, not jump) fails
  // the gate.
  var failures = [];
  var jumpNotes = [];
  function checkPoint(kind, x, z, y) {
    if (reachableAt(walk, x, z, y)) return;
    if (reachableAt(jump, x, z, y)) jumpNotes.push('JUMP-ONLY ' + kind + ' at (' + x + ',' + z + ') y' + y.toFixed(1) + ' (reachable by jumping)');
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

  // jump-only objectives are allowed but worth surfacing — prepend them so the
  // operator sees "this intel needs a jump" ahead of the dead-top advisories.
  var allWarnings = jumpNotes.concat(warnings);
  return { pass: failures.length === 0, failures: failures, warnings: allWarnings, jumpTops: jumpTops, deadTops: deadTops, skipped: false };
}
