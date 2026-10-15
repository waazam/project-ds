# 2026-10-14g: Worn leather and a work jacket; an arm of the right length; held things that face the right way and are their right size

What the owner asked for:
- "The player's arms need more texture, and the hands too."
- "The arm itself is too long and unrealistic, especially while walking."
- "The axe and the hammer are not angled forward."
- "Crowbar isn't held correctly."
- "Make sure all items are the correct proportions, for example the knife blade is too short."
- "The items in hands still look funky and like they're facing the wrong way."

The research behind the item carries, the conventions shared by first-person games, and a table of each item's
size before and after: `docs/research/first-person-held-items.md`.

## What was wrong
- **The sleeve:** a plain, smooth olive tube. In the game it read as a dark navy-black under the held things' cool rim
  light.
- **The glove:** in daylight it read as a greyish tan. That was the sky's specular and the rim over a dark, dull
  albedo.
- **The arm:**
  - The lantern's fist was 64 cm out from the eye and nearly level with it. That is further than an arm reaches from a
    shoulder 25 cm below the eye.
  - The sleeve ran 48 cm straight back from the wrist.
  - The walk's bob slid the whole arm about in the air.
- **The tools:**
  - The Stick pose's knuckles point forward and to the left, and the game turned it yaw -66 on top of that. Meanwhile
    every tool had been turned to "its knuckles' way". The working ends ended up some 70° off the knuckles, so the
    tools faced the wrong way.
  - The tools stood upright and were held near their middles.
  - The camera's thumb lay over its top.
  - The cap's hand showed only the wrist.
- **Sizes:**
  - The knife's blade was 11.8 cm in the hand and 23.5 cm on the ground.
  - The crowbar was 57 cm in the hand and 81 cm on the ground.
  - The axe was 64 cm in the hand and 75 cm on the ground, with a bit that was too tall.
  - The compass was 10 cm across.

## The glove and the jacket (`tools/Blender/fp_arms.py`, `assets/shaders/fp_arm.gdshader`)
- **The glove has its own baked colour map.** The finer skin (four levels of subdivision) is painted in its vertices
  and baked to a 1024 map with clean UVs, so it holds detail the game mesh's vertices can't:
  - **The leather:** a darker, oiled brown with a slow mottle, redder in places, and enough colour in it to stay brown
    under the cool rim light.
  - **Wear:** rubbed paler and warmer over the knuckles and fingertips.
  - **Grime and scuffs:** grime worked into the palm, the fingertips and every crease; scuffed patches; about 90 fine
    pale scratches through the finish.
  - **Knuckle creases:** dark creases across the back of each knuckle (also carved into the normal map).
  - **Stitching:** the seams' pale thread in short stitches along the fingers' sides, round the thumb, round the
    cuff's hem and down the back's darts. The stitch ring round each fingertip was left out: drawn there, it read as
    a toe.
- **The glove's vertices** now carry the baked occlusion, how worn each place is and the palm.
- **The glove's shader:**
  - The pebbled grain is stronger, and dirt sits in its hollows.
  - The worn places are polished smoother, with a slight sheen the lantern catches.
  - Its specular and rim are lower.
  - It is warmed a little against the cool light.
- **The fingertips** taper a little: they are narrower and flatter over the nail, and the cap is drawn out toward the
  pad.
- **The sleeve: a cotton-duck work jacket.**
  - **Its colour (in the model):** an olive-brown, sun-faded paler on top and darker underneath. Mud is splashed on and
    rubbed in, worst near the hem and underneath. There is a grease mark on the outside, the crests of the folds are
    paler, and the hollows dark.
  - **Its shape:** the canvas bunched in soft ridges above the cuff, each going part of the way round, and creased at
    the elbow.
  - **Its UVs** now run in metres up the arm.
  - **Its shader:**
    - A plain weave, faded out wherever it would be finer than a pixel.
    - Uneven, slubbed threads.
    - Two felled seams up the sleeve, each a raised welt with two rows of stitching.
    - A double row of stitching round the hem.
    - A frayed hem edge: pale broken threads and grime.
    - Fine creases bunched above the cuff and at the elbow.
    - Dirt in the weave.
    - It is warmed against the cool light, so it stays olive by the lantern and in the dark.

## The arm's length and its walk
- **The sleeve ends at the elbow:** 25 cm of it from the hem, which puts the elbow about 31 cm from the wrist. It was
  42 cm.
- **The lantern** is held as a man holds one up to see by, the elbow bent, at (-0.22, -0.085, -0.48) m. Its foot is
  just under the view's edge, and only the forearm comes in from the lower left corner.
  - Its gathered-beam pose is now (-0.12, -0.06, -0.54).
  - Its thrust pose is now (-0.04, 0.06, -0.5).
