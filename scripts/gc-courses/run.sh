#!/bin/zsh
# Abandoned-course GC — invoked daily by the launchd agent. Cloudflare Pages has
# no cron, so this local job POSTs /api/gc with the GC_TOKEN secret; the worker
# runs gcAbandonedUnverified (two-stage soft→hard delete of unverified courses
# that have gone quiet). See server/worker.js.
#
# Managed by ~/Library/LaunchAgents/com.promptworld.gccourses.plist.
#
# The GC token is NOT in git. Put it in ~/.pw_gc_env as:
#   export PW_GC_TOKEN="<the same value set via: npx wrangler pages secret put GC_TOKEN>"
#
# Appends ONE line per run to gc.log (date | softHidden=N hardDeleted=M), then
# trims the file to the last LOG_KEEP lines so it can never grow without bound —
# at one run a day that caps the log at a few hundred KB forever.
set -eu
REPO="/Users/fukushimatakumi/develop/promptworld"
cd "$REPO"

BASE_URL="${PW_BASE_URL:-https://promptworldgame.org}"
LOG="$REPO/scripts/gc-courses/gc.log"
LOG_KEEP=500            # keep only the most recent N lines (≈ N days of runs)

# One-line summary appended to the log, then rotate to the last LOG_KEEP lines.
log_line() {
  print -r -- "$(date '+%Y-%m-%d %H:%M:%S') | $1" >> "$LOG"
  if [ -f "$LOG" ]; then
    tail -n "$LOG_KEEP" "$LOG" > "$LOG.tmp" && mv "$LOG.tmp" "$LOG"
  fi
}

# Load the GC token (kept out of git, same pattern as ~/.pw_keystore_env).
if [ -f "$HOME/.pw_gc_env" ]; then
  # shellcheck disable=SC1090
  source "$HOME/.pw_gc_env"
fi
if [ -z "${PW_GC_TOKEN:-}" ]; then
  log_line "ERROR: PW_GC_TOKEN not set (create ~/.pw_gc_env)"
  exit 1
fi

# -sS: quiet but show errors; --fail-with-body: non-2xx exits non-zero but still
# prints the body so we can log the server's error. Falls back to --fail on old
# curl. Capture body + HTTP status.
resp="$(curl -sS --fail-with-body -X POST -H "X-GC-Token: $PW_GC_TOKEN" \
          -w $'\n%{http_code}' "$BASE_URL/api/gc" 2>&1)" || {
  # Request failed — log the last line (status or curl error) and bail.
  log_line "ERROR: $(print -r -- "$resp" | tail -n1)"
  exit 1
}

code="$(print -r -- "$resp" | tail -n1)"
body="$(print -r -- "$resp" | sed '$d')"

# Pull the two counters out of the JSON with a tiny grep (no jq dependency).
soft="$(print -r -- "$body" | grep -o '"softHidden":[0-9]*' | grep -o '[0-9]*' || true)"
hard="$(print -r -- "$body" | grep -o '"hardDeleted":[0-9]*' | grep -o '[0-9]*' || true)"

if [ "$code" = "200" ] && [ -n "$soft" ] && [ -n "$hard" ]; then
  log_line "ok | softHidden=$soft hardDeleted=$hard"
else
  log_line "ERROR: HTTP $code | $body"
  exit 1
fi
