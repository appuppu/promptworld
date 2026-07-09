// Prompt World API + MCP server — runs as a Pages Functions advanced-mode
// worker (_worker.js) on the same origin as the WebGL player.
//
// REST (used by the game client):
//   POST /api/stages                -> create draft, returns id + editKey + URLs
//   GET  /api/stages/:id            -> stage JSON (draft fetch starts a test session)
//   POST /api/stages/:id/clear     -> record a browser clear (editKey required)
//   POST /api/stages/:id/publish   -> ONLY allowed after a clear is recorded
//   GET  /api/stages                -> list of published stages
//
// MCP (used by anyone's Claude — Streamable HTTP, JSON-RPC 2.0):
//   POST /mcp  with tools: get_toolbox, create_stage, stage_status,
//              publish_stage, list_stages
//
// Trust model: publish is gated on a recorded clear. Clears are checked
// against real elapsed session time (and soon: replay certificates).

// ---------------------------------------------------------------- validation

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

// ---------------------------------------------------------------- core ops
// Shared by the REST API and the MCP tools.

function newId() {
  const alphabet = 'abcdefghijklmnopqrstuvwxyz0123456789';
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  let id = '';
  for (const b of bytes) id += alphabet[b % alphabet.length];
  return id;
}

// ------------------------------------------------------------ abuse control

const RATE_LIMITS = {
  create: 30,           // drafts per IP per day
  clear: 200,           // clear submissions per IP per day
  publish: 10,          // publishes per IP per day
  publishPerCreator: 5, // publishes per creator identity per day
  publishGlobal: 200,   // publishes per day platform-wide (circuit breaker)
  vote: 300,            // vote writes per IP per day
  score: 300,           // leaderboard submissions per IP per day
  stats: 2000,          // play-stat reports per IP per day
};
const MIN_CLEAR_MS_TO_PUBLISH = 3000; // trivially short stages cannot be published
const DRAFT_TTL_DAYS = 7;             // unpublished drafts are garbage-collected

// Hard capacity ceilings. Stage JSON is a few KB, so even these caps use a
// fraction of the free tier — they exist to bound worst-case flooding, not
// storage cost. Raise them when the platform legitimately grows.
const MAX_TOTAL_STAGES = 20000;
const MAX_PUBLISHED_STAGES = 5000;

async function countStages(env, publishedOnly) {
  const sql = publishedOnly
    ? "SELECT COUNT(*) AS n FROM stages WHERE status = 'published'"
    : 'SELECT COUNT(*) AS n FROM stages';
  const row = await env.promptworld_stages.prepare(sql).first();
  return row ? row.n : 0;
}

async function bumpCounter(env, key) {
  await env.promptworld_stages
    .prepare('INSERT INTO rate_limits (key, count) VALUES (?, 1) ON CONFLICT(key) DO UPDATE SET count = count + 1')
    .bind(key)
    .run();
  const row = await env.promptworld_stages
    .prepare('SELECT count FROM rate_limits WHERE key = ?').bind(key).first();
  return row ? row.count : 1;
}

async function overRateLimit(env, request, action, max) {
  const ip = request.headers.get('CF-Connecting-IP') || '0.0.0.0';
  const day = new Date().toISOString().slice(0, 10);
  const data = new TextEncoder().encode(`${ip}|prompt-world`);
  const digest = await crypto.subtle.digest('SHA-256', data);
  const hash = [...new Uint8Array(digest).slice(0, 8)]
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');
  const count = await bumpCounter(env, `${action}:${hash}:${day}`);
  return count > max;
}

async function cleanupExpiredDrafts(env) {
  const cutoff = new Date(Date.now() - DRAFT_TTL_DAYS * 86400000).toISOString();
  await env.promptworld_stages
    .prepare("DELETE FROM stages WHERE status = 'draft' AND created_at < ?")
    .bind(cutoff)
    .run();
}

// ------------------------------------------------------------ invisible ids
// Creators get a frictionless identity: the first create_stage silently
// mints a creator record and hands back a creatorToken for reuse. No signup,
// no login — but bans and per-creator quotas become enforceable. A real
// login (Google/Firebase) can later become another issuer of the same ids.
// PLAYERS NEVER NEED ANY OF THIS.

