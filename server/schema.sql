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

-- One HIDE (report / block) per device per stage. Hiding removes a stage from
-- THAT device's lists only — it never removes the stage globally, so a targeted
-- report campaign can't take down an honest creator. The distinct count is
-- mirrored onto stages.reports so an admin can review the most-reported stages
-- and remove genuinely harmful ones by hand (App Store Guideline 1.2).
CREATE TABLE IF NOT EXISTS hides (
  stage_id TEXT NOT NULL,
  player_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (stage_id, player_id)
);
CREATE INDEX IF NOT EXISTS idx_hides_player ON hides(player_id);

-- Distinct-device report counter on stages (admin moderation queue signal):
--   ALTER TABLE stages ADD COLUMN reports INTEGER NOT NULL DEFAULT 0;

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
-- Testbench survival aggregates (every attempt, not per-device):
--   ALTER TABLE stages ADD COLUMN survive_ms_total INTEGER NOT NULL DEFAULT 0;
--   ALTER TABLE stages ADD COLUMN survive_n INTEGER NOT NULL DEFAULT 0;
-- status also allows 'unverified' (testbench: shipped without a clear;
-- the first verified world clear promotes it to 'published').
--   ALTER TABLE stages ADD COLUMN creator_id TEXT;

-- Which game a stage belongs to: NULL = the original 2D platformer,
-- 'tac' = the 3D TPS stealth shooter (played at /tac). Keeps the two games'
-- discovery lists separate. Applied 2026-07-17 (deploy-web.sh runs it):
--   ALTER TABLE stages ADD COLUMN game TEXT;
