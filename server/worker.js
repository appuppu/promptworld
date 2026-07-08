// Prompt World API — runs as a Pages Functions advanced-mode worker
// (_worker.js) on the same origin as the WebGL player.
//
// Creator flow (the clear-gate is the core rule of the platform):
//   POST /api/stages                     -> create draft, returns id + editKey + URLs
//   GET  /api/stages/:id                 -> stage JSON (draft or published)
//   POST /api/stages/:id/clear          -> record a browser clear (editKey required)
//   POST /api/stages/:id/publish        -> ONLY allowed after a clear is recorded
//   GET  /api/stages                     -> list of published stages
//
// Trust model v1: the clear event is reported by the game client during a
// test session. v2 will replace this with an input-replay certificate that
// the server re-simulates deterministically (unforgeable).

const LIMITS = {
  minTimeLimit: 5,
  maxTimeLimit: 1800, // no 30-minute-plus stages
  maxParts: 300,
  maxCoord: 500,
  minSize: 0.05,
  maxSize: 100,
  maxPower: 60,
  minPeriod: 0.5,
  maxPeriod: 30,
  maxJsonBytes: 256 * 1024,
};

const KNOWN_TYPES = new Set([
  'solid', 'hazard', 'jumpPad', 'boost', 'gravityFlip', 'movingPlatform', 'crumble',
]);

const SUPPORTED_VERSIONS = new Set(['0.2', '0.3']);

function validateStage(data) {
  const errors = [];
  const inWorld = (x, y) =>
    x >= -LIMITS.maxCoord && x <= LIMITS.maxCoord && y >= -LIMITS.maxCoord && y <= LIMITS.maxCoord;
  const sizeOk = (w, h) =>
    w >= LIMITS.minSize && w <= LIMITS.maxSize && h >= LIMITS.minSize && h <= LIMITS.maxSize;

  if (!data || typeof data !== 'object') return ['Stage JSON could not be parsed.'];
  if (!SUPPORTED_VERSIONS.has(data.schemaVersion)) errors.push(`Unsupported schemaVersion '${data.schemaVersion}'.`);
  if (typeof data.name !== 'string' || data.name.length < 1 || data.name.length > 60)
    errors.push('name must be a string of 1-60 characters.');
  if (typeof data.timeLimit !== 'number' || data.timeLimit < LIMITS.minTimeLimit || data.timeLimit > LIMITS.maxTimeLimit)
    errors.push(`timeLimit must be within [${LIMITS.minTimeLimit}, ${LIMITS.maxTimeLimit}] seconds.`);
  if (!data.playerStart || typeof data.playerStart.x !== 'number' || typeof data.playerStart.y !== 'number')
    errors.push('playerStart is required.');
  else if (!inWorld(data.playerStart.x, data.playerStart.y)) errors.push('playerStart is outside the world bounds.');
  if (!data.goal) errors.push('goal is required.');
  else {
    if (!inWorld(data.goal.x, data.goal.y)) errors.push('goal is outside the world bounds.');
    if (!sizeOk(data.goal.w, data.goal.h)) errors.push('goal size is out of range.');
  }
  if (!Array.isArray(data.parts) || data.parts.length === 0) {
    errors.push('At least one part is required.');
    return errors;
  }
  if (data.parts.length > LIMITS.maxParts) errors.push(`${data.parts.length} parts exceeds the maximum of ${LIMITS.maxParts}.`);

  data.parts.forEach((p, i) => {
    const label = `parts[${i}] (${p && p.type})`;
    if (!p || !KNOWN_TYPES.has(p.type)) { errors.push(`${label}: unknown type.`); return; }
    if (typeof p.x !== 'number' || typeof p.y !== 'number' || !inWorld(p.x, p.y)) errors.push(`${label}: outside the world bounds.`);
    if (typeof p.w !== 'number' || typeof p.h !== 'number' || !sizeOk(p.w, p.h)) errors.push(`${label}: size out of range.`);
    if ((p.type === 'jumpPad' || p.type === 'boost') && p.power !== undefined &&
        (typeof p.power !== 'number' || p.power < 0 || p.power > LIMITS.maxPower))
      errors.push(`${label}: power exceeds the maximum of ${LIMITS.maxPower}.`);
    if (p.type === 'movingPlatform' && p.period !== undefined && p.period !== 0 &&
        (typeof p.period !== 'number' || p.period < LIMITS.minPeriod || p.period > LIMITS.maxPeriod))
      errors.push(`${label}: period must be within [${LIMITS.minPeriod}, ${LIMITS.maxPeriod}] seconds.`);
  });

  return errors;
}