async function getCreatorByToken(env, token) {
  if (!token || typeof token !== 'string') return null;
  return env.promptworld_stages.prepare('SELECT * FROM creators WHERE token = ?').bind(token).first();
}

async function getCreatorById(env, id) {
  if (!id) return null;
  return env.promptworld_stages.prepare('SELECT * FROM creators WHERE id = ?').bind(id).first();
}

async function mintCreator(env, name) {
  const token = crypto.randomUUID();
  const id = newId();
  let clean = typeof name === 'string' ? name.trim().slice(0, 30) : '';
  if (clean.length === 0) clean = 'anonymous';
  await env.promptworld_stages
    .prepare('INSERT INTO creators (token, id, name, created_at) VALUES (?, ?, ?, ?)')
    .bind(token, id, clean, new Date().toISOString())
    .run();
  return { token, id, name: clean, banned: 0 };
}

async function opCreateStage(env, origin, stage, request, creatorToken, creatorName) {
  if (await overRateLimit(env, request, 'create', RATE_LIMITS.create)) {
    return { status: 429, body: { error: `Rate limit: at most ${RATE_LIMITS.create} stages may be created per day.` } };
  }
  await cleanupExpiredDrafts(env);
  if (await countStages(env, false) >= MAX_TOTAL_STAGES) {
    return { status: 503, body: { error: 'The platform has reached its stage capacity. Please try again later.' } };
  }

  // Invisible identity: reuse the token if provided, mint silently if not.
  let creator;
  let minted = false;
  if (creatorToken) {
    creator = await getCreatorByToken(env, creatorToken);
    if (!creator) return { status: 401, body: { error: 'Invalid creatorToken.' } };
    if (creator.banned) return { status: 403, body: { error: 'This creator is banned.' } };
  } else {
    creator = await mintCreator(env, creatorName);
    minted = true;
  }

  if (stage && typeof stage.name === 'string') {
    stage.name = stage.name.replace(/[\u0000-\u001f\u007f]/g, '').trim();
  }
  const errors = validateStage(stage);
  if (errors.length > 0) return { status: 422, body: { error: 'Validation failed.', details: errors } };

  const id = newId();
  const editKey = crypto.randomUUID();
  stage.id = id;
  await env.promptworld_stages
    .prepare('INSERT INTO stages (id, json, name, status, edit_key, created_at, creator_id) VALUES (?, ?, ?, ?, ?, ?, ?)')
    .bind(id, JSON.stringify(stage), stage.name, 'draft', editKey, new Date().toISOString(), creator.id)
    .run();

  const body = {
    id,
    editKey,
    testUrl: `${origin}/?stage=${id}&key=${editKey}`,
    playUrl: `${origin}/?stage=${id}`,
    status: 'draft',
    creatorId: creator.id,
    note: 'A human must clear the stage at testUrl in the browser within its time limit. Publishing stays blocked until that clear is recorded.',
  };
  if (minted) {
    body.creatorToken = creator.token;
    body.identityNote = 'A creator identity was minted for you. Pass creatorToken in future create_stage calls to keep all your stages under one identity.';
  }
  return { status: 201, body };
}

async function opGetStage(env, id) {
  return env.promptworld_stages.prepare('SELECT * FROM stages WHERE id = ?').bind(id).first();
}

async function opMarkTestStarted(env, id) {
  await env.promptworld_stages
    .prepare('UPDATE stages SET test_started_at = ? WHERE id = ?')
    .bind(new Date().toISOString(), id)
    .run();
}

