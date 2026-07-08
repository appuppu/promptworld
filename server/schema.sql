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
  created_at TEXT NOT NULL,
  published_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_stages_status ON stages(status, published_at DESC);