function newId() {
  const alphabet = 'abcdefghijklmnopqrstuvwxyz0123456789';
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  let id = '';
  for (const b of bytes) id += alphabet[b % alphabet.length];
  return id;
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

async function handleApi(request, env, url) {
  const path = url.pathname.replace(/\/+$/, '');
  const method = request.method;

  // POST /api/stages — create a draft
  if (path === '/api/stages' && method === 'POST') {
    const raw = await request.text();
    if (raw.length > LIMITS.maxJsonBytes) return json({ error: 'Stage JSON too large.' }, 413);
    let stage;
    try { stage = JSON.parse(raw); } catch { return json({ error: 'Invalid JSON.' }, 400); }

    const errors = validateStage(stage);
    if (errors.length > 0) return json({ error: 'Validation failed.', details: errors }, 422);

    const id = newId();
    const editKey = crypto.randomUUID();
    stage.id = id;
    await env.promptworld_stages
      .prepare('INSERT INTO stages (id, json, name, status, edit_key, created_at) VALUES (?, ?, ?, ?, ?, ?)')
      .bind(id, JSON.stringify(stage), stage.name, 'draft', editKey, new Date().toISOString())
      .run();

    return json({
      id,
      editKey,
      testUrl: `${url.origin}/?stage=${id}&key=${editKey}`,
      playUrl: `${url.origin}/?stage=${id}`,
      status: 'draft',
      note: 'Clear the stage at testUrl in the browser, then POST /publish. Publishing is blocked until a clear is recorded.',
    }, 201);
  }

  // GET /api/stages — list published stages
  if (path === '/api/stages' && method === 'GET') {
    const rows = await env.promptworld_stages
      .prepare("SELECT id, name, published_at FROM stages WHERE status = 'published' ORDER BY published_at DESC LIMIT 100")
      .all();
    return json({ stages: rows.results });
  }

  const stageMatch = path.match(/^\/api\/stages\/([a-z0-9]+)(\/(clear|publish))?$/);
  if (!stageMatch) return json({ error: 'Not found.' }, 404);
  const id = stageMatch[1];
  const action = stageMatch[3];

  const row = await env.promptworld_stages
    .prepare('SELECT * FROM stages WHERE id = ?').bind(id).first();
  if (!row) return json({ error: 'Stage not found.' }, 404);

  // GET /api/stages/:id — fetch stage JSON for the player
  if (!action && method === 'GET') {
    return new Response(row.json, { headers: { 'Content-Type': 'application/json' } });
  }

  if (method !== 'POST') return json({ error: 'Method not allowed.' }, 405);
  let body = {};
  try { body = await request.json(); } catch { /* empty body ok for error message below */ }
  if (body.editKey !== row.edit_key) return json({ error: 'Invalid editKey.' }, 403);

  // POST /api/stages/:id/clear — record a creator test clear
  if (action === 'clear') {
    const stage = JSON.parse(row.json);
    const ms = Number(body.clearTimeMs);
    if (!Number.isFinite(ms) || ms <= 0 || ms > stage.timeLimit * 1000) {
      return json({ error: 'clearTimeMs must be within the stage time limit.' }, 422);
    }
    await env.promptworld_stages
      .prepare('UPDATE stages SET cleared_at = ?, clear_time_ms = ? WHERE id = ?')
      .bind(new Date().toISOString(), Math.round(ms), id)
      .run();
    return json({ id, cleared: true, clearTimeMs: Math.round(ms), note: 'Clear recorded. The stage can now be published.' });
  }

  // POST /api/stages/:id/publish — THE GATE: no clear, no publish.
  if (action === 'publish') {
    if (!row.cleared_at) {
      return json({
        error: 'Publish blocked: no clear has been recorded for this stage.',
        rule: 'A stage can only be published after its creator has cleared it in the browser within the time limit.',
        testUrl: `${url.origin}/?stage=${id}&key=${row.edit_key}`,
      }, 403);
    }
    await env.promptworld_stages
      .prepare("UPDATE stages SET status = 'published', published_at = ? WHERE id = ?")
      .bind(new Date().toISOString(), id)
      .run();
    return json({ id, status: 'published', playUrl: `${url.origin}/?stage=${id}` });
  }

  return json({ error: 'Not found.' }, 404);
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname.startsWith('/api/')) {
      try {
        return await handleApi(request, env, url);
      } catch (err) {
        return json({ error: 'Internal error.', detail: String(err) }, 500);
      }
    }
    return env.ASSETS.fetch(request);
  },
};
