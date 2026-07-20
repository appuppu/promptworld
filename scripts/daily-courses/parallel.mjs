#!/usr/bin/env node
// Parallel course generator. Spawns N Claude CLI processes AT ONCE, each
// designing a distinct LONG stage with a Claude-invented concept, then
// (as each returns) size-checks, solver-checks (hint), and creates a DRAFT.
// Records each into course-ideas.md 実装済み. Does NOT publish/deploy.
//
// Run: node scripts/daily-courses/parallel.mjs [count] [concurrency]
//   count       how many courses to make (default 10)
//   concurrency how many claude processes at once (default = count)

import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  REPO, getToolbox, extractJson, solvable, stageStats, createDraft,
  askClaudeAsync, pool,
} from './lib.mjs';

const IDEAS = resolve(REPO, 'course-ideas.md');
const COUNT = parseInt(process.argv[2] || '10', 10);
// Default 5-way concurrency: big-stage generation is slow, so don't over-subscribe.
const CONCURRENCY = parseInt(process.argv[3] || '5', 10);
const toolbox = getToolbox();

// Distinct hooks so 10 parallel calls don't converge on the same stage.
const HOOKS = [
  'a long horizontal dash with escalating cannon-dodging',
  'a tall vertical climb using jump pads and moving platforms',
  'a gravity-flip puzzle that floats between mid-air gravitySet blocks (little/no floor)',
  'a precise timing run over timedGate and crumble tiles',
  'a key-and-door hunt where you backtrack through changed hazards',
  'a conveyor-belt momentum puzzle with boosts and gaps',
  'a crusher (faller) gauntlet you slip through on rhythm',
  'a descent down a tall shaft threading shifting hazard gaps',
  'a launcher-trap maze where you weave around deadly upward flings',
  'a mixed gravity + moving-platform ride across a wide canyon',
  'a bounce-heavy jumpPad ascent with hazard rings',
  'a stop-and-go run gated by timed walls and cannon fire',
];

// A tagged list so we can avoid repeating already-made ideas.
function readDone() {
  const md = readFileSync(IDEAS, 'utf8');
  const h = md.indexOf('## 実装済み');
  const body = h === -1 ? '' : md.slice(h);
  return body.split('\n').filter(l => l.trim().startsWith('- '))
    .map(l => l.trim().slice(2).replace(/^\[[^\]]*\]\s*/, ''));
}

function appendDone(stageId, name) {
  const lines = readFileSync(IDEAS, 'utf8').split('\n');
  const h = lines.findIndex(l => l.trim() === '## 実装済み');
  const stamp = `- [${stageId}] ${name}`;
  if (h !== -1) lines.splice(h + 1, 0, stamp);
  else lines.push('', '## 実装済み', stamp);
  writeFileSync(IDEAS, lines.join('\n'));
}

function buildPrompt(i, avoid) {
  const hook = HOOKS[i % HOOKS.length];
  const avoidTxt = avoid.length ? avoid.slice(0, 30).map(a => `- ${a}`).join('\n') : '(none)';
  return [
    'You are designing ONE Prompt World stage as a single JSON document.',
    'Follow this toolbox EXACTLY (schema v0.3):', '', toolbox, '',
    `Invent a fresh, fun stage concept themed around: ${hook}.`,
    'Give it a punchy ENGLISH name (2-3 words). Do NOT duplicate any of these existing ideas:',
    avoidTxt, '',
    'LENGTH IS MANDATORY (a short stage is a FAILURE):',
    '- Use 45-90 parts.',
    '- Chain 4-6 escalating sections into one continuous journey.',
    '- Horizontal stages: goal.x - playerStart.x >= 140. Vertical stages: span >= 70 tall.',
    '- timeLimit generous for the length (150-240s).',
    'Rules: lives 5; sensible zoom (7-10); exactly one goal; physically clearable by a',
    'human (gaps <=5 units, flip blocks reachable at jump apex ~3.3, crushers whose dy',
    'reaches the floor). Output ONLY the JSON, no prose.',
  ].join('\n');
}

async function main() {
  const avoid = readDone();
  const jobs = Array.from({ length: COUNT }, (_, i) => i);
  console.log(`Spawning ${COUNT} courses at concurrency ${CONCURRENCY}…`);

  const results = await pool(jobs, CONCURRENCY, async (i) => {
    const r = await askClaudeAsync(buildPrompt(i, avoid));
    if (r.err) return { i, failed: true, lastErr: 'claude: ' + r.err };
    const stage = extractJson(r.out);
    if (!stage || !stage.goal || !Array.isArray(stage.parts)) return { i, failed: true, lastErr: 'bad JSON' };
    const st = stageStats(stage);
    // Solver as a hint. Lighter episode count so 10 concurrent checks stay sane.
    let solverOk = false;
    try { solverOk = solvable(stage, 1200); } catch {}
    try {
      const draft = await createDraft(stage);
      appendDone(draft.id, stage.name || `parallel-${i}`);
      return { i, stage, draft, solverOk, st };
    } catch (e) { return { i, failed: true, lastErr: e.message }; }
  });

  console.log('\n===== PARALLEL COURSES =====\n');
  let ok = 0;
  for (const r of results) {
    if (!r) continue;
    if (r.failed) { console.log(`❌ #${r.i} — ${r.lastErr}\n`); continue; }
    ok++;
    const conf = r.solverOk ? '✅ solver cleared' : '⚠️ solver could NOT clear (verify)';
    console.log(`${r.stage.name}  (${conf})`);
    console.log(`  size: ${r.st.count} parts, spanX ${r.st.spanX.toFixed(0)}, spanY ${r.st.spanY.toFixed(0)}`);
    console.log(`  ${r.draft.testUrl}\n`);
  }
  console.log(`Done: ${ok}/${COUNT} created.`);
}

main().catch(e => { console.error(e); process.exit(1); });
