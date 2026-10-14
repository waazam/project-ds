# 2026-10-13 — The blacklight's ink on real surfaces, handprints that look like hands, clawed creature hands, and no spikes on the stalker's shoulders

Commits: `a5f9372` (the ink), `54d772e` (the hands and the shoulders), and this entry's.

## The blacklight's marks (the owner: "floating on nothing ... too grouped up ... on an invisible piece of paper")
- **The paper**: each mark was a flat card drawn additively, and the fog was added over the whole card, so every
  mark sat on a pale rectangle, lit or not. The ink is now a **projected decal** (`uv_ink.gdshader`): a thin box
  that finds the real surface behind each pixel from the depth buffer and draws only the ink, only where that surface
  lies inside the box and faces the mark's way. There is no card, no fog on it, nothing on the back of a thin wall, and
  nothing at all where there is no surface. It wraps bark, books and boards.
- **Floating**: the cards stood off what they were meant to be on. On the trees, the ink was wrapped round a perfect
  cylinder 1 cm outside the trunk's eight-sided bark, so it floated up to 3 cm off each flat face and stood out at the
  sides. In the cabin the tally marks were 13 cm off the wall and the door's hands 9 cm off the door; the camp's hand
  was off the stump.
  - Trunk marks are now cast onto the bark from outside it; the camp's hand is wrapped round the stump.
  - Every mark on a wall, floor or ceiling is checked against the visible geometry 45 frames after it is placed
    (`UvInkSettler`): moved onto its surface if it is off it, slid along to the nearest spot that has one, or dropped
    if there's none near (words are never dropped). Every move is logged (`[uvink]`).
  - The audit (`--uv-tour --uv-audit`) lights each mark alone, head on, and counts the ink that lands on a surface:
    0 of 126 marks show nothing from Act 8; from Act 13, the three that show nothing are in places the game itself
    hides at that checkpoint (the settler found their surfaces).
  - Moved: the cabin's 4 tallies (13 cm), its 2 small hands and "IT KNOCKS THREE TIMES" (8-9 cm), the bunker's
    "YOU'VE BEEN HERE BEFORE" (10 cm) and "THE STAIRS WANT YOU BACK" (6 cm), the stairwell's words and hands (2-3 cm),
    the station's writing and Room 1's hands (3 cm), "TURN THE LIT ONES" 55 cm along the catwalk, "ROW. DON'T STOP."
    12 cm along the dock.
- **Grouped**: round each of R.H.'s trees stood three decoy trees 3-6 m away with up to three marks each, and seven
  more marks on the ground within 5 m: a heap of glowing things in one spot.
  - Decoy trees now stand 4.5-10 m away and at least 3.5 m from each other, with one or two marks each, never
    overlapping each other or his number.
  - Four ground marks round each, at least 2.5 m apart (were seven).
  - The cabin's tallies are spread along the wall, the bunker's hand comes every 2-3 m (it was one stamp every 1.4 m),
    Room 1's ceiling hands are a stride apart.
  - The clues stay where the story needs them: the handprint on the secret book's spine, the fingertips on its top, the
    hand by the closet's switch, the lodge's hand over "UNDER", R.H.'s digits.

## The handprints (the owner: "don't look like hands, look up references")
- Built from palm-print anatomy (`UvInk.Hands.cs`): the three interdigital pads under the fingers, the thenar pad at
  the thumb, the hypothenar pad down the outer edge, the heel, the hollow barely printing; the heart, head and life
  lines as gaps; four fingers of three pads with gaps at the joints, the fingertips strongest; the thumb set low and
  angled out. Hard pressure prints solid, light pressure breaks up.
- Kinds: pressed, splayed, partial (a grip), fingertips (feeling the way), dragged (smeared down), a child's, and the
  creature's: a narrow palm, long knuckled fingers, hooked talons scored past the tips, splayed and raked.
- Where they are: R.H.'s and people's are human (the library's book, the stump, the closet, the stairwell's rail, the
  bunker's dragged hand, the cabin door's small hands); the thing's are clawed (Room 1's ceiling, most hands on the
  trees and the ground). Every print is a hand's real size (was up to 60 cm).

## The creatures' hands (the owner: "could be way better, more claw like")
- A shared `talon()` in `tools/Blender/wendigo.py`: rooted in the fingertip, curving harder and harder toward the
  palm, drawn to a needle point.
- **The wendigo** and **its crawlspace arm**: 17 cm black talons hooked in (were 11 cm straight cones); knuckles
  standing out of the back of the hand; tendons from the wrist to each knuckle.
- **The stalker** (and the giant, the same model): 7 cm talons for its 3 cm nails; thinner fingers between knobbed
  knuckles; tendons on the back of the hand.
- **The crawler**: hooked black claws curling into the floor (were 2 cm nails); knobbed knuckles.
- Skeletons and clips unchanged (stalker 123 bones, crawler 35); the stalker's grip on the bark still lands exactly.

## The stalker's shoulders (the owner: "remove spikes from the stalker's shoulders and the giant's shoulders")
- The seven root spikes and their side shoots on each shoulder are gone, and the four bark shards on the shoulders
  with them. The knotted root masses stay, lowered into the shoulder so they read as one. The shards down the middle
  of the back stay. The procedural fallback (`StalkerBody.cs`) matches.

## Tools
- `--uv-tour[=name] [--uv-audit] [--no-gate]` (with `--start-act=8` or `13`): blacklight screenshots round the game
  and the per-mark audit, to `test-output/uvtour/`.
- `scenes/levels/uv_prints_preview.tscn`: every print on one sheet.
- `creature_preview.tscn -- --hands[=name]`: every creature's hands up close, the stalker's shoulders, the giant.

## Tests
- `--autotest --story-from=3 --story-to=7`: 174/174 passed (the four numbers read under the blacklight).
