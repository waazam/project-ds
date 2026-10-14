# 2026-10-14a — The early acts made over: the cabin's door, the timber, the trailhead

The owner: "go through each scene and make everything look better. for example cabin wall and door shouldn't be the same
texture", and before it, "make sure everything blends and looks good and not too boxy or robotic or disjointed."
This pass covers Acts 1-7: the trailhead and the car, the trail and its signs, the camp, the cabin inside and out, the
shed, the footbridge, the woodpile. Every space was photographed with `LookShots` from fixed poses before and after.

## Why so much of it looked the same
Since the 2026-10-10b wood pass, all the buildings' wood was one scan: the clean, new hinoki planks, projected at 14 cm
a board. The cabin's walls, its door, its gables, its floor, its porch posts, rails, deck and steps, its rafters, the
shed's walls and door, and the footbridge all drew it (`BuildingTextures.BoardsMat`, `FloorMat`, `PropTextures.PostMat`,
`DeckMat`, `WetPostMat`). Under the cabin's lamp and the lantern it read as one sheet of new orange pine: the door
vanished into the wall, and the posts showed plank seams running up them.

## What changed

| Scene | What looked wrong | What changed |
|---|---|---|
| The cabin's door (Acts 3-7) | The same planks as the walls and the gables: a flat slab with two strips and a bar, a box for a latch | Its own door: six upright boards, each its own shade and a hair proud of the next, with dark gaps; an old bottle-green paint worn through to grey wood at the foot and in flakes; ledges and a Z brace on the inside; long tapering strap hinges with nail heads and their knuckles at the jamb; a thumb latch with its cusped plates, bar and keeper; a keyhole plate |
| The cabin's door and window casings | The porch's post wood | Painted the door's green, thinner and more worn; a drip cap over the door head and each window head; the sashes painted to match |
| The cabin's gables | The door's and floor's planks | Bare, grey-brown weathered upright boards |
| The cabin's porch: posts, rails, balusters, deck, rim, skirt, steps, bench | New orange lumber with plank seams up every post | Grey weathered timber, grain along each member (no seams); the deck and treads grey boards; the skirt weathered boards; a knee brace off each post into the header |
| The cabin's logs (inside and out) | Orange pine under the lamp, the same tone as everything else | The cabin's own log material, aged to a grey-brown; the lamp a touch less orange (1, 0.7, 0.45) and 1.9 (was 2.1) |
| The cabin's floor, rafters and roof boards | The same planks as the walls | A dark old floor, boards front to back, stained; the rafters, beams and the boards under the roof smoked dark by the stove |
| The cot | A flat red slab for a blanket and a box for a pillow | A blanket crowned over the mattress and hanging down over the rails and the foot, its turned-back fold rolled; a soft lofted pillow |
| The stove pipe | Six-sided, its elbow open | Round, with a collar at the stove, a joint, a damper handle, a closed elbow and a thimble at the wall |
| The shed (Act 5) | Walls, door and roof the cabin's planks; a pale concrete box for a step; concrete piers; an eight-sided disc for a coil of rope | Board-and-batten walls in old barn red almost all weathered off; a red door with more of its paint; battens, corner boards and studs in the weathered timber; the soffit grey boards; fieldstone piers; a flat fieldstone set into the ground for the step; a coil of rope in three loops on an iron peg; a pegboard of its own |
| The trailhead's vault toilet (Act 1) | One pale concrete cube with a flat tilted lid, a steel slab for a door and a stick for a vent | A poured-concrete building (the varied concrete: pours, rain streaks from the eave, grime at the foot) on a pad, a darker splash course and a cap course; a shingled gable roof with fascia, rake boards and a ridge cap over brown board gables; a steel door in its frame with a louvre, a kick plate, a lever, a deadbolt and hinges; a screened vent high in each side; the vault's vent stack up the back with its cap and straps; RESTROOM on a routed board in the gable |
| The car (Act 1) | A stack of boxes: flat flanks, the wheels sunk in them, ten-sided drums for wheels and a flat bar for each arch | The body sliced along its length so it has a shape: the nose and tail rounded, the bonnet falling to the nose, an arch cut round each wheel, the flanks rolled in at the shoulder and the sill; round tyres with rolled sidewalls, dished steel wheels with a hub cap and five nuts, the arch liners curved; the greenhouse, hatch and load space as they were |
| The park's signs: the trailhead sign, the fork's and the mile signposts (Act 1) | A 64 px painted texture: up close its flakes were blocky pixels | Park-brown and green enamel over boards (the new painted-boards shader), worn at the flakes' edges, grain along each board |
| The footbridge (Act 6) | New orange lumber, the same as the cabin | Damp weathered timber: planks grain across the deck, stringers and rails along it, the posts dark with wet at the foot |
| The camp's stump (Act 3) | A ten-sided drum in a flat, dark procedural bark | The photographed bark, 22 sides, roots flaring into the ground in lobes, a lip of bark over the old grey cut |
| The stranger's pack (Act 3) | Three red boxes | A stuffed, rounded body, a bulging front pocket and a slumped lid flap, in a duller red |
| The woodpile (Act 5) | Six-sided sticks in the flat bark | Nine-sided rounds in the bark scan, their end grain inset |

