# 2026-10-14h — The white line: every pickup and every interactable outlined, and every pickup found where it lies

The owner asked: "add a very thin but noticeable white line highlight for items you can pick up and make sure
placement of all items are recognizable". Then he widened it: outline every interactable except the doors and windows.
The fix is in the same commit as this file.

## Additions
- **A thin white outline** (`ItemOutline`, `item_outline.gdshader`, `item_outline_mask.gdshader`).
  - **What it looks like:** about one of the game's own pixels wide (of its 360 lines, whatever the 3D's
    supersampling), white, just outside the thing's silhouette. It doesn't shimmer, and it's the same width near or far.
  - **When it shows:** it fades in between 7.5 m and 5.5 m. It gets a little brighter and wider (1.35 px) while the
    crosshair is on the thing, with the old dim rim highlight inside. It goes when the thing is taken, used up or has
    nothing to say: an empty prompt, a disabled interactable, a crate already open.
  - **Walls:** it's depth-tested, so it doesn't show through walls. It's drawn a few centimetres nearer the camera than
    the thing (2.5 cm plus 0.6 % of the distance), so the edge where an item meets the table in front of it keeps its
    line.
    - At first that was 6 % of the distance. The sheet over a dining table came through as a white slab: the table had
      been picked as the thing to outline, hidden a couple of centimetres under the cloth.
    - Now each sheet's own cloth is outlined, and the pull is small.
  - **How it's drawn:** the thing's meshes get one overlay. It marks their visible pixels in the stencil buffer, and its
    next pass draws the mesh grown outward on the screen, only where the stencil isn't marked.
    - The grow follows the normals, blended toward "away from the centre" where a face looks at the camera.
    - Flat things (cards, notes, keys, the envelope) grow straight out from their centre instead. Otherwise the near edge
      of a card seen at a slant lost its line.
    - A second, chained radial pass broke the stencil in 4.7, so it's one pass, chosen per thing.
  - **Cost:** two extra draws for each outlined mesh, and only while its line shows; the overlay comes off beyond 7.5 m.
    In the round room, with six switch panels outlined, frame time was 12.76 ms with the outlines, 12.37 ms without and
    12.91 ms with them again. That is within noise.
  - **Warm-up:** `ShaderWarmup` draws every outlined mesh's vertex layout with the outline pair, plus a MeshKit box, so a
    pickup revealed later doesn't hitch.
- **Which things get it:**
  - Every pickup: all the `Pickup`s (gear, tools, keys, keycards, the cap, the chalice, the crowbar, the finds in
    drawers), the walkie, the flares in the snow, the lodge's tins and the station's eye. The eye shows only while it
    can be hunted.
  - Every other `Interactable` sets up its own: notes, posters and letters, valves, switches, candles, the font, the
    niches, keypads, puzzle boxes, the crate, the boat, the sheets and the iron door's hollows and web.
  - **Excluded:** the architectural openings, decided from the thing's name, what it belongs to and its prompt: doors,
    hatches, gates, windows and a wardrobe's doors. `Interactable.Outline` (true/false) overrides that; set on the
    lodge's front doors from inside, the station's front door and the iron door's hollows and web.
  - **Drawers and cupboards** (`Searchable`) are outlined only while something is in them.
  - **Hidden until found:**
    - 202's keycard shows only under the blacklight (`Pickup.OutlineWhen`).
    - The library's jutting book shows once the blacklight has found its print, or once the bookmark is in hand
      (`Interactable.OutlineWhen`).
    - The journal and the station's puddles stay disabled until revealed, as before.
  - **An interactable with no mesh of its own:**
    - Its parent is outlined when that's a small thing.
    - Otherwise it's only the small meshes right at its pick point, or the one bigger mesh it's on (a candle stand, a
      sheet over a long table). The table a note lies on is never outlined.
    - The great door's niches are carved in the door's one mesh, so each gets an unseen stand-in the frame's size
      (`Interactable.OutlineProxy`).
