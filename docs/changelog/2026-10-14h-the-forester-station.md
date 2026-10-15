# 2026-10-14h — The forester station: a ranger station, lit in pools, weathered with the interiors kit; Room 2 muted, the late lobby rots, Room 3 under warm caged bulbs

The look survey (`test-output/survey/SURVEY.md`, sections 2 and 6, and the replacement table) found the station lit edge
to edge, papered in oversized flat prints, its notices glowing near white, Room 2 a saturated red box, the late lobby
full of glossy spot-lit flesh, and Room 3 a cool-white "lab corridor" whose lamp shades threw huge half-moon shadows over
riveted steel, under a giant orange rust ceiling and over a loud tread-plate floor. This pass rebuilds Acts 13-14's
rooms with the interiors kit (`docs/design/interior-kit.md`): baked dark and light pools (`RoomShade`), weathered
surfaces (`RoomSurface`), swept trim (`TrimKit`), decals and clutter by rule. No story beat, puzzle, pickup or
interactable moved.

## The lobby (`stationparts/LobbyDecor.cs`, `StationInterior.cs`)
- **A ranger station:** the radio desk between the front doors (an olive steel set with its dials, meter and power
  supply, the microphone and its cord, the aerial lead up the wall, the call log and a mug, a frequency card and a ruled
  calendar pinned over it, a swivel chair, knocked over from stage 3). Two steel filing cabinets in the corner by the
  way in, rolled maps on top: shut while the place is kept, rifled from stage 2 (a drawer out, files on the floor). The
  district's survey map framed on the wall by Room 2 (a new procedural texture: woods, lake, contours, a red-dashed
  trail, pencil rings). A cold pot-bellied stove on its zinc mat with its flue into the wall, a kettle, a poker and
  kindling. Cartons of pamphlets in the back corner. All against the walls, clear of the doors and the paths.
- **Surfaces, each its own:** the paper a quarter the motif size, a quiet faded sage with a fine pinstripe and a sprig
  (the kit found loud paper drowns the weathering), weathered; a knotty-pine beadboard wainscot with its chair rail and
  a tall skirting swept round the room (was a plain box strip); weathered plank floor, plaster ceiling, doors, casings.
- **Light in pools:** the whole room baked: dark seams and corners, under the desk, cabinets, bench, radio desk and
  stove, soot over the stove and lamp, damp by the basement door and in the back corner, the boards worn from the door
  to the desk and to each room. The ceiling lamp is a pool (range 8, attenuation 1.4, was 10.5 / 1.1); its globe an old
  yellowed opal glass. The lining and the floor cast no shadow.
- **Paper under the lantern's pool:** the notice board's sheets in a darker linen-paper material, five of them overlapping
  and tilted with pins, a moulded frame; the fire-danger sign an old enamelled board; the photos sepia under glass; the
  rug a worn brown-red with an olive border (was flat bright red and gold).
- **The late stages:** stage 3's rust plate weathered and darker, rust streaks under it; the light browns out to a dirty
  amber instead of going club red. **Stage 4 is the rot:** the ceiling down in two places (black ragged holes, plaster
  hanging off their lips, lath dangling, heaps of plaster and lath under them, dust, a puddle), black mould spreading
  from the corners, along the cornice and round every growth, long water streaks, a sheet of paper sagged off the back
  wall; the flesh is six smaller growths sunk into the plaster in a matte, mould-furred liver-brown (`MeatMat`: was ten
  glossy, wet, saturated red lumps). `StationProps.Growth` is a subdivision rounder.

## Rooms 1 and 2 (`StationRoom1.cs`, `StationRoom2.cs`)
- **Room 1:** baked and weathered, one bare bulb on a flex (it had a light and no lamp) throwing a dull warm pool; a crate
  and a carton, a stack of files, a pail, a frame leant, a chair knocked over, all in the corners clear of the box, the
  three pools and the door's swing; decals on the door wall, floor and ceiling only, never over the writing.
- **Room 2:** still the red room, now an old oxblood regency stripe at real paper's scale, faded and weathered (was a
  saturated blood red at 23 cm a stripe); the velvet the colour of old wine, the brass dulled, the window's glass dark
  and grimy; the pale marble dirtied and a step darker; a cold cast-iron radiator under the window; baked, its bulb a
  pool; decals by rule. Nothing else: the room is bare but for the table.

## Room 3, the stairwell, the basement
- **Room 3 (`StationRoom3.cs`):** the plate is painted steel flaking to rust (a new 128 px texture: grey-green paint,
  sharp flakes with a lifted lip, rivet rows, rust weeping), streaked by the weathering; the tread plate twice as fine
  and a third the contrast, dirtied; the ceilings a sooted dark plate (was the big orange rust print); hazard paint and
  bollards a worn ochre. The lamps are **caged incandescent bulbs on conduit drops**, warm (1, 0.72, 0.46), the cage
  casting no shadow (the old enamel shades threw the half-moons); one dead, its stretch of hall left dark; one failing,
  buzzing and dipping (`bulb_buzz_loop`). Corridor and hall baked separately; decals by rule (not on the portrait walls).
- **The stairwell (`Stairwell.cs`, `stairwell_concrete.gdshader`):** the walls a step darker and stained from the top
  (`top_stain`: never the bare pale pour; stronger macro variation); the trench's block walls weathered and baked; the
  trench's poured concrete and the shaft's ceiling darker; the hole's lip a dull ochre.
- **The basement (`StationBasement.cs`):** the brick weathered (a flood's tide line, streaks, desaturated) and darker,
  both the stair and the room baked (dark seams, two pools under the bulbs, damp along the walls, worn path to the drain).

## Files
`scripts/world/stationparts/LobbyDecor.cs`, `StationTextures.cs`, `StationProps.cs`; `scripts/world/StationInterior.cs`,
`StationRoom1.cs`, `StationRoom2.cs`, `StationRoom3.cs`, `StationBasement.cs`, `Stairwell.cs`;
`scripts/world/stairwellparts/StairwellTextures.cs`; `assets/shaders/stairwell_concrete.gdshader`.

## Verified
- Before and after from the same poses (lobby, Rooms 1-2, the late lobby, Room 3, the stairwell, the basement):
  `test-output/look/b2_*` and `a4_*`/`a5_r3`/`a6_*`, side by side in `test-output/st/station_before_after.png`.
- `--playtest-only=13a,13b,14`: 3/3 beats, 67/67 checks, 0 engine errors; fps avg 163-220, min 68-108.
- Cost (`--look-perf=5`, same poses): the lobby 11.2 / 10.9 ms a frame against 11.6 / 11.7 before (draws 1094 / 1002
  against 874 / 850; primitives about 1.2M against 0.57M, the finer bake grids and the furniture in the shadow passes);
  Room 3 8.6 ms against 8.9.

## Still open
- The Room 2 door reads pale from the lobby: the Act 13 safe light (`SaveLamps`) stands right by it.
- The web over the iron door is still the brightest thing in the kept lobby (WebKit's photo cards).
- Primitives doubled in the lobby; frame time held, but a lower `Tess` on the shadow-casting walls would win some back.
