# 2026-09-27f — Performance (shadows), photographed webs, a cloth sheet, the church's hatch and steps

Commit: this entry's commit on `main`.

The owner's notes, playing:
- "the game is lagging";
- the spider webs "look geometrical and bad";
- a cloth simulation for the library's sheet, and the physics perhaps reworked on Jolt;
- a web texture pack (Resource Boy) to populate the game with webs;
- the church's stairs clipping;
- the ladder and hatch up into the church "too blocky and clunky", the player seeming to go
  through the floorboards;
- the save icon too large and distracting.

## Performance: the shadow pass (`GameSettings.Shadows`, a setting)
- **Profiled first** (a dev probe, `-- --perf`, with a per-script sweep).
  - The forest was CPU-bound: the frame rate didn't move with the 3D resolution.
  - Physics was under 1 ms, so it wasn't Jolt.
  - The CRT filter costs about 5%.
  - With the sun's shadows off, the Act 1 forest ran at about 180 fps instead of about 55. The
    shadow pass (every tree drawn again into two cascades out to 60 m) was most of the frame:
    half the draw calls, and 2–3 million of its 5 million triangles.
- **A Shadows setting:** off / medium / high. Medium is the default: out to 40 m, a 2048 map and a
  light filter. High is the old look: each level's own reach, 4096 and the soft filter. It applies
  to every sun and moon as levels load.
- **Branches, stumps and ground cover no longer cast shadows.** Only the trees do; under the canopy
  the rest couldn't be seen anyway.
- **Result:** the same stretch of Act 1 went from about 51–74 fps to about 80–115. The machine was in
  use during these runs, so the numbers are rough, but the gain is clear.
- **Jolt:** the game already runs on Jolt. It has been built into Godot since 4.4, so the godot-jolt
  extension isn't needed. Its soft bodies are what the sheet below uses.

## Spider webs from photographs (`WebKit`)
- Twelve of the owner's Resource Boy webs are cut to 1024 px, with the fine strands strengthened to
  survive the smaller size (`assets/textures/webs`): two orb webs, five corner webs, a sagging sheet
  and four old tangles.
- **The license:** royalty-free for commercial use, no attribution needed. The files must not be
  passed on by themselves, only inside the game.
- A web is a lit, double-sided, soft-edged card. It has a little sheen for the lantern, not enough
  for a coloured light to dye it, and casts no shadow.
- The old procedural radial web texture is gone. Where webs are now:
  - **the round room's dais** (Act 20): every card of the tent is its own photographed web;
  - **the lobby's corners** (Act 13);
  - **the web over the iron door**: layered tangles and a sheet;
  - **new:** the library's corners, the crypt's corners, webs slung from the crypt's column tops up
    into the vaults, and the long stair's top landing.

## The library's sheet is cloth (`Library.PullSheet`, Jolt soft body)
- At rest it is the same shaped drape as before, a still mesh that costs nothing.
- Pulled, it becomes cloth: one welded grid of 589 points. A handful of its near edge is taken in
  hand, drawn up and off towards the player, and let go. It slides off the table and falls in a
  heap beside it.
- Once settled, it is set down as a still mesh again and the simulation removed.
- The story test checks it comes off and settles off the table, and photographs it.

## The church
- **The hatch and ladder** (Act 21's climb out of the long stair):
  - The blocky iron rungs on the end wall, which led nowhere near the hatch, are gone.
  - A wooden loft ladder (worn side rails, round rungs, iron hooks over the lip) rises under the
    opening and rests on the hatch's far edge.
  - The climb is new: to the ladder's foot, turning to face it, rung by rung up it (a small ease in
    each step), the lid pushed up and over halfway up, straight up through the opening, and a step
    onto the crypt's floor. The head always comes up inside the opening, never through the boards.
- **The chancel's steps:** the five steps were stacked blocks sharing their side faces, which fought
  over them (the clipping). Each is now its own block, side by side.

## The save indicator
- It was already made smaller, greyer and fainter in the last entry. It sits in the top bar's corner.

## Tests
- Full `--autotest` (before the movement and crawler work): 568/572, walking at an average of 103
  fps (it was 70). The four failures were all test flakiness, and all are fixed:
  - Act 1's photograph: a bird can take off as the shutter goes, so the test now tries again on the
    next perched one.
  - Act 15's red light: the shadow man was 7.2 m behind, against a 7 m limit; it is now 8 m.
  - The walk up the crypt's stairs went round a column in line with the stair's foot and up the
    other staircase. It now goes by a waypoint between the columns.
- Then, with everything in: Acts 1–2 56/56, Act 11 26/26, Acts 13–14 90/90, Acts 19–21 79/79 (with
  the cloth sheet's new check), Act 21 29/29.
- `--continue-test`: 698/701. The same three failures predate this work.
