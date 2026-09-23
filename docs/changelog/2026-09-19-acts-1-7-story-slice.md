# 2026-09-18/19 — Acts 1-7 story slice

Commits: `712be26`, `9068b69`, `07cebdc`, `a35b609`

The tech demo became a story: a start menu, checkpoint saves, and the first
seven acts wired end to end, from the opening trail to the cabin catching
fire.

## Story Beats
- **Act 1**: reaching a fresh playable state checkpoints automatically.
- **Act 2**: stepping on the first stair takes movement away and carries
  the player to the top in an unnaturally smooth, silent climb (no
  footsteps), then hands control back.
- **Act 3**: returning to the cabin and finding the door boarded shut
  completes the checkpoint.
- **Acts 3-5 continuation**: a lantern and a compass appear on the porch,
  gated until the door is boarded (so they can't be grabbed on the way out
  at the very start). Leaving the cabin's safe zone with both items
  triggers a storm — the forest goes dead silent, rain fades in, lightning
  flashes with rolling thunder at random intervals. Act 4 is a one-time
  set-piece: a giant-scaled stalker crosses the far horizon in the fog for
  about 10 seconds, never to return, then the compass repoints to the
  cabin. Act 5 is the break-in: an axe (hard to find) chops the boarded
  door open quickly, or a key (far down the trail) unlocks a hammer in a
  shed, prying the door open slower via a hold-to-pry mini-game. Either
  way, stepping inside finds the friend, who admits to boarding the door
  themselves — the checkpoint for this beat.
- **Act 5 rework**: the friend is now found dead rather than alive. A newel
  post item on the table hands off into two new acts.
- **Act 6**: crossing the bridge, dawn breaking, the forest turning
  menacing, and a clearing of fifteen small stair variations with a voice
  line and an optional extended climb.
- **Act 7**: the cabin is found on fire.
- **Act 1** also gains an optional bird photography minigame.

## Level Design
- Trailhead signage renamed from Harrow Creek to Cullen Creek.
- A runtime navmesh baked over the terrain and buildings.
- Fixed a placement bug: the cabin and shed sat close enough together that
  a straight line out of the cabin door ran into the shed's wall.

## Additions
- A start menu (New Game / Continue / Settings, no manual save).
- Checkpoint-based saves that survive a crash mid-write.
- A translucent, Skyrim-style compass ribbon HUD that points at the
  current objective and glitches based on proximity to the stairs
  specifically, independent of the storm's forced silence.
- New rain/thunder placeholder audio, generated through `tools/AudioGen`
  rather than hand-added files.
- The first staircase's forced ascent made slower and more dramatic, with
  vision now pulsing shut and open like heavy, involuntary blinking (a
  tween-driven vignette pulse) for the whole climb; later tuned to blink to
  full blackness by its final cycles and hold a forced look-down off the
  top step before handing control back.
- Environmental clutter: a torn map, a forgotten tent (more "remnants of
  things people left behind" set dressing called for by the story
  outline).

## Bug Fixes
- A scene-tree race in the main menu's deferred scene change.
- Godot's `Area3D` never retroactively fires body-entered for a body
  already standing in it when monitoring toggles on later — this broke
  every checkpoint-gated pickup.
- A vignette-pulse loop that desynced the climb's completion signal from
  the tween actually finishing.
- The cabin sat on sloped terrain with an interaction radius too tight for
  the elevation variance.

## Tests
- `--autotest` extended to walk the full round trip (stairs, then back to
  the cabin) and check every checkpoint fires and saves correctly: 53/53
  checks passing by the end of this run.
