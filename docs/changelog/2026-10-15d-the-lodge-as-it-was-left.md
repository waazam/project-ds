# 2026-10-15d — The lodge as it was left

The lodge's part of the repetition, clipping and clutter scan (`test-output/scan/SCAN.md`, section (b), and the lodge
rows of the top findings). The fix is in the same commit as this file. Shared kits (the room shader, DecalDresser,
ClutterKit, TrimKit, FurnitureKit, MeshKit) are untouched: their findings are another pass's.

## The dining hall (scan #7)
- **The chairs** (`SkiLodge.Furnish.cs`, `DiningChairs`). They were 84 of one chair in a perfect grid, 0.9 m apart,
  every one tucked square.
  - Now each is pushed in or left out by its own amount and turned a little. About one in five is pulled well out and
    swung; about half are left out a little.
  - A few are gone from their places: they stand in a huddle by the end wall near the door, one tipped onto its side.
  - One is knocked onto its back behind its place at the second table.
  - The runs of three at one end of three tables are under **dust sheets** of their own (`ChairDustSheet`): the cloth
    on the seat, over the back and down behind it, falling in folds to an uneven hem with the legs showing under it.
    A new two-sided linen, `LodgeTextures.ChairSheetMat`.
  - The six sheeted tables, their pulls and everything under them are as they were. Still one MultiMesh.
- **The frozen tables' icicles** (`SkiLodge.Frozen.cs`). They were one every 0.17 m, one width, square (908 in a comb).
  Now in clusters with bare runs, each its own length, girth and turn; short over a chair tucked under the edge, so
  none runs into a seat.
- **Christmas on the floor** (`SkiLodge.Overhaul.cs`). The tinsel, baubles and party hats fell anywhere, under the
  tables and into chair legs, all at one height (the clip audit's coplanar pieces). Now only on open floor, clear of
  the tables and chairs, the trees, the huddle and the window's snow; 1.4 m apart; each 2 mm over the last. The
  presents sit round each tree, not into each other and the trunk.

## The mudroom (scan #9)
- **The ski rack** (`SkiLodge.cs`). Four identical red pairs at an even 0.85 m are now:
  - the modelled pairs in red, faded navy and black, each its own length, lean and turn, at uneven places;
  - an old pair of wooden skis with turned-up tips and cable bindings (`OldWoodSkis`);
  - a pair of poles hung from the rail by their wrist straps.
- **The rack's rails** stood 15 cm off the plaster on nothing. They are on blocks screwed to the wall now, every
  1.3 m, both rails.
- **The back door's snow** (`SkiLodge.Act23.cs`) read as a grey faceted boulder. Rounded now (smooth, finer), its
  packed snow paler with smaller, softer gouges, and loose powder where the fan thins out.
- **The coats**: the long coat's buttons and the parka's quilting sat 1-5 cm off the cloth. They follow its face now.

## Furniture back to the walls
The props pass brought the cases' fronts out to the drawers, which stayed put, and left their backs 11-21 cm off the
wall. Now case, drawers and colliders stand back against the skirting together. Each Searchable drawer keeps its
name, flag and mesh, so its prompt and outline work as before.
- **Nightstands** (`FurnishRoom`): 21 cm back. Where a window is over one (rooms 201 and 203), against the sill
  instead (`UnderSill`). The lamp and the mug or glass move with them.
- **Vanities**: 12 cm back; the basin top now runs from the wall to a hand over the front.
- **Room 204's chest**: 20 cm back.
- **The pantry's cupboards**: 11 cm back; the worktop and the collider follow.

## The rest of the lodge
- **The exterior drifts** (`BuildDrifts`). They were one pillow every 4.5 m, 2.6 x 0.55 x 1.1, faceted. Now a chain of
  soft lumps at uneven steps, each its own size, rising and falling in long swells, bare in places, deepest at the
  wings' far corners and on the north side.
- **The low windows' snow** (`Window`). Every one had the same three lumps. Now each window's own: none at some,
  one to three lumps at others, banked to its own height.
- **Room clutter** (`SkiLodge.Kit.cs`). Rooms 202, 203 and 204 shared one recipe.
  - 202: a leather suitcase with a breakfast tray on it, a ski bag leant by it, boots.
  - 203: a suitcase knocked over, magazines blown about.
  - 204: the footlocker and papers, books, a loden cardigan and a shirt thrown over a chair.
- **The bar** (`Furnish.cs`).
  - The stools stood on a dead-straight line at 0.8 m. Now each sits a little out of line, and one lies on its side.
  - The table chairs stood square at exactly 0.62 m from every table. Now each faces its table, pushed
    back and round by its own amount.
  - The back bar's bottles were the lodge's glow material and looked lit from inside. Now plain dark glossy glass.
- **Reception.**
  - The 32 pigeonholes were black plates standing proud of a solid box. Now the case is open-fronted, with a back,
    shelves and uprights, and a room key or a slip in some holes.
  - The room keys' hooks and the RECEPTION sign hung 24 cm off the wall on nothing. They are on a board on the case's
    cornice now.
- **The lobby's knocked-over chair** (`SkiLodge.Christmas.cs`) was on its front, hanging 17 cm over the floor. It lies
  on its back now, the front legs up.
- **The burst front door's boards** (`SkiLodge.Finale.cs`). Sixteen lay flat at one height, through each other. Each
  now comes down on what's already there, tipped and rolled to fit.
- **The staff notice board** (`SkiLodge.Kit.cs`). The cork and the notes were buried in each other and the board. They
  are in layers off its face now.
- **Room 203's window icicles** hang in clumps and gaps, not a comb every 13 cm.
- **The hung skis, snowshoes and poles** on the lodge's walls have colliders (the clip audit walked through them). The
  side-by-side pair's strap sits on the skis; it stood 15 mm off them.
- **The porch piers** (`BuildPorch`), seen close from Act 24's vault: their half-metre cobble tile read as a
  checkerboard up 4.6 m. Now the stair pier's laid river stone, each pier at its own scale, on a plinth under a timber
  cap.
- **The bookshelves** (`SkiLodge.Overhaul.cs`, `Shelf`). The book rows were turned a quarter and ran 0.46 m back into
  the wall. A shelf in room 203 against the corridor partition pushed them out through the corridor's wall, in a column
  of red book ends. Now they run along the shelf, spines out, backs at the shelf's back.
- **Linen** (`LodgeTextures.SheetLinenMat`): the weave was stretched 3.5 : 1. Now the same scale both ways.

## Not changed
- **The `Hall#3` / `Wings#0` coplanar faces.** They are undersides sitting on the stone course's top, inside the
  wall's thickness where the hall meets the wing. Nothing can see them.
- **The knockable corridor chair** has no collider on purpose: walking into it knocks it over and makes the noise.
- **The single plank scan over most of the lodge's woodwork** is a shared-surfaces finding, left for that pass.
- **The decals** over the ski rack, and the wall streak comb, belong to the DecalDresser and room-shader pass.

## Verified
- Before and after shots at the scan's lodge poses, plus close-ups (`test-output/tmp/`).
- The playtest beats 23a, 23b, 23c, 23d, 23x, 23y, 23e and 24a.
