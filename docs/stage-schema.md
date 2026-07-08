# Stage JSON Schema — v0.3

The deployable unit of Prompt World is a single JSON document. The Unity
client never executes user code — it interprets this data (see
`PromptWorld/Assets/Scripts/StageLoader.cs`). That keeps stages tiny (a few
KB), safe to share as URLs, easy for AI to generate, and ready for future
P2P exchange.

v0.2 flattens the structure (no nested `stage` wrapper) so Unity's built-in
JsonUtility can parse it directly, and replaces free-form trigger/action
rules with a typed **parts vocabulary** — see `parts-catalog.md`, which is
the companion document handed to stage-generating AIs.

## Shape

```json
{
  "schemaVersion": "0.3",
  "id": "stage-001",
  "name": "First Flight",
  "timeLimit": 45,
  "playerStart": { "x": -12, "y": -2.5 },
  "goal": { "x": 27, "y": -2.3, "w": 1.4, "h": 2.6 },
  "parts": [
    { "type": "solid", "x": -12, "y": -4, "w": 8, "h": 1 },
    { "type": "hazard", "x": -3.5, "y": -3.1, "w": 0.8, "h": 0.8 },
    { "type": "jumpPad", "x": 2, "y": -3.35, "w": 1.5, "h": 0.3, "power": 22 },
    { "type": "boost", "x": 23, "y": -2.6, "w": 0.4, "h": 1.8, "dirX": 1, "power": 10 },
    { "type": "gravityFlip", "x": 9.5, "y": 4, "w": 1.2, "h": 1.2 }
  ]
}
```

## Fields

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | string | Clients refuse unknown majors. |
| `id` | string | Assigned at deploy time; becomes the share URL (`.../stage?id=...`). |
| `name` | string | Shown in the HUD. |
| `timeLimit` | number (s) | Countdown start value. Creator's Claude must prove a clear within this to deploy. |
| `playerStart` | {x, y} | Spawn and respawn point. |
| `goal` | {x, y, w, h} | Clear trigger volume (exactly one). |
| `parts[]` | array | Everything else. `type` selects behavior; `power`/`dirX` only where the part uses them. |

Part types and their design rules live in `parts-catalog.md`.

## Runtime lifecycle

1. `StageLoader` reads the JSON from `StreamingAssets/Stages/` (later: Firebase by stage id).
2. Parts are instantiated as white-on-black shapes with typed behaviors.
3. `GameManager.Configure()` receives the time limit and player reference.
4. Pressing R reloads the scene → the JSON is re-read. Editing a stage file
   and pressing R hot-reloads the stage without recompiling.

## Roadmap

- v0.3: free-form trigger/action rules on parts (the AI-generated custom
  physics layer) on top of the typed vocabulary.
- Deploy pipeline: JSON + replay-certificate (proof-of-clear input trace)
  uploaded to Firebase; server re-simulates to verify before publishing.