async function opRecordClear(env, row, body, request) {
  if (await overRateLimit(env, request, 'clear', RATE_LIMITS.clear)) {
    return { status: 429, body: { error: 'Rate limit: too many clear submissions today.' } };
  }
  const stage = JSON.parse(row.json);
  const limitQ = Math.fround(stage.timeLimit);
  const maxTicks = Math.trunc(limitQ / 0.02);

  // Level 3: a clear only counts if its replay certificate re-simulates to
  // the goal. simRunReplay runs the exact deterministic sim the client used.
  const replay = body.replay;
  if (!replay || replay.v !== 1 || !Array.isArray(replay.rle) ||
      replay.rle.length === 0 || replay.rle.length > 400000) {
    return { status: 422, body: { error: 'A replay certificate is required to record a clear.' } };
  }
  const result = simRunReplay(stage, replay.rle, maxTicks);
  if (!result.cleared) {
    return { status: 422, body: { error: `Replay verification failed: ${result.error || 'goal not reached'}` } };
  }
  const ms = result.ticks * 20; // authoritative: from the verified simulation

  // Defense in depth: the claimed play must also fit real elapsed time.
  if (!row.test_started_at) {
    return { status: 422, body: { error: 'No test session: load the stage via its testUrl first.' } };
  }
  const elapsedMs = Date.now() - Date.parse(row.test_started_at);
  if (elapsedMs < ms - 2000) {
    return { status: 422, body: { error: 'Clear rejected: verified clear time exceeds real elapsed play time.' } };
  }

  await env.promptworld_stages
    .prepare('UPDATE stages SET cleared_at = ?, clear_time_ms = ?, clear_replay = ? WHERE id = ?')
    .bind(new Date().toISOString(), ms, JSON.stringify(replay), row.id)
    .run();
  return {
    status: 200,
    body: { id: row.id, cleared: true, verified: true, clearTimeMs: ms, note: 'Replay verified by server re-simulation. The stage can now be published.' },
  };
}

async function opPublish(env, origin, row, request) {
  if (row.creator_id) {
    const creator = await getCreatorById(env, row.creator_id);
    if (creator && creator.banned) {
      return { status: 403, body: { error: 'This creator is banned.' } };
    }
    if (await overRateLimit(env, request, `publish-creator:${row.creator_id}`, RATE_LIMITS.publishPerCreator)) {
      return { status: 429, body: { error: `Rate limit: at most ${RATE_LIMITS.publishPerCreator} publishes per creator per day.` } };
    }
  }
  if (await overRateLimit(env, request, 'publish', RATE_LIMITS.publish)) {
    return { status: 429, body: { error: `Rate limit: at most ${RATE_LIMITS.publish} stages may be published per day.` } };
  }
  if (await overRateLimit(env, request, 'publish-global:all', RATE_LIMITS.publishGlobal)) {
    return { status: 429, body: { error: 'The platform-wide daily publish limit has been reached. Try again tomorrow.' } };
  }
  if (await countStages(env, true) >= MAX_PUBLISHED_STAGES) {
    return { status: 503, body: { error: 'The published-stage capacity has been reached.' } };
  }
  if (row.clear_time_ms !== null && row.clear_time_ms < MIN_CLEAR_MS_TO_PUBLISH) {
    return {
      status: 422,
      body: { error: `Publish blocked: the verified clear took ${row.clear_time_ms}ms — stages clearable in under ${MIN_CLEAR_MS_TO_PUBLISH}ms are too trivial to publish.` },
    };
  }
  if (!row.cleared_at) {
    return {
      status: 403,
      body: {
        error: 'Publish blocked: no clear has been recorded for this stage.',
        rule: 'A stage can only be published after its creator has cleared it in the browser within the time limit.',
        testUrl: `${origin}/?stage=${row.id}&key=${row.edit_key}`,
      },
    };
  }
  await env.promptworld_stages
    .prepare("UPDATE stages SET status = 'published', published_at = ? WHERE id = ?")
    .bind(new Date().toISOString(), row.id)
    .run();
  return { status: 200, body: { id: row.id, status: 'published', playUrl: `${origin}/?stage=${row.id}` } };
}

