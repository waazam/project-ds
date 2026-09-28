# 2026-09-27i — A clean sky, and micro-surface detail on everything (in the PS2 style)

Commit: this entry's commit on `main`.

The owner:
- the skybox shows "creases and lines in it that break immersion";
- another texture pass on the clutter (plants, vegetation, books, wood, tables, chairs, all of it), using
  detail maps: a tiny repeating micro-texture laid over each surface so it looks detailed up close for
  next to no memory, from Poly Haven's free textures;
- "keep it in the style of our PS2 horror art design … classic and not hyper modern."

## The sky (`sky_dusk.gdshader`)
- **The creases came from its noise.**
  - The clouds' value noise showed its grid.
  - Its sin/fract hash broke into streaks where the cloud coordinates grow large toward the horizon.
- **Now:**
  - an integer hash (PCG), with no precision streaks;
  - gradient noise with a quintic fade for the clouds, with each octave turned so no grid lines up
    between octaves;
  - a gentler cloud projection toward the horizon, fading out before its coordinates stretch.
- The mountain ridges keep their broad, rounded crests: value noise, on the new hash. Gradient noise
  there made cliffs.
- A dev preview renders the sky alone from four directions (`sky_preview.tscn` → `test-output/sky`).

## Micro-surface detail (`DetailKit`, `DetailSweep`, `assets/textures/detail`)
- **Twelve detail maps** from Poly Haven (CC0; credited in `assets/textures/detail/README.md`):
  - wood grain, plaster, concrete, rusty metal;
  - fabric weave, leather, stone, marble;
  - bark, leaf and forest-floor litter;
  - general grime.
- **How each is made** (`tools/Textures/make_detail.py`):
  - The source's colour is turned to grey, and a wrap-around FFT high-pass removes everything broad,
    so only the fine grain is left and the tile stays seamless.
  - It becomes a multiplier near white (about 0.95 on average), so nothing darkens, with the source's
    normal map for relief.
- **Kept in the PS2 style** (the owner):
  - 256 px and soft;
  - the grain 25% weaker than the source's;
  - the relief shallow (normal strength 0.3);
  - the game's own procedural grime (stains, specks, pitting, hairline scratches) baked over each one
    at load, so the old character stays and the grain sits under it.
  - It adds depth up close and doesn't turn anything photoreal.
- **Where it goes:**
  - **The texture factories** (station, building, props, bunker, sewer, stairs, stairwell, boss, church,
    items, and the shared ones) pass their texture key, which names the kind: plank → wood, velvet →
    fabric, iron → metal, brick → stone, and so on. The old single grime layer is now this.
  - **Everything else** (the many props that make their own materials) is swept as it enters the
    world, classed by its node's name or its parents' (a Bookcase's Shelves, a Table, a Rail, a
    Curtain), falling back to grime. Books, ledgers and journals take the leather.
  - **The trees, plants and ground** have their own shaders, which now sample the detail themselves:
    - bark at three times the bark texture's frequency;
    - leaves faint over the foliage cards;
    - the forest floor's leaf litter within about 20 m of the eye.
- **Memory:** 24 small textures, 2.2 MB on disk.

## Tests
- Acts 1–2: 56/56, walking at an average of 102 fps.
- Acts 13–14: 90/90, 98 fps.
- Acts 19–21: 79/79, 146 fps.
- The detail costs next to nothing (a texture read or two per pixel).
