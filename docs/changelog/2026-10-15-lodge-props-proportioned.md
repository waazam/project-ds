# 2026-10-15 — The lodge's props, proportioned and rounded (Act 23)

The owner: "do another pass on all props like lamps on the table etc and make sure they're proportioned and not too
blocky looking. For example the green lamp in the room." The lodge's own pieces (the ones not modelled in Blender) were
drawn as boxes: the reception's green lamp was the bedside lamp's tall pleated shade on a stick, the nightstands, the
chest, the vanity and the pantry's cupboards were plank-textured boxes with their drawers standing 15-25 cm out of them,
the bathrooms' tub, basin and lavatory were mint boxes, the sconces a box plate and a straight stick. They are rebuilt to
real measures in a new lodge-only kit, `lodgeparts/LodgeProps.cs`: turned (lathed) where the real thing is round,
rounded slabs and eased edges where it's joinery, a PS2 budget (8-18 sides to a turned piece).

Before and after: `test-output/look/before*` and `test-output/look/after*` (the runner is `test-output/lodgeprops/pose.sh`,
poses in the lodge's own frame), the paired contact sheet `test-output/lodgeprops/contact_props_1.png`.

## The kit (`lodgeparts/LodgeProps.cs`, new)

- Geometry: `Loft` (a surface through rings, normals from the profile, smooth unless it bends sharply), `Lathe`,
  `Ring`, `RoundRect`, `RoundSlab` (a rounded-corner top with its edge eased), `Cap`, `Zip` (a flat band between two
  rings of any counts: a counter's top round a basin's rim), `PictureFrame`, `SplitLog`.
- Pieces: `TableLamp`, `BankersLamp`, `CeilingDome`, `Sconce`, `TableCandle`, `DrawerFront`, `Knob`, `Carcass` (case
  furniture built round its drawers so they sit flush), `CabinetCase`, `CabinetDoor`, `WritingDesk`, `Tub`,
  `BasinTop`, `Lavatory`, `FireTools`.
- New material: `CrockMat` (a brown drip-glazed stoneware for the lamp bases), `OpalMat` (the banker's lamp's lit
  lining).

## What changed (was, a real one, now)

| Piece | Was | Real | Now |
|---|---|---|---|
| Reception's green lamp | the bedside lamp in green, 45 cm: a 36 cm pleated empire shade on a 6 cm brass stick | banker's lamp ~40 cm, shade ~25 cm long | a banker's lamp: an oval stepped brass base 26 x 16, a turned upright, a cradle, a 25 cm half-cylinder of cased green glass with a lit opal lining, a pull chain; 41 cm |
| Nightstand lamps (rooms, 201's), the stair pier's | 50 cm (42 on the pier): a tall pleated shade on a thin reeded column | 55-70 cm, drum shade 35-40 cm | 60 cm (55 on the pier): a glazed crock base like a ginger jar (17 cm across), brass neck and socket, a 38/33 cm drum shade 27 cm tall with its trims and a finial; still on the room's switch |
| Nightstands | 50 x 60 x 45 plank box, the drawer 15 cm out of it | ~50 x 60 x 40-45 | 50 x 60 x 48 on four turned legs, the top overhanging 1.5 cm with its edge eased, the drawer flush in a face frame, an open shelf under it |
| 204's writing desk | a 5 cm plank on two slabs, the drawer hanging under it | top ~75 cm, 60 deep | 120 x 64, top 76.5 cm (rounded slab, eased edge), an apron with the drawer in it, four legs square at the apron tapering to turned feet |
| 204's chest of drawers | 120 x 110 x 50 box, the drawers 22 cm out | | the same size on turned feet, the drawers flush between rails, its top overhanging |
| Wardrobes and closets | a box under a gilt slab, the door a plank 4 cm off its front | | a case of boards (panelled sides), a moulded three-step cornice, a base rail under the door, a brass hanging rail inside, the dark of it; the door frame-and-panel (two raised panels), a turned knob, its hinges |
| Bathroom vanity | 90 x 84 x 50 box, a mint slab for a basin, drawers 18 cm out | ~85 cm top, ~55 deep | a case on a plinth round its two flush drawers, a white top 94 x 72 at 90 cm run back to the wall with an oval mint basin let into it, a drain, a spout and two cross-handled taps; the mirror in a wooden frame |
| Tub | a mint box 180 x 56 x 80 with a black lid | | the same footprint: its apron rounded at the corners, a rolled rim, the basin sloping in to its floor, a drain, spout and taps |
| Lavatory | two mint boxes | rim ~40 cm, ~70 deep | a pedestal flaring to an oval bowl (rim 40 cm), a wooden seat and lid, a rounded cistern with a stepped lid and a brass lever; 70 deep |
| Rooms' ceiling light | a gilt drum, never lit | | a frosted dome on a brass canopy with a finial, 37 cm, lit with the room's switch (203's dead) |
| Sconces (hall, corridor) | a box plate, a straight stick, a cone | | a turned wooden pattress, a brass boss, a swan-neck arm, a cup, a flared shade (the bulb where it was) |
| Front desk | a 320 x 110 x 60 box under a gilt slab | counter ~105-110 | a plinth, six raised panels between pilasters, the ends, a 5 cm top with its edge rounded and a hand over the front, a brass rail under it; top still 1.13. The ledger an open register (boards, page blocks, a ribbon), the bell turned |
| Bar's back bar | a box, three planks, a mirror with a gilt bar on top | | panelled cupboards in eight doors under a bar top, shelves with a lip on brackets, the mirror in a gilt frame with a moulded cornice |
| Bar tables' candles | a glowing tube | | a net-glass candle globe, 10 cm |
| Pantry's cupboards | boxes under a plaster slab, drawers 25 cm out | worktop ~90-94 | a run of base cupboards (drawer over a panelled door, flush), one worktop run back to the wall at 94 |
| Mantel | a 380 x 24 x 36 box | | a split log: its split face the shelf (still at 2.07), the round of it under, sawn ends |
| Fire irons | none | | a companion set on the hearth (stand, ring, poker, shovel, brush), 79 cm |
| Mudroom bench | a plank on two blocks | seat ~45 | three slats on two trestle ends with rails and a stretcher, seat 45 |
| Ski rack, wall skis | square bars, square skis | | eased edges |
| Bookshelves | six planks | | eased edges, a plinth, a two-step cornice; the furniture's varnish |
| Picture frames | four flat battens | | a moulding swept round and mitred (an eased edge, a crest, a cove to a lip) |
| Drawer fronts | a plank with a brass bar | | eased, a raised field on a deep one, turned knobs (two on a wide one) |
| Antler chandeliers | square-section arms | | six-sided arms, a cup under each candle |
| Scarf on the mudroom coat | three sharp slabs | | soft-edged, a fold over its hook |

Furniture is in the smooth varnished wood the modelled pieces use (`DoorWoodMat`), not the wainscot's planks
(`DarkWoodMat`, which the kit pass weathers as boards).

## Fixes

- **Room 203's snow on the sill** floated 0.85 m over it (a box at 5.44 for a sill at 4.586): now two drifts of snow
  sitting on the sill, the key's end of it clear.
- **The lobby's curios** (the bottle, the books on the low table and on the mantel, the mug) are their own small meshes
  under `LobbyFurniture`, each its curio's highlight root: the white outline finds them (they were drawn into the room's
  one mesh, so their outline was off). The freeze still glazes them.
- **The drawers stood out of their furniture** (nightstands 15 cm, the vanity 18, the chest 22, the pantry 25): the
  drawers are where they were (interactables don't move); the cases come out to them.
- Room 201's roses moved beside the new lamp (they stood in its base).

## Performance

A few thousand triangles a room (a turned lamp is about 600, a case piece 300-500, the vanity's top and basin about
600), a handful of new surfaces where a mesh takes a material it hadn't (the crock, the opal, the varnished wood in the
rooms' meshes). Look-shot perf (`--look-perf=2`, nine poses across the hall, bar, rooms, bathroom, reception, pantry,
corridor): 12.7-14.0 ms a frame, 1570-2960 draw calls. No lights added (the fire irons, the dome and the lamps light
from the lights that were there).

## Tests

`--playtest --playtest-only=23,24a`: 8/8 beats (23a-d, 23x, 23y, 23e, 24a), 120/120 checks, no engine errors, run
twice. Frame rates were taken with four other Godot instances running on the machine (other worktrees' tests), so they
are lower and noisier than a quiet run: averages 93-198, minimums 24-139 (the minimums the loads' hitches; the last clean
run of 23b and 24a, before this pass, read 190 / 89 and 184 / 20).

## Not changed

The modelled pieces (`FurnitureKit`: chesterfields, wing and club chairs, bed, dining chairs, bar stools, the floor lamps,
the tree and presents, the bottles, mugs, books, clocks) are the shared kit's; their sizes check out (bar stool seat 80 cm
at a 113 cm bar top, dining chair seat 47 / back 98, chesterfield seat 44 / back 78, bed 170 x 210 with its mattress top
at 54). The bed is a generous queen-king for a hotel, left as it is.
