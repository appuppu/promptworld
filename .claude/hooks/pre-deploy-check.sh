#!/bin/zsh
# PreToolUse hook: when a deploy / commit / push command is about to run,
# surface the 6-point regression checklist so it's verified before anything
# reaches production or history. Non-blocking: it prints the checklist and
# lets the command proceed (the user chose "present & continue").
#
# Wired in .claude/settings.json under hooks.PreToolUse (matcher: Bash).
# Input: a JSON object on stdin with .tool_input.command (the bash command).

input=$(cat)

# Extract the command string from the tool input JSON.
cmd=$(printf '%s' "$input" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("tool_input",{}).get("command",""))' 2>/dev/null)

# Does this command deploy, commit, or push?
if printf '%s' "$cmd" | grep -Eq 'deploy-web\.sh|wrangler +pages +deploy( |$)|git +commit|git +push'; then
  # Emit the checklist as a systemMessage; allow the command to continue.
  cat <<'JSON'
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "additionalContext": "🚦 DEPLOY/COMMIT GATE — verify the 6-point regression checklist BEFORE this command runs. Do not just assert; actually check each:\n  1. 既存公開ステージに影響がないか (append-only — published tac stages play identically; changing an existing gimmick's behavior needs the user's explicit instruction)\n  2. 決定論・リプレイ検証が無傷か (tac tests ALL PASS; the SAME tacsim.js ships to client and _worker.js so server re-simulation of clears stays bit-identical — the publish gate must never break)\n  3. キャッシュバストが効いているか (new TAC_HASH ?v= URL confirmed serving the NEW code at the edge — poll the cache-busted asset URLs until propagated)\n  4. 既存データが消えないか (D1 writes touch only intended columns; clear records/creator/vote rows preserved; stage updates only via editKey, drafts stay drafts, URLs unchanged)\n  5. ステージ音楽が壊れていないか (per-stage music recipes still honored; stealth/combat layer crossfade intact; no BGM bleed between stages)\n  6. 2D(/classic)が無傷か (classic game at /classic + its API defaults + Unity assets untouched; /api/stages default list still 2D)\nReport pass/fail for each in your next message. If anything is unverified, stop and check it before relying on this deploy."
  }
}
JSON
fi

exit 0
