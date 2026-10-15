# 2026-10-15 — A navy jacket, a grey rib cuff, charcoal-black gloves

The owner first asked for a hiker's winter coat whose sleeve covers the glove, and darker grey or black gloves. He then
changed it twice. The second commit of the day is the final state:

- **The jacket:** a worn, muted dark navy-indigo canvas. It's the v5 work-jacket sleeve: the same shape, length, seams,
  wear and dirt.
- **The cuff:** the v5 ribbed knitted cuff, back between the sleeve and the glove, in a dark charcoal-grey rib.
- **The gloves:** charcoal-black.

The first commit (the sleeve over the glove, an olive-brown sleeve) was undone by the second, apart from the gloves, the
lighting work and the flamethrower fix below.

## Changes
- **The jacket: a worn dark navy** (`SLEEVE*` in `tools/Blender/fp_arms.py`, `fp_arm.gdshader`).
  - Sun-faded paler and greyer on top, darker underneath, with mud and a grease mark. Light and blue enough to read as
    blue, not black, under the lantern and in daylight.
  - The shader's old warm bias on the sleeve is now neutral. It was there to keep v5's olive canvas olive, and it turned
    the navy grey.
- **The knitted storm cuff restored** (`knit_cuff`, v5's geometry and hem position). Dark charcoal-grey wool (`CUFF*`),
  a shade lighter and warmer than the glove so the two read apart.
- **The gloves: charcoal-black winter work gloves** (`GLOVE*`, `GLOVE_PALM`).
  - Charcoal-black leather on the back, rubbed to a worn grey over the knuckles and fingertips, with grey scuffs,
    scratches and grey seam thread.
  - The palm is a blacker synthetic grip panel: matte (roughness 0.86) with a finer grain in the shader.
  - The back keeps a low sheen and a rim (0.2), so the fingers and silhouette don't sink into the dark.
- **The arm's own lighting** (`fp_arm.gdshader`, a `light()` function).
  - **The problem:** the held things are lit by warm orange fills (the lantern's hand light) and a cool rim. Under those
    the right glove read tan-brown and the left grey, and the warm fills lifted the sleeve's colour.
  - **The fix:** the shader now does the engine's lighting itself (Lambert diffuse, GGX specular, the rim) but first takes
    part of each light's colour out: 75 % for the glove, 60 % for the cloth (`glove_neutral`, `sleeve_neutral`). A very
    strong light close by keeps its colour, so the lantern's glow under the hand stays warm instead of clipping to
    cream-white.
  - It is still one shader with uniforms, so there are no new variants to warm up. `HeldTool.Prewarm` already draws every
    pose for both sides.
- **The flamethrower's hands on the held layer** (`Flamethrower.cs`). With both hands on the wand, the lantern hangs at
  the belt, and its real light, an inch under the front hand, blew the glove and the sleeve out to white. That was
  already happening with the brown glove, which went tan; the black glove went silver. The hands are now out of that
  light, like every other held thing, under their own warm fill (`HandLight`, held layer only).

## Verified
- Before and after, each lit and dark: `--hand=poses` (every pose, both sides), `--grips` (every carried item), the hands
  look-book (29/29 checks) and `--stride` (walking and running).
- `--playtest --playtest-only=0,4,13a` (the forest's storm walk, the station basement): 3/3 beats, 64/64 checks. The
  sleeve reads navy, the cuff grey and both gloves charcoal in the forest and the basement.
