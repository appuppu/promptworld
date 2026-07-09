-- Prompt World stage storage.
-- A stage may only be published after a clear has been recorded for it.
CREATE TABLE IF NOT EXISTS stages (
  id TEXT PRIMARY KEY,
  json TEXT NOT NULL,
  name TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'draft',  -- draft | published
  edit_key TEXT NOT NULL,
  cleared_at TEXT,
  clear_time_ms INTEGER,
  clear_replay TEXT,
  test_started_at TEXT,
  created_at TEXT NOT NULL,
  published_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_stages_status ON stages(status, published_at DESC);

-- Daily counters for abuse prevention: key = action:ipHash:YYYY-MM-DD
CREATE TABLE IF NOT EXISTS rate_limits (
  key TEXT PRIMARY KEY,
  count INTEGER NOT NULL DEFAULT 0
);

-- Invisible creator identities: minted silently on first create_stage.
-- No signup, no login — but bans and per-creator quotas are enforceable.
CREATE TABLE IF NOT EXISTS creators (
  token TEXT PRIMARY KEY,       -- secret bearer token (returned once)
  id TEXT UNIQUE NOT NULL,      -- public creator id (stamped on stages)
  name TEXT NOT NULL,
  banned INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL
);

-- One GOOD/BAD vote per device per stage (upsert — no vote inflation).
CREATE TABLE IF NOT EXISTS votes (
  stage_id TEXT NOT NULL,
  player_id TEXT NOT NULL,
  good INTEGER NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (stage_id, player_id)
);

-- Replay-verified best time per device per stage (lower is kept).
CREATE TABLE IF NOT EXISTS scores (
  stage_id TEXT NOT NULL,
  player_id TEXT NOT NULL,
  name TEXT NOT NULL,
  time_ms INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (stage_id, player_id)
);
CREATE INDEX IF NOT EXISTS idx_scores_stage ON scores(stage_id, time_ms);

-- Per-device play records: attempts/clears on the stages table are exact
-- distinct-device counts derived from this, so a single device can never
-- inflate a stage's play count or clear rate.
CREATE TABLE IF NOT EXISTS plays (
  stage_id TEXT NOT NULL,
  player_id TEXT NOT NULL,
  cleared INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (stage_id, player_id)
);

-- Distinct-device play counters live on the stages table:
--   ALTER TABLE stages ADD COLUMN attempts INTEGER NOT NULL DEFAULT 0;
--   ALTER TABLE stages ADD COLUMN clears INTEGER NOT NULL DEFAULT 0;
--   ALTER TABLE stages ADD COLUMN creator_id TEXT;