async function opListPublished(env, query, sort) {
  // Banned creators' stages vanish from discovery (legacy NULL-creator
  // stages stay visible). Optional ?q= substring search on the name.
  // Sorts: new (default) | top (good ratio) | hard / easy (clear rate).
  let sql = `SELECT s.id, s.name, s.published_at, s.clear_time_ms, s.attempts, s.clears,
                    c.name AS creator,
                    COALESCE(SUM(CASE WHEN v.good = 1 THEN 1 END), 0) AS goods,
                    COALESCE(SUM(CASE WHEN v.good = 0 THEN 1 END), 0) AS bads
             FROM stages s
             LEFT JOIN creators c ON c.id = s.creator_id
             LEFT JOIN votes v ON v.stage_id = s.id
             WHERE s.status = 'published' AND (c.banned IS NULL OR c.banned = 0)`;
  const binds = [];
  if (typeof query === 'string' && query.trim().length > 0) {
    sql += ' AND s.name LIKE ?';
    binds.push(`%${query.trim().slice(0, 40)}%`);
  }
  sql += ' GROUP BY s.id';

  // Unrated stages sit in the middle (0.5) so sorting stays meaningful.
  const clearRate = 'CASE WHEN s.attempts >= 10 THEN s.clears * 1.0 / s.attempts ELSE 0.5 END';
  switch (sort) {
    case 'top':
      sql += ' ORDER BY (goods + 1.0) / (goods + bads + 2.0) DESC, s.published_at DESC';
      break;
    case 'hard':
      sql += ` ORDER BY ${clearRate} ASC, s.published_at DESC`;
      break;
    case 'easy':
      sql += ` ORDER BY ${clearRate} DESC, s.published_at DESC`;
      break;
    default:
      sql += ' ORDER BY s.published_at DESC';
      break;
  }
  sql += ' LIMIT 100';
  const rows = await env.promptworld_stages.prepare(sql).bind(...binds).all();
  return rows.results;
}

async function opGhost(row) {
  // The creator's verified replay doubles as a ghost + par time.
  if (!row.clear_replay) return { status: 404, body: { error: 'No verified replay for this stage yet.' } };
  return {
    status: 200,
    body: { id: row.id, clearTimeMs: row.clear_time_ms, replay: JSON.parse(row.clear_replay) },
  };
}

// ------------------------------------------------------------ player feedback
// Players stay anonymous: a device-local playerId (random GUID in the
// browser's storage) deduplicates votes and leaderboard entries. Re-voting
// UPDATES the same row — spamming a button can never inflate counts.

function validPlayerId(id) {
  return typeof id === 'string' && /^[a-zA-Z0-9-]{8,64}$/.test(id);
}

async function opVote(env, row, body, request) {
  if (row.status !== 'published') return { status: 403, body: { error: 'Votes are only accepted on published stages.' } };
  if (!validPlayerId(body.playerId)) return { status: 422, body: { error: 'playerId required.' } };
  if (typeof body.good !== 'boolean') return { status: 422, body: { error: 'good must be true or false.' } };
  if (await overRateLimit(env, request, 'vote', RATE_LIMITS.vote)) {
    return { status: 429, body: { error: 'Rate limit: too many votes today.' } };
  }
  await env.promptworld_stages
    .prepare(`INSERT INTO votes (stage_id, player_id, good, updated_at) VALUES (?, ?, ?, ?)
              ON CONFLICT(stage_id, player_id) DO UPDATE SET good = excluded.good, updated_at = excluded.updated_at`)
    .bind(row.id, body.playerId, body.good ? 1 : 0, new Date().toISOString())
    .run();
  const agg = await env.promptworld_stages
    .prepare(`SELECT COALESCE(SUM(CASE WHEN good = 1 THEN 1 END), 0) AS goods,
                     COALESCE(SUM(CASE WHEN good = 0 THEN 1 END), 0) AS bads
              FROM votes WHERE stage_id = ?`)
    .bind(row.id)
    .first();
  return { status: 200, body: { id: row.id, goods: agg.goods, bads: agg.bads } };
}

// Clear rate = clears / attempts, reported automatically by the client
// (each death, timeout or clear ends one attempt). Deltas are capped so a
// hostile client can only nudge, not swamp, the statistics.
async function opStats(env, row, body, request) {
  if (row.status !== 'published') return { status: 200, body: { ok: true } };
  if (await overRateLimit(env, request, 'stats', RATE_LIMITS.stats)) {
    return { status: 429, body: { error: 'Rate limit.' } };
  }
  let attempts = Number(body.attempts);
  let clears = Number(body.clears);
  if (!Number.isInteger(attempts) || !Number.isInteger(clears)) {
    return { status: 422, body: { error: 'attempts and clears must be integers.' } };
  }
  attempts = Math.max(0, Math.min(attempts, 30));
  clears = Math.max(0, Math.min(clears, 1));
  if (clears > attempts) attempts = clears;
  await env.promptworld_stages
    .prepare('UPDATE stages SET attempts = attempts + ?, clears = clears + ? WHERE id = ?')
    .bind(attempts, clears, row.id)
    .run();
  return { status: 200, body: { ok: true } };
}

