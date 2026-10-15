# 2026-10-14h — The Hollow's cabin and shed, and the bunker, dressed (interiors phase 2)

The second interiors pass, after the global lighting (2026-10-14g, interiors lit like the woods) and the interiors kit
(2026-10-14g, `docs/design/interior-kit.md`). It covers three places from the look survey
(`test-output/survey/SURVEY.md`): the cabin in the Hollow (Acts 3-7, and its derelict copy in Act 25), the shed, and the
bunker (Acts 8-10). No story beat, puzzle, item, prompt, pickup or interactable moved; every collider is where it was.

## The cabin (`Cabin.cs`, `Cabin.Logs.cs`, `Cabin.Interior.cs`, `HollowLook.cs`)

| What looked wrong | What changed |
|---|---|
| Logs, floor, rafters, roof boards and furniture were one orange-brown value, lit evenly to the ridge. | Every part has its own surface and value (`HollowLook`): silver-brown logs, pale lime chinking, near-black dirty floor boards, smoked-black poles and roof boards, a dark hardwood cot, a stained shelf, a pale scrubbed crate, black cast iron. The room's dark is baked into its vertices with `RoomShade`: corners and joints, the roof black to the ridge (its two slopes are the bake's ceiling planes), soot over the stove, its pipe and the lamp, the dark under the furniture, the floor worn from the door to his table. The lamp is an oil lamp's yellow-white (1, 0.79, 0.6) at 1.75, not orange. |
| The logs read as flat planks (boxes with a log texture). | The logs are round: half an ellipse each side of the wall, a few centimetres proud of it, smooth-shaded and darkened in their vertices where they turn into the joint. A band of chinking sits back in each groove, so every course has a shadow over it and a pale line under it. The crossed corners show elliptical end grain. The outside halves cast the shadows; the inside halves and their chinking are the room's lining, tessellated and baked. |
| The stove, the crate and the shelf were boxes. | The stove is a cast-iron box stove: eased firebox on an ash lip, a top plate with two cook lids and their lifter notches, cast ribs, a framed door with hinge pins and a coil-spring handle, a round draft, four splayed legs. The crate is the modelled one. The shelf boards are bevelled and stand on cleats and braces. The cot stands on crossed legs with a slung canvas and a ticking pad. |
| The red blanket was a slab, the rug a flat card. | A grey army wool blanket (linen weave, a darker stripe at the foot). A braided rag rug of oval rings. A bucket, a broom, newspapers tied for the stove; soot, stains and a scuff by hand (the walls are logs: no plaster decal rules). |
| The lit window glowed from inside. | It was the glossy dark glass throwing back the lamp and the door's fill light. The inside glass is now dull and filmed (roughness 0.6). |
| The wall and the door shared a texture. | Still separate: the door keeps its green-painted ledged boards; the walls are now round logs of their own wood. |

The `Derelict` copy (Act 25) builds from the same code: dark, no papers, its door hanging.

## The shed (`Shed.cs`, `Shed.Tools.cs`)

| What looked wrong | What changed |
|---|---|
| One wood on everything (walls, ceiling, bench, pegboard). | The outside keeps its weathered barn red. The inside faces are bare rough-sawn grey-brown boards (never painted), the roof boards from below are dark, the bench top is black with oil, the floor is dusty pale boards, the pegboard is brown hardboard with its holes. The room is baked with `RoomShade` like the cabin. The studs have eased edges; the rafters are dark. |
| The tools were flat cards, the shovel a beam and a box. | Modelled tools, on steel pegs: a hand saw (tapered blade, a row of teeth, a closed wooden grip with brass screws), an adjustable wrench (handle, head, jaws, worm), pliers (jaws, pivot, oxblood dipped grips), a screwdriver (fluted handle, ferrule, shank, tip), the rope coil. A machinist's vise in chipped green paint with its screw and tommy bar. A block plane on the bench, spilled nails. The shovel has a turned handle, a dished blade on its socket; the rake has a head of tines. The gas can has a handle and a spout. |

