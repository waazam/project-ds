# 2026-10-14f — Hands modelled like hands: a subdivided quad cage instead of a sculpted field; a worn leather glove in its own shader; a knitted storm cuff under a slimmer canvas sleeve; a real right hand; the tools' fists brought into view

The owner, a fourth time: "skeletor robot hands".

## What was wrong
Looked at from the eye, every pose and every held thing, lit and dark
(`test-output/hand/before15/`, `test-output/hand/poses_before15/`, `test-output/hands_before15/`):
- **Lumpy clay, not a hand.** The 2026-10-14e signed-distance sculpt meshed into a lumpy, blobby skin. The fingers of
  the Camera pose fused into a mitten; the lantern's fist was a brown lump with no readable fingers.
- **Ragged light.** Its normal map was baked onto a decimated triangle soup through smart-projected UVs: torn, flaky
  patches wherever the light grazed (the lantern's underside, the camera hand).
- **A pale, flat glove.** In daylight it read as one uniform tan.
- **The right hand was the left one scaled by -1**, so its baked normal map lit it inside out.
- **The tools' fists sat low** and pitched toward the eye; little but the sleeve and the back of the hand showed.

## The hands, rebuilt (`tools/Blender/fp_arms.py`)
Built the way game hands are modelled, all in code (nothing new in `docs/CREDITS.md`):
- **A low quad cage, subdivided smooth.** The palm is nine twenty-vertex sections from inside the sleeve to the
  knuckles: a narrow wrist, the heel of the hand, the ball of the thumb, a slight hollow in the palm, an arched back.
  Four eight-sided fingers grow out of the knuckles' end, side by side, with their webbing. The thumb grows out of the
  palm's thumb side over the heel, through a swelling section for the ball of the thumb.
- **Posed joint by joint** on the same skeleton and grip solver as before (every socket, grip axis and radius kept).
  Each joint's section is mitred so the finger keeps its thickness round a bend, the knuckles stand proud as they close,
  and the palm side creases. Fingers taper and have pads. Two levels of Catmull-Clark make one continuous skin with no
  gaps: about 14,600 triangles of glove a pose.
- **The glove's detail is baked from a finer copy** (four levels) with clean UVs: folds bunched inside each bend, soft
  wrinkles here and there along the fingers (no longer even rings, which read as a robot's segments), the fingers' side
  seams, the fingertip caps, the thumb's root seam, two palm creases, two darts on the back, the cuff's hem. Short bake
  rays, so pressed-together fingers don't bake each other.
- **Colour in the vertices:** worn, dull brown leather; rubbed paler over the knuckles and fingertips; darker in the
  creases and on the palm; a few soft scuffs; the baked occlusion.
- **The wrist hides under the jacket.** A knitted storm cuff hugs the glove's wrist, with a rolled edge, and runs back
  into a slimmer waxed-canvas sleeve with a turned hem.
- **The right hand is its own file** (`fp_hand_R_<pose>.glb`), the left's skin mirrored in the build. Its normal map
  lights it properly, and the warm-up now draws the very meshes the game shows.
- `-- look` builds only the shapes and renders previews coloured by part (palm, fingers, thumb); `-- cage` shows the
  raw cage.

## In the engine
- **`assets/shaders/fp_arm.gdshader`:** one shader for the three cloths, so a hand brings one pipeline.
  - **Leather:** the baked normal map plus a pebbled grain at two sizes (from the existing seamless cellular noise),
    a shade darker than the model's colours, smoother where it's worn so the lantern catches the knuckles.
  - **Knit:** ribs along the arm and stitches.
  - **Canvas:** faint slubs, dull (no sheen washing it out in the sun).
  - It uses the models' own tangents. `FirstPersonHand.Dress` gives every surface one of these.
- **`FirstPersonHand`:** loads the right hand's own model, and mirrors the left only if that model is missing.
- **`HeldModels`:**
  - The axe, hammer, crowbar and knife are held 1.5–2 cm higher, pitched 10–16° less toward the eye and yawed 4–6°
    less, so the fingers round the handle show above the sleeve.
  - The cap is held 3 cm higher and nearer.
- **The warm-up is unchanged** (`HeldTool.Prewarm` already makes every pose for both sides); it now covers the new
  shader and the right-hand files.

## Before and after
- Every held thing from the eye, light and dark: `test-output/hands_before15/` and `test-output/hands_after15/`.
- The lantern's hand: `test-output/hand/before15/` and `test-output/hand/after15/`.
- The pose gallery: `test-output/hand/poses_before15/` and `test-output/hand/poses_after15/`.
- Side by side: `test-output/before_after15.png`.

## Tests
- Build: 0 errors.
- The hand harness, the pose gallery, the hands look-book and the studio ran clean.
- `--playtest --playtest-only=0,13a,13b`: 3 of 3 beats, 76 of 76 checks, no engine errors.

## Left
- Seen end-on from the palm side with nothing in them, the fists' fingertips are round and alike.
- The camera pose's thumb lies over the top of the body rather than up its back.
- The cap's hand still shows more wrist than fingers from the eye.
- The leather grain is a tiling noise through smart-projected UVs: it jumps at island edges, which only shows very close.
