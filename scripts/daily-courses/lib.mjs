// Shared helpers for Prompt World course generation: Claude CLI invocation,
// JSON extraction, the local solvability check, toolbox extraction, and draft
// creation via the REST API. Used by generate.mjs (daily cron) and
// redesign.mjs (targeted rebuilds with a hand-written design brief).

import { execFileSync, spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const __dirname = dirname(fileURLToPath(import.meta.url));
export const REPO = resolve(__dirname, '../..');
export const ORIGIN = process.env.PW_ORIGIN || 'https://promptworldgame.org';
export const CREATOR_TOKEN = process.env.PW_CREATOR_TOKEN || '770122b7-11d9-4d45-869d-d71292b60e5d';

const require = createRequire(import.meta.url);

// The exact toolbox doc the MCP server hands creators (TOOLBOX_DOC in worker.js).
export function getToolbox() {
  const src = readFileSync(resolve(REPO, 'server/worker.js'), 'utf8');
  const m = src.match(/const TOOLBOX_DOC = `([\s\S]*?)`;/);
  return m ? m[1] : 'Prompt World stage JSON v0.3.';
}

// Non-interactive Claude CLI. Longer default timeout — big stages take a while.
// input:'' gives the CLI an immediately-closed empty stdin so it doesn't wait
// 3s for piped input (which under heavy parallelism can stall the process).
export function askClaude(prompt, timeout = 300000) {
  return execFileSync('claude', ['-p', prompt], {
    encoding: 'utf8', maxBuffer: 8 * 1024 * 1024, timeout, input: '',
  });
}

// Async Claude CLI via spawn. stdin='ignore' reliably closes stdin (execFile
// ignores the stdio option when a callback is given). Returns {out} or {err}.
// Big stages take minutes — default timeout 600s.
export function askClaudeAsync(prompt, timeout = 600000) {
  return new Promise((res) => {
    const child = spawn('claude', ['-p', prompt], { stdio: ['ignore', 'pipe', 'pipe'] });
    let out = '', errBuf = '';
    const timer = setTimeout(() => { child.kill('SIGKILL'); res({ err: `timeout ${timeout / 1000}s` }); }, timeout);
    child.stdout.on('data', d => { out += d; });
    child.stderr.on('data', d => { errBuf += d; });
    child.on('error', e => { clearTimeout(timer); res({ err: e.message }); });
    child.on('close', code => {
      clearTimeout(timer);
      res(code === 0 ? { out } : { err: `exit ${code}: ${errBuf.slice(0, 200)}` });
    });
  });
}

// Concurrency-limited map: run fn over items, at most `limit` at once.
export async function pool(items, limit, fn) {
  const out = new Array(items.length);
  let idx = 0;
  const workers = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (idx < items.length) { const cur = idx++; out[cur] = await fn(items[cur], cur); }
  });
  await Promise.all(workers);
  return out;
}

export function extractJson(text) {
  const fence = text.match(/```(?:json)?\s*([\s\S]*?)```/);
  const candidates = [];
  if (fence) candidates.push(fence[1]);
  const brace = text.match(/\{[\s\S]*\}/);
  if (brace) candidates.push(brace[0]);
  for (const c of candidates) {
    try { const o = JSON.parse(c); if (o && o.parts) return o; } catch {}
  }
  return null;
}

// Greedy stochastic solver over the deterministic sim — a CONFIDENCE HINT only.
export function solvable(stage, episodes = 2500) {
  const simSrc = readFileSync(resolve(REPO, 'server/sim.js'), 'utf8');
  const vm = require('node:vm');
  const ctx = { console };
  vm.createContext(ctx);
  vm.runInContext(simSrc + '\n;this.__SimWorld = SimWorld;', ctx);
  const SimWorld = ctx.__SimWorld;

  let seed = 1;
  const rnd = () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed >>> 0) / 4294967296; };
  const flips = (stage.parts || []).filter(p => p.type === 'gravitySet' || p.type === 'gravityFlip')
    .map(p => ({ x: p.x, y: p.y })).sort((a, b) => a.x - b.x);
  const maxTicks = Math.min((stage.timeLimit || 90) * 50, 12000);

  for (let e = 0; e < episodes; e++) {
    seed = (0x9e3779b9 ^ (e * 2654435761)) >>> 0 || 1;
    const w = SimWorld.fromStage(stage);
    const chain = [];
    for (const f of flips) if (rnd() < 0.6) chain.push(f);
    chain.push({ x: stage.goal.x, y: stage.goal.y });
    let ci = 0, jumpCd = 0, stuck = 0, lx = w.px, ly = w.py;
    for (let t = 0; t < maxTicks; t++) {
      const tgt = chain[Math.min(ci, chain.length - 1)];
      const dx = tgt.x - w.px;
      let h = dx >= 0 ? 1 : -1;
      if (Math.abs(dx) < 0.6) h = 0;
      if (rnd() < 0.12) h = -h;
      let jump = false;
      if (jumpCd <= 0) { const p = 0.10 + (stuck > 25 ? 0.30 : 0) + (tgt.y - w.py > 1.5 ? 0.10 : 0); if (rnd() < p) { jump = true; jumpCd = 5 + Math.floor(rnd() * 9); } } else jumpCd--;
      const ev = w.step({ left: h < 0, right: h > 0, jump });
      if (ev.cleared) return true;
      if (w.timedOutFlag) break;
      if (Math.abs(tgt.x - w.px) < 1.2 && ci < chain.length - 1) ci++;
      if (Math.abs(w.px - lx) < 0.05 && Math.abs(w.py - ly) < 0.05) stuck++; else stuck = 0;
      lx = w.px; ly = w.py;
    }
  }
  return false;
}

// Basic size stats so a caller can reject stages that came out too small.
export function stageStats(stage) {
  const parts = stage.parts || [];
  const xs = parts.map(p => p.x), ys = parts.map(p => p.y);
  const spanX = Math.max(...xs) - Math.min(...xs);
  const spanY = Math.max(...ys) - Math.min(...ys);
  return { count: parts.length, spanX, spanY };
}

export async function createDraft(stage) {
  const res = await fetch(`${ORIGIN}/api/stages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Creator-Token': CREATOR_TOKEN },
    body: JSON.stringify(stage),
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error('create failed: ' + JSON.stringify(body));
  return body; // { id, editKey, testUrl, ... }
}
