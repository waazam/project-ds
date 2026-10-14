# 2026-10-14b — Acts 8-14: each part its own surface

The owner: "go through each scene and make everything look better. for example cabin wall and door shouldn't be the same
texture", and earlier: "make sure everything blends and looks good and not too boxy or robotic or disjointed."
This pass covers Acts 8-14: the bunker inside, the lake, the forester station and the stairwell. Each space was
photographed first, from the bunker's preview harness and from `LookShots` poses in the station, Room 3, the shaft and
on the shore. The worst problems were fixed first, then each space was photographed again.

## What changed

| Scene | What looked wrong | What changed |
|---|---|---|
| Station lobby (Act 13) | The floor, ceiling, wainscot, doors, door frames and furniture were all the same hinoki wood, a shade lighter or darker. | Each part has its own scan, projected in the object's own space (MeshKit meshes carry no tangents): a worn oak plank floor (`ph_wood_floor`), a plaster ceiling in old distemper (`plaster`), boarded wainscot (`old_planks`), casings and a chair rail in dark stained wood (`darkwood`), and doors painted an institutional green over an oak grain. |
| Station doors (Acts 13-14) | Room 1's and Room 2's doors were flat 5 cm slabs of the walls' boards. The front door was a slab with four boxes on it. | Real panel doors (`StationKit.PanelDoor`): stiles, rails, a lock rail and a muntin with eased edges, around raised-field panels, the same on both faces, with grime in the recesses. |
| Station door frames | Casings with knife-cut edges, one piece bent round the opening. | Eased edges, a deeper head than the legs, and plinth blocks at the feet. |
| Station lobby lamp | The plaster rose right over the globe blew out to a white disc. | The rose is a step darker. A pressed-brass gallery holds the globe and shades the ceiling, and the lamp now casts a shadow. |
| Station Room 1, Room 2 | Room 1's floor and ceiling were the walls' boards. Room 2's ceiling was a flat, untextured off-white, the brightest thing in the room. | The lobby's plank floor and plaster ceiling (darker in Room 1). Room 2 gets the plaster ceiling. |
| Room 3 (Act 14) | The corridor's floor and every ceiling were the walls' own riveted plate. The corridor lamp's shade was upside down (wide at the rod) and cast nothing, so the plate over each lamp went white. | The corridor floor is the hall's tread plate. The ceilings are a dark rusted plate. The lamps have flared enamel shades that cast shadows (double-sided, visible from below), so the ceiling sits in their shadow and the walls get pools of light. |
| Act 14 stairwell walls | No relief: a flat print of concrete under the lantern. The clean pour at the top went flat white a foot from the lantern. | `stairwell_concrete.gdshader` now uses each stage's normal and roughness map (clean, stained, grime), blended the way the colours are. The tangent frame comes from screen-space derivatives, so the shader needs no mesh tangents. A large rotated variation breaks up the scans' 2.5 m repeat. The walls are a step darker (tint 0.74). The grime's pits and pocks now show under the lantern and the blacklight. |
| Act 14 trench, shaft ceiling, chamber | The concrete scans sat on MeshKit box UVs with no tangents, so their relief was flat or lit from the wrong side. | Projected copies of the stairwell's concrete (`CleanProjectedMat`, `GrimeProjectedMat`). The shared `CleanMat` and `GrimeMat` that Act 21 and the sewer use are unchanged. |
| Bunker puddles (Acts 8-10) | Near-black ellipses at 75 % opacity read as holes in the floor or pools of ink, in the hall, the CRT room and the rooms. | Lobed, ragged puddles: a thin film at the rim and a darker, glossier middle, so they read as wet concrete. The damp blotches are browner and fainter. |
| Bunker hall and room floors | A cold blue-grey strip under warm brown walls; it didn't belong to them. | Warmer, toward the walls' own concrete. |
| Bunker side doors (AUX 2, AUX 3) | Frame and leaf were one painted steel, two shades apart, all slabs. A solid orange rust "spike" ran from the concrete lintel down the door. The chain was flat bars in a row, not touching. | The frame is bare, dark, oiled steel with eased edges. The leaf is painted, with a pressed panel and a scuffed kick plate, a lever rose and round hinge barrels. The rust is thin runs of different lengths weeping from under the stencil. The chain is oval links of round bar, each turned a quarter to the last, with a padlock on a shackle. |
| Bunker wall telephone | A black box with a smaller box on it, and three boxes for a handset. | A bakelite case with eased edges on a steel backplate, a rotary dial with ten finger holes, a hub and a finger stop, the empty cradle's prongs, and a handset with its grip and ear and mouth cups. |
| Bunker junction boxes, fire-hose cabinet, room plinths, vent grille | Knife-edged boxes. | Eased edges (`MeshKit.BevelBox`). |
| Lake shore (Act 12) | The banks were the small drawn textures with no relief: a pale, flat print of ground under the low dawn sun. The sand drifts were one flat tone. | `lake_shore.gdshader` lays the photographed forest floor and leaf litter through the duff and grass (55 %), with their relief projected from above. The drifts take the litter's light and dark, a step darker. |
| Lake bald cypresses | The foot was a straight cone from 1.25 m to 0.44 m, a tent against the dawn sky. Its flutes were straight struts across it. | The foot swells out in a curve, steep near the ground and quickly slender. The flutes follow the same curve. |
| Lake near miss (performance) | A one-off 137-147 ms hitch at the near miss, with no pipelines compiled. Every spray burst made its own material, quad and fade ramp, and the sounds' takes were read off the disk on the spot. | The spray material, its quads and the ramp are built once and shared (`LakeFx.Warm` in the crossing's load). The breach, foghorn, limb, creak and thoom takes are read in the load's black and held. No timing or gameplay changed. In this pass's playtest run the crossing logged no hitch. |

