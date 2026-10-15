# 2026-10-15d — No comb on the walls, no stain on the chairs

The shared-kit part (group a) of the repetition, clipping and clutter scan (`test-output/scan/SCAN.md`). The fixes are in
the same commit as this file.

## Walls, floors and ceilings (`room_surface.gdshader`, `RoomSurface.cs`) — SCAN #1
- **The problem:** every RoomSurface interior (the lodge mudroom and corridors worst, also the station and the vestry)
  had the same drip streak every 0.476 m along every wall, a strict comb. The 256 px seamless noise was sampled with
  the world position as its UV, so the streaks, the tide line's wobble and the mottling all repeated on fixed periods.
- **Now:**
  - The streaks are cells of about 0.38 m whose boundaries wander. Each cell has a chance of one streak, and that streak
    has its own place, width (3.5-16 cm, with a faint wash round it), length (0.35-1.6× the material's) and strength.
    Its edges are ragged and its line drifts as it runs down. Whole bays are wetter or drier, on a slow aperiodic noise.
  - The mottling, the tide line, the ceiling stains and the floor dirt use arithmetic value noise (no tile, no period).
    The texture is only tapped at turned, incommensurate scales for grain.
  - Each face is hashed to its own pattern: the axis it faces, which way, and how far along it. Two walls of one room,
    or the two sides of a corridor, never stain alike.
  - The detail grain is sampled at its own scale (×1.37) and turned 30°, so it no longer locks to the albedo's tiling.
    RoomSurface uses the grain without the baked-in grime (`DetailKit.Grain`), because the shader draws its own grime
    across the room. Small aperiodic specks replace the baked ones.

## Decals (`DecalDresser.cs`, `DetailSweep.cs`, `FurnitureKit.cs`) — SCAN #8 and the MED decal row
- **Depth:** the ray-placed floor decals projected 0.3 m from 5 cm up, and the wall decals 0.25 m from 4 cm off the
  wall, so they stained chair legs, crates and pickups. Now floors are 0.1 m deep at +1 cm and walls 0.08 m at +6 mm.
  The rule-placed room decals (`Room()`) are 0.08 m deep (they were 0.16).
- **Cull mask:** every DecalDresser decal (ray, rule and `Place()`) leaves out `DecalDresser.NoDecalLayer` (render layer
  13), the held item and the player's body. `DetailSweep` puts pickups, rigid bodies and anything marked `furniture`
  (`FurnitureKit.Place` marks its pieces) on that layer as they enter the tree, if they are still on the default layer.
- **`Fixed()`** no longer accepts furniture: a body or shape marked `furniture`, a `ClutterKit` shape (`Clutter_*`),
  or a pickup. Two new geometric checks:
  - A "floor" with the ground well below it on two or more sides is a raised top (a table, a bench, a crate), so it gets
    no floor puddle.
  - A "wall" must carry on up. A ray the same way 1.2 m higher has to meet it at the same distance, otherwise it is a
    wardrobe or a cabinet front.
- **Repetition in `Room()`:**
  - Each decal is ±20% in size and ±10% in strength, and half of them are mirrored.
  - A quarter of the windows get no streak.
  - `RoomSpec.Furniture` (boxes) keeps decals from behind and under what stands in the room. `ClutterKit.Boxes` gives
    them, so other areas can now fill it.
  - The ray decals on walls are mirrored half the time.

## Clutter (`ClutterKit.cs`, `DebrisKit.cs`, `WebKit.cs`, `DetailKit.cs`)
- `ClutterKit.Model()` sets the model so its box's foot-middle is the piece's origin. Off-centre models used to fit
  the room by one box and stand in another, with their colliders off.
- Pieces against a wall or in a corner clear their own turned back corner (sin(turn) × half-width) plus the skirting
  (`Skirting`, 22 mm). Their back corners went up to ~2 cm into walls.
- `DebrisKit` paper scraps keep out from under the room's standing colliders (beds, tables, cabinets), and each scrap
  has its own size and shape.
- `WebKit.Corner` varies the lean across the angle, how far it drops from the ceiling, the aspect and the size, and it
  mirrors half of them. `DressRoom` always leaves one top corner (sometimes two) bare, and takes an optional `avoid`
  list of boxes (a bookcase's crown).
- `DetailKit.Classify`: words of four letters or fewer only match at the start of a word of the name. "trail" is no
  longer Metal (via "rail"), "painting" no longer Metal (via "tin"), and "Notice_" no longer Ice (via "ice_").

## Forest (`ForestScatter.cs`, `ForestScatter.Growth.cs`)
- **Fir slabs:**
  - **The problem:** the lowest fir tiers, seen from under or beside them, read as flat dark slabs (the floating slab at
    the lake). The cause was each tier's wide, shallow core cone.
  - **Now:** tiers under 3.4 m have no core, only their boughs. Higher tiers have a narrower (0.42 R), steeper skirt
    with a ragged rim and a gap or two.
  - This makes no extra draws on the tree's random stream. It is shared with the lake's cypresses (`FirCrown`).
- **Vine mounds** are lopsided, lower and flatter, darker, with a ragged hem and dead stems poking out. There is a
  third, long low variant (`vinemound_c`).
- **Stumps, logs and branches** each have a second mesh (snapped stump, broken log, forked branch). The variant, a
  ±10-12% tint and the stump's scale (0.8-1.2) come from a hash of the spot. The scatter's random stream is untouched,
  so no tree, rock or log has moved.
- **Logs on crests** are bedded deeper (the ground audit found three hanging).

## Not done here (other owners' files)
- The Hinoki plank repetition (`SurfaceSets`, `LodgeTextures`).
- The candle flames (church, round room, library).
- The library's webs through the bookcases: it can now pass `avoid` to `WebKit.DressRoom`.
- Filling `RoomSpec.Furniture` in the lodge and vestry rooms.

## Also: the lake's broadleaves (`ForestScatter.DeciduousMesh`)
- The other "floating slab" by the lake was a broadleaf's crown: five broad, flattened blobs fused into a dark cap on a
  pole. It is now nine smaller clumps, taller than wide, at uneven heights round the limbs and up the leader.

## Verified
- Look shots at the scan's poses, before (`test-output/scan/shots`) and after, in these areas:
  - the lodge mudroom, corridor and room 202;
  - the station lobby and Rooms 1-2;
  - the church and vestry;
  - the cabin;
  - the lake;
  - the Act 25 woods;
  - the trailhead.
- Contact sheet: `test-output/fix/SHEET_group_a.png` in the worktree.
- `dotnet build`.
- Playtest beats 1, 5a, 12, 13a, 21 and 23a: 6/6 passed, 137/137 checks, 0 engine errors.
