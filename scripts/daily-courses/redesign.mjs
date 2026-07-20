#!/usr/bin/env node
// Targeted rebuild of specific courses from hand-written design briefs.
// Each brief carries the director's intent (from user feedback). We ask Claude
// to build a LONG stage to that brief, enforce a minimum size, solver-check as
// a hint, and create a fresh DRAFT. Prints testUrls. Does NOT publish/deploy.
//
// Run: node scripts/daily-courses/redesign.mjs

import { getToolbox, askClaudeAsync, extractJson, solvable, stageStats, createDraft, pool } from './lib.mjs';

const toolbox = getToolbox();

// Each brief: {key, name, primary, minParts, minSpanX, minSpanY, brief}
// Names are ENGLISH per the director's request. These incorporate direct
// player feedback (v2 tuning) on the earlier drafts.
const BRIEFS = [
  {
    key: 'cannon-bridge-v2',
    name: 'Cannon Bridge',
    primary: 'x', minParts: 50, minSpanX: 150, minSpanY: 12,
    brief: [
      'Concept: a long horizontal cannon-timing bridge run — HARD but FAIR.',
      'FEEDBACK FIX (important): do NOT stack two cannons vertically at the same x.',
      'Each firing lane must have only ONE cannon, so the player has a single bullet',
      'stream to time per spot, not two overlapping ones. Space cannons out along the',
      'course at DIFFERENT x positions and varied heights instead of doubling up.',
      'RULES:',
      '- Single cannons spread along the run, staggered phases (dx offsets), periods',
      '  ~1.4-2.2s so there is a clear, readable gap to run through each one.',
      '- The walkway mixes solid ledges with a few timedGate slabs / crumble tiles so',
      '  there is rhythm, but keep it readable — one hazard idea at a time.',
      '- 4-5 escalating sections; goal.x - playerStart.x >= 150; timeLimit 160-220s.',
    ].join('\n'),
  },
  {
    key: 'key-roundtrip-v2',
    name: 'Key Round Trip',
    primary: 'x', minParts: 50, minSpanX: 150, minSpanY: 14,
    brief: [
      'Concept: a long there-and-back KEY course. Travel far right to grab a key,',
      'then return left to a door near the start; the door opens once the key is taken.',
      'Note: the engine now RETURNS the key to the stage if the player dies, so dying',
      'after grabbing it simply means re-collecting it — design around that (no need',
      'for checkpoints). Keep the RETURN trip challenging but forgiving: mostly same',
      'height as the outbound path, a reverse-running conveyor as a speed obstacle,',
      'and a few well-spaced learnable hazards — not a wall of spikes.',
      'RULES:',
      '- Outbound (left->right): a fun traverse to a key at the far right.',
      '- Return (right->left): reverse conveyor + a few new dodges; door near start.',
      '- Outbound span >= 75 units (round trip ~150). timeLimit 200-260s, lives 5.',
    ].join('\n'),
  },
  {
    key: 'drop-shaft-v2',
    name: 'Drop Shaft',
    primary: 'y', minParts: 50, minSpanX: 12, minSpanY: 80,
    brief: [
      'Concept: a tall vertical drop shaft — the player falls down, steering through',
      'gaps between hazards. EASIER than the previous version per feedback.',
      'FEEDBACK FIXES (important):',
      '- Give GENEROUS landing room: after each hazard band there must be a wide safe',
      '  ledge/floor (at least 4-5 units of clear standing space, NOT a thin sliver',
      '  right next to spikes). The player must be able to land and breathe.',
      '- Keep spikes and the safe gap well separated — the gap to fall through should',
      '  be WIDE (>= 4 units), not a pixel-perfect slot.',
      '- Do NOT stack punishing sections back to back; every 1-2 hazard bands, insert',
      '  a full rest ledge. Around the 4th-6th descent especially, ease off — no',
      '  brutal double hazards there.',
      '- Vertical span >= 80 units; zoom 8-9; timeLimit 120-180s; lives 5.',
    ].join('\n'),
  },
  {
    key: 'stairs-order-v2',
    name: 'Rising Stairs',
    primary: 'x', minParts: 45, minSpanX: 120, minSpanY: 20,
    brief: [
      'Concept: a climb over a staircase of timedGate steps that appear and vanish.',
      'FEEDBACK FIX (critical): the vanishing steps must be climbable in physical',
      'order — arrange them so LOWER steps are solid when the player needs them and',
      'the sequence lets you actually ascend. Use timedGate PHASE offsets (dx) so the',
      'bottom step is present first, then the next one up becomes present as you climb,',
      'like a wave rising from the bottom. Tune each step\'s dx to the time it takes to',
      'run+jump to it (player run speed 8 u/s, jump apex ~3.3), so the rhythm of the',
      'gates MATCHES the climb — a step is solid exactly when the player arrives.',
      'RULES:',
      '- A staircase rising left-to-right; each higher step\'s dx is offset later so it',
      '  materializes just as the player reaches it. Period ~2-3s, present-half long',
      '  enough to stand and jump.',
      '- Add solid safe landings between staircase sections. Fair and readable.',
      '- goal.x - playerStart.x >= 120; timeLimit 150-210s; lives 5.',
    ].join('\n'),
  },
  {
    key: 'conveyor-boost-v2',
    name: 'Runaway Belts',
    primary: 'x', minParts: 45, minSpanX: 150, minSpanY: 10,
    brief: [
      'Concept: a fast horizontal run over conveyor belts and boost strips.',
      'FEEDBACK FIX (critical): every boost/launch that flings the player right MUST',
      'land them on a REACHABLE next platform. Verify the physics: a boost of power P',
      'carries ~0.6*P units horizontally; the landing platform must be within that',
      'arc AND at a reachable height (not above jump apex ~3.3 from the boost path).',
      'Never place the next foothold beyond where the boost can carry the player.',
      'RULES:',
      '- Chain conveyor sections (some with the belt against you) and boost strips,',
      '  each boost clearly landing on a solid platform you can see ahead.',
      '- Keep gaps <= 5 units where the player jumps unaided; use boosts only where',
      '  the landing is confirmed reachable.',
      '- 4-5 sections; goal.x - playerStart.x >= 150; timeLimit 150-210s; lives 5.',
    ].join('\n'),
  },
];

