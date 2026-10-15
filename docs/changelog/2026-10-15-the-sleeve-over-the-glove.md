# 2026-10-15 — The sleeve over the glove: a darker jacket, charcoal-black gloves, no wrist showing

The owner asked: "make the player's coat ... cover the sleeve of the glove, and make the gloves darker grey or black".
He then narrowed it: no winter or hiker coat. Keep the v5 work-jacket sleeve (cotton duck, its seams, wear, shape and
length) in a darker colour, and keep the sleeve end covering the glove's gauntlet. The fix is in the same commit as this
file.

## Changes
- **The sleeve comes down over the glove** (`tools/Blender/fp_arms.py`, all 8 poses, both hands).
  - The hem now sits at the wrist joint (`HEM_AT`, 4 mm behind it), not 5.8 cm back. The glove's whole gauntlet is
    inside the sleeve: no knitted cuff, no wrist. The sleeve's far end stays where it was, so the forearm's framing and
    length are v5's. The bunched ridges above the hem moved with it, and the elbow's creases stayed at the elbow.
  - The jacket's ribbed knit storm cuff (`knit_cuff`) is gone from the models. The shader keeps its `kind 1` branch for
    any older model that still has an `fp_cuff` surface.
- **The jacket: a very dark olive-brown** (`SLEEVE*` in `fp_arms.py`). It is about 60 % as bright as v5's olive canvas
  and a little browner. It's the body's own dark olive jacket (`player_body.py`), worn. It reads as dark against the woods
  and stays brown-olive under the lantern rather than black, so the black glove still stands out against it. The hem's
  stitching and frayed threads are toned down to match (`fp_arm.gdshader`).
- **The gloves: charcoal-black winter work gloves** (`GLOVE*`, the new `GLOVE_PALM`; `fp_arm.gdshader`).
  - Dyed charcoal-black leather over the back, rubbed to a worn grey over the knuckles and fingertips, with grey scuffs,
    scratches and grey thread at the seams. The palm is a blacker synthetic grip panel.
  - In the shader, the back keeps a low sheen and a rim (0.2) so the silhouette and fingers don't sink into the dark. The
    palm is matte (roughness 0.86) with a finer grain. A slight warmth stops the black reading navy under the cool rim
    light. A stronger sheen (0.3 specular, 0.32 rim) was tried and washed out silver under close light.
- **The flamethrower's hands on the held layer** (`Flamethrower.cs`). The lantern hangs at the belt while both hands are
  on the wand, and its real light, an inch under the front hand, blew the glove and sleeve out to white. That was already
  happening with the brown glove, which went tan, but the black glove read silver. The hands are now out of that light,
  like every other held thing, under a warm fill of their own (`HandLight`, held layer only).

## Not changed
- Poses, grips, the carry table and every held item's placement. The sleeve's hem is the v5 sleeve's radius
  (3.7 × 2.9 cm), which clears the glove at the wrist in every pose.
- Shader warm-up. There are no new shader variants: the glove's palm and sheen are uniforms and vertex data in the same
  `fp_arm` shader, and `HeldTool.Prewarm` already warms every pose for both sides.

## Verified
- `--hand=poses` (every pose, both sides, lit and dark), `--grips` (every carried item, day and dark), the hands
  look-book (29/29 checks; every item ahead, up, down, dark, blacklight and walking, the camera raised, the flare, the
  flamethrower), `--stride` (walking and running, lantern and axe). Each was shot before and after: no sleeve clipping
  into the lantern's bail, the camera, the knife, the long tools or the flamethrower, and no wrist showing.
- `--playtest --playtest-only=0,13a`: 2/2 beats, 48/48 checks.