The plank steps and the hammer are untouched.

## The bunker (`bunker_concrete.gdshader`, `BunkerTextures.cs`, `CrtRoom.cs`, `BunkerRooms*.cs`)

| What looked wrong | What changed |
|---|---|
| The hall's vault was a blotchy pink-orange concrete. | The concrete shader has a painted mode, measured in the mesh's own space: a dark green dado to 1.15 m, a black stripe, a pale grey-green over the vault; chalked, yellowed where oldest, flaked off in clusters (more at the damp foot and up the vault) with a lifted lip and a dark rim round each bare patch; rust bled down from the fixings beside some of the ribs and in thin runs; calcite weeping from the vault; the dried tide line of an old flood at the foot. The scan's own colour is taken to grey under it (`concrete_desat`). The hall floor is the same concrete, grey, with two faded yellow lines down it. |
| The CRT room's floor was one flat grey plane. | Old linoleum tiles in two dull greens and greys, worn and scuffed, whole clusters of them gone to the concrete and its combed black adhesive. The floor is a half-metre grid with the dark baked into it at the walls and under the shelving, and wet round two cast-iron floor drains (rust round them) and down the damp left wall. Loose tiles lifted and kicked about, concrete fallen from a spalled patch of ceiling, papers blown out of the filing cabinets, a heavy cable run from the fuse box round the room's edge. The way from the door to the console and the chair is clear. The walls take the hall's coat; the ceiling is bare with ring-edged water stains. |
| The CRT walls. | Untouched (they were the good part). |

The looping rooms of Act 10 take the same painted walls and grey floor. The title screen's TV room keeps its old
materials (`RoomWallMat`, `RoomFloorMat` are unchanged); the maze materials are unchanged.

## Cost

Look shots before and after from the survey's poses (	est-output/look/b_*, _*; contact sheet
	est-output/contact_hollow_bunker.png in the worktree). Primitives a frame are within 0.3 % of before in every pose
(cabin 11.72M vs 11.70M with the forest around it, CRT room 1.16M vs 1.15M, hall 0.81M both); the frame rate was the
same or higher in every pose (cabin 92-111 fps vs 86-102, shed 78-117 vs 85-96, bunker 124-140 vs 118-146).

Playtest beats 3, 4, 5a (the shed walked into, the hammer, the cabin's slam), 5b, 6, 7 (the cabin burning), 8 and 8c
(the bunker, the CRT room, the rooms, the run out) and 25-1 (the derelict cabin by the trail home): all pass, 0 engine
errors.

## Still to do

- The shed under the lantern at arm's length still reads warm brown: the lantern dominates a 2 x 2 m room.
- The porch timbers outside the cabin are now the warmest thing on it beside the grey logs.
- The friend's table and chair (FriendBody.cs) keep their own wood and are not in the bake.
- The hall's rib faces show the paint's pattern edge-on as pale arcs.

## Files

- `scripts/world/HollowLook.cs` (new): the cabin's and the shed's own materials.
- `scripts/world/Cabin.cs`, `Cabin.Logs.cs` (new), `Cabin.Interior.cs` (new): the round logs, the lining and the bake, the furnished room.
- `scripts/world/Shed.cs`, `Shed.Tools.cs` (new): the shed's inside, its bake, its tools.
- `assets/shaders/bunker_concrete.gdshader`: painted, lino and floor-line modes, concrete desaturation (all off by default).
- `scripts/world/bunkerparts/BunkerTextures.cs`: `HallWallMat`, `HallFloorMat` restyled; `PaintedRoomWallMat`, `LinoFloorMat`, `GreyRoomFloorMat` new.
- `scripts/world/bunkerparts/CrtRoom.cs`: the floor (`BuildFloor`).
- `scripts/world/bunkerparts/BunkerRooms.cs`, `BunkerRooms.Trim.cs`: the new wall and floor materials.
