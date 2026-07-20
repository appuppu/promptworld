#!/usr/bin/env node
// "One stage a day" — the daily deploy pipeline.
//
// Picks TODAY'S stage to hand you for clearing + publishing (+ X post):
//   1. If daily-queue.md has queued draft IDs, take the top one (existing-first).
//   2. Otherwise, invent + generate a fresh stage via the local Claude CLI.
// Either way it prints the testUrl and writes a report. It does NOT publish —
// you clear it in the browser and publish (the human-clear gate is mandatory).
//
// The queue entry it used is left in place with a ✔ note; you move it to
// 公開済み after you actually publish (or let it stay — it won't be re-picked
// because published stages are filtered out).
//
// Run: node scripts/daily-courses/daily-one.mjs
// Env: PW_ORIGIN (default https://promptworldgame.org)

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  REPO, ORIGIN, getToolbox, askClaudeAsync, extractJson, solvable,
  stageStats, createDraft,
} from './lib.mjs';

const QUEUE = resolve(REPO, 'daily-queue.md');
const REPORT_DIR = resolve(REPO, 'scripts/daily-courses/reports');
const PLAY_ORIGIN = ORIGIN; // where the human plays; matches the deployed site

// ---- queue parsing ------------------------------------------------------
function readQueue() {
  const md = readFileSync(QUEUE, 'utf8');
  const lines = md.split('\n');
  const qHead = lines.findIndex(l => l.trim() === '## queue');
  const dHead = lines.findIndex((l, i) => i > qHead && l.trim() === '## 公開済み');
  const body = lines.slice(qHead + 1, dHead === -1 ? lines.length : dHead);
  // Each queued line: "- <id>  # note"
  const ids = body
    .filter(l => l.trim().startsWith('- '))
    .map(l => {
      const m = l.trim().slice(2).trim().match(/^([A-Za-z0-9]+)/);
      return m ? m[1] : null;
    })
    .filter(Boolean);
  return { md, lines, qHead, dHead, ids };
}

// Is this draft still a valid, unpublished stage we can hand out?
// Retries a couple of times so a transient network/DNS hiccup doesn't wrongly
// skip a perfectly good queued draft.
async function draftUsable(id) {
  for (let attempt = 0; attempt < 3; attempt++) {
    let res;
    try { res = await fetch(`${PLAY_ORIGIN}/api/stages/${id}`); }
    catch { res = null; }
    if (res) {
      if (res.status === 404) return null;          // genuinely gone
      if (res.ok) {
        const stage = await res.json().catch(() => null);
        if (stage && stage.parts) return stage;
        return null;
      }
    }
    // transient (network error / 5xx): brief backoff, then retry
    await new Promise(r => setTimeout(r, 400 * (attempt + 1)));
  }
  return null; // gave up after retries — treat as unusable, try next in queue
}

// Fetch edit key from the local DB is not available here; the report just needs
// the testUrl. The edit key is stored when the draft was created — we can't
// reconstruct it, so for queued (already-created) drafts the human uses the
// testUrl already recorded in course-drafts.md. We still surface the play URL.
function playUrl(id) { return `${PLAY_ORIGIN}/?stage=${id}`; }

// ---- new-stage generation (queue empty) ---------------------------------
const HOOKS = [
  'a long horizontal cannon-dodging dash',
  'a tall vertical climb with jump pads and movers',
  'a floating gravity-flip journey with little floor',
  'a precise timing run over timedGate and crumble tiles',
  'a key-and-door hunt with a changed return path',
  'a conveyor-belt momentum puzzle',
  'a crusher (faller) rhythm gauntlet',
  'a descent down a tall shaft threading hazards',
];

function buildPrompt(hook, avoid) {
  const toolbox = getToolbox();
  const avoidTxt = avoid.length ? avoid.slice(0, 20).map(a => `- ${a}`).join('\n') : '(none)';
  return [
    'You are designing ONE Prompt World stage as a single JSON document.',
    'Follow this toolbox EXACTLY (schema v0.3):', '', toolbox, '',
    `Invent a fresh, fun stage themed around: ${hook}.`,
    'Give it a punchy ENGLISH name (2-3 words). Avoid duplicating:',
    avoidTxt, '',
    'LENGTH IS MANDATORY (a short stage is a FAILURE):',
    '- Use 45-90 parts. Chain 4-6 escalating sections.',
    '- Horizontal: goal.x - playerStart.x >= 140. Vertical: span >= 70 tall.',
    '- timeLimit 150-240s.',
    'Rules: lives 5; zoom 7-10; exactly one goal; physically clearable by a human',
    '(gaps <=5 units, flip blocks reachable at jump apex ~3.3, crushers whose dy',
    'reaches the floor). Output ONLY the JSON, no prose.',
  ].join('\n');
}

