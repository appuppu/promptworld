#!/bin/zsh
# TAC JS<->C# determinism crosscheck. Bit-identical traces or the build fails.
# Uses the dotnet SDK bundled inside the installed Unity editor (no global dotnet).
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET=$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet 2>/dev/null | head -1)
if [ -z "$DOTNET" ]; then
  echo "FATAL: no Unity-bundled dotnet found" >&2
  exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$ROOT"
cat "$ROOT/server/tacsim.js" "$ROOT/scripts/tac-crosscheck.js" | node - > "$ROOT/taccheck_js.txt"
"$DOTNET" run --project "$ROOT/scripts/tac-crosscheck" -c Release -v q -- "$ROOT/scripts/tac-crosscheck/stages" > "$ROOT/taccheck_cs.txt"

if cmp -s "$ROOT/taccheck_js.txt" "$ROOT/taccheck_cs.txt"; then
  echo "TAC CROSSCHECK: BIT-IDENTICAL ($(wc -l < "$ROOT/taccheck_js.txt" | tr -d ' ') trace lines)"
else
  echo "TAC CROSSCHECK: DIVERGED — first differences:" >&2
  diff "$ROOT/taccheck_js.txt" "$ROOT/taccheck_cs.txt" | head -20 >&2
  exit 1
fi
