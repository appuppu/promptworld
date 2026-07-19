// Local dev server for the TAC shooter client. No Cloudflare needed.
//   node scripts/tac-dev.js          -> http://localhost:8787/tac  (demo stage id: dev)
// Serves tac.html / tacsim.js / tac-client.js from server/, plus a tiny
// in-memory stage API that mirrors the worker's contract:
//   GET  /api/stages/dev            -> sample stage JSON (starts a test session)
//   POST /api/stages/dev/clear      -> verifies the replay with tacRunReplay
'use strict';

const fs = require('fs');
const path = require('path');
const http = require('http');
const vm = require('vm');

const ROOT = path.join(__dirname, '..');
// Load the sim into this process. runInThisContext (not eval): the sim is a
// strict-mode script, and strict eval would keep its vars out of global scope.
vm.runInThisContext(fs.readFileSync(path.join(ROOT, 'server', 'tacsim.js'), 'utf8'));

const SAMPLE = {
  schemaVersion: "0.3",
  game: "tac",
  name: "DEV ARENA",
  timeLimit: 600,
  lives: 3,
  ammo: 0,
  arena: {
    w: 70,
    d: 110
  },
  playerStart: {
    x: 35,
    z: 5,
    yaw: 0
  },
  parts: [
    {
      type: "rock",
      x: 20,
      z: 14,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 34,
      z: 18,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "rock",
      x: 50,
      z: 15,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 12,
      z: 24,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 42,
      z: 26,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 58,
      z: 24,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "mine",
      x: 28,
      z: 22
    },
    {
      type: "mine",
      x: 30,
      z: 24
    },
    {
      type: "medkit",
      x: 35,
      z: 28
    },
    {
      type: "wall",
      x: 18,
      z: 34,
      w: 20,
      d: 1.2,
      h: 3
    },
    {
      type: "wall",
      x: 52,
      z: 34,
      w: 20,
      d: 1.2,
      h: 3
    },
    {
      type: "wall",
      x: 10,
      z: 48,
      w: 1.2,
      d: 16,
      h: 3
    },
    {
      type: "wall",
      x: 60,
      z: 48,
      w: 1.2,
      d: 16,
      h: 3
    },
    {
      type: "rock",
      x: 24,
      z: 42,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 46,
      z: 42,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 35,
      z: 52,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "rock",
      x: 18,
      z: 56,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 52,
      z: 56,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "barrel",
      x: 40,
      z: 50
    },
    {
      type: "barrel",
      x: 41.3,
      z: 50.6
    },
    {
      type: "mine",
      x: 6,
      z: 40
    },
    {
      type: "mine",
      x: 7.5,
      z: 42
    },
    {
      type: "mine",
      x: 5,
      z: 44
    },
    {
      type: "medkit",
      x: 35,
      z: 58
    },
    {
      type: "platform",
      x: 14,
      z: 72,
      w: 16,
      d: 12,
      h: 2.4
    },
    {
      type: "slope",
      x: 25,
      z: 72,
      w: 6,
      d: 6,
      h: 2.4,
      dir: 3
    },
    {
      type: "rock",
      x: 36,
      z: 68,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 48,
      z: 74,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "rock",
      x: 60,
      z: 68,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 40,
      z: 80,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "barrel",
      x: 58,
      z: 78
    },
    {
      type: "barrel",
      x: 59.2,
      z: 78.8
    },
    {
      type: "medkit",
      x: 66,
      z: 66
    },
    {
      type: "platform",
      x: 35,
      z: 98,
      w: 24,
      d: 10,
      h: 3
    },
    {
      type: "slope",
      x: 35,
      z: 89,
      w: 6,
      d: 8,
      h: 3,
      dir: 0
    },
    {
      type: "rock",
      x: 20,
      z: 88,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 50,
      z: 88,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 35,
      z: 82,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "mine",
      x: 31,
      z: 84
    },
    {
      type: "mine",
      x: 39,
      z: 84
    },
    {
      type: "medkit",
      x: 35,
      z: 80
    },
    {
      type: "trench",
      x: 14,
      z: 66,
      w: 10,
      d: 3
    },
    {
      type: "trench",
      x: 56,
      z: 66,
      w: 10,
      d: 3
    },
    {
      type: "river",
      x: 35,
      z: 63,
      w: 70,
      d: 3
    },
    {
      type: "switch",
      x: 35,
      z: 95,
      r: 14
    }
  ],
  enemies: [
    {
      type: "soldier",
      x: 25,
      z: 18,
      yaw: 90,
      patrolX: 45,
      patrolZ: 18
    },
    {
      type: "soldier",
      x: 58,
      z: 28,
      yaw: 200
    },
    {
      type: "drone",
      x: 15,
      z: 20,
      yaw: 90,
      patrolX: 55,
      patrolZ: 20
    },
    {
      type: "soldier",
      x: 28,
      z: 46,
      yaw: 180,
      patrolX: 42,
      patrolZ: 46,
      group: 1
    },
    {
      type: "soldier",
      x: 15,
      z: 52,
      yaw: 180,
      group: 1
    },
    {
      type: "soldier",
      x: 55,
      z: 52,
      yaw: 90,
      patrolX: 55,
      patrolZ: 42,
      group: 1
    },
    {
      type: "gatling",
      x: 35,
      z: 44,
      yaw: 180
    },
    {
      type: "sniper",
      x: 12,
      z: 72,
      yaw: 90,
      group: 2
    },
    {
      type: "soldier",
      x: 44,
      z: 70,
      yaw: 180,
      patrolX: 56,
      patrolZ: 70,
      group: 2
    },
    {
      type: "soldier",
      x: 52,
      z: 82,
      yaw: 200,
      group: 2
    },
    {
      type: "drone",
      x: 36,
      z: 76,
      yaw: 90,
      patrolX: 64,
      patrolZ: 76,
      group: 2
    },
    {
      type: "sniper",
      x: 30,
      z: 98,
      yaw: 180,
      group: 3
    },
    {
      type: "gatling",
      x: 42,
      z: 98,
      yaw: 180
    },
    {
      type: "soldier",
      x: 20,
      z: 92,
      yaw: 90,
      patrolX: 50,
      patrolZ: 92,
      group: 3
    },
    {
      type: "soldier",
      x: 56,
      z: 96,
      yaw: 200,
      group: 3
    },
    {
      type: "operator",
      x: 66,
      z: 78,
      yaw: 270,
      group: 2
    }
  ]
};

