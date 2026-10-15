# 2026-10-14h — The ski lodge through the interiors kit (Act 23)

The look survey (`test-output/survey/SURVEY.md`) found the lodge lit edge to edge like a showroom: clean plaster, no dark
in its corners, empty floors, a bar whose ceiling read as an office's fluorescent panels, and room 202 a clash of magenta
velvet on a green carpet under a red rug. The global lighting pass (2026-10-14g) dimmed it; this pass applies the
interiors kit (`docs/design/interior-kit.md`) room by room and makes it read as a real 1970s-80s mountain lodge.

Before and after: the same 18 poses (`test-output/lodge/shots.sh`, `test-output/look/before/`, `.../after*/`), contact
sheets in `test-output/lodge/`.

## The kit, room by room (`SkiLodge.Kit.cs`, new)

The lodge's builders draw each wing into a few big meshes, so the kit goes on after everything is built
(`BuildKitPass`, from `_Ready` after `StartAct23`):

- **The bake** (`RoomShade`): eleven rooms described by their boxes: the mudroom, the service corridor, the bar, the
  pantry, the upstairs corridor, rooms 201-204, the dining hall, and the great hall (its four slanted sides as planes, and
  a hexagon test so the hall and the rooms against its sides don't claim each other's walls). Each vertex takes the room
  it lies in, so a partition's two faces belong to the two rooms either side (the kit's `ApplyTo` would have baked the
  first room only: its second UV marks a mesh as modelled). The lamps found in each room are its light pools (soot over
  the shaded ones); windows add damp and a faint pool; every piece of furniture's collider puts the dark under it; feet
  wear paths from the doors.
- **Tessellation**: the shells are drawn finely for it (`MeshKit.Tess` in the mudroom, the west wing's ground floor and
  upstairs, the dining hall, the hall's walls and roof; the hall's floor fan cut into small triangles, `TriGrid`).
- **Weathering** (`RoomSurface`): the plaster, paint, panelling, boards, carpets, tile, lino, flagstones and ceilings of
  the rooms' shells swapped for their weathered selves by material; furniture, brass and glass left alone.
- **Decals by rule** (`DecalDresser.Room`): about 250 across the rooms (streaks, tide lines, cracks, peeling paint on
  the painted rooms, scuffs at the doors, soot over the lamps, ceiling stains); the dining hall's only under its lower
  row of windows.
- **Clutter** (`ClutterKit`, about 40 pieces): wall-hugging, kept clear of every door's swing, every drawer, cabinet,
  switch, pickup and curio, the save spots and R.H.'s page, the snow at the back door and the spawn.
- **Room 201 stays perfect** (STORY.md: the one perfect room): its shell is its own mesh (`WestUpper201`), its paper
  its own material; it takes the bake's shade but no grime, decals or clutter.

## Per room

| Room | Was | Now |
|---|---|---|
| Mudroom | Dark, empty floor, three coats as flat slabs | A hook rail with a long red wool coat (collar, buttons), a quilted navy parka (seams, a red stripe, a fur-trimmed hood), a loden jacket with a scarf over its hook; a slatted rack of rental boots by the back door with its sign and a peg rail (a toque, a scarf); a framed trail map by the inner door; a log pile, a crate with a salt sack, a pail, a snow shovel and a broom leant, boots kicked off. Flagstones a metre and a half, not one stretched over the room. Its bulb 0.9 (was 0.75). |
| Service corridor | Flat orange plaster | Weathered plaster with streaks and a tide line, a staff notice board, a fuse box with its conduit, cartons, a crate, a pail and a broom |
| Bar | Glowing strips on the ceiling (`lodge_barceil`, the "fluorescents"), a counter front lit from within, red damask walls | Two wagon-wheel chandeliers on chains (hub, twelve spokes, iron tyre, eight amber chimneys) and three green enamel pendants low over the counter: pools on the bar top and the tables, dark between. The counter's front in raised panels between pilasters (kept short of the blacklight ink on it), a brass foot rail, a rounded nosing. Knotty pine over the dark wainscot. Crates behind the bar, a chair knocked over. |
| Great hall | Evenly lit | The bake (seams, the wear from the doors to the stair), weathered boards and logs, a few decals; Navajo rugs |
| Dining hall | Magenta chairs | Oxblood corduroy chairs; the bake and weathering; decals under the lower windows |
| Upstairs corridor | Clean plaster | A chair rail capping the wainscot, stained ceiling, streaks, peeling paint, scuffs at the doors, a little left by the doors |
| Room 202 | Magenta velvet bed and sofa, green peacock carpet, red rug, mauve walls, a strong blue light | A rust-and-oatmeal plaid bedspread, a tobacco headboard, an olive sofa, a tobacco carpet with a quiet rust lattice, a Navajo rug, oatmeal walls; the snow's blue light 0.45 of a greyer blue (was 1.1); luggage, magazines |
| Rooms 203, 204 | The same velvet and carpet | The same calmer palette (a rust sofa in 203); grime; luggage, books |
| Room 201 | — | The new palette (a mustard sofa), kept clean |
| Pantry | — | The bake, weathering, crates and flour sacks |

All upstairs rooms and the bar, the dining hall and the upstairs corridor get a moulded chair rail on their wainscot
(`ChairRail`, broken at doors and windows).

## Materials (`LodgeTextures.cs`)

- `KnottyPine` / `KnottyPineMat` (new): upright tongue-and-groove boards, six to the tile, knots, V-grooves, aged amber.
- `RoomCarpet`: tobacco brown with a rust lattice and harvest-gold dots (key `lodge_carpet_room_70s`).
- `Rug`: a Navajo pattern (oatmeal, terracotta, charcoal, a turquoise heart; key `lodge_rug_navajo`). Every rug drawn
  with `RugMat` changes (the hall's fire rug, the landing's, the bar's runner, the rooms'); the Christmas oval rugs stay.
- `Bedspread` / `BedspreadMat`, `Upholstery(tone)` (rust, mustard, olive, oxblood, tobacco corduroy), `EnamelMat`,
  `TrailMap` / `TrailMapMat` (new).
- Room 202's paint is an oatmeal (was dusty rose). `RoomPaperMat(201)` is its own material (same colour).
- `VelvetVioletMat` is kept for 201's keycard and the church's kneelers.

## Kit pieces (`LodgeKit.cs`)

`WagonWheel` and `Pendant` fixtures.

## Performance

Look-shot perf (`--look-perf=1.5`, the 18 poses): before 11.0-13.5 ms a frame, after 11.2-13.7 ms (about +0.5 ms); draw
calls up about 20-120 where a room's decals are in view. No new shadow-casting lights (the lodge's lights cast none); the
clutter's small pieces cast no shadows. The pass costs about 0.55 s of the lodge's load (`[build] SkiLodge.KitPass`): the
bake runs each surface's vertices in parallel, and only over the rooms' shells and procedural furniture (the modelled
furniture, the tree and the pantry's stock are left; copying their arrays was most of the 1.9 s it first took).

## Tests

`--playtest --playtest-only=23`: 7/7 beats (23a-d, 23x, 23y, 23e), 88/88 checks, no engine errors; fps averages 120-200,
minimums 91-151. The blacklight card under the bar's lip, the ink on the counter's front, every drawer, door and pickup
still work.