## Shared code touched
- `MeshKit.BevelBox` (new, additive): a box with its twelve edges chamfered. Six faces, twelve chamfers and eight
  corner facets, with UVs projected per facet.
- `StationKit`: `PanelDoor` (new). `DoorFrame`'s casings are eased and have plinth blocks, which affects every station
  doorway.
- `StationTextures`: `PlankFloorMat`, `CeilingMat`, `WainscotMat`, `DoorPaintMat`, `CasingMat` (new, projected).
- `StairwellTextures`: `CleanProjectedMat`, `GrimeProjectedMat` (new). `CleanMat`, `GrimeMat` and `StainedMat` are
  unchanged.
- `BunkerTextures`: `Puddle`/`PuddleOrm` (new shape), `Blotch` (fainter), `RustRun` (runs), and the hall and room
  floor tints.
- Shaders: `stairwell_concrete.gdshader` (relief, macro variation), `lake_shore.gdshader` (scans and relief). Both
  are used only by these scenes.
- `LakeCrossingEvent._Ready`: the warm-up only (LakeFx and the held sounds).

## A note for other passes
The baked surface sets' normal maps are imported as two-channel normal maps (`compress/normal_map=1`). A custom shader
that reads them with `texture()` gets no z and must rebuild it. Otherwise the relief comes out as black noise; the
first try at the stairwell did. Also, MeshKit commits no tangents, so a `StandardMaterial3D` normal map on its UVs is
lit in an arbitrary direction per face. Use a projection (`Uv1Triplanar`) or a derivative frame, as here.

## Tests
- `dotnet build`: 0 errors.
- `--playtest --playtest-only=8,8c,11,12,13a,13b,14 --hitches`: 158/159 checks on the first run. The one failure was
  beat 8's engine-errors check: the bunker keypad's new sounds, merged from main, weren't imported yet. After
  `--import`, `--playtest-only=8,8c`: 2/2 beats, 45/45 checks. Every other beat passed, the near miss included (3.4 m).
- Before and after shots: `test-output/gfx814/before/` (the bunker preview's `after/` and `bunker/` sets, `look/b11`-`b14`)
  and `test-output/gfx814/after/` (the same bunker sets, the station `a13`, Room 3 and the shaft `a14`, the shore
  `c12` and `d12`).

## Left
- The station basement and the iron door were not re-photographed in this pass. They use the shared door frames and
  `BevelBox` only through `DoorFrame`.
- The hall's papers (`FurnitureKit` "papers") still read bright on the floor.
- The rooms' small black wall plaque.
- The stairwell's clean top walls still go pale with the lantern a hand's width away.
- 1.19 s at the knife pickup in the station (`draw+rest`, 3 pipeline compiles). Logged by `--hitches`. It's the held
  item, most likely, not these materials. It was not measured before this pass, so it is left for the hands pass to
  check.
