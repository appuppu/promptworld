#!/bin/zsh
# Deploy the WebGL build + API worker to Cloudflare Pages.
# Run after BuildScript.BuildWebGL. Usage: zsh scripts/deploy-web.sh
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/PromptWorld/Builds/WebGL"

# Gate: the TAC sim test suite must pass before anything ships.
if ! cat "$ROOT/server/tacsim.js" "$ROOT/scripts/tac-test.js" | node - | grep -q "ALL PASS"; then
  echo "FATAL: tac sim tests failing — aborting deploy. Run: cat server/tacsim.js scripts/tac-test.js | node -" >&2
  exit 1
fi
echo "tac tests: ALL PASS"

# Unity regenerates index.html on every build — reapply branding + ad hook.
node "$ROOT/scripts/patch-index.js" "$OUT/index.html"

# The API worker rides along as a Pages advanced-mode worker.
# sim.js (v1 replay verifier) and tacsim.js (tac shooter verifier) are
# concatenated in front so the worker can re-simulate clears for both games;
# tacreach.js (reachability analyzer) gates tac create/update.
cat "$ROOT/server/sim.js" "$ROOT/server/tacsim.js" "$ROOT/server/tacreach.js" "$ROOT/server/worker.js" > "$OUT/_worker.js"

# TACTICAL (game:"tac") web client — served as static assets at /tac.
# Cache-bust the script URLs with a content hash so updates always reach
# players' browsers.
TAC_HASH=$(cat "$ROOT/server/tacsim.js" "$ROOT/server/tac-client.js" | md5 -q | cut -c1-10)
sed -e "s#src=\"/tacsim.js\"#src=\"/tacsim.js?v=$TAC_HASH\"#" \
    -e "s#src=\"/tac-client.js\"#src=\"/tac-client.js?v=$TAC_HASH\"#" \
    "$ROOT/server/tac.html" > "$OUT/tac.html"
cp "$ROOT/server/tacsim.js" "$OUT/tacsim.js"
cp "$ROOT/server/tac-home.html" "$OUT/tac-home.html"
cp "$ROOT/server/tac-client.js" "$OUT/tac-client.js"

# The tac list/create paths need the stages.game column. Attempt the ALTER
# (errors if it already exists), then HARD-VERIFY the column is present —
# deploying a worker that references a missing column would break the 2D lists.
npx wrangler d1 execute promptworld-stages --remote \
  --command "ALTER TABLE stages ADD COLUMN game TEXT" >/dev/null 2>&1 || true
if npx wrangler d1 execute promptworld-stages --remote --json \
     --command "SELECT COUNT(*) AS n FROM pragma_table_info('stages') WHERE name='game'" \
     | grep -q '"n": *1'; then
  echo "D1: stages.game column verified"
else
  echo "FATAL: stages.game column missing and could not be added — aborting deploy." >&2
  exit 1
fi

# Testbench survival stats: aggregate columns on stages (same guarded pattern).
npx wrangler d1 execute promptworld-stages --remote \
  --command "ALTER TABLE stages ADD COLUMN survive_ms_total INTEGER NOT NULL DEFAULT 0" >/dev/null 2>&1 || true
npx wrangler d1 execute promptworld-stages --remote \
  --command "ALTER TABLE stages ADD COLUMN survive_n INTEGER NOT NULL DEFAULT 0" >/dev/null 2>&1 || true
if npx wrangler d1 execute promptworld-stages --remote --json \
     --command "SELECT COUNT(*) AS n FROM pragma_table_info('stages') WHERE name IN ('survive_ms_total','survive_n')" \
     | grep -q '"n": *2'; then
  echo "D1: survive columns verified"
else
  echo "FATAL: survive columns missing — aborting deploy." >&2
  exit 1
fi

# Per-device HIDE / report moderation: a hides table + a distinct-report counter
# on stages (same guarded ALTER + hard-verify pattern).
npx wrangler d1 execute promptworld-stages --remote \
  --command "CREATE TABLE IF NOT EXISTS hides (stage_id TEXT NOT NULL, player_id TEXT NOT NULL, created_at TEXT NOT NULL, PRIMARY KEY (stage_id, player_id))" >/dev/null 2>&1 || true
npx wrangler d1 execute promptworld-stages --remote \
  --command "CREATE INDEX IF NOT EXISTS idx_hides_player ON hides(player_id)" >/dev/null 2>&1 || true
npx wrangler d1 execute promptworld-stages --remote \
  --command "ALTER TABLE stages ADD COLUMN reports INTEGER NOT NULL DEFAULT 0" >/dev/null 2>&1 || true
if npx wrangler d1 execute promptworld-stages --remote --json \
     --command "SELECT COUNT(*) AS n FROM pragma_table_info('stages') WHERE name='reports'" \
     | grep -q '"n": *1'; then
  echo "D1: stages.reports column verified"
else
  echo "FATAL: stages.reports column missing — aborting deploy." >&2
  exit 1
fi
if npx wrangler d1 execute promptworld-stages --remote --json \
     --command "SELECT COUNT(*) AS n FROM sqlite_master WHERE type='table' AND name='hides'" \
     | grep -q '"n": *1'; then
  echo "D1: hides table verified"
else
  echo "FATAL: hides table missing — aborting deploy." >&2
  exit 1
fi

# Static extras: share card, creator onboarding page, and legal pages
# (privacy + terms are required for Google AdSense approval).
cp "$ROOT/server/og-card.png" "$OUT/og-card.png"
cp "$ROOT/server/create.html" "$OUT/create.html"
# creator guide is tac-first now; the classic 2D guide lives on at /create-classic
cp "$ROOT/server/create-classic.html" "$OUT/create-classic.html"
cp "$ROOT/server/support.html" "$OUT/support.html"
cp "$ROOT/server/privacy.html" "$OUT/privacy.html"
cp "$ROOT/server/terms.html" "$OUT/terms.html"
cp "$ROOT/server/mcp-spec.html" "$OUT/mcp-spec.html"
cp "$ROOT/server/ads.txt" "$OUT/ads.txt"
cp "$ROOT/server/app-ads.txt" "$OUT/app-ads.txt"
cp "$ROOT/server/robots.txt" "$OUT/robots.txt"
cp "$ROOT/server/sitemap.xml" "$OUT/sitemap.xml"

cd "$ROOT"
npx wrangler pages deploy --commit-dirty=true