function buildPrompt(b, retryNote) {
  return [
    'You are designing ONE Prompt World stage as a single JSON document.',
    'Follow this toolbox EXACTLY (schema v0.3):', '', toolbox, '',
    `DESIGN BRIEF — name it "${b.name}" (English name; you may refine it but keep it ENGLISH):`,
    b.brief, '',
    'LENGTH IS MANDATORY:',
    `- Use at least ${b.minParts} parts. A short stage is a FAILURE.`,
    b.minSpanX >= 100 ? `- Horizontal span >= ${b.minSpanX} units (goal far from start).` : '',
    b.minSpanY >= 60 ? `- Vertical span >= ${b.minSpanY} units.` : '',
    '- Chain multiple escalating sections into one continuous journey.',
    'Rules: lives 5; sensible zoom (7-10); exactly one goal; physically clearable',
    'by a human (gaps <=5 units, flip blocks reachable at jump apex ~3.3, crushers',
    'whose dy reaches the floor).',
    retryNote || '',
    'Output ONLY the JSON, no prose.',
  ].filter(Boolean).join('\n');
}

// Size gate: require enough parts AND that the brief's PRIMARY axis is long
// enough. A horizontal float course can be short vertically and vice-versa, so
// we don't demand both axes — only the one that defines the stage's shape.
function tooSmall(b, st) {
  const primaryOk = b.primary === 'y' ? st.spanY >= b.minSpanY : st.spanX >= b.minSpanX;
  if (st.count < b.minParts) return `parts ${st.count} < ${b.minParts}`;
  if (!primaryOk) return b.primary === 'y'
    ? `spanY ${st.spanY.toFixed(0)} < ${b.minSpanY}`
    : `spanX ${st.spanX.toFixed(0)} < ${b.minSpanX}`;
  return null;
}

async function main() {
  // Generate all briefs in parallel (5-way via pool inside askClaudeAsync fan-out).
  const results = await pool(BRIEFS, 5, async (b) => {
    let stage = null, note = '', lastErr = '';
    for (let attempt = 1; attempt <= 3 && !stage; attempt++) {
      const r = await askClaudeAsync(buildPrompt(b, note));
      if (r.err) { lastErr = 'claude: ' + r.err; continue; }
      const s = extractJson(r.out);
      if (!s || !s.goal || !Array.isArray(s.parts)) { lastErr = 'bad JSON'; note = 'Previous output was invalid. Return STRICT JSON only.'; continue; }
      const st = stageStats(s);
      const small = tooSmall(b, st);
      if (small) {
        lastErr = 'too small (' + small + ')';
        note = `Your previous stage was TOO SMALL (${small}). Make it BIGGER: >=${b.minParts} parts, ${b.primary === 'y' ? 'vertical span >= ' + b.minSpanY : 'horizontal span >= ' + b.minSpanX}. Add more sections.`;
        continue;
      }
      stage = s;
    }
    if (!stage) return { b, failed: true, lastErr };

    let solverOk = false;
    try { solverOk = solvable(stage); } catch {}
    try {
      const draft = await createDraft(stage);
      return { b, stage, draft, solverOk, st: stageStats(stage) };
    } catch (e) { return { b, failed: true, lastErr: e.message }; }
  });

  console.log('\n===== REDESIGNED COURSES =====\n');
  for (const r of results) {
    if (r.failed) { console.log(`❌ ${r.b.name} — ${r.lastErr}\n`); continue; }
    const conf = r.solverOk ? '✅ solver cleared' : '⚠️ solver could NOT clear (please verify)';
    console.log(`${r.stage.name}  (${conf})`);
    console.log(`  size: ${r.st.count} parts, spanX ${r.st.spanX.toFixed(0)}, spanY ${r.st.spanY.toFixed(0)}`);
    console.log(`  ${r.draft.testUrl}\n`);
  }
}

main().catch(e => { console.error(e); process.exit(1); });
