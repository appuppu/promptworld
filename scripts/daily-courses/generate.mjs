#!/usr/bin/env node
// Daily course generator for Prompt World.
//
// Reads course-ideas.md, takes the top N un-implemented ideas, asks the local
// Claude CLI (NOT the API) to design each as a stage JSON, verifies each is
// actually solvable with the local solver, creates a DRAFT via the REST API,
// and writes a report the user can act on. Implemented ideas are moved to the
// "実装済み" section so they don't repeat. It does NOT publish or deploy — the
// human must clear each testUrl to publish, per platform policy.
//
// Run: node scripts/daily-courses/generate.mjs [count]
// Env: PW_ORIGIN (default https://promptworldgame.org)
//      PW_CREATOR_TOKEN (reuse one creator identity)

import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO = resolve(__dirname, '../..');
const IDEAS = resolve(REPO, 'course-ideas.md');
const REPORT_DIR = resolve(REPO, 'scripts/daily-courses/reports');
const ORIGIN = process.env.PW_ORIGIN || 'https://promptworldgame.org';
const CREATOR_TOKEN = process.env.PW_CREATOR_TOKEN || '770122b7-11d9-4d45-869d-d71292b60e5d';
const COUNT = parseInt(process.argv[2] || '2', 10);
const MAX_ATTEMPTS = 2; // Claude retries per idea if the JSON is malformed / solver fails

const require = createRequire(import.meta.url);

// ---- read ideas ---------------------------------------------------------
function readIdeas() {
  const md = readFileSync(IDEAS, 'utf8');
  const lines = md.split('\n');
  const start = lines.findIndex(l => l.trim() === '## 未実装');
  const doneHeader = lines.findIndex(l => l.trim() === '## 実装済み');
  const body = lines.slice(start + 1, doneHeader === -1 ? lines.length : doneHeader);
  const ideas = body.filter(l => l.trim().startsWith('- ')).map(l => l.trim().slice(2).trim());
  // Implemented ideas are stored as "- [stageId] text"; strip the id for the avoid-list.
  const doneBody = doneHeader === -1 ? [] : lines.slice(doneHeader + 1);
  const done = doneBody.filter(l => l.trim().startsWith('- '))
    .map(l => l.trim().slice(2).trim().replace(/^\[[^\]]*\]\s*/, ''));
  return { md, lines, start, doneHeader, ideas, done };
}

// Record an implemented idea in 実装済み. If the idea came from the user's
// 未実装 list, remove it there; if it was auto-invented, just append it.
function markImplemented(ideaText, stageId) {
  const { lines } = readIdeas();
  const idx = lines.findIndex(l => l.trim() === `- ${ideaText}`);
  if (idx !== -1) lines.splice(idx, 1);
  const doneHeader = lines.findIndex(l => l.trim() === '## 実装済み');
  const stamp = `- [${stageId}] ${ideaText}`;
  if (doneHeader !== -1) lines.splice(doneHeader + 1, 0, stamp);
  else { lines.push('', '## 実装済み', stamp); }
  writeFileSync(IDEAS, lines.join('\n'));
}

// ---- toolbox (the exact doc the MCP server hands creators) --------------
// It lives as a template literal `const TOOLBOX_DOC = \`...\`;` in worker.js —
// the same text get_toolbox returns. Extract it verbatim so the local Claude
// designs against the identical schema the server validates.
function getToolbox() {
  const src = readFileSync(resolve(REPO, 'server/worker.js'), 'utf8');
  const m = src.match(/const TOOLBOX_DOC = `([\s\S]*?)`;/);
  if (m) return m[1];
  return 'Prompt World stage JSON v0.3. Parts: solid, hazard, jumpPad, boost, launcher, cannon, gravityFlip, gravitySet, movingPlatform, crumble, faller, conveyor, timedGate, key, door.';
}

// ---- ask the local Claude CLI to design one stage -----------------------
function askClaude(prompt) {
  // Non-interactive: `claude -p "<prompt>"` prints the answer and exits.
  const out = execFileSync('claude', ['-p', prompt], {
    encoding: 'utf8', maxBuffer: 8 * 1024 * 1024, timeout: 180000,
  });
  return out;
}

// Ask Claude to brainstorm fresh course concepts (used when the user hasn't
// queued enough ideas). Returns an array of one-line idea strings.
function inventIdeas(n, avoid) {
  const avoidList = avoid.length ? avoid.map(a => `- ${a}`).join('\n') : '(none yet)';
  const prompt = [
    `Brainstorm ${n} NEW course ideas for Prompt World, a black & white 2D physics platformer.`,
    'Each idea is ONE Japanese line describing a distinct, fun, clearable stage concept.',
    'Vary the hook: some horizontal, some vertical climbs, some gravity puzzles, some',
    'timing/dodging (cannons, crushers, moving platforms, conveyors), some key+door.',
    'Aim for a clear "one wow moment" each. Avoid duplicating these already-made ideas:',
    avoidList,
    '',
    `Output EXACTLY ${n} lines, each starting with "- ", no numbering, no prose, no headers.`,
  ].join('\n');
  let text;
  try { text = askClaude(prompt); } catch { return []; }
  return text.split('\n')
    .map(l => l.trim())
    .filter(l => l.startsWith('- '))
    .map(l => l.slice(2).trim())
    .filter(Boolean)
    .slice(0, n);
}