// Leaderboard entries require a replay certificate — the server re-simulates
// it, so times cannot be forged. Best time per (stage, device) is kept.
async function opScore(env, row, body, request) {
  if (row.status !== 'published') return { status: 403, body: { error: 'Leaderboards exist only on published stages.' } };
  if (!validPlayerId(body.playerId)) return { status: 422, body: { error: 'playerId required.' } };
  if (await overRateLimit(env, request, 'score', RATE_LIMITS.score)) {
    return { status: 429, body: { error: 'Rate limit: too many submissions today.' } };
  }

  const stage = JSON.parse(row.json);
  const maxTicks = Math.trunc(Math.fround(stage.timeLimit) / 0.02);
  const replay = body.replay;
  if (!replay || replay.v !== 1 || !Array.isArray(replay.rle) ||
      replay.rle.length === 0 || replay.rle.length > 400000) {
    return { status: 422, body: { error: 'A replay certificate is required.' } };
  }
  const result = simRunReplay(stage, replay.rle, maxTicks);
  if (!result.cleared) {
    return { status: 422, body: { error: `Replay verification failed: ${result.error || 'goal not reached'}` } };
  }
  const timeMs = result.ticks * 20;

  let name = typeof body.name === 'string' ? body.name.replace(/[ -]/g, '').trim().slice(0, 16) : '';
  if (name.length === 0) name = 'anonymous';

  const existing = await env.promptworld_stages
    .prepare('SELECT time_ms FROM scores WHERE stage_id = ? AND player_id = ?')
    .bind(row.id, body.playerId)
    .first();
  if (!existing || timeMs < existing.time_ms) {
    await env.promptworld_stages
      .prepare(`INSERT INTO scores (stage_id, player_id, name, time_ms, created_at) VALUES (?, ?, ?, ?, ?)
                ON CONFLICT(stage_id, player_id) DO UPDATE SET name = excluded.name, time_ms = excluded.time_ms, created_at = excluded.created_at`)
      .bind(row.id, body.playerId, name, timeMs, new Date().toISOString())
      .run();
  }
  const top = await opTopScores(env, row.id);
  return { status: 200, body: { id: row.id, verified: true, timeMs, top } };
}

async function opTopScores(env, stageId) {
  const rows = await env.promptworld_stages
    .prepare('SELECT name, time_ms FROM scores WHERE stage_id = ? ORDER BY time_ms ASC LIMIT 5')
    .bind(stageId)
    .all();
  return rows.results;
}

// ---------------------------------------------------------------- REST

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

async function handleApi(request, env, url) {
  const path = url.pathname.replace(/\/+$/, '');
  const method = request.method;

  if (path === '/api/stages' && method === 'POST') {
    const raw = await request.text();
    if (raw.length > LIMITS.maxJsonBytes) return json({ error: 'Stage JSON too large.' }, 413);
    let stage;
    try { stage = JSON.parse(raw); } catch { return json({ error: 'Invalid JSON.' }, 400); }
    const result = await opCreateStage(env, url.origin, stage, request,
      request.headers.get('X-Creator-Token'), request.headers.get('X-Creator-Name'));
    return json(result.body, result.status);
  }

  if (path === '/api/stages' && method === 'GET') {
    return json({ stages: await opListPublished(env, url.searchParams.get('q'), url.searchParams.get('sort')) });
  }

  const stageMatch = path.match(/^\/api\/stages\/([a-z0-9]+)(\/(clear|publish|ghost|vote|stats|score|scores))?$/);
  if (!stageMatch) return json({ error: 'Not found.' }, 404);
  const id = stageMatch[1];
  const action = stageMatch[3];

  const row = await opGetStage(env, id);
  if (!row) return json({ error: 'Stage not found.' }, 404);

  if (!action && method === 'GET') {
    // Fetching a draft marks the start of a creator test session.
    if (row.status === 'draft') await opMarkTestStarted(env, id);
    return new Response(row.json, { headers: { 'Content-Type': 'application/json' } });
  }

  if (action === 'ghost' && method === 'GET') {
    const result = await opGhost(row);
    return json(result.body, result.status);
  }
  if (action === 'scores' && method === 'GET') {
    return json({ id: row.id, top: await opTopScores(env, row.id) });
  }

  if (method !== 'POST') return json({ error: 'Method not allowed.' }, 405);
  let body = {};
  try { body = await request.json(); } catch { /* handled below */ }

  // Player endpoints (no editKey — anonymous device ids instead).
  if (action === 'vote') {
    const result = await opVote(env, row, body, request);
    return json(result.body, result.status);
  }
  if (action === 'stats') {
    const result = await opStats(env, row, body, request);
    return json(result.body, result.status);
  }
  if (action === 'score') {
    const result = await opScore(env, row, body, request);
    return json(result.body, result.status);
  }

  // Creator endpoints (editKey required).
  if (body.editKey !== row.edit_key) return json({ error: 'Invalid editKey.' }, 403);

  if (action === 'clear') {
    const result = await opRecordClear(env, row, body, request);
    return json(result.body, result.status);
  }
  if (action === 'publish') {
    const result = await opPublish(env, url.origin, row, request);
    return json(result.body, result.status);
  }
  return json({ error: 'Not found.' }, 404);
}

