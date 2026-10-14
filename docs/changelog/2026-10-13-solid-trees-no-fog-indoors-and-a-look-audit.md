# 2026-10-13 — Solid trees, no fog indoors, and a look audit

Three asks from the owner: "the trees don't look as good as they used to; at the beginning they look like paper; I think
it's a lighting issue", "there's fog inside some of the buildings", and a pass over every act for anything off the
game's look.

## The trees (`tree_solid.gdshader`, `foliage.gdshader`, `ProcTextures`, `ForestScatter`, `LightShafts`)
Why they read as paper:
- **One tone edge to edge.** The woods' ambient is a flat colour. A trunk in the canopy's shade, lit only by it, was the
  same tone across its whole width, a flat card.
- **Near black in the shade.** The 10-12 dark bark took the bark's tint to 0.6, 0.53, 0.47. Against the sunlit floor
  the shaded trunks went to black cut-outs.
- **Pale and flat in the sun.** The fine willow bark averaged to one beige under the sun.
- **Pale paper strips.** The light shafts near the eye were thin, crisp, bright bars standing among the trunks. The
  snags' bleached tops and the hanging moss glared pale off the dark trunks.

What changed:
- **Round trunks.** The ambient on a trunk falls off toward its silhouette, on its downward faces and in the bark's
  furrows. This is AO, so it darkens only the ambient light, never the sun.
- **The bark's own light.** In the sun and the lantern, the lit side falls off faster toward the trunk's edge, and the
  furrows stay dark where the light catches only the plates (a `light()` of its own).
- **Lifted out of black.** The bark's tint is lifted (1, 0.88, 0.77), and the direct light is held at 0.7 so the sunlit
  side doesn't go pale. In the shade the trunks show their bark and their roundness.
- **Crowns in depth.** The bough cards' ambient falls off toward the trunk (`inner_ao`). The sun comes a little through
  the needles (`translucency`).
- **The pale bits.** The snags' bleached tops are darker (0.66 at the top, was 0.9), and so is the deciduous bark. The
  moss beards are 0.62 (was 0.95).
- **The light shafts.** Wider and softer (1.8 to 4.5 m), fading in from 10 to 22 m (was 3 to 9), and fainter on the
  open trail (0.075, was 0.16).

## No fog indoors (`ForestAtmosphere.Enclosed`, `Shed`)
Why: the woods' fog, its volumetric light-fog and the drifting fog banks (a volume that rides with the camera) were cut
only in the church, the lodge and underground. In the cabin, the shed, the station, the library and the round room, the
outdoors came in with you: a haze across the room and wisps lit by the lantern.

Now every building's room or tunnel (AudioDirector's reverb spaces: every interior has one; the snow maze's open
trenches don't count) eases an `Enclosed` factor to 1 over about a third of a second. Inside:
- the fog banks are off;
- the volumetric fog is 0.0035, enough for a lamp's halo and a window's shaft;
- the exponential fog is 0.2 of the outdoor fog, and Act 1's and the winter woods' depth fog starts 16 m out;
- the outdoor ambient is 0.55 of what it was, so the rooms are lit by their lamps, windows and the lantern;
- the church keeps its own near-black haze, and the church and the lodge keep their own ambient.

The view out of a door keeps its fog. Underground keeps its black fog, which is its darkness. The shed had no room of
its own and now has one. `EnclosedClearsFog` turns all of this off, for comparisons.

## The look audit: fixed
- **The station lobby's paper** was near white under its lamp. It's aged a step darker (`StationTextures.WallpaperMat`).
- **The lodge's plaster and ceilings** went flat white by the lantern. They're a step darker, and so is the plain paint
  from 10-12.
- **Act 25's snow** at the trailhead: the bank and the drifts were faceted, like pale boulders. They're smooth and
  subdivided now.

## The lake's trees, and the firs everywhere (`ForestScatter.FirCrown`, `FirSprayMat`, `LakeDressing.Shore`)
The owner: "the lake shore firs look bad too."
- **Why they read as palms.** Every fir's bough was two big frond-shaped cards, up to four metres long. The lake's bald
  cypresses were sixteen sticks high on a bare trunk, with big sprays hanging off their ends that thinned to nothing
  with distance. Against the dawn sky, both read as palms or paper fronds.
- **The firs.**
  - Each bough is now a limb of wood out of the trunk. Along it are small sprays of needles: the photographed fir spray
    (`assets/textures/winter/fir_bough.png`, turned so its stem is at the card's root), a fraction of the old size.
  - Each bough has a leading spray and a pair fanning out to its sides, rolled apart so it has thickness.
  - Each tier keeps its dark core skirt.
  - The upper crown's short boughs leave out their limb and their side pair (they can't be seen).
- **The cypresses.** Their crown is the firs' crown now (`FirCrown`): whorls of boughs from a third of the way up,
  tapering to the top, in the cypress's brighter, yellower green, with the moss still hanging through it. Their fluted
  foot, roots and knees are as they were. The lake's dead trees (the snags) have no cards and were left as they were.
- **The cost.** About 1.5x the drawn primitives where the woods are thick:
  - Act 1: 5.3M to 7.8M;
  - the lake: 3.6M to 5.2M.
  The frame rate measured the same within its noise on this machine (100-180 fps in both).

## Tools
- `scenes/levels/look_shots.tscn` (`LookShots`) is a dev camera. It writes an act's save the way the story test does,
  continues into it on the test save slots (without the story test), and photographs the view from the spawn or from
  given poses into `test-output/look/`. `--look-ab` compares the tree shading on and off in the same frame;
  `--look-fogab` does the same for the indoor fog. Usage is in the class header.
