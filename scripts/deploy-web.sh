#!/bin/zsh
# Deploy the WebGL build + API worker to Cloudflare Pages.
# Run after BuildScript.BuildWebGL. Usage: zsh scripts/deploy-web.sh
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/PromptWorld/Builds/WebGL"

# Unity regenerates index.html on every build — reapply branding patches.
node -e '
const fs = require("fs");
const p = process.argv[1];
let s = fs.readFileSync(p, "utf8");
s = s.replace("<title>Unity Web Player | Prompt World</title>", "<title>Prompt World</title>");
if (!s.includes("body { background: #000; }")) {
  s = s.replace(
    "<link rel=\"stylesheet\" href=\"TemplateData/style.css\">",
    "<link rel=\"stylesheet\" href=\"TemplateData/style.css\">\n    <style>body { background: #000; }</style>"
  );
}
fs.writeFileSync(p, s);
console.log("index.html patched");
' "$OUT/index.html"

# The API worker rides along as a Pages advanced-mode worker.
# sim.js (deterministic replay verifier) is concatenated in front.
cat "$ROOT/server/sim.js" "$ROOT/server/worker.js" > "$OUT/_worker.js"

cd "$ROOT"
npx wrangler pages deploy --commit-dirty=true