// ---------------------------------------------------------------- MCP

const TOOLBOX_DOC = `PROMPT WORLD — STAGE CREATOR'S TOOLBOX (schema v0.3)

You are building a 2D physics platformer stage as a single JSON document.
Strictly black & white minimalism. The player is a 1x1 white square.

PLAYER PHYSICS (design around these):
- run speed 8 units/s; jump apex ~3.3 units; max safe jump gap ~5 units
- gravity can be inverted by gravityFlip parts (everything mirrors)
- falling below y=-12 or above y=+15 respawns at playerStart (timer keeps running)

STAGE JSON SHAPE:
{
  "schemaVersion": "0.3",
  "name": "Stage Name",            // 1-60 chars
  "timeLimit": 60,                  // seconds, 5..1800
  "playerStart": { "x": -12, "y": -2.5 },
  "goal": { "x": 20, "y": -2.3, "w": 1.4, "h": 2.6 },   // exactly one, the exit door
  "parts": [ ... 1..300 parts ... ]
}

PARTS (x,y = center; w,h = size; coords within ±500, sizes 0.05..100):
- {"type":"solid","x","y","w","h"} — terrain to stand on (floor/wall/ceiling)
- {"type":"hazard","x","y","w","h"} — spike diamond; touch = respawn to start. ~0.8x0.8, rest on a solid (y = solidTop + ~0.6)
- {"type":"jumpPad","x","y","w","h","power"} — vertical relaunch; power<=60, 22 reaches ~8 units up; thin slab h~0.3 on a solid
- {"type":"boost","x","y","w","h","dirX","power"} — horizontal launch (dirX +1/-1, power<=60, 10 carries ~6 units); thin strip on a solid
- {"type":"gravityFlip","x","y","w","h"} — hollow square; touch inverts gravity; ~1.2x1.2 floating in the path
- {"type":"movingPlatform","x","y","w","h","dx","dy","period"} — rideable, oscillates to (x+dx,y+dy) and back every period s (0.5..30)
- {"type":"crumble","x","y","w","h"} — blinks 0.5s after touch, vanishes 2.5s, returns

DESIGN RULES:
- Chain parts into cause-and-effect sequences; one wow moment beats many gimmicks.
- Verify reachability: gaps >7 units or steps >3 units need a pad/boost/platform.
- WARNING from experience: chained mid-air gravityFlips pump velocity and diverge —
  prefer discrete floor<->ceiling sections. Never assume clearable from theory alone.
- The stage MUST be cleared by a human in the browser before it can be published.

IDENTITY: no signup exists. Your first create_stage mints an invisible
creator identity and returns a creatorToken — remember it and pass it in
later create_stage calls so all stages share one identity. Players never
need any identity.

WORKFLOW: create_stage -> give the human the testUrl -> they clear it in the
browser (auto-recorded, replay-verified) -> publish_stage -> share the playUrl.`;

