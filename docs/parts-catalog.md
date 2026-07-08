# Parts Catalog — The Stage Creator's Toolbox (v0.2)

This is the toolbox handed to any AI (or human) creating a Prompt World
stage. A stage is a composition of these parts and nothing else — creativity
comes from combining a small, well-understood vocabulary, not from inventing
new mechanics. When stage generation runs, this document (plus
`stage-schema.md`) IS the system prompt context.

## Player physics constants (design around these)

| Constant | Value | Design consequence |
|---|---|---|
| Run speed | 8 units/s | — |
| Jump apex height | ~3.3 units | Max step-up per jump ≈ 3 units |
| Full jump airtime | ~0.96 s | Max jump gap ≈ 7 units (safe: ≤ 5) |
| Player size | 1 × 1 unit | Corridors need ≥ 1.5 units clearance |
| Gravity | scale 3, flippable | Everything mirrors when inverted |

## Parts

### `solid` — terrain
White rectangle. The only part players stand on (works as floor, wall, or
ceiling — essential for gravity-flip sections).
`{ "type": "solid", "x", "y", "w", "h" }`

### `hazard` — spike
White diamond (rendered rotated 45°). Touch → player respawns at start;
the timer keeps running, so hazards cost time, not lives.
`{ "type": "hazard", "x", "y", "w", "h" }` — typical size 0.8 × 0.8, placed
resting on a solid (y = solidTop + ~0.6).

### `jumpPad` — launcher
Thin white slab. Touch → vertical relaunch at `power` units/s (respects
current gravity direction). Player keeps air control.
`{ "type": "jumpPad", "x", "y", "w", "h", "power" }` — power 22 reaches
~8 units up; place as a thin slab (h ≈ 0.3) sitting on a solid.

### `boost` — horizontal launcher
Thin vertical white strip. Touch → horizontal launch at `power` units/s
toward `dirX` (+1 right / −1 left), with control locked for 0.6 s.
This is the "blown away" rule family.
`{ "type": "boost", "x", "y", "w", "h", "dirX", "power" }` — power 10
carries ~6 units before control returns. Place standing on a solid.

### `gravityFlip` — inverter
Hollow white square (outline). Touch → gravity inverts; the player falls
toward the ceiling and can run and jump there. Flipping back requires
another `gravityFlip`. 0.7 s cooldown per block.
`{ "type": "gravityFlip", "x", "y", "w", "h" }` — typical 1.2 × 1.2,
floating in the player's path.

### `goal` — the exit (required, exactly one)
Tall hollow white frame (door). Touch → Stage Clear.
Defined at stage top level: `"goal": { "x", "y", "w", "h" }` — typical
1.4 × 2.6 standing on a solid.

### `playerStart` (required)
Spawn point, also the respawn point for hazards/falls.
`"playerStart": { "x", "y" }` — place ~1 unit above a solid.

## World rules

- Falling below y = −12 or above y = +15 respawns the player at start.
- The countdown starts immediately; reaching 0 = Game Over.
- R restarts the stage at any time.
- Visuals are strictly white on black; parts are told apart by shape
  (rect / diamond / slab / strip / hollow box / tall frame).

## Composition guidance for generators

- Chain parts into cause-and-effect sequences (pad → airborne → flip →
  ceiling run → drop) rather than scattering them.
- Hazards punish with time, so pair them with the clock: a tight time limit
  makes safe-but-slow routes tense.
- Always verify reachability against the physics constants above — gaps
  wider than 7 units or steps taller than 3 units need a pad or boost.
- One clear "wow moment" per stage beats many gimmicks.