function extractJson(text) {
  // Grab the first {...} block that parses as a stage.
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

// ---- solver (repo copy) -------------------------------------------------
function solvable(stage) {
  // Load the solver's SimWorld and run the greedy search inline.
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
  const maxTicks = Math.min((stage.timeLimit || 90) * 50, 6000);

  const episodes = 2500;
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

// ---- create draft via REST ----------------------------------------------
async function createDraft(stage) {
  const res = await fetch(`${ORIGIN}/api/stages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Creator-Token': CREATOR_TOKEN },
    body: JSON.stringify(stage),
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error('create failed: ' + JSON.stringify(body));
  return body; // { id, editKey, testUrl, ... }
}

// ---- main ----------------------------------------------------------------
async function main() {
  const { ideas, done } = readIdeas();
  // Prefer the user's queued ideas; if there aren't COUNT of them, let Claude
  // invent the rest so the generator self-drives even with an empty draft file.
  const picked = ideas.slice(0, COUNT);
  if (picked.length < COUNT) {
    const need = COUNT - picked.length;
    console.log(`Only ${picked.length} queued idea(s); inventing ${need} more…`);
    const invented = inventIdeas(need, [...done, ...picked]);
    picked.push(...invented);
  }
  if (picked.length === 0) {
    console.log('No ideas available (and idea generation failed) — nothing to do.');
    return;
  }
  const toolbox = getToolbox();
  const results = [];

  for (const idea of picked) {
    // Attempt: get a well-formed stage JSON from Claude (retry only on bad JSON).
    let stage = null, lastErr = '';
    for (let attempt = 1; attempt <= MAX_ATTEMPTS && !stage; attempt++) {
      const prompt = [
        'You are designing ONE Prompt World stage as a single JSON document.',
        'Follow this toolbox EXACTLY (schema v0.3):',
        '', toolbox, '',
        `Design a stage for this idea (idea may be Japanese): "${idea}"`,
        'Give the stage a punchy ENGLISH name (2-3 words), even if the idea is in Japanese.',
        attempt > 1 ? `Previous output was invalid (${lastErr}). Return STRICT JSON only.` : '',
        'MAKE IT LONG AND SUBSTANTIAL — this is the most important rule:',
        '- Use 45-90 parts. Do NOT stop at 15-20; a short stage is a FAILURE.',
        '- Chain 4-6 distinct SECTIONS into one continuous journey, each with its own',
        '  mini-challenge, escalating in difficulty. Give the player real distance to travel.',
        '- Horizontal stages: span at least 140 units wide (goal.x - playerStart.x >= 140).',
        '- Vertical stages: span at least 70 units tall.',
        '- timeLimit generous enough for the length (e.g. 150-240s).',
        'Rules: lives default 5; set a sensible zoom (7-10); exactly one goal;',
        'ensure the path is physically clearable by a human (gaps <=5 units, flip blocks',
        'reachable at jump apex ~3.3, crushers whose dy reaches the floor). Output ONLY the JSON, no prose.',
      ].join('\n');

      let text;
      try { text = askClaude(prompt); }
      catch (e) { lastErr = 'claude cli error: ' + e.message; continue; }
      const s = extractJson(text);
      if (!s) { lastErr = 'no valid JSON in Claude output'; continue; }
      if (!s.goal || !Array.isArray(s.parts)) { lastErr = 'missing goal/parts'; continue; }
      stage = s;
    }

    if (!stage) { results.push({ idea, failed: true, lastErr }); continue; }

    // Solver is a CONFIDENCE HINT, not a gate. The human clear is the real gate,
    // and the greedy solver is weak on vertical climbs — so we still ship the
    // draft, just flagging low confidence so the user knows to scrutinize it.
    let solverOk = false;
    try { solverOk = solvable(stage); } catch (e) { lastErr = 'solver error: ' + e.message; }

    try {
      const draft = await createDraft(stage);
      markImplemented(idea, draft.id);
      results.push({ idea, stage, draft, solverOk });
    } catch (e) {
      results.push({ idea, failed: true, lastErr: e.message });
    }
  }

  // ---- report --------------------------------------------------------
  mkdirSync(REPORT_DIR, { recursive: true });
  const lines = ['# Daily courses — please CLEAR these to publish', ''];
  for (const r of results) {
    if (r.failed) { lines.push(`- ❌ "${r.idea}" — could not generate (${r.lastErr})`); lines.push(''); continue; }
    const conf = r.solverOk ? '✅ solver cleared it' : '⚠️ solver could NOT clear it — please check it is actually beatable';
    lines.push(`- ${r.stage.name || r.idea}  (${conf})`);
    lines.push(`  idea: ${r.idea}`);
    lines.push(`  PLAY & CLEAR: ${r.draft.testUrl}`);
    lines.push('');
  }
  lines.push('Clear each testUrl in the browser, then ask to publish (name-confirmed).');
  lines.push('If a ⚠️ course is unbeatable, tell me and I will redesign it.');
  const report = lines.join('\n');
  const reportPath = resolve(REPORT_DIR, 'latest.md');
  writeFileSync(reportPath, report);
  console.log(report);
  console.log('\nReport written to ' + reportPath);
}

main().catch(e => { console.error(e); process.exit(1); });