const MCP_TOOLS = [
  {
    name: 'get_toolbox',
    title: 'Get the stage creation toolbox',
    description: 'Returns the full documentation for building Prompt World stages: JSON schema, available parts, physics constants, limits, and design guidance. Call this FIRST before creating a stage.',
    inputSchema: { type: 'object', properties: {}, additionalProperties: false },
  },
  {
    name: 'create_stage',
    title: 'Create a stage (draft)',
    description: 'Validates and saves a new stage as a draft. Returns the stage id, an editKey (keep it), a testUrl for the human creator to clear in the browser, and a playUrl for after publishing.',
    inputSchema: {
      type: 'object',
      properties: {
        stage: { type: 'object', description: 'The stage JSON document (see get_toolbox for the schema).' },
        creatorToken: { type: 'string', description: 'Your creator identity token from a previous create_stage response. Omit on first use — one is minted automatically.' },
        creatorName: { type: 'string', description: 'Display name for a newly minted creator identity (max 30 chars). Only used when creatorToken is omitted.' },
      },
      required: ['stage'],
    },
  },
  {
    name: 'stage_status',
    title: 'Check stage status',
    description: 'Returns a stage\'s status (draft/published), whether a clear has been recorded, and the clear time. Use it to check if the human has cleared the test yet.',
    inputSchema: {
      type: 'object',
      properties: { id: { type: 'string' } },
      required: ['id'],
    },
  },
  {
    name: 'publish_stage',
    title: 'Publish a cleared stage',
    description: 'Publishes a stage so it appears in the community list. BLOCKED (403) until a browser clear has been recorded for it — have the human clear the testUrl first.',
    inputSchema: {
      type: 'object',
      properties: {
        id: { type: 'string' },
        editKey: { type: 'string' },
      },
      required: ['id', 'editKey'],
    },
  },
  {
    name: 'list_stages',
    title: 'List published stages',
    description: 'Returns all published community stages with their play URLs.',
    inputSchema: { type: 'object', properties: {}, additionalProperties: false },
  },
];

async function mcpCallTool(env, origin, name, args, request) {
  switch (name) {
    case 'get_toolbox':
      return { text: TOOLBOX_DOC };

    case 'create_stage': {
      if (!args || typeof args.stage !== 'object' || args.stage === null) {
        return { text: 'Error: pass the stage JSON document as the "stage" argument.', isError: true };
      }
      const result = await opCreateStage(env, origin, args.stage, request, args.creatorToken, args.creatorName);
      return { text: JSON.stringify(result.body, null, 2), isError: result.status >= 400 };
    }

    case 'stage_status': {
      const row = await opGetStage(env, String(args?.id ?? ''));
      if (!row) return { text: 'Error: stage not found.', isError: true };
      return {
        text: JSON.stringify({
          id: row.id,
          name: row.name,
          status: row.status,
          cleared: !!row.cleared_at,
          clearTimeMs: row.clear_time_ms ?? null,
          playUrl: row.status === 'published' ? `${origin}/?stage=${row.id}` : null,
        }, null, 2),
      };
    }

    case 'publish_stage': {
      const row = await opGetStage(env, String(args?.id ?? ''));
      if (!row) return { text: 'Error: stage not found.', isError: true };
      if (args?.editKey !== row.edit_key) return { text: 'Error: invalid editKey.', isError: true };
      const result = await opPublish(env, origin, row, request);
      return { text: JSON.stringify(result.body, null, 2), isError: result.status >= 400 };
    }

    case 'list_stages': {
      const stages = await opListPublished(env);
      const withUrls = stages.map((s) => ({ ...s, playUrl: `${origin}/?stage=${s.id}` }));
      return { text: JSON.stringify({ stages: withUrls }, null, 2) };
    }

    default:
      return null;
  }
}

function rpcResult(id, result) {
  return { jsonrpc: '2.0', id, result };
}

function rpcError(id, code, message) {
  return { jsonrpc: '2.0', id, error: { code, message } };
}

