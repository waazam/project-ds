# 2026-09-18a — Foundations and forest vertical slice

Commits: `5e270dd`, `e88a81c`, `7ce2ce3`, `d24a1d6`, `27df14b`, `c73e684`

The project's first day: engine setup, a controllable player, a placeholder
audio pipeline, and a walkable forest slice from trailhead to a staircase in
a clearing — the tech-demo skeleton everything else grew from.

## Additions
- Godot 4.7.2 .NET project skeleton: Forward+/D3D12, Jolt physics, a 640x360
  viewport stretch, an audio bus layout (nature categories, player,
  unnatural, voice), a `GameSettings` autoload, code-defined input actions,
  and a conventions doc.
- `CharacterBody3D` player controller: camera-relative walk/run,
  acceleration/deceleration, facing turn. `PlayerInput` isolates input so
  scripted control and a real player drive the exact same path.
- Third-person orbit camera rig: smoothed follow, a `SpringArm` that pulls
  in instantly on collision and eases back out, scroll-wheel distance,
  invert-Y.
- First-person camera mode added alongside third-person (eye height,
  stride-synced head bob, body hidden but still casting its shadow); third
  person kept behind a settings/dev toggle (F5, `--third-person`).
- Footsteps driven by floor surface metadata; breathing that surfaces in
  silence.
- `ForestAmbienceManager`, `SilenceZone`, `AmbienceLoop`, and a bird emitter
  — the reusable silence system (sounds for it land in the next pass).
- PS2-style post-process shader (dither, grain, grade), a fader, a pause
  menu with camera settings, an F3 debug overlay, `GameFlow`, and a
  scripted `--autotest` walkthrough harness.
- `tools/AudioGen`: a deterministic, from-scratch procedural audio
  generator (no samples, no NuGet) producing every placeholder sound —
  wind, leaves, insects, a distant bed, a stream, an unnatural drone, ear
  ringing, breathing loops, birds, dirt/wood footsteps, cloth — with
  seam-checked loops, meant to be replaced file-for-file by real
  recordings later.
- Stairs research notes and mythology proposals (design docs).

## Level Design
- A procedural forest valley with a 324 m trail: trailhead lot, across a
  footbridge, through deepening woods, to a clearing.
- MultiMesh conifers, deciduous trees, rocks, logs, ferns and grass with
  trunk collision; runtime low-res textures; fog and light deepen the
  further north you go.
- An interior staircase standing alone in the clearing, carrying its own
  `SilenceZone`; a top-landing trigger ends the slice (the placeholder
  "stand on top" ending, later replaced by real story checkpoints).
- Environmental clutter: an out-of-sequence trail marker, a dropped pack, a
  lost boot, a cut log, a snapped stick, and a stair fragment that vanishes
  (the fragment was later removed in the moodboard pass).

## Bug Fixes
- Wood footsteps resynthesized as damped noise (heel knock, softer toe,
  grit, a faint creak) instead of ringing sine partials, which read as hand
  drums rather than footsteps.

## Tests
- `--autotest` walks spawn → stairs top: 18/18 checks passing by the end of
  this run, including a check that the first-person eye height is correct
  in both camera modes.
