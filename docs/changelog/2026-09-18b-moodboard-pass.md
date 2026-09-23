# 2026-09-18b — Moodboard pass: stalker, audio rework, first-person polish

Commits: `b7509e7`, `d2d86b3`

A full art-direction pass to match the moodboard, plus the game's first
antagonist: a stalker that shadows the player through the trees.

## Level Design
- Forest re-art to match the moodboard: tall conifers, a leaf-litter trail,
  mossy boulders, a dusk sky with ridge silhouettes, a darker grade, and
  thicker fog.
- Trail extended to roughly 534 m, with an overlook fork and three faint
  paths that fade out into nothing.
- Staircase rebuilt as a long, weathered stone flight with cheek walls
  (replacing the placeholder interior staircase).
- Restyled trailhead sign, info board, and markers; new arrow signposts;
  new bridge design.

## Additions
- The stalker: a new body (`StalkerBody` + a `stalker_skin` shader, faint
  yellow eyes) that stays behind a tree trunk with only a sliver exposed
  (capped by line of sight), tails the player tree to tree, closes in while
  unseen, ducks and dissolves the moment it's seen, and occasionally
  appears far ahead instead. Heard from wherever it actually is: footsteps
  that shadow the player's own, twig snaps; focusing on it with the camera
  (right mouse) triggers a sting.
- `ForestDirector`: a stillness response, activity waves, silence
  dropouts, a distinct room acoustic near the stairs, a heartbeat, and a
  startle hush.
- A reactive score (pad + shimmer) on its own Music bus, cut by the
  silence system; an undertone layer; a scripted "bridge gunshot" event;
  one-breath-at-a-time breathing (added muted, pending a mix pass).
- Borderless fullscreen with an F11 toggle; dev-only debug keys (F3
  overlay with a stalker frame, F5, F6/F7) gated to debug builds.
- Windows export preset and the `.sln` file .NET export needs.

## Subtractions
- Removed the early vanishing stair fragment (superseded by the rebuilt
  stone staircase).
- Removed the opening caption.
- Many placeholder sounds rebuilt or removed outright after a listening
  review.
- README trimmed down to just requirements, run/export commands, and
  controls.

## Audio Fixes
- Scene-placed ambience loops were silently never playing, because of a
  missing null-stream check.
- QOA loop end points were being computed from the compressed file size
  instead of the decoded sample length, throwing loop timing off.

## Tests
- `--autotest` extended to also verify that placed loops actually play:
  31/31 checks passing.