// Pick a hook by day-of-run without Date (unavailable): use queue length as a
// rotating index so successive empty-queue days vary the theme.
async function generateNew(seedIdx, avoid) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const hook = HOOKS[(seedIdx + attempt) % HOOKS.length];
    const r = await askClaudeAsync(buildPrompt(hook, avoid));
    if (r.err) continue;
    const stage = extractJson(r.out);
    if (!stage || !stage.goal || !Array.isArray(stage.parts)) continue;
    const st = stageStats(stage);
    if (st.count < 40) continue; // too small, retry
    let solverOk = false;
    try { solverOk = solvable(stage, 1500); } catch {}
    try {
      const draft = await createDraft(stage);
      return { draft, stage, solverOk, st };
    } catch {}
  }
  return null;
}

// Mark a queue line with a ✔ handled note (so you can see what was served).
function annotateQueue(id) {
  const { lines } = readQueue();
  const idx = lines.findIndex(l => l.trim().startsWith('- ') && l.includes(id));
  if (idx !== -1 && !lines[idx].includes('✔ served')) {
    lines[idx] = lines[idx] + '  ✔ served';
    writeFileSync(QUEUE, lines.join('\n'));
  }
}

// Record a freshly generated stage at the END of the queue section (so it is
// tracked but doesn't jump ahead of the human's curated order). Inserted just
// before the "## 公開済み" header, or at the queue section's end.
function pushQueue(id, name) {
  const { lines, qHead, dHead } = readQueue();
  if (qHead === -1) return;
  const insertAt = dHead !== -1 ? dHead : lines.length;
  lines.splice(insertAt, 0, `- ${id}  # ${name} (auto-generated) ✔ served`);
  writeFileSync(QUEUE, lines.join('\n'));
}

// ---- main ---------------------------------------------------------------
async function main() {
  const { ids } = readQueue();
  let chosen = null;

  // 1. existing-first: take the first queued draft that is still usable and
  //    not already served today.
  const { lines: qlines } = readQueue();
  for (const id of ids) {
    const line = qlines.find(l => l.includes(id));
    if (line && line.includes('✔ served')) continue; // already handed out before
    const stage = await draftUsable(id);
    if (stage) { chosen = { id, name: stage.name, fromQueue: true }; break; }
  }

  // 2. queue empty/exhausted: generate a fresh one.
  if (!chosen) {
    const avoid = ids; // avoid names of queued items (best effort)
    const gen = await generateNew(ids.length, avoid);
    if (gen) {
      chosen = { id: gen.draft.id, name: gen.stage.name, testUrl: gen.draft.testUrl,
                 fromQueue: false, solverOk: gen.solverOk, st: gen.st };
      pushQueue(gen.draft.id, gen.stage.name);
    }
  } else {
    annotateQueue(chosen.id);
  }

  // ---- report ----------------------------------------------------------
  mkdirSync(REPORT_DIR, { recursive: true });
  const out = ['# 今日の1ステージ (clear it → publish → post on X)', ''];
  if (!chosen) {
    out.push('❌ Could not pick or generate a stage today. Check the queue / Claude CLI.');
  } else {
    out.push(`Stage: ${chosen.name}`);
    out.push(`Source: ${chosen.fromQueue ? 'existing draft (queue)' : 'newly generated'}`);
    if (chosen.st) out.push(`Size: ${chosen.st.count} parts, spanX ${chosen.st.spanX.toFixed(0)}, spanY ${chosen.st.spanY.toFixed(0)}`);
    if (chosen.solverOk !== undefined) out.push(`Solver: ${chosen.solverOk ? '✅ cleared' : '⚠️ could not clear (verify playable)'}`);
    out.push('');
    out.push(`PLAY & CLEAR: ${chosen.testUrl || playUrl(chosen.id)}`);
    out.push('');
    out.push('Then: confirm the (English) name → I publish it → post the play URL + a clip on X.');
    out.push(`X caption: I built "${chosen.name}" in Prompt World — can you clear it? ${playUrl(chosen.id)} #PromptWorld`);
  }
  const report = out.join('\n');
  writeFileSync(resolve(REPORT_DIR, 'today.md'), report);
  console.log(report);
}

main().catch(e => { console.error(e); process.exit(1); });
