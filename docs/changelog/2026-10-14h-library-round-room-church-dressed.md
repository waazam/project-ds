# 2026-10-14h: the library, the round room and Act 21 dressed as old and abandoned

The look survey's items for Acts 19 to 21, done with the interiors kit. Every puzzle, item and prompt is where it was.

## Act 19, the library (`Library.Shelves.cs`, `Library.Floor.cs`, `book_cloth.gdshader`)

- **Bookcases:** each bay of a run is now built on its own. Its shelves sit at one of four sets of heights (adjustable
  shelves), and some bays stand on a panelled cupboard with a door left open. Neighbouring bays no longer line up.
  There is a plinth and a lip under the crown, and the uprights' faces are rubbed paler. The journal's bay and the
  hinged bookcase keep the standard heights, so the journal, its READ and the jutting book are where they were.
- **Books:** each shelf is filled in one of five ways:
  - packed, with runs of matching sets;
  - part full, the last books leaning on their neighbours;
  - piles laid flat among upright books;
  - nearly bare;
  - one long set.

  Heights run from octavo to folio and fit each shelf. Most books are pulled to the shelf edge, a few pushed back.
  There are three bindings: banded calf, plain cloth, and cloth with a paper label. The colours are a muted, faded
  palette of old cloth, converted from sRGB; the old colours were linear values that read as candy. The shader lays
  grey dust on anything facing up, fades the spine heads and darkens the feet.
- **Floor books:** eight piles and five single fallen books lie at the shelves' feet, out of every route.
- **Rug:** a 192x256 Persian pattern drawn in code: an indigo lozenge border, a madder field with a lattice, a
  medallion and corner pieces. It is faded, worn to the warp along the line from the door, and stained. Its mesh has
  a ruck, a curled corner, a dark bound edge and a ragged alpha-scissor fringe.
- **Middle of the room:**
  - a second long table with books, papers, a candlestick and an open book on it, one chair pushed back and one
    tipped over;
  - an oak card catalogue with 30 drawers, brass label frames and pulls. One drawer is pulled out, one is ajar, and
    one lies upside down on the floor with 46 cards spilled round it;
  - a loaded book cart.

  All of it is kept clear of the bot's routes to the bookcase, the journal and the sheeted table.
- **Parquet:** drawn tessellated, weathered with `RoomSurface.Wood`, and baked with `RoomShade`: dark under the
  furniture and at the shelves' feet, pools round the lamps and the fire, and wear along the paths. The rug is baked
  too, and the ceiling plaster is weathered.

## Act 20, the round room (`RoundRoom.Dais.cs`, `dais_lens.gdshader`)

- **Dais:**
  - the top is laid as flags: a centre stone, a ring of 8 and a kerb of 16, each its own shade with grimed edges, on
    a dark joint bed;
  - the sloped foot is 24 dressed blocks, darker toward the floor;
  - the stone is darker;
  - the brass inlay is dulled;
  - there is a scuffed chalk circle and stroke marks, and old wax stubs and runs by three posts.
- **Ring:** it is no longer an unshaded red band. It is now an iron band of chipped red paint (a generated texture)
  set into the foot, with 64 pressed-glass signal lenses in brass bezels, alternating ruby and green, each with a lamp
  under it. The lenses are shaded glass, so the lantern glints on them; only the lamp behind them emits. With the
  power off the ruby lamps burn ("the ring stays red"). On the surge the green chaser runs round exactly as before,
  through the same `head`/`tail`/`intensity` parameters, until every green lamp burns. The ring light is unchanged;
  the unshaded additive glow on the floor (`dais_ring_halo`) is removed. `dais_ring*.gdshader*` are deleted.
- **Floor candles:** four clusters of candles on wax pools round the dais's foot. They join the first tier of
  `_candles`, so they light with the surge.
- Up in the room at the top, the ring light is now at 30%; it used to flood that room green.

## Act 21

- **The room at the top** (`RoundRoom.Top.cs`):
  - the stone is weathered and the inner face tessellated, each block its own shade;
  - a darker plinth course runs round the foot and a string course under the ceiling;
  - the floor is its own worn flag material with joint lines;
  - the ceiling is old boards on four chamfered joists and a cross beam;
  - the arch to the stair is shored with timber posts, a lintel and a sill board;
  - a lamp hangs on its flex with a tin shade over the shaft, and its light is a little stronger;
  - clutter by the walls: crates, a sack, a footlocker with papers, books and a candlestick, a tipped chair, a coil of
    rope and a hook;
  - the room is baked with `RoomShade` (damp, a path from the shaft to the arch) and has tide, streak and floor decals.

  The long stair is unchanged: per STORY.md it starts in concrete and turns to timber higher up.
- **Sigil** (`Church.Gothic.cs`): it is now unlit, shaded paint and old blood, dark brown-red. The red omni is gone.
  Candle stubs on wax pools stand at the star's five points, with one warm light.
- **Crypt votives** (`Church.Crypt.cs`): four candles burn low in ruby glass votives. The glass is shaded and faintly
  warm from the flame inside, and the flames are ordinary warm ones. The light is warm instead of red; the red comes
  only from the glass and the altar cloth.
- **Stained glass** (`ChurchTextures.Lancet`, new cache key `ch_lancet_aged*`): aged like the rose window. The colours
  are greyed and smoky, each piece its own shade, with some later amber or grey repairs and a few cracks. Grime is
  heaviest at the bottom and the sides, under a film of dirt, and the leading is broader. Stained-glass emission is
  80% of what it was. The clear quarries' glow is down from 0.14 to 0.10.
- **Shaft dust** (`window_shaft.gdshader`): new `dust_contrast` and `dust_scale` uniforms. Their defaults reproduce the
  old look for the lodge and the snow maze. The church uses soft, broad dust (0.28, 0.45), and its motes are down from
  26 to 12 per shaft.
