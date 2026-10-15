# 2026-10-15d — Acts 15-21: no two alike

Group (d) of the repetition, clipping and clutter scan (`test-output/scan/SCAN.md`): the hallway, the closet, the sewer,
the pit, the library, the round room and the church. The fixes are in the same commits as this file.

Red light, green light is untouched: the hall's dice print `state 8407554524482128862` after the build, as before.
Every new choice is drawn from its own generator or from a hash of a position.

## The hallway (Acts 15-16)
- **The walls no longer repeat (#2).** `corridor_wall.gdshader` no longer samples the shared 256 px noise tile.
  - That tile, at world scale, put the same "continent" stains and paint chips every 3 m for 450 m, mirrored on both walls.
  - Everything is now arithmetic gradient noise, so it never repeats. The noise is domain-warped and seeded per wall side.
  - A slow wear mask varies the walls along their length: a nearly clean bay, then one stripped back to the block.
- **No colour seams.** The paint's fade now drifts along the wall in the shader. It used to step every 36 m or 40 m.
  - The per-segment tint draw is still made, so the dice stay the same, but its value is no longer used.
- The floor strips are kept as they were: aged, some dead. No signs were added.

## The closet (Act 16)
- **Spray bottles have real trigger heads.**
  - Each has a collar, a rounded body tapering to the nozzle, a shroud and a curled trigger.
  - The heads are in sprayer colours: black, navy, oxblood, green or grey. They used to be light grey boxes that read as paper flags.
- The rows of stock are no longer in a ruled line: each item sits a little forward or back.
- The amber glass is rougher, so the bottles' feet no longer sparkle in single pixels.
- **The shelves are solid.** A collider round each shelf unit means you can no longer walk through the stock. The floor,
  the switch, the door and the trigger inside the closet are all clear of them.

## The sewer (Act 17)
- **What floats is sodden.** Lumps of bin bag and rag, planks and bottles. The glossy black balls are gone.
- In the tunnel, floaters are pushed toward the sides, not ruled down the middle. Planks near a wall lie along the flow.
- **The heaps are mounds now.** Rubble and silt, with the bricks lying on the surface and the sacks slumped on top. The
  bricks used to hang in mid-air at up to 0.9 m.
- Graffiti sits beside the pipe mouths at arm's reach, 1.3 m to 2.2 m up. One tag used to be 5 m up a wall.
- The hall's dice are drawn in the same order as before.

## The pit (Act 18)
- **The slam warning no longer paints the hand red (#12).**
  - The decal's cull mask now leaves out the held-item layer and the body layer.
  - It is 0.9 m deep (was 3 m), so the pipes underneath stay clean.
- The dent decal is 0.22 m deep (was 0.6 m).
- **The cables vary.** Brackets are unevenly spaced. Each cable has its own slack and each span its own sag, and now
  and then a loop hangs loose. Every cable used to sag in the same arc every 2.4 m.
- **Pipe end caps no longer flicker.** Each pipe end is cut back by its own few millimetres to centimetres, so no two caps
  share a plane.

## The library (Act 19)
- **The passage to the round room is stone.**
  - The walls are dressed ashlar, and there is a candle on an iron bracket on each wall, with a drip pan and wax run over its edge.
  - It used to be a clean concrete box with two bare candle sticks stuck to the wall.
- The secret bookcase's packed books now lean a hair now and then. They used to stand dead upright, which gave the bookcase away.
- The long table's items each have their own patch of the top. The three lying books stack by their own thickness.
- The journal sits on its ledger. It was 4 mm into it.

## The round room (Acts 20-21)
- **No web over the switches.** The banked webs and hanging cards keep off all six switches. Before, the same
  photographed web hung over four of the six.
- Five of the six switches get a small web of their own beside the plate. One is left clean.
- **The dais posts vary.** Each has its own height, lean and rust, and the web anchors follow the posts' tops.
- The wall candles have each burnt down their own way, with their own wax shade.
- In the room above, each lining face of the stone has its own patch of the texture. The faces either side of the arch were mirrored.

## The church (Acts 21-22)
- **The aisle bays differ.**
  - Some racks are full and burning, some are sparse.
  - One rack has gone cold, burnt down to stubs, and one bay has no rack at all.
  - The racks sit a little along their bays, their candles out of line, and now and then a candle lies on the floor.
  - Each bay's glow matches what is burning on its rack.
- **Every flame has its own size and lean.** There were 259 identical beads.
- **The pews are out of line.**
  - Each pew is a little out of line and out of square. Two are shoved askew and one is pushed back.
  - Hymnals have been dropped between the pews, one splayed open, and a kneeler cushion has been kicked out to the aisle.
- **The great door's leaves** have a shade per board, the grain on their backs, and strap-hinge tails with nail heads.
- **The church meets the snow outside.**
  - The woods' ground used to skip the whole rectangle round the transepts, leaving a hole beside the aisles and the
    chancel. A flat grey floor showed through it: the "grey wedge".
  - It now skips only the church's own outline (`Church.InsideFootprint`; one line in `WinterWoods.cs`).
  - A stone plinth runs round the outer walls, its foot sunk into the ground, with a drift banked against it. The drift
    wanders in depth and stays clear of the doors.

## Not done
- **The snow at the great door's foot (#F12).** That is the woods' ground or road, in group (e)'s files.
- **The mirrored crack strokes on the long stair's walls.** These are beyond Act 21's arch, in `LongStair.cs`, which is
  not one of group (d)'s files.
- **The church's paired aisle windows.** These are part of the wall shell. The racks under them now differ instead.

## Verified
- **Look shots.** Taken before and after at the scan's poses. The sheet is `test-output/sheets/groupd_before_after.png`
  in the worktree.
- **Playtest.** `--playtest-only=15,16,17,18,19,20,21,21c,22`: all beats pass, with 0 engine errors.
- **Hall dice.** `state 8407554524482128862` on every build.