- **Audit tools** (dev only):
  - `look_shots.tscn -- --look-pickups`: every outlined thing in the act, shot from about 3.5 m and 1.5 m with a clear
    line to it, logged with its collider gap and the drawn surface over and under it (covered, floating, sunk).
    - `--look-ab` adds shots without outlines, and `--look-around=N` shoots from N sides.
    - `--look-only=` and `--look-dists=` narrow the run, `--look-interactables` lists every interactable and its outline
      state, and `--look-perf` measures frame time with and without the outlines.
  - `--playtest ... --pickup-shots`: each thing the bot uses is photographed from its approach and from where it takes it.

## Bug fixes
- **Room 203's dining room key** lay 9 cm down inside the snow heaped in from the window. Only its prompt said it was
  there. It now lies on the snow.
- **The station clock's key** flew out of the clock with the Hollow key's flat stone under it, and lay on that stone in
  the basement. It no longer has the stone (`Pickup.WithRest`), and it lies on the floor. Without the stone it was 10 cm
  over the floor.
- **The crowbar's prompt** read "Take the item". It now reads "Take the crowbar" (the flamethrower got its name too).

## The pickup audit
Each pickup was shot from the side the player comes from (`test-output/look/b_a*`, `r_a*`, `c_a*`,
`test-output/pickshots/`).

| Act | Where | Item | Verdict |
|---|---|---|---|
| 1 | the car | camera | given at the car, not a world item |
| 3 | the camp's stump | lantern, compass | pass |
| 3-5 | the friend's cabin table | the cap (newel post) | pass |
| 4-5 | the stump by the cabin | axe | pass; it's dark at night, and the outline is what finds it |
| 5 | the shed's bench | hammer | pass from inside the shed (it lies flat on the bench); from the steps, the line shows through the door |
| 8-10 | the bunker's CRT room desk | walkie | pass |
| 13 | the station lobby desk | knife | pass from the room side; from behind the desk the lamp shade hides it |
| 13 | the basement | clock's key | **fixed**: no stone under it, and on the floor |
| 13 | Room 2's table | lighter | pass (small, beside the cryptex) |
| 13 | Room 1 | the eye | outlined only while it can be hunted |
| 13 | Room 1's blood, Room 2's stair | hand, step | no world item (a reach and a cutscene) |
| 19 | the library table | bookmark | pass (dark wood; the outline reads) |
| 20 | the round room sill | spare lighter | only for an old save without the lighter; not shot |
| 21 | the vestry's key rack | iron key | pass |
| 21 | the font | chalice | pass |
| 23 | under the bar's lip | 202's card | hidden until the blacklight, as the story has it |
| 23 | 202's drawer, the pantry drawer | pantry key, 203's card | pass |
| 23 | 203's sill | dining room key | **fixed**: on the snow, not in it |
| 23 | the dining hall's bowl | 201's card | pass |
| 23 | the crawlspace | tins | pass (dark; the outline) |
| 24 | the maze | flares | pass (planted 10 cm into the snow, as meant) |
| 24 | the trenches | crowbar | pass with the outline (pitch dark without a flare); prompt fixed |
| 24 | the crate | flamethrower | given by the crate's cutscene |

## Not done
- **The lodge's curios** (the bottle, the mug, the books) have no outline.
  - They're drawn into the room's one mesh, and the nearest mesh of their own was something else: the books on the
    mantel outlined the ski poles beside them. So `Curio` sets `Outline = false`.
  - They'd need splitting out in the furnishing (`SkiLodge.Furnish`, left alone for the room-dressing work).
- **The lodge's wardrobe hiding spots** ("Hide", prompt empty until usable) have no mesh of their own under them and
  show no line.
- **Room 203's "snow on the sill" box** sits about 0.85 m over the sill (`SkiLodge.Furnish`, room dressing), so it
  floats in the open window.

## Tests
- Playtest `3`, `5a`, `13b`, `19`, `21`, `23a`, `23b`, `23c` and `24a`: all pass, with no engine errors.
  - `13a` passes on its own. With `--pickup-shots`, the extra second of photographs let the clock's key fly off before
    the bot reached for it.