async function handleMcpMessage(env, origin, msg, request) {
  if (!msg || msg.jsonrpc !== '2.0' || typeof msg.method !== 'string') {
    return rpcError(msg?.id ?? null, -32600, 'Invalid request.');
  }

  // Notifications need no response.
  if (msg.id === undefined || msg.id === null) return null;

  switch (msg.method) {
    case 'initialize': {
      const requested = msg.params?.protocolVersion;
      const supported = new Set(['2025-06-18', '2025-03-26', '2024-11-05']);
      return rpcResult(msg.id, {
        protocolVersion: supported.has(requested) ? requested : '2025-06-18',
        capabilities: { tools: { listChanged: false } },
        serverInfo: { name: 'prompt-world', title: 'Prompt World', version: '1.0.0' },
        instructions: 'Prompt World: build 2D physics stages as JSON and publish them as playable URLs. Call get_toolbox first to learn the stage format. Publishing requires the human creator to clear the stage in the browser (testUrl) — that gate cannot be skipped.',
      });
    }
    case 'ping':
      return rpcResult(msg.id, {});
    case 'tools/list':
      return rpcResult(msg.id, { tools: MCP_TOOLS });
    case 'tools/call': {
      const { name, arguments: args } = msg.params ?? {};
      const outcome = await mcpCallTool(env, origin, name, args, request);
      if (outcome === null) return rpcError(msg.id, -32602, `Unknown tool: ${name}`);
      return rpcResult(msg.id, {
        content: [{ type: 'text', text: outcome.text }],
        isError: outcome.isError === true,
      });
    }
    default:
      return rpcError(msg.id, -32601, `Method not found: ${msg.method}`);
  }
}

const MCP_CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Accept, Mcp-Session-Id, MCP-Protocol-Version',
};

async function handleMcp(request, env, url) {
  if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: MCP_CORS });
  if (request.method !== 'POST') {
    return new Response(null, { status: 405, headers: { Allow: 'POST, OPTIONS', ...MCP_CORS } });
  }

  let payload;
  try { payload = await request.json(); } catch {
    return new Response(JSON.stringify(rpcError(null, -32700, 'Parse error.')), {
      status: 400, headers: { 'Content-Type': 'application/json', ...MCP_CORS },
    });
  }

  const messages = Array.isArray(payload) ? payload : [payload];
  const responses = [];
  for (const msg of messages) {
    const response = await handleMcpMessage(env, url.origin, msg, request);
    if (response !== null) responses.push(response);
  }

  if (responses.length === 0) return new Response(null, { status: 202, headers: MCP_CORS });
  const body = Array.isArray(payload) ? responses : responses[0];
  return new Response(JSON.stringify(body), {
    headers: { 'Content-Type': 'application/json', ...MCP_CORS },
  });
}

// ---------------------------------------------------------------- entry

// ---------------------------------------------------------------- OGP

function escapeHtml(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// Shared links unfurl into branded cards (LINE / X / Discord etc.).
async function serveIndexWithOg(request, env, url) {
  const res = await env.ASSETS.fetch(request);
  let title = 'Prompt World';
  let desc = 'Black & white worlds made of prompts. Describe a stage to your AI, clear it, share the URL — anyone plays instantly in the browser.';

  const stageId = url.searchParams.get('stage');
  if (stageId && /^[a-z0-9]+$/.test(stageId)) {
    const row = await opGetStage(env, stageId);
    if (row) {
      const stage = JSON.parse(row.json);
      title = `${row.name} — Prompt World`;
      desc = `A prompt-built stage. Can you clear it within ${stage.timeLimit} seconds? Play instantly — no install.`;
    }
  }

  let html = await res.text();
  html = html.replace('<title>Prompt World</title>', `<title>${escapeHtml(title)}</title>`);
  const og = [
    `<meta property="og:title" content="${escapeHtml(title)}">`,
    `<meta property="og:description" content="${escapeHtml(desc)}">`,
    `<meta property="og:image" content="${url.origin}/og-card.png">`,
    '<meta property="og:type" content="website">',
    '<meta name="twitter:card" content="summary_large_image">',
    `<meta name="description" content="${escapeHtml(desc)}">`,
  ].join('\n    ');
  html = html.replace('</head>', `    ${og}\n  </head>`);
  return new Response(html, {
    headers: { 'Content-Type': 'text/html; charset=utf-8' },
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (url.pathname.startsWith('/api/')) return await handleApi(request, env, url);
      if (url.pathname === '/mcp') return await handleMcp(request, env, url);
      if ((url.pathname === '/' || url.pathname === '/index.html') && request.method === 'GET') {
        return await serveIndexWithOg(request, env, url);
      }
    } catch (err) {
      return json({ error: 'Internal error.', detail: String(err) }, 500);
    }
    return env.ASSETS.fetch(request);
  },
};
