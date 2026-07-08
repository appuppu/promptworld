# Stage JSON Schema — Draft v0.1

The deployable unit of Prompt World is a single JSON document. The Unity
client (and future WebGL editor) never executes user code — it interprets
this data. That keeps stages tiny (a few KB), safe to share as URLs, easy
for AI to generate, and ready for future P2P exchange.

## Example

```json
{
  "schemaVersion": "0.1",
  "stage": {
    "id": "stage_12345",
    "name": "First Steps",
    "author": "user_abc",
    "timeLimit": 60,
    "playerStart": { "x": -6, "y": 0 },
    "goal": { "x": 7, "y": -2.5, "w": 1, "h": 2 },
    "blocks": [
      { "id": "b1", "type": "solid", "x": 0, "y": -4, "w": 20, "h": 1, "rules": [] },
      {
        "id": "b2", "type": "solid", "x": 3, "y": -1, "w": 2, "h": 0.5,
        "rules": [
          {
            "trigger": "onTouch",
            "action": "knockback",
            "params": { "direction": "right", "speedKmh": 50 }
          }
        ]
      }
    ]
  }
}
```

## Fields

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | string | Bump on breaking changes; clients refuse unknown majors. |
| `stage.id` | string | Assigned at deploy time; becomes the share URL (`.../stage?id=...`). |
| `stage.timeLimit` | number (s) | Countdown start value. Creator must clear within this to deploy. |
| `stage.playerStart` | {x, y} | Spawn point. |
| `stage.goal` | {x, y, w, h} | Clear trigger volume. |
| `stage.blocks[]` | array | Every placed object. Position, size, and attached rules. |
| `blocks[].rules[]` | array | Trigger & Action pairs — the AI-generated physics rules. |

## Rule Vocabulary (to grow)

| Trigger | Meaning |
|---|---|
| `onTouch` | Player collides with this block |
| `onEnter` | Player enters this block's trigger volume |
| `onTimerBelow` | Remaining time drops below `params.seconds` |

| Action | Params | Meaning |
|---|---|---|
| `knockback` | direction, speedKmh | Launch the player |
| `setGravity` | scale (negative = inverted) | Change global gravity |
| `toggleBlock` | targetId | Enable/disable another block |

Rules are **data interpreted by the client**, never executable code —
this is the platform's core safety property.

## Day 1 → Schema Mapping

Today's hardcoded scene maps 1:1 onto future schema fields, so loading
from JSON later is a refactor, not a rewrite:

| Day 1 (Unity scene) | Schema field |
|---|---|
| `GameManager.timeLimit` (Inspector, 60) | `stage.timeLimit` |
| Player's initial Transform position | `stage.playerStart` |
| Goal object position/scale | `stage.goal` |
| Ground object | `blocks[]` entry with `type: "solid"` |
