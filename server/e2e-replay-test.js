// End-to-end replay-certificate test. Run from repo root:
//   cat server/sim.js server/e2e-replay-test.js | node -
// 1. creates a short test stage via the live API
// 2. bot-solves it locally with the deterministic sim (hold right)
// 3. submits the winning replay -> must be verified
// 4. submits a losing replay   -> must be rejected
// 5. publishes, then cleans the test stage out of the database (manual step printed)

const ORIGIN = 'https://promptworld.pages.dev';

const stage = {
  schemaVersion: '0.3',
  name: 'E2E Replay Test',
  timeLimit: 10,
  playerStart: { x: -2, y: -2.5 },
  goal: { x: 2, y: -2.3, w: 1.4, h: 2.6 },
  parts: [{ type: 'solid', x: 0, y: -4, w: 12, h: 1 }],
};

function solveHoldRight(stageData, maxTicks) {
  const world = SimWorld.fromStage(stageData);
  for (let t = 0; t < maxTicks; t++) {
    const ev = world.step({ left: false, right: true, jump: false });
    if (ev.cleared) return { cleared: true, ticks: world.tickCount };
    if (ev.timedOut) break;
  }
  return { cleared: false };
}

(async () => {
  // 1. create
  const created = await (await fetch(`${ORIGIN}/api/stages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(stage),
  })).json();
  console.log('created:', created.id);

  // 2. bot-solve locally
  const solved = solveHoldRight(stage, 500);
  if (!solved.cleared) { console.log('FAIL: bot could not solve'); return; }
  console.log(`bot solved in ${solved.ticks} ticks (${solved.ticks * 20}ms)`);
  const winningReplay = { v: 1, ticks: solved.ticks, rle: [2, solved.ticks] };

  // 3. start a test session (GET the stage), then submit the winning replay
  await fetch(`${ORIGIN}/api/stages/${created.id}`);
  const clearRes = await fetch(`${ORIGIN}/api/stages/${created.id}/clear`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ editKey: created.editKey, clearTimeMs: solved.ticks * 20, replay: winningReplay }),
  });
  console.log('winning replay ->', clearRes.status, JSON.stringify(await clearRes.json()));

  // 4. losing replay (hold LEFT — walks away from the goal) must be rejected
  const badRes = await fetch(`${ORIGIN}/api/stages/${created.id}/clear`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ editKey: created.editKey, clearTimeMs: 1000, replay: { v: 1, ticks: 50, rle: [1, 50] } }),
  });
  console.log('losing replay  ->', badRes.status, JSON.stringify(await badRes.json()));

  // 5. publish (should succeed thanks to the verified clear)
  const pubRes = await fetch(`${ORIGIN}/api/stages/${created.id}/publish`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ editKey: created.editKey }),
  });
  console.log('publish        ->', pubRes.status, JSON.stringify(await pubRes.json()));

  console.log(`\ncleanup: npx wrangler d1 execute promptworld-stages --remote -y --command "DELETE FROM stages WHERE id='${created.id}'"`);
})();
