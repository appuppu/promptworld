#!/bin/zsh
# Daily "one stage a day" job — invoked by the launchd agent every morning.
# Picks TODAY'S stage (existing queued draft first, else generates a fresh one),
# writes a report, opens it in VS Code, and posts a desktop notification.
# It does NOT publish — you clear the testUrl in the browser, then publish and
# post the URL + a clip on X.
#
# Managed by ~/Library/LaunchAgents/com.promptworld.dailycourses.plist (06:03).

set -e
REPO="/Users/fukushimatakumi/develop/promptworld"
cd "$REPO"

# launchd has a minimal PATH; make node / claude / code resolvable.
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:$PATH"

echo "===== $(date '+%Y-%m-%d %H:%M:%S') daily-one run ====="
node "$REPO/scripts/daily-courses/daily-one.mjs"

REPORT="$REPO/scripts/daily-courses/reports/today.md"
# Open today's report + the queue so you can see/curate what's next.
if command -v code >/dev/null 2>&1; then
  code "$REPORT" "$REPO/daily-queue.md" 2>/dev/null || true
fi

osascript -e 'display notification "Today'\''s Prompt World stage is ready — clear it & publish" with title "Prompt World"' 2>/dev/null || true
