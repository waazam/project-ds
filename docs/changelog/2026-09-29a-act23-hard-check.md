# 2026-09-29a — Act 23 hard check: polish, a clipping and UV audit, Poly Haven surfaces (and the church's)

Commit: this entry's commit on `main`.

The owner asked for a complete hard check of Act 23 before the next build:
- debugging;
- a polish pass;
- a strict collision and UV/texture audit against clipping and artifacting;
- Poly Haven's textures used to make everything pop, in the lodge and in Act 21's church.

## The audit (`ClipAudit`, extended)
- **`-- --winter` mode:** the church, the winter woods and the ski lodge, stood up on their own.
  - About 300,000 triangles, with the existing checks: z-fighting, walk-through props, UV faults.
  - Entries in the lodge are also given in the lodge's own coordinates, so each one can be found in its
    build code.
- **New: a pierce-through check** on the lodge and the church, for meshes cutting through each other where it
  shows:
  - a thin sliver of something behind a surface poking out through it (what the exterior sill did to the
    dining hall's panelling);
  - a thing passing right through a wall.
  - It found three things the z-fight check can't (below). Its first version had a face's seen side backwards
    (Godot winds front faces clockwise), which is fixed.
- **Visual sweep:** the lodge preview (`--lodge`) gained a six-way sweep of the lobby looking up at the walls
  and roof, a shot of the chimney from outside, and one of the fireplace's top. Every lodge shot was checked by
  eye after each fix.

### What it found, and the fixes
- **The dining tables were built twice.** The furniture pass still built all six tables, and Act 23 built its
  own six (each its own node, so one can fall apart).
  - The two sets fought over every surface (six 20 m² z-fights).
  - Worse, when the fifth table collapsed, the static copy would have stood on.
  - The furniture pass's copies are gone.
- **The wing roofs ran on into the lobby.** Both wings' roofs started at the wing's nominal start, 6 m out,
  inside the hexagonal hall. Their dark undersides showed in the lobby as great black V-shapes across the upper
  walls (east and west).
  - The roofs now start at the hall's outer wall (the two facets either side of the corner at the ridge).
  - A timber gable fills the wing's section above the hall's wall top, so there's no gap outside.
  - The wings' walls start at the hall's wall too; their ends stood into the lobby's four corners.
- **The wing eaves' first icicles hung inside the lobby's corners.** The row now starts outside the hall.
- **A balcony post came down through the front desk.** The balcony's front corner post stood in the middle of
  the reception counter. The desk, the pigeonholes, the key hooks and the RECEPTION sign moved to the east
  side of the front doors.
  - The keys' numbers now read 201 to 204, left to right.
- **The fireplace's chimney breast.** It was 4.2 m wide all the way up to 21 m. It ran up through the roof
  and stood out beside the chimney outside.
  - Now: the full-width breast to 7.5 m, a stepped shoulder, then a stack narrow enough to run up inside the
    chimney.
  - The chimney outside stands flush over it: its inner face on the breast's face, its foot where the roof
    crosses it. There's no stone in the lobby under the roof, and no gap under the chimney outside.
- **The mudroom's back wall.** One board wall covered the whole wall, the back door's opening included, and
  lay level with the stone behind it (a 15 m² z-fight).
  - It now goes round the door's opening, a hair in front of the stone.
- **Thresholds.** The stone thresholds in the ground floor's doorways lay level with the floors running under
  the walls and flickered. They're now 3 mm proud.
- **The dining tables' sheets.** The drape's grid is coarse: falling from the table's edge itself, the straight
  span to the next row cut under the top's edge, and the wood showed through the linen. The sheet now lies flat
  a hand's width past the edge before it falls.
- **The wardrobes' and closets' doors had no collision.** Opened, they stood out into the room and could be
  walked through. Each door now has its own collider, swinging with it.
- **Room ceilings.** Their quads had no UVs, so one plaster tile was stretched over a whole room (86x texel
  variation). All the lodge's ceilings are now mapped in metres over the plan.
- **The pillows** (sphere-mapped blobs) now take plain linen, so the weave doesn't pinch at their ends.
- **The church:**
  - the crypt stair's top riser lay level with the nave floor's cut edge (1 m²); it's now 4 mm in;
  - the wall plinth's top met the arcade piers' bases level (1 m²); the plinth is now 1 cm lower.
- The remaining z-fight pairs are all hidden: undersides at floor level, the casing tops inside door openings,
  and faces inside wall thickness.

## Poly Haven surfaces (`SurfaceKit`, `tools/Textures/make_surfaces.py`, `assets/textures/surfaces`)
- **The pipeline:** 18 CC0 surfaces from Poly Haven (credited in the folder's README), downloaded at 1k and
  made in the PS2 look.
  - 256 px, area-downscaled (soft);
  - contrast pulled in a little;
  - the normal maps softened toward flat.
- **`SurfaceKit`:** the texture factories sample a surface at their own tile coordinates as they draw.
  - They either take it whole and tint it, or lay only its grain (its light and dark, around 1) under their
    own pattern.
  - A material can take the photo's normal map at the same repeat.
- **The lodge, taken whole:**
  - the dining hall's herringbone parquet;
  - the dark wood of the panelling, the bar, the desk, the doors and the furniture (a shade deeper, its red
    taken down);
  - the fireplace's rustic stacked stone;
  - the lobby's floor: a monastery's big irregular flagstones;
  - the lobby's lower walls inside: a cool stacked stone, replacing the outside's mossy stone, which read as
    dirt in the lobby;
  - the red leather of the chesterfields and stools;
  - the violet velvet's pile;
  - the linen of the beds and sheets (the tables' sheets repeat it across their width);
  - the plaster.
- **The lodge, as grain under drawn patterns (the Shining-inspired designs stay ours):**
  - a carpet's weave and wear under the corridor's arches, the rooms' peacock scales and the rug;
  - a jacquard's weave under the bar's damask;
  - a worn pine's grain along the log walls;
  - a faint mottle under the wallpaper, the tiles and the pantry's linoleum.
- **The church (Act 21):**
  - the ashlar walls are now Poly Haven's limestone blocks, cream and pitted;
  - the crypt's vaults a medieval red brick;
  - the floor's cobbles a real cobblestone floor, its moss greyed out;
  - the pews and doors a close-grained dark oak;
  - the old stair's planks carry a weathered plank's grain;
  - the vault's plaster, the marble floor and the crypt's chequer carry a faint real mottle;
  - each whole surface has its photo's relief.
- **Kept clean:** the wallpaper, the plaster and the ceilings no longer take the micro-detail layer.
  - It shares their UVs (a tile of two or three metres), so blown up that much its fine grime showed as
    metre-wide stains.
  - The detail sweep now leaves alone a material that has no detail on purpose.

## Polish
- **The snowed-in moment:**
  - the pile in the back door is the woods' own snow now: packed to the lintel, a soft fan spilled in over
    the sill, a few clumps thrown in;
  - the mudroom's bulb is a step up (it read as black).
- **The snow indoors** (203's drifts, the hole into 204, the dining hall's open window, the skeleton's
  clumps): a soft near-white with only the snow's fine grain. The photo at any scale read as grey marble
  under lamplight.
- **The fireplace's stones** are the photo's own; the old painted-on dabs read as brown spots over it.
- **The elk's head** is darker; the light fur read as a pale lump.
- **The animations** (doors, drawers, the mop, the collapse, the melt) were reviewed and are smooth, with
  nothing abrupt.

## Test fixes
- **Act 23's walk from 203's windowsill to the bathroom.**
  - It now waits for the controls to come back after the key's pickup. In the full run's timing the walk had
    started and given up while they were still held.
  - It now goes down the gap between the bed and the sofa and round the bed's foot, instead of cutting the
    bed's corner.
- **Act 14's descent** now switches the blacklight on once the flame is out, as a player would. Without it,
  the halfway and deep screenshots were black frames that checked nothing; they now show the steps in violet.

## Tests
- Clip audit (`-- --winter`):
  - z-fights: 23 → 10 pairs, all hidden (undersides, faces inside walls and door openings);
  - walk-through props: 8 → 0;
  - UV faults: 4 → 0;
  - no visible pierce-throughs left (the rest are structure meeting inside walls, furniture against
    walls, and the snow drift inside the wall's thickness).
- Story test, Acts 21–23: 105/105. Act 23 alone: 51/51. Act 14 alone: 35/35.
- Full autotest: 650/651 before the last test fix (the 203 walk above); Acts 22–23 after it: 77/77.
- Continue test: 813/813 over 30 scenarios.
- Screenshot scan: no magenta (missing textures) and no blown-out frames. The only dark frames left are the
  fall into the stairwell's bottom chamber and the hatch climb, which are dark on purpose.