**Looked at and left as they were:**
- **The bunker's face and its approach (Act 7).** Already in the mood; the keypad and its wall are another pass's.
- **The clearing's stairs (Act 6) and the lookout (Act 7).** Nothing out of place from the poses taken.
- **The fresh planks nailed over the door (Acts 3-5).** Left pale and new on purpose: the friend boarded it a day ago.
- **The woods, the trees and the ground.** The 2026-10-13 tree pass already.

## Shared code (focused)
- **New `assets/shaders/painted_planks.gdshader`:** the weathered brown planks scan (already in
  `assets/textures/surfaces`) projected three ways in the mesh's own space, the boards up or along, under a coat of paint
  that fades, chalks and flakes to grey wood, with grime and splash from a foot height. `across` < 1 keeps a member in one
  board of the scan (grain only, no seams). The normal map's z is rebuilt (the maps import two-channel).
- **`BuildingTextures`** (additions only; nothing existing changed): `PaintedBoards(...)` and the materials made with it
  (`CabinDoorMat`, `CabinTrimMat`, `GableBoardsMat`, `ShedWallMat`, `ShedDoorMat`, `TimberUpMat`, `TimberAlongMat`,
  `SmokedTimberMat`, `SmokedBoardsMat`, `PorchDeckMat`, `CabinFloorMat`, `BridgePlankMat`, `BridgeAlongMat`,
  `BridgePostMat`, `SignBrownMat`, `SignPostBrownMat`, `SignGreenMat`), `CabinLogMat`, `BarkScanMat`. The shared
  `BoardsMat`, `FloorMat`, `LogMat`, `PostMat` and `DeckMat` are untouched, so the lodge, the station, the lake and the
  bunker look as they did.
- **`ParkProp`**: `VaultToilet`, `Car` (the body and the wheels), `Backpack`, and the trailhead sign's materials.
  The car is the same car in Act 25's endings.
- `SignPost`'s paint materials.
- `BarkScanMat` takes the bark scan's colour and roughness but not its normal map: MeshKit writes no tangents, so a
  normal map on a lofted mesh's own UVs would light from the wrong side. The painted-boards shader builds its own frame.

## Cost
No new texture files. The painted-boards shader samples one scan three ways (9 taps) plus a noise. Primitives on the
cabin's poses: within 1 % of before; frame rates on this machine unchanged within their noise (90-130 fps at the cabin,
150-230 at the trailhead). Everything new is built with the level, so the load's shader warm-up covers it.

## Shots
`test-output/look/a443/`: before `cabin_before/`, `toilet_before/`, `trailhead_before/`, `r1_bridge/`, `camp_close/`;
after `cabin_after4/`, `cabin_door4/`, `cabin_props2/`, `final_shed/`, `final_toilet/`, `final_trailhead/`, `trailhead_after2/`,
`bridge_after1/`, `final_camp/`, `fork_after/`. (No before shots of the shed were kept.)

## Tests
- `dotnet build`: 0 errors.
- `--playtest --playtest-only=1,1b,1c,2,3,4,5a,5b,6,6b,7`: 11/11 beats, 171/171 checks, 0 engine errors.