- **The flare** is held at (0.23, -0.2, -0.45).
- **The arm swings from the shoulder** (`HeldItem.Shoulder`, off-screen, beside and below the eye). The stride's dip
  and sway, the run's fore-and-aft swing and the lag behind the view's turns are small turns of the whole arm about
  the shoulder. The hand moves on an arc and the sleeve leaving the view hardly moves.

## Held things: one convention (`HeldModels`, `FirstPersonHand`, `HeldTool`)
- **The convention.** The socket is the middle of the grip, +Y runs up the handle and -Z is the working end's way.
  - Every item model already followed it.
  - The hand's grip poses now follow it too, so each item drops in with no turn of its own.
- **New poses:**
  - **`Fist`:** the hammer grip, knuckles toward -Z. Its forearm is built turned back by the 30° the tools lean, so
    only the wrist cocks.
  - **`Cradle`:** the open hand, palm up, under something broad.
- **`Small`** (the lighter) and the flare use the new frame too.
- **One table** in `HeldModels` gives each thing its pose, where it sits in the grip, and how it's carried: where the
  fist is, then its lean (in the hand's own frame), yaw and roll.
  - **The axe, hammer and crowbar:**
    - The fist is down at the view's bottom edge, the handle and head rising out of the lower right (the owner's
      suggestion for the axe).
    - Each leans 30° forward and turns 30–35° in toward the middle, so the bit, face or claw reads in outline.
    - Each is held near its butt: the axe by its knob, the hammer by its butt, the bar by its straight end above the
      chisel. The bar's grip tape moved there.
  - **The knife:** a little higher, its blade forward and up 40°, the edge underneath.
  - **The walkie:** upright, its speaker to the eye.
  - **The lighter:** upright, close in.
  - **The camera:** by its end, the thumb now flat up its back toward the middle. It no longer lies over the top.
  - **The cap:** carried on the open hand under its plinth. The ball hid any hand round its neck.
- **`-- --grips`** (`hands_preview.tscn`) renders each held thing from the eye, by day and in the dark, to
  `test-output/grips/`.
- **The studio** (`--hands-studio`) draws each item's axes: +X red, +Y green, -Z blue.

## Sizes
- **The knife:** its blade is 16 cm and 3 cm deep, both in the hand and on the ground. It was 11.8 cm in the hand and
  23.5 cm on the ground.
- **The axe:** a 26" axe. The world one is 66 cm (was 74), with a 12 cm bit (was 17). The held one's head is its real
  size, 16 cm poll to bit (it was drawn at ×1.15).
- **The crowbar:** a 24" bar, 61 cm. The held one was 57 cm; the world model (81 cm) is now placed at ×0.75. Its
  prying motion shows it at ×1 (was ×0.9 of 81 cm).
- **The lighter's flick** shows it at its own size (was ×1.25).
- **The compass** is 6 cm across (was 10).

## Before and after
- **Every held thing, day and dark, plus the flare, the camera raising and the axe walking:**
  - Before and after shots: `test-output/hands_before16/`, `test-output/hands_after16/`.
  - Side by side: `test-output/before_after16_items.png`.
- **The poses and the lantern's hand up close:**
  - Before: `test-output/hand/poses_before16/`, `test-output/hand/before16/`.
  - After: `test-output/hand/poses_after16/`, `test-output/hand/after16/`.
  - Side by side: `test-output/before_after16_hands.png`.
- **Frames across a stride, walking and running:**
  - Before and after: `test-output/stride/before16/`, `test-output/stride/after16/`.
  - Side by side: `test-output/before_after16_stride.png`. To make these: `-- --stride=<tag>` on `body_preview.tscn`.
- **In the game (beat 13a):**
  - Before and after: `test-output/playtest_before16/`, `test-output/playtest_after16/`.
  - Side by side: `test-output/before_after16_playtest.png`.
- **Each held thing as carried:** `test-output/grips/`.

## Tests
- **Build:** 0 errors.
- **The hand tools:** the hand harness, the pose gallery, the look-book, the studio, `--grips` and `--stride` all ran
  clean.
- **`--playtest --playtest-only=0,13a`:** 2 of 2 beats, 48 of 48 checks, no engine errors.
  - The first run after the shader edit failed three of beat 0's timing checks (stamina, rebinding, the saved settings).
    Its minimum was 1 fps while the edited shader compiled cold.
  - The rerun passed.
- **The warm-up:** `HeldTool.Prewarm` already makes every pose for both sides, so the new `Fist` and `Cradle` are
  covered. It is still one shader.

## Left
- **The grey glove in one in-game shot.** In beat 13a's room the glove still reads more grey-brown than brown. The
  held things there are lit only by their own fills and the sky.
- **The flamethrower** keeps the Stick pose.
- **The keys and keycards** (only shown in a use) still use the Stick fist, not a pinch.
- **The lighter** is small in the view, at its real size.
- **The camera's body** is a little tall for a K1000: 11.3 cm with the prism, against 9.2.
- **The research behind the carries** comes mostly from the games themselves, not from sources that could be checked;
  see the note at the top of the research file.
