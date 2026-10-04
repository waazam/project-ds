# 2026-10-03g — More signs, moss by the lake, trim in room 3, neglect in the lodge and cabin, the Act 1 fog, the crawlspace made uniform, the lodge's dark Christmas tree, and Act 18's eyes

Commit: this entry's commit on `main`.

The owner: "yea lets do those 5 recs, and test them out and push them to the main. there also seems to just be some
mismatched textures in the ski lodge, inside the crawl spaces and what not, just go make sure it is all looking
uniform and polished there too." Then: "earlier when i was watching you in the ski lodge it seemed like a lot of textures
were missing from assets like the christmas tree and what not, double check that building", and "the monster we defeat in
act 18 has its eyes all pop when it dies, they should start off popping slow then progressively get faster".

## 1. More signs in the woods (`forest_world.tscn`)
- **A mileage board** 118 m in: "Blackfern Trail 1.8 mi".
- **A second fork post** at 328 m: Blackfern Loop, Trailhead 1.2 mi, Ranger Station.
- **"TRAIL CLOSED / No Entry Past This Point"** on a low board leaning hard, 28 m before the trail's end and the dead
  fir: the park closing the way, and the woods closing it after.
- All in the painted park style.

## 2. Moss (`LakeDressing`, `WinterWoods.Trees`)
- **The lake's big firs and snags** hang with moss from their dead limbs, as the owner's photo.
- **The winter road's snags:** moss too, frosted by the foliage shader's winter.

## 3. Trim in the station's room 3 (`StationRoom3`)
- **The hall:** riveted steel plate, its portraits floor to ceiling, so wood mouldings would have clashed. Steel trim
  instead:
  - a riveted kick plate round the walls' feet;
  - an angle-iron cornice where they meet the ceiling, in the hall and down the corridor;
  - never over the stairwell's hole.

## 4. Neglect in the lodge and the cabin
- **The lodge's four guest rooms and its corridor:** webs in the ceiling corners (years after the party, nobody has been
  up there).
- **The cabin:** webs in its top corners, and bark, dirt and leaves blown in along the walls' feet, the doorway kept
  clear.

## 5. The Act 1 fog (`ForestAtmosphere`)
- **The owner's depth fog** still closes in as they walk toward the trail's end.
- **The bubble at the end is wider:** clear to 6 m, gone by 32 m (it was 3.5 and 15). The trees coming down there,
  15-22 m off, were lost in it.
- **Its grey is darker** (0.3 from 0.44), so it reads as gloom, not a pale glare, and the pale trunks show against it.
- A first try thinned the exponential and volumetric fog round the fir instead. Act 1's fog is depth fog, so neither
  changed anything, and the try was taken out.

## The lodge's crawlspace (`LodgeTextures`, `SkiLodge.Crawlspace`)
- **It mixed four looks:**
  - bright, clean orange boards;
  - the rooms' red varnish on its studs and joists;
  - a grey slab of a floor;
  - glossy iron pipes catching a blue line along the top.
- **Now one material family:**
  - **the boards:** old unfinished pine gone grey-brown, grimy, water-stained in tide lines;
  - **the studs and joists:** the same pine, darker;
  - **the ceiling:** the boards' underside, darker still;
  - **the floor:** the same pine, walked on, dust in its grain;
  - **the brick:** sooted and damp like the boards, its plaster smears greyer;
  - **the pipes:** old iron, rusted and matte (the duck-under pipes too).

## The lodge's "missing textures" (`FurnitureKit`, `SkiLodge.Christmas`)
- **Nothing was missing:** every surface of every piece had its material (checked; the kit now prints any surface it
  can't dress).
- **The Christmas tree read black.** Its baked cavity map (the shading in its creases) averaged 22% grey, because a
  dense tree occludes itself almost everywhere, and the map is multiplied into a piece's colour and its light both.
  The garland, the tinsel, the books and the lamps were darkened the same way, less.
- **Every cavity map is now lifted at load** to average no darker than 62%, keeping its shape (the creases still
  darkest). The tree was lifted from 22%; most furniture moved only a few percent.
- **The baubles and tinsel brightened** (in the hall's low light their old colours read black), and the tree's dense
  core a little.

## Act 18's eyes (`BossRoom.Fight`)
- **The pops accelerate:** they start one at a time with a gap of 0.75 s, then the gap shrinks geometrically to about a
  hundredth of a second, and in the rush several go at once. About 7.6 s for 80 eyes.
- **The sound:** every pop is heard while they're slow, then fewer as they rush, so it doesn't become one smear of
  noise.

## Tests
- **Looked at in the previews and tuned:**
  - the crawlspace (`creature_preview -- --lodge`, before and after);
  - the Christmas tree up close (new `lodge_xmas_*` shots: black, then lifted and brightened);
  - Act 1's fog (the fir's fall and the stairs in its screenshots);
  - the falling trees in `forest_world_preview -- --falls`.
- **The full autotest:** 734/734. New: the lake's shore has its character (41 cypresses, 316 reed stands, 370 patches of
  wrack), and its view of the nearest cypress (`lake_cypress`).
- **Frame rates while walking:** 155 fps on average over the whole game; Act 1 to the stairs 99 (as before, with the
  undergrowth, the palmettos and the photo litter), the lake 177.
- **The continue test:** 925/925, 34 scenarios.
- **The clip audits:** the trailhead 0 z-fighting pairs, the forest acts 40 and the winter 14 (both as before), no
  walk-through props.
- **The sign audit:** nothing blocking. The new signs are clear of the trail: the fork post by 0.48 m, the closed sign
  by 0.97 m.
- **Act 18** passed twice on its own after one flaky run (the bot crushed by a slam at the tenth valve, the valve order
  being random).
