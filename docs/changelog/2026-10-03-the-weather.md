# 2026-10-03 — The weather: thick snow in three layers, a blizzard that takes your sight, shared wind, rain, smoke and air

Commit: this entry's commit on `main`.

The owner: "The snow and other particle effects in the game need to be drastically improved... a thicker amount of
snow on screen". I suggested building our own weather layer on Godot's GPU particles rather than a whole new engine
system, and they took it. They added their snowflake image ("make variations of so the snow doesn't look like one
repeating pattern"), and asked for "a blizzard mechanic for later that acts like the fog in the way it restricts the
player's fov (vision)", play tests of the snow sections, debugging, and a push to main.

## What it was
- **The snow:** 5,000 plain white squares, all one size and speed, spread through a 68 m box: about one flake every
  14 cubic metres. It could never look thick, and it read as confetti.
- **Around the church:** four more fixed sheets of it.
- **The rain:** two emitters that trailed behind as you walked.
- **The smoke:** one static puff, switched off in places for showing as a red cloud.
- **The embers:** squares.

## The snowflakes (`tools/Textures/make_snow.py` → `assets/textures/weather/`)
- **Cut from the owner's image:**
  - The image had its transparency baked in as a checkerboard of two greys. That background is regular, so each
    pixel's coverage comes back as how far it stands above its own square's grey.
  - Real snowflakes are six-fold symmetric, so the crystals are picked by how well each matches itself turned 60
    degrees. That throws out the dots, the stray lines and the flakes lying across each other.
- **The atlas** (16 cells of 128 px):
  - eight crystals, each made different (mirrored, turned, the arms thinned or thickened);
  - four defocused crystals;
  - two wet clumps;
  - two soft specks.
- **The far curtain's texture:** hundreds of the flakes shrunk to a few pixels and scattered, tileable.
- **The credit:** the image's licence asks for "Designed by Kjpargeter / Freepik". It's in the end credits now
  (`Act11Ending.CreditSnowflakes`) and in the new `docs/CREDITS.md`. The source image stays out of the repository,
  as its licence asks.

## The weather layer (`Weather.cs`, one per level, made on demand)

### The wind, shared
- A direction and a speed, and slow gusts rolling through (two swells, never a jerk).
- It's published as the shader globals `wind` and `wind_gust`. The snow, the rain, the far curtain, the fires' smoke
  and the trees' sway (`tree_wind.gdshaderinc`) all move with the same air.
- At the default breeze the trees sway exactly as before. In a gale they lean further, capped.

### The snow, in three layers by distance
- **One particle shader** (`weather_particles.gdshader`) for all of it. Each layer lives in a box that rides with the
  camera and wraps round it: a flake that drifts out of one side comes back at the other, and one that falls out of
  the bottom starts again at the top.
- **Equally thick everywhere:** however fast you go, nothing trails behind you and nothing pops in ahead.
- **Each flake its own:**
  - its own fall (0.6–1.4 m/s);
  - a lazy flutter;
  - a tumble;
  - its atlas frame.
- **The wind carries them,** the light ones swirling more on the gusts.
- **Near:** 1,400 flakes within 4.5 m, the crystals, 2.5–5 cm, faded out within half a metre of the eye so nothing
  smears across it.
- **Middle:** 42,000 smaller ones out to 15 m (the defocused crystals, the clumps, the specks).
- **Far:** a moving curtain of flakes on two rings round the camera, at 15 m and 24 m (`snow_curtain.gdshader`):
  - two sizes and speeds, for parallax;
  - carried by the wind, sinking into the fog;
  - drawn only out in the open (under a roof a great hall's ring would stand inside the hall).
- **Lit, cold grey-blue, never white:**
  - Each flake's faint glow follows the scene's own haze, a little over it, so the snow reads at dusk and stays dark
    at night except where a light falls (the lantern's warm glow on the flakes round you).
  - The first in-game test showed the middle flakes vanishing into the grey sky, which is why.
- **Gentle:** in a gale the flakes streak a little, never long.

### No snow indoors, under roofs or under the firs
- **Roofs and boughs:** a heightfield collider rides with the camera. What lands on the ground, a roof or a bough
  starts again at the top, unseen until it's under open sky.
- **Interiors registered as boxes** (`Weather.RegisterShelter`), where nothing shows. The church's nave vaults to
  about 38 m, and its roof isn't seen from above by the collider.
  - The church: the nave and aisles to the apse, the transepts, the vestry.
  - The ski lodge.
  - The forest acts' cabin (RainVfx's shelter).
- **Bugs found playing:**
  - snow fell inside the church: the collider's reach and the respawn height, then the inward-facing vault;
  - all fixed, and Act 21's church is dry inside again, with the snow falling past its windows from outside.

### The blizzard (`Weather.SetBlizzard`, a mechanic for the story to use later)
- **The snow** at its thickest, driven sideways by a 6.5 m/s gale with heavy gusts. The far curtain becomes a
  whiteout, and the gale's roar rises (`storm_wind_loop`).
- **The view closes in like the fog** (`ForestAtmosphere.Blizzard`):
  - a cold grey snow-fog, mid-grey, not a white glare;
  - its reach down to about 24 m (`BlizzardSightMetres`);
  - thicker light in the fog, the sun dimmed;
  - in Act 22 the sight goes from 46 m to 24 m and back.
- `Weather.VisibilityMetres` gives the sight for the story and the tests.
- `--blizzard` turns it on for a run.

### Where it falls
- **Act 21:** round the church, through the windows from inside.
- **Act 22:** heavy on the road out of the church, thinning to nothing by the frozen ground at the lodge.
- **Act 23:** the lodge's broken windows, the draughts, the drift that buries the back door, and the wendigo's snow
  dump off the boughs all use the flake atlas now (`Weather.FlakeMaterial`).
- **The ice crystals near the lodge** are the atlas's crystals, tiny, lit.

## The rain
- **The streaks are Weather's** (RainVfx keeps its splashes, the wet ground and the lightning). They wrap round the
  camera, slant with the shared wind, never fall under a roof or the trees, and are drawn out along their fall.
- **Still faint** (the owner: seen through, never a curtain), and a touch fewer than full, as asked in September.

## Fire, smoke and embers (`FireVfx`)
- **The smoke:** a flipbook (`tools/Textures/make_smoke.py`) plays through each puff's life, billowing from a dense
  knot into a thin ragged wisp. It drifts off with the wind and stays soft where it meets things.
- **The embers:** short sparks streaking along their flight (`weather_spark.gdshader`), not squares.

## The air (`AirParticles`)
- **Dust indoors and spores in the woods:** on the same wrapping layers, so the air is as thick wherever you walk.
  The dust is still drawn only where light falls.
- **Your breath in the cold:** a soft puff of vapour from the smoke's flipbook, billowing and thinning.

## Also
- **The stalker's sides:** it now prefers sides it hasn't come from lately (its rule: it moves around, so it comes
  from all round you). With only three peeks on the storm walk it had sometimes come back to the same side, which
  failed the test now and then.

## Tests
- **Play tests of the snow sections:**
  - Act 21 (the church): 28/28, dry inside, the snow beyond the windows.
  - Act 22 (the road): 43/43:
    - the snow's three layers, sheltered;
    - the blizzard closing the sight from 46 m to 24 m;
    - the view opening again after.
  - Each was looked at in its screenshots and tuned:
    - the near flakes made smaller and faded sooner;
    - the far snow's glow tied to the haze;
    - the church's snow fixed.
- **The weather preview** (`creature_preview -- --weather`, `-- --shelter`): light and heavy snow, under a roof, the
  blizzard, the lantern at night, the rain, a fire's smoke and sparks.
- **The full autotest:** 726/726. New checks:
  - the rain's streaks are the weather's;
  - the snow's layers and shelter;
  - the blizzard and its passing.
- **Frame rates while walking:** 150 fps on average over the whole game; the church 167, the winter road 164 (was
  166), the lodge 165, the storm walk 98.
- **The continue test:** 925/925, 34 scenarios.
- **The clip audits:** the forest acts 40 z-fighting pairs, the winter 14 (both as before), no walk-through props, no
  UV faults.