let testStartedAt = null;

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8' };
function file(res, rel) {
  const p = path.join(ROOT, 'server', rel);
  if (!fs.existsSync(p)) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'Content-Type': MIME[path.extname(p)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  res.end(fs.readFileSync(p));
}
function json(res, obj, status) {
  res.writeHead(status || 200, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(obj));
}

http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const p = url.pathname;
  if (p === '/tac' || p === '/tac.html' || p === '/') return file(res, 'tac.html');
  if (p === '/tacsim.js') return file(res, 'tacsim.js');
  if (p === '/tac-client.js') return file(res, 'tac-client.js');

  if (p === '/api/stages/dev' && req.method === 'GET') {
    testStartedAt = Date.now();
    return json(res, SAMPLE);
  }
  if (p === '/api/stages/dev/clear' && req.method === 'POST') {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => {
      try {
        const b = JSON.parse(body);
        const maxTicks = Math.trunc(Math.fround(SAMPLE.timeLimit) / 0.02);
        const result = tacRunReplay(SAMPLE, b.replay, maxTicks);
        if (!result.cleared) return json(res, { error: 'Replay verification failed: ' + (result.error || 'not cleared') }, 422);
        const ms = result.ticks * 20;
        if (!testStartedAt) return json(res, { error: 'No test session.' }, 422);
        if (Date.now() - testStartedAt < ms - 2000) return json(res, { error: 'Clear exceeds elapsed time.' }, 422);
        return json(res, { id: 'dev', cleared: true, verified: true, clearTimeMs: ms, note: '(dev) replay verified' });
      } catch (e) {
        return json(res, { error: 'bad request: ' + e.message }, 400);
      }
    });
    return;
  }
  if (p === '/api/stages/dev/stats' && req.method === 'POST') return json(res, { ok: true });
  json(res, { error: 'not found' }, 404);
}).listen(8787, () => console.log('TAC dev server: http://localhost:8787/tac?stage=dev&key=devkey'));
