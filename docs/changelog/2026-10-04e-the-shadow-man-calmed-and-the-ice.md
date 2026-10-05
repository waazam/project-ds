# 2026-10-04e — The shadow man's turns pulled back to the scary ones; the ice maze given baked high-res ice and snow; the maze wendigo's walk made smooth

## Act 15: the shadow man's red lights (`Act15Hallway.Variants.cs`)
- **The owner, after playing the twelve turns:** "the randomizations we made on the shadowman for the red lights just looks too cheesy and funny now, having him face down on the ground and coming in from the ceiling looked goofy, we should go closer to what we had before the change and make it as scary as possible."
- **Out:** from the floor, from above, the face-down slither, side to side and the zigzag are no longer picked.
- **The inch is most of them again** (44 in 100). The rest stay close to it, all of him standing, all of him coming:
  - the far lunges (12);
  - the stillness, far off and then right in front of them (11);
  - the looming, growing over them (11);
  - the melting eyes, face to face (9);
  - out of the wall beside them (7);
  - many of him (6).
- **The inch made scarier:** each jump closer, his eyes burn a little brighter and his breath is louder, the last one right on them.

## Act 24: the ice maze (`tools/textures/ice_maze.py`, `assets/textures/ice/`, `ice_cave.gdshader`)
- **The owner:** "Increase the texel density on the ice walls, Check the mipmaps on the snow shaders, Upres the snow cavern textures, bake way higher-res texture sets for the ice maze, we should make the tunnel more icy ... we could even use that ice texture you created for the back door of the ski lodge."
- **Baked texture sets, 1024 × 1024, seamless** (a new generator, `python tools/textures/ice_maze.py`):
  - **Glacial ice:** deep blue, banded where it froze in layers, cloudy-white patches, strings of trapped bubbles, faint fractures.
    - Its normal map carries the scallops (dimples about 25 cm across) and the cracks as fine grooves.
    - Its roughness map is glassy where the ice is clear and frosted where it's cloudy and along the cracks.
  - **The cave floor's snow:** packed and granular, bluish in its hollows, a crust here and there, with its own normal map.
- **Texel density:** the walls are mapped at about 410 texels a metre. Before, they were small noise textures stretched over the caves. A second, broad, turned sample of the ice is laid over the first, so the repeat never shows.
- **Mipmaps:** the new textures import VRAM-compressed with mipmaps, the normals as normal maps, and are sampled with anisotropic filtering. The snow textures already in the game were checked: all of them import with mipmaps.
- **Icier:** the walls take the lodge's iced back door's look: a glassy sheen (clamped, so it never glares off every dimple) and a cold rim at the ice's edges. A faint cold light comes from inside it where it's thin and clear. The icicles are the door's ice too.

## Act 24: the wendigo's walk (`WendigoHunter.cs`, `Wendigo.cs`)
- **The owner:** "act 24 is a big set piece so we need to make sure the wendigo animations are very smooth and terrifying as well."
- **It steers, not snaps.** It speeds up and slows down. It swings wide round the junctions' corners rather than touching each one and turning on the spot. Its body turns after its way (quicker when it runs). It slows to a stop at the end of a route.
- **Its legs keep to the ground.**
  - Standing still, it plays its idle (breathing, its head turning). Slow, it walks; fast, it runs.
  - Each gait plays at the rate its stride covers the ground it's really crossing. Its stride is scaled with it in the tunnels, where it's drawn at two thirds size.
  - Before, the walk and run played at fixed rates sized for its full height, so its feet slid, and it went on walking in place when it stopped.
  - The changes between gaits blend over a third of a second, with a margin so they don't flicker back and forth.
- **Spotting them:** it stops dead, turns on them and screams for a moment, then comes. Its footsteps fall with its stride.

## Tests
- **Act 15:** 25 of 25, its reds taking the turns in order: the inch, the far lunges, the stillness, the looming, the melting eyes, from the wall.
- **Acts 24 and 25:** 39 of 39, with the new ice and the new walk. Average 175 fps in the maze.
- **The screenshots** show the maze's walls in the game as faceted glacial ice, glinting under the lantern.
