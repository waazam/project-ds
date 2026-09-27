# 2026-09-27b — Alpha polish, the title (Dead Silent), and the teaser

Commit: this entry's commit on `main`.

The owner: "Do some polishing to cap off the night." An optimization pass, an atmospheric and
lighting pass, pacing and tension mapping, an audio spatialization sweep, a beauty pass, an
animation cleanup, and a teaser trailer in the style of the Evil Dead trailer's camera.

## The title: Dead Silent
- The owner set the working title: **Dead Silent** ("DS"). It now appears on:
  - the title screen's TV;
  - the end card before the credits;
  - the window title;
  - the exported program (`DeadSilent.exe`, product name and description);
  - the trailer's title card.
- The engine project's own name stays "Project DS", so the save and settings folder doesn't move
  and nobody's saves disappear.

## Optimization (`RenderBudget`, `WinterGlade`, per-act frame rates)
- The underground (the station and its rooms, the stairwell, the long hallway, the sewer, the pit,
  the library, the round room, the long stair and the church) all sit within the camera's 400 m
  reach of each other. Nothing culls what's behind rock, so every one of them was being drawn from
  every other.
- `RenderBudget` fixes that once they've built:
  - every mesh, multimesh and particle system under the station gets a visibility range: its own
    reach plus 140 m, so nothing ever pops in view of where it can be seen from;
  - every light gets a distance fade (the light at 90 m, its shadow at 45 m).
  - In a test run: 2,793 meshes and 175 lights.
- The church's snowfall (14,000 flakes) was falling all game, underground and in the summer woods.
  It now runs only while the camera is near the church, and only once it's winter.
- The story test now reports the frame rate per act (average and minimum), so the heavy places can
  be found.

## Atmosphere and lighting (readability in the dark)
- Every door the player needs, or needs to know is locked, can be found without the lantern:
  - candles on iron brackets either side of the great door, the vestry's door and the chapel's
    door, and beside the tower's door;
  - the four puzzle candles stand out as the only dark ones among dozens burning.
- The church's candlelight breathes: each flame's light wavers a little, slowly and out of step
  with the rest. It is a glow that lives, never a flicker that flashes.

## Pacing and tension mapping (`docs/pacing-tension-map.md`)
- Every act is rated for stress, from 1 to 5, with its role. The rule we keep: no more than two hard
  acts back to back without a quiet beat.
- Changes:
  - The sewer (between the hallway and closet and the boss, four hard acts in a row) has its
    haunting sounds spaced out, 16–34 s (was 8–20 s), so it becomes a breath before the fight.
  - The station's basement: 14–30 s (was 9–22 s).
  - The long stair: 18–34 s (was 10–22 s), so each moan lands instead of becoming wallpaper.
  - The church: 30–56 s (was 18–36 s). It's the quiet puzzle act; its scares are the shut hatch
    and the blood.

## Audio spatialization
- Strict 3D:
  - the project's 3D panning strength is 0.9 (Godot's default is 0.5);
  - every positional sound in the world is panned at 1.6 on top of that;
  - the effective left/right separation is about three times what it was, so a whisper at your
    left ear is at your left ear, and steps behind you sound behind you.
- The player's own sounds stay centred.

## Beauty and animation
- The church's pews: the spiky cone finials (they read as a field of spikes in the owner's
  screenshot) are now turned necks with round poppyhead knobs.
- The candle flicker (above). The doors, the font's lid and the hatch already ease in and out.

## The teaser (`TrailerDirector`, `TrailerSounds`, `docs/trailer.md`)
- A trailer of 1:37, filmed in-engine and recorded with Godot's movie writer, in two parts that are
  joined:
  - the Hollow's shots (`-- --trailer`);
  - the ending in Act 1's trail level (`-- --trailer-ending`).
- It is encoded to a 1080p MP4 with a nearest-neighbour upscale (so the PS2 pixels stay sharp) and a
  gentle brightness grade.
- The camera: the Evil Dead trailer's "force" (the owner's reference), low glides through the dark
  woods at the cabin. After the owner's notes on the first cut:
  - slower, about four metres a second, so it doesn't look fast-forwarded;
  - smooth, with only a slight sway (the owner gets headaches from shaky cameras);
  - no strobing: only cuts and short dips to black;
  - lit brighter: each shot opens up the exposure, most in the night woods.
- It reveals little. The monsters are only glimpsed:
  - a tall shape between two trunks as the camera drifts past;
  - two red eyes at the far end of the crypt for the last second of that shot.
- The shots:
  - the woods at dusk, the cabin, the glimpse;
  - the lake at dawn;
  - the bunker's hallway, the stairwell's drop, the long hallway, the crypt;
  - the woods at night;
  - the candlelit nave and its red circle;
  - the long stair;
  - the great door, the slam, and black;
  - the last shot (the owner): a slow walk up the end of the Act 1 trail, the first staircase just
    peeking out of the fog, then DEAD SILENT. The trailer holds Act 1's fog fully closed in for it
    (`ForestAtmosphere.Act1FogOverride`), so the stairs emerge as the camera approaches.
- The score is timed to the cuts: a drone swelling over a minute, wind under the glides, a toll on
  the cabin, a thin cluster for each glimpse, soft booms on the cuts, a heartbeat from the bunker
  on that quickens to the door, a riser, the slam, and one great hit under the title.

## Tests
- Full `--autotest` (the whole game, Act 1 to Act 21's credits): 574/574 passed, 106 fps average
  walking.
- Per act, the forest acts are the heaviest (52–85 fps average). The underground runs at 105–115,
  and the library, round room and church at 147–160. The single-digit minimums are level-load
  hitches.
- `--continue-test`: 698/701. The same three failures predate this work.
- `--story-from=1 --story-to=2` after the renaming: 56/56.
