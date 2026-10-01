# 2026-10-01 — Fidelity pass: modelled furniture, clutter, baked maps

Commit: this entry's commit on `main`.

The owner:
- **The fidelity pass:**
  - push the polygon count way up on the game's models, mainly furniture and interiors;
  - mesh and bake higher-density maps;
  - a micro-surface detail pass;
  - more clutter and environmental detail.
- **Testing:** test the interiors for clipping and texture flickering.
- **An earlier report, also fixed here:** sometimes a press of E on the mudroom door didn't take.

## How it's built (`tools/Blender/furniture.py`, `tools/Blender/clutter.py`, `FurnitureKit.cs`)
- **Where the pieces come from:**
  - every piece is modelled in Blender 4.0 by script (headless) and exported to `assets/models/furniture/`;
  - each has a **baked cavity map**: ambient occlusion at 1024 px with its creases darkened and a faint grime
    mottle, on a unique second UV layout.
- **How they take the game's own materials:**
  - a piece's faces are grouped by role (upholstery, wood, metal, linen, pillow, cover, glass, paper...);
  - the game gives each role the room's own material (the lodge's photo-sourced leather, velvet and wood, with
    their normal maps), so a modelled chesterfield is in the lodge's own leather;
  - the first UV set is the game's box projection at a metre a repeat, so those materials tile on the pieces as
    on everything else.
- **How the cavity map is used:**
  - it is multiplied over the material as its detail layer and used as its ambient occlusion;
  - this darkens the tufts, the creases, under the arms and where legs meet the floor.
- **Draw cost:** the pieces go into each room's single mesh (`MeshKit.AddMesh`, normals, tangents and both UV
  sets kept), so a room is still one draw per material.
- **Triangle budgets:** each piece is decimated to a budget, because some are used by the dozen (72 dining
  chairs in the hall). The lodge interior went from about 90k to 537k triangles. Acts 22–23 ran at 127 fps on
  average.
- **Fallback:** where a piece is missing, the old built-in shape is drawn instead.

## The furniture (the lodge)
- **Chesterfields** (2.1 m and 2.4 m, about 9k triangles each):
  - deep-buttoned diamond tufting across the back;
  - rolled scroll arms with pleated fronts and a button;
  - piped seat cushions, a plinth, turned bun feet.
- **Wing armchairs:**
  - a tall tufted back, curved wings swept forward;
  - rolled arms, a piped loose cushion;
  - cabriole front legs with pad feet.
- **The low table:** a moulded top edge, an apron with a carved bead, turned baluster legs, a lower shelf.
- **The café tables:** black tops with a brass rim, a turned brass column, a three-footed base.
- **Dining chairs** (the bar's and all 72 in the dining hall):
  - turned front legs, stiles raked back, a carved crest rail;
  - a piped, upholstered drop-in seat;
  - a buttoned back panel, stretchers.
- **The beds:**
  - turned posts with finials, a panelled footboard;
  - a headboard tufted in diamonds inside a moulded frame with an arched crest;
  - a piped mattress, two plump pillows;
  - the counterpane hanging over the sides in folds, the sheet turned down.
- **The lamps** (floor and table): a stepped, turned brass base, a reeded column with a knop, a pleated drum shade
  with brass trim.

## The clutter (the owner: more clutter and environmental detail)
- **The pieces:**
  - wine, whiskey and liqueur bottles (labels, foils, corks);
  - preserving jars, tins, tumblers, wine glasses, enamel mugs;
  - a glass ashtray with a stubbed-out cigarette;
  - rows and stacks of hardbacks (rounded spines with raised bands);
  - brass candlesticks with drips of wax;
  - a bracket clock and a longcase grandfather clock (dial, numerals' ring, hands, columns, a broken-arch
    pediment, finials);
  - bar stools (a piped leather seat, a brass column, a foot ring on spokes);
  - split logs in an iron cradle;
  - snow boots (lugged soles, laces, felt cuffs);
  - old wooden skis with bindings and bamboo poles;
  - a wool coat hung on a hook;
  - fanned magazines and a newspaper.
- **The lobby:**
  - the grandfather clock by the stairs;
  - logs in their cradle on the hearth;
  - on the mantel: the clock, two candlesticks, books;
  - on the low table: books, magazines, a whiskey bottle and two tumblers;
  - by the wing chairs: an ashtray, a glass, a mug;
  - books and an ashtray on the front desk.
- **The bar:** the back bar's shelves are lined with modelled bottles in their amber, green and clear glass, and
  the stools are modelled.
- **The mudroom:**
  - the rack's skis are modelled pairs with their poles;
  - coats hang on hooks over the bench, snow boots are kicked off under it.
- **The guest rooms:** a mug or a glass on each nightstand, books stacked by the bed's foot. Room 201 is spared;
  it's perfect.
- **The cabin (Act 1):** the woodpile, the shelf's jars, tins and books, a mug on the stove, boots by the door and
  a coat on a peg are all modelled.
- **The library (Act 19):** its hundreds of books are a modelled hardback now (a rounded spine with bands, the
  boards proud of a cream page block), each in its own cloth colour. They were plain coloured boxes.

## Clipping and flicker (the clip audit, `--winter`)
- **The crawlspace:**
  - the panel overlap that closed the see-through slits at its corners also overlapped panels along straight
    runs, so boards and brick fought in one plane (5.3 m² of flicker);
  - panels now overlap only where the wall turns.
- **The lodge sign:** the snow on its gable had collapsed UVs (smeared); it is re-projected.
- **Results:**
  - no new pierce-throughs or z-fights from the furniture and clutter;
  - the count fell from 247 to 236 pierce-throughs;
  - the remaining z-fights are the known hidden ones;
  - 0 UV faults.
- **Fixes along the way:**
  - the lobby's two wing chairs (broader than the old blocks) overlapped, and are spread apart;
  - the first pass hung the mudroom's coats over the ski rack and stood the skis on the bench.

## The missed press (`PlayerInteraction.cs`)
- **The cause:** a press of E counted only on the exact physics tick it arrived. Now and then the view wasn't
  quite on the thing that tick, and the press was lost. That was the test's rare failure at the mudroom door,
  and a player's too.
- **The fix:**
  - a press is now held for 0.15 s and used as soon as something usable is in view;
  - a press that closed a note or a menu is spent, so it can't carry over.

## Act 15's pace, and the lodge's logic running everywhere (`SkiLodge.Crawlspace.cs`, `SkiLodge.Interior.cs`)
- **The symptom:** the full test run found Act 15's hallway walk taking 160 s instead of about 125.
- **The cause:**
  - The lodge's per-frame logic runs from the moment the game loads, wherever the player is.
  - It decided the player was in the crawlspace's hidden maze, and inside the lodge, purely by being more than
    40 m below it. Act 15's hallway is.
  - So in Act 15 it slowed the player to the crawlspace's 75% pace, pushed the interior lighting, and hid the
    outdoors.
  - Before the deep-snow change, the sewer reset the pace every frame everywhere and masked it.
- **The fix:** being in the maze or the lodge now means being within the maze's actual footprint, not just that
  deep.

## Tests
- Acts 22–23: 109/109 (127 fps average while walking).
- The full autotest: 690/690 (129 fps average while walking).
- The continue test: 897/897, over 33 scenarios.
- These runs cover this entry and the two before it (2026-09-30b, 2026-09-30c), which land in the same commit.
