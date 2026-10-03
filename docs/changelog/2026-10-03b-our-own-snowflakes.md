# 2026-10-03b — Our own snowflakes: a generator in place of the licensed image

Commit: this entry's commit on `main`.

The owner: "let me find a better snowflake image so we dont need to deal with that [the credit]. Looks like people use
star texture generators", with a canvas star-generator shader to start from, "you may need to make some tweaks to
drastically improve it".

## What it was
- The weather pass's flakes were cut from a Freepik image whose licence asks for a credit ("Designed by Kjpargeter /
  Freepik"), carried in the end credits and `docs/CREDITS.md`.
- The pasted generator traced one star outline round the middle from a curve: a star, not a crystal.

## The generator (`assets/shaders/snowflake_generator.gdshader`)
- **Twelve-fold, like ice:** the angle folded six ways and mirrored (the pasted shader's `mirrored` sampling, taken all
  the way), so each flake is built once in a 30-degree wedge.
- **Built from distances to its parts:**
  - the arm, tapering to its tip;
  - branches off it at 60 degrees, longest partway along, with their own sub-branches parallel to the arm (the
    fern-like dendrites);
  - a hexagonal heart with an inner hexagon traced in it, ridges out along the arms;
  - small hexagonal plates at the tips, or the arm broadened into a ribbed blade (the sectored plate).
- **The ice:** a little clear across a face, fuller along every edge and ridge.
- **Six kinds** (stellar dendrite, fern, plate, sectored plate, star with tip plates, needle star), every proportion from
  `seed`, so no two flakes are alike. A `blur` for the defocused middle flakes, a `round_clump` for wet clumps and
  specks.
- **Baked once** (`creature_preview -- --bake-snow`) into the same atlas and curtain the weather already reads
  (`assets/textures/weather/snow_atlas.png`, `snow_curtain.png`): far too much to work out for forty thousand flakes a
  frame. Alpha only, white, so the weather's own lighting stays in charge (grey-blue in the haze, never a glare).
- **The atlas:** eight crystals (dendrites, ferns, tip-plate stars, a sectored star, a needle star), four defocused, two
  wet clumps, two specks. The plain plate was left out of it: a hexagon with spokes and a ring read as a spider's web.
- **The far curtain:** 900 tiny flakes from the generator, scattered and tileable.

## Subtractions
- The Freepik credit: gone from the end credits (`Act11Ending`) and `docs/CREDITS.md` (the snowflakes are listed there
  as our own, with how to re-bake them).

## Bug fixes
- `tools/Textures/make_snow.py` is the Poly Haven ground-snow tool again: the weather pass had overwritten it with the
  flake-cutting script. Restored from the commit before.

## Tests
- **The weather preview** (`creature_preview -- --weather`): the new crystals in heavy snow round the shelter, read as
  stars and dendrites up close and soft specks further off.
- **The full autotest:** 731/731 (Act 21's church and Act 22's road with the new flakes; the winter road 139 fps, the
  lodge 119 on this run).
- The continue test and the clip audits run with the next entry's work (nothing here touches a scene's geometry).
