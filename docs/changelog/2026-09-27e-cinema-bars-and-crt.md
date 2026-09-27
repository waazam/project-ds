# 2026-09-27e — The teaser's look in the game (cinematic bars, the CRT filter); Act 13's walls and window

Commit: this entry's commit on `main`.

The owner, on the teaser: "i absolutely love the aspect ratio and crtv filter in the trailer, can we
make the aspect ratio a little thinner … would be excellent if we can incorporate these into the game
too."

## The teaser: 2.2:1
- The frame is now 2.2:1 (1920x872), down from 2.35:1: a little less black bar at the top and the
  bottom.

## Cinematic bars in the game (`CinemaFrame`, a setting)
- The game is framed at 2.2:1 by letterbox bars while it is played. The bars sit over the picture and
  its finish (layer 11), and under every piece of the HUD: the prompts, the compass and the subtitles
  can sit on the black.
- They slide away (0.35 s, eased) while the camera is raised, so a photograph is never cropped by
  them, and come back when it is lowered.
- Not in the menus (the title screen has no bars) or in the trailer, which frames itself.
- Settings: **Cinematic bars**, on by default.

## The inventory: one item, in the bottom bar (`EquippedItemHud`, the owner)
- The list of everything carried is gone. The HUD shows one item, the one in hand, bottom-right inside
  the bottom black bar.
- Under it, small: "next:" and the item the wheel moves to, with a little up-and-down scroll mark. It
  all fits in the bar.
- The mouse wheel (or the left shoulder button, new) moves through what's carried, except while the
  camera is up (the wheel zooms it) or the album is open (it turns the pages).
- A new pickup comes to hand, with a brief brightening of its name.
- Everything carried still works at any time, as before. Only one is shown.
- Without the bars (the setting off), it sits in the bottom-right corner.

## The CRT filter in the game (`ps2_post.gdshader`, `GameSettings.ApplyDisplay`, a setting)
- Each of the game's 360 lines is drawn as a TV's scanline: full in its upper part, darkening to its
  lower edge (as in the teaser).
- A slight horizontal colour fringe: red a touch left, blue a touch right.
- The teaser's heavier grade: a touch darker in the mids, a little more contrast, a little less
  colour.
- **How:** with the filter on, the window draws its 2D and the finish at its full resolution
  (`canvas_items`), and the 3D still renders at 360 lines, scaled up smoothly, the way a TV's picture
  is. So it costs about the same as before.
  - The finish's dither and grain stay on the game's own pixel grid.
  - The HUD and the text are drawn at the window's resolution, so they're a little crisper.
- Off, the picture is the plain 640x360 frame, pixel for pixel, as before.
- Settings: **CRT filter**, on by default. It applies immediately; the window scaling follows window
  resizes.
- There is no flicker, roll or strobe: the scanlines are static.

## Act 13, room 1: the writing on the walls (`StationRoom1`, the owner)
- The words are scrawled over the whole of each wall. Each wall's sentence is broken into three huge
  lines (DO NOT / LOOK AT / THEM, and so on), each sized to span most of the wall and a little
  crooked.
- The letters are painted twice, a hair apart, for a thick, smeared stroke.
- Blood runs from under the letters: about fifteen drips to a wall, each a wavering run with a bead
  at its foot.
- **The words drip off the wall** when the button is pressed:
  - every run lets go and races down to the floor, faster as it falls, its bead at its foot;
  - the words sag, stretch and slide down after the runs, and thin away;
  - a slow drain of blood sounds at each wall.
  - The runs stay on the walls as stains, all the way down to the pools (also on a Continue). A
    faint ghost of the words stays, as before.
- **The pools are much larger:** a wide, glossy pool along each wall's foot (about 3.5 x 1.5 m, from
  a new, finer pool texture), with smaller splashes round it. The hand is still in the pool under
  DO NOT TOUCH THEM.

## Act 13, room 2: what breaks the window (`WindowTentacle`, `StationRoom2`, the owner)
- It is one of the tentacles of the thing in the lake, made the same way (`TentacleKit`): grimy
  octopus flesh, ring suckers, bloodshot eyes that turn to stare at the camera, and the toothed maw
  at its tip.
- **The sequence:**
  - A dark shape presses against the glass and the first blow cracks it.
  - It draws back while the player turns to look.
  - It **blasts through**: glass everywhere, a slam, the lake pouring in round it. The player is
    knocked flat.
  - It lunges about 2 m into the room at them, maw first, writhing.
  - Then it drags itself back out through the window with a wet squelch, and is gone.
- The story test checks that the tentacle is in the room when the window breaks, and takes a
  screenshot.

## The save indicator, quieter (`SaveIndicator`, the owner)
- The film roll is about half the size it was, in the top bar's corner, greyer and fainter (about 40%
  opaque at most; it was 72%).

## Fixes that came with it
- A photograph's crop is taken in the rendered frame's own pixels, so photographs stay framed right
  at any window size with the filter on.
- The CRT scaling keeps the project's 1.5x supersampled 3D: the CRT renders 540 lines. Off, it is
  1.5x as before; a first version had dropped it to 1x.
- The window's scaling is only set when it changes. It is re-applied on a real change of window size,
  not on every size signal.
- The library journal's pick volume stands out from the bookcase. It only just reached the
  bookcase's collision box, so aiming at the journal could hit the shelf by a hair.
- The startup logo hands the window back to the CRT setting's scaling (it used to restore the
  project's own).
- **The story test's climb of the last staircase (Act 11)** now settles at the foot and faces straight
  up the flight before walking up.
  - With the CRT filter on, the walk arrived at a run from the side, turned a few frames late and
    walked up beside the narrow flight instead of onto it: 17/26.
  - The player's own climb was never affected; this was the test's steering. Now 26/26.

## Tests
- **Test robustness:**
  - The tests ignore the real mouse. The machine was in use while a run went, and a desk mouse turned
    the view off what a test had aimed at.
  - Aiming clears any look still pending from a walk's steering.
  - Injected key presses are flushed at once.
  - The hatch's check waits for its prompt, which arrives on the next physics tick.
- Full `--autotest`, with the CRT filter and bars on (the defaults): 561/565 on the first run. The four
  failures were all aiming or input timing (above). After the fixes, Acts 19–21 pass 78/78.
- `--story-from=11 --story-to=11`: 26/26. `--story-from=13 --story-to=13`: 57/57 (with the tentacle's
  new check). `--story-from=19`: 22/22.
- `--continue-test`: 698/701. The same three failures predate this work.
