# 2026-10-14e — A man's gloved hand: the hands sculpted, not swept; a worn work glove; a cradle for the cap and a fist for the lighter; every held thing warmed at load

The owner: "hands still look like skeletor robot hands" (after "make the players hand look more realistic and less
robot" and "less bendy at the wrist and more strong").

## What was wrong
Looked at honestly, both hands, every pose, from the eye and close up, lit and dark
(`test-output/hand/before14e/`, `test-output/hand/poses_before14e/`, `test-output/hands_before14e/`):
- **Thin, even fingers with gaps between them.** Each finger was a swept tube of near-constant width. Open or round a
  grip, there was daylight between every pair, so they read as a row of rods.
- **A flat palm with lumps stuck on.** The ball of the thumb and the heel of the hand were separate ellipsoids, each with
  a visible outline, like a mannequin's.
- **Machine joints.** A ring seam at the wrist and round the thumb's root, and creases at every knuckle, read as the
  joints of a robot. The thumb was a long, even sausage.
- **A fist that didn't close.** Round the 3.2 cm stick, the fingers made a wide empty curl. In the Camera pose they were
  barely bent at all.
- **A latex glove.** Glossy, skin-tight and all one colour, it looked like a coating over bone.
- **The cap and the lighter.** The cap's 7 cm neck went straight through the Stick fist. The lighter floated inside it.

## The hands, rebuilt (`tools/Blender/fp_arms.py`)
No base mesh was used. Everything is still built in code, so nothing new goes in `docs/CREDITS.md`.
- **One signed-distance field per hand.** Each bone is a rounded cone at a real man's proportions:
  - the palm as wide as it is long;
  - short, thick fingers;
  - a heavy thumb whose metacarpal is buried in the ball of the thumb.

  The flesh is ellipsoids blended in with smooth unions: the ball of the thumb, the heel of the hand, the pad under the
  fingers and the back of the hand. Each finger bone has a fleshy pad underneath. The knuckles are soft bumps standing a
  couple of millimetres proud of the back of the hand.
- **The fingers are pressed side by side, with no gaps.** Their roots blend broadly into the palm, but between each
  other they only touch, so a crease separates them and nothing shows through. The grip solver now works with gloved
  thickness, so the middle and last bones lie on the handle and the fist really closes.
- **The glove.** The whole hand is inflated by 2.9 mm (3.2 mm as drawn), with slow lumps where the leather is slack.
  - **The cuff:** a snug gauntlet of the same leather grows out of the wrist and over the jacket's sleeve. The sleeve is
    tucked into it, its folds flattened there, with no gap.
  - **An earlier, flared cuff was cut.** From the eye it hid the right hand.
- **Meshing and detail.** Surface nets turn the field into a mesh. It is smoothed, subdivided once, and then marked:
  - folds bunched inside every bend;
  - soft wrinkles either side of the back of each knuckle;
  - fine, wandering slack wrinkles along the fingers;
  - two creases across the palm;
  - stitched side seams down the fingers, a seam round each fingertip's cap and round the thumb's root;
  - two darts down the back of the hand, and the cuff's welt.
- **Bake.** The detail and a pebbled leather grain are baked into a 1024 normal map, on a copy decimated to 16,000
  triangles. Ambient occlusion over 1.2 cm is baked into its vertex colours.
- **Leather colour.** Worn brown, with a slow mottle. It is rubbed paler over the knuckles and fingertips, and darker in
  the folds and seams.
- **Size.** About 16,000 triangles of glove and 4,000 of sleeve per pose (was 40,000). It builds in about 1.5 minutes a
  pose: `blender -b --python tools/Blender/fp_arms.py [-- handle stick ...] [-- fast]`.
- **Kept:**
  - every socket, grip axis and grip radius;
  - the forearm nearly in line with the hand;
  - the lantern's place and its handle.
- **Checked against photographs** of worn leather work gloves (Flickr, CC BY; used only for reference, not shipped):
  puffy rounded fingers, slack wrinkles, seams, a short cuff, pale scuffed leather when lit.
  `test-output/hand/compare14e.png`.

## In the engine (`FirstPersonHand.cs`, `HeldModels.cs`)
- `Dress` keeps a model's own baked normal map, and the leather is now matte: roughness 0.74, specular 0.3, a softer
  rim. A model without a baked map still gets the old three-sided grain.
- **Two new poses**, modelled like the rest and mirrored for the right hand:
  - `HandPose.Wide`: a hand cradling the newel cap's 7.2 cm neck, the fingers half round it and the thumb opposite.
    The cap now uses it.
  - `HandPose.Small`: a tight fist round a 1.9 cm upright. The lighter now uses it, raised so the lid and the top of the
    case stand above the fingers.
- **Nothing builds or compiles on a pickup.** `HeldTool` now makes every held model and every hand pose (both sides)
  once at the level's load and hands them to `ShaderWarmup.Prewarm`, which draws them in the black and then frees them.
  - **The knife's pickup in the station:** the one frame there fell from 1.19 s (3 pipeline compiles) to 40 ms
    (2 compiles). The 33 ms left is the model being built in code.

## Before and after
- The lantern's hand: `test-output/hand/before14e/` and `test-output/hand/after14e/`.
- Every pose, both sides, light and dark: `test-output/hand/poses_before14e/` and `test-output/hand/poses_after14e/`.
- Every held thing from the eye, and alone in the studio: `test-output/hands_before14e/` and `test-output/hands_after14e/`.
- Side by side: `test-output/hand/before_after14e.png`.

## Tests
- Build: 0 errors. The 16 warnings were there before.
- The hand harness (`--hand=after14e`), the pose gallery (`--hand=poses`, now 24 shots) and the hands look-book and
  studio: all pass (29 of 29).
- Playtest beats 0, 3, 4, 13a, 13b, 21 and 24a: 7 of 7, 167 of 167 checks.
- Playtest 13a and 13b with `--hitches`: the frames round the knife are above.

## Left
- The two compiles left at the knife's pickup are probably the mirrored right hand, which the warm-up draws unmirrored.
- The lantern's light catches the underside of the hand with a ragged edge. That is its shadow on the leather's bumps.
- Seen from the eye, the cap's hand is mostly its back, so the fingers round the neck barely show.
