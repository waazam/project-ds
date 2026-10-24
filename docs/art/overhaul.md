# The AA overhaul: tracker

The loop and gates are in `.claude/skills/aa-pipeline/SKILL.md`. Stages: brief, reference, blockout, build, integrate,
review, done. Before/after pictures sit beside each entry. The baseline of every act (four headings from its start, no
HUD) is `test-output/look/baseline/aNN/`.

## Animation

| Asset / clip | Place | Stage | Before | After | Note |
|---|---|---|---|---|---|
| Wendigo walk | Acts 15–24 | review | `build/blender/anim_wendigo_walk_old.png` | `build/blender/anim_wendigo_walk.png`, `test-output/creatures/ab_wendigo_walk.png` | UAL `Walk_Loop` layered on its own walk (additive; legs replaced). Stride 3.29 m/cycle feeds the hunter's rate. |
| Wendigo run | Acts 15–24 | review | `anim_wendigo_run_old.png` | `anim_wendigo_run.png`, `ab_wendigo_run.png` | UAL `Sprint_Loop` on its run. The old run barely moved; this one is a hunched, long-reaching gallop. |
| Wendigo stalk (plays as "approach") | Acts 15–24 | review | — | `anim_wendigo_stalk.png`, `ab_wendigo_approach.png` | UAL `Crouch_Fwd_Loop` on its approach: a deep, knuckle-low prowl. Watch foot contact in play. |
| Wendigo idle | Acts 15–24 | review | `anim_wendigo_idle_old.png` | `anim_wendigo_idle.png` | UAL `Idle_Loop` breathing and weight shift, head tremble 4°. |
| Stalker walk | Acts 4–12 (and the giant) | review | `anim_stalker_walk_old.png` | `anim_stalker_walk.png`, `ab_stalker_walk.png` | Phase-locked to the walker as before. A clear heel strike and push-off now; the old stride shuffled. |
| Stalker idle | Acts 4–12 | review | `anim_stalker_idle_old.png` | `anim_stalker_idle.png` | Head tics 7°, hands 3°. |
| Stalker shove | Act 11 | review | `anim_stalker_shove_old.png` | `anim_stalker_shove.png`, `ab_stalker_shove.png` | UAL `Push_Loop` (0–40): a real reach and push. |

Tried and rejected (2026-10-24): wendigo `smash` from `Sword_Attack` (the one-handed swing twists its torso sideways), `lunge` from
`Punch_Jab` (the arms fly up out of reach). The old hand-keyed two-armed slam reads better. `grab` from `PickUp_Table` (it
brings the prey to its mouth) is worth another look with the arms' amplitude lower. Human attack clips don't suit it; its
attacks stay hand-keyed.

Switches: `Wendigo.RetargetedGaits`, `StalkerBody.RetargetedClips` (both on); `-- --no-retarget` runs the wendigo on its old clips for an A/B. Next up: hit reactions from `Hit_Chest`/`Hit_Head`; the stalker's `loom` and `stare` from `Idle_Torch_Loop`.

## Art

| Asset | Place | Stage | Before | After | Note |
|---|---|---|---|---|---|
| Trailhead cabin (exterior, porch, interior) | Act 1 | blockout → build | `test-output/look/cabin_before/` | `test-output/look/cabin_after/` | Pilot. Poses in `docs/art/poses.md`. |
