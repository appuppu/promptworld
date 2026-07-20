#!/bin/zsh
# Stop hook: at the end of each task/turn, remind that the Engine v2 design doc is
# the canonical spec any v2 work must conform to. Surfaces the path so it can be
# re-read before the next v2 change, guarding against drift from the agreed design.
DOC="$CLAUDE_PROJECT_DIR/docs/ENGINE-V2-DESIGN.md"
if [ -f "$DOC" ]; then
  cat <<EOF
{"systemMessage": "📐 Engine v2 canonical design: docs/ENGINE-V2-DESIGN.md — all v2 work must conform to it (4 modules · 5 skeletons · 6 determinism rules · schema-first, module-paced roadmap). Re-read before any v2 change and keep the Progress section current. v1 stays frozen."}
EOF
fi
exit 0
