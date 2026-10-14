# 2026-10-13c — The look audit's four: the lodge's plaster, the closet's cans, the snow tunnel, the room at the top

Four things from the look audit ([Solid trees, no fog indoors, and a look audit](2026-10-13-solid-trees-no-fog-indoors-and-a-look-audit.md))
that the owner approved fixing. Each was photographed before and after with `LookShots` from the same poses.

## The lodge's glowing panel (`LodgeTextures.PlasterMat`)
- **What was wrong:** in the mudroom (Act 23's first view), the wall with the door on into the lodge read as a glowing
  white panel. It's the service corridor's partition, and the corridor's and the pantry's walls are the same plaster.
  - The plaster was a flat tint over a near-even grain, with no relief and no roughness of its own.
  - So it took the mudroom's bulb and the lantern as one even sheet, edge to edge, a step brighter than the boards
    round it.
- **Now:** it's painted plaster, made the same way as the rooms' painted walls: the plaster scan's albedo, relief and
  roughness under a coat of paint. The paint is a dirtier back-of-house cream (0.5, 0.45, 0.36), a step darker than the
  corridor's.
  - The lantern now falls off across the wall, which shows its trowel marks and its grime.
  - It reads as a wall in the room, not a light in it.
  - Everything else built with `PlasterMat` changes with it: the pantry's counter tops, the plaster rubble round 204's
    and 201's broken walls.

## The closet's cans (`HallwayParts/JanitorShelf.cs`, `Act15Hallway.BuildCloset`)
- **What was wrong:** Act 16's shelves held plain eight-sided cylinders in flat primary colours, the mop's head was a
  cone, and the bucket was a tapered tube. They looked like placeholders.
- **Now:** every item is turned on a lathe from its real profile, and every label comes from one atlas drawn in code.
- **The items:**
  - **Paint cans** (gallon, quart, pint): a rolled chime at the foot, a rolled rim at the top, and the lid pressed into
    its ring with its groove. The gallons have wire bails on ear lugs, with a grip.
  - **Plastic jugs:** squared off, a moulded handle at the back of the shoulder, a screw cap.
  - **Trigger spray bottles:** the head, nozzle, shroud and trigger.
  - **Brown glass bottles:** a shoulder, a neck and a cap.
- **The labels:** eight designs, with lettering from a 5x7 bitmap font and no real brands: LATEX / BARN RED, ENAMEL /
  GLOSS GREY, FLOOR WAX, THINNER with a hazard diamond, BLEACH, AMMONIA, CLEANER, LYE / DRAIN OPENER.
  - The text reads left to right from the front.
  - Paint has run down from the lids in each paint's own colour and stopped in beads.
  - All of them are aged: yellowed and faded, a ragged water-stain tide line up from the foot, grimy blotches and
    scuffs.
  - On the tins the label is torn away at the top and the bottom, showing the tin, rusted, under the tear.
- **The tin:** dull tinplate with fine streaks and a grey film. Rust blooms through it, and where it rusts it is
  rough and not metallic.
- **Dents:** about half the cans have one or two dents in the body (a jug sometimes has a squeezed side), and the label
  dents with them.
- **On the shelves:** the paint is on the low shelves and the sprays and bottles are up high. The back shelf is two
  deep. Each item is turned a little off square, with gaps here and there.
- **On the floor:**
  - The mop bucket is yellow plastic with a rolled lip and a bail, and has grey water in it.
  - The mop's head is a heap of kinked grey strands on its socket.
  - A tin lies on its side, lid off, in the dried red spill it left.
- **The cost:** the lathed items cast no shadows of their own. Plain eight-sided stand-ins, drawn only into the shadow
  passes, cast them (`StockShadows`). The closet draws 150-260k primitives a frame, against 85-130k before (it was
  700k-1M with the real meshes in every shadow pass); 100-135 fps on this machine.
- **The seed:** the stock has its own random seed (1616), so the shelves are the same every game. The hall's own
  generator no longer draws for the shelves, so its red light, green light timings come out in a different (still
  random) order.

## The snow tunnel (`packed_snow.gdshader`, `SnowMazeLook.PackedSnow`, `Act24Maze.BuildHole`)
- **What was wrong:** Act 24's vault of snow, out of the lodge's doorway down to the hole, read as white plastic.
  - It was the fresh snow shader over the CSG's big flat facets: smooth, even shading and the same faint sheen
    everywhere.
- **Now:** packed snow, gouged, in its own shader.
  - **Its grain** is the maze's own baked trodden-snow set (albedo and normal), world-projected three ways at two
    scales.
  - **Its relief:** scallops gouged out of it about a forearm across (a ridged noise read as height, its slope bent into
    the normal), so no facet is ever flat. The light catches the rims and the hollows hold the dark.
  - **Its colour:** a cold blue-grey in the hollows and the shadow, and paler on the crests. A faint backlight in the
    hollows gives the blue that comes back out of snow. There are grey blotches where the storm's grit packed in.
  - **Its roughness:** matte almost everywhere (specular 0.18). Streaks and patches of ice glaze run where it thawed and
    froze again: a little smoother, darker and bluer, a dull glint, never a gloss.
  - Now and then a crystal on a crest catches the eye as it moves.
- The snow lip round the hole's mouth uses it too. It's warmed with the lodge's other late materials (`SkiLodge.Perf`),
  since the tunnel is built as the finale starts.
- **Unchanged:** the maze inside (`ice_cave.gdshader`) and the porch's drift.

## The room at the top (`RoundRoom.TopStone`, `LongStair.BuildEntry`)
- **What was wrong:** Act 21 starts in the little room at the top of the round room's shaft. With the lantern up close,
  its pale walls (the drum's concrete at 0.8, 0.77, 0.7, with no relief) went flat and blown out, one bright tone wall
  to wall.
- **Now:** the room has its own stone, `RoundRoom.TopStone`:
  - older and darker (0.5, 0.47, 0.42 over the stained concrete scan);
  - fully rough, with little specular (0.12);
  - with the scan's relief at 1.4.

  The lantern now shows its pits, pours and stains and falls off across it. The ceiling is a shade darker too.
- The long stair's way out of the room (the jambs through the room's wall and the floor to the first step) uses the
  same stone.
- **Unchanged:** the room's lamp, the drum below (Act 20), the long stair itself and the church. The rest of the act is
  as bright as it was.

## Tests
- `dotnet build`: 0 errors.
- Before and after shots: `test-output/look/before16`, `before21`, `before23`, `before24`, `before24t` and
  `after16`, `after21`, `after23`, `after24`, `after24v`.
- `--playtest-only=16,21,23,24`: 13/13 beats, 174/174 checks, 0 engine errors.
