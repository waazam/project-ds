# 2026-10-13 — Act 1: the falls are seen, the stop to watch, the wrong way, the title in view

The owner: "The introduction when you first see the stairs should be triggered in a way that the user sees the trees
falling, maybe force the player to stop and watch them all fall but it would need to feel natural, plus need to deal
with what we do when the player doesn't go the right way at first. All in all I don't want to see the stairs text on
the screen until the player is close to the stairs and can see them in view."

## Seen, not just heard (`Act1TreeFalls.cs`, `FallingTree.cs`, `FallenTree.cs`)
- **A fall waits to be seen.** Every tree ahead of the player creaks, then hangs at its lean, groaning now and then
  (a different creak take each time, pitch varied, quieter as it goes on, only the nearest one), until it is in view:
  on screen, within 34° of the middle, within 38 m, nothing solid between (a trunk at arm's length doesn't count: seen
  round it). Then it cracks and falls.
- **The dead fir** begins to creak within 26 m and comes down once they're within 22 m and looking. With their back to
  it, it waits (25 s, and then only if they're within 15 m).
- **Behind them** the falls are unchanged: heard more than seen.
- Nothing falls with the player within 4.5 m of where it will lie (6.5 m for the fir and the trees out in the woods);
  the fir now also puts a player out beside it if it ever lands on them.

## The stop
- **The fir's crack stops them.** Their pace dies away over 0.6 s (`PlayerController.WadeScale`, nothing else is
  taken: the mouse still looks), the view is drawn gently toward the falling fir (at most 0.6 rad/s, eased in and out;
  look hard away and it lets go), and then the first trees of the walls come down after it one by one, the view drawn to
  each. They're only ones near where they're already looking (within 85° of the fir, and within 27 m). As the last one starts down,
  their pace comes back over 1.3 s. About 7-9 s in all, 13 s at most.
- **A new first wall tree by the trail's end** (9 m along the way, on the near side): close enough to read through the
  fog while they watch, and it closes the woods past the corner.

## The wrong way
- **The walls come down only while they're on the way** (within 8 m of it) **and looking.** Turn back or stand still
  and they wait.
- **A wall never comes down with them on its far side**, so it can't shut them out of the way.
- **Out in the woods either side** (16 m off the way, seven trees): heading off the way, one ahead of them creaks and
  comes down across their path, in view, and they pull up to watch it land (a short stop, the same as the fir's).
- **In from the side, or round the standing fir:** the walls begin all the same; trees already well behind them stay
  standing.

## The title (`FirstClimbEvent.cs`, `ChapterCard.cs`)
- DEAD SILENT (and then chapter two) only once the player's eye is within 18 m of the stairs and some point of the
  flight (or the post at the top) is on screen, within 36° of the middle, nothing between. Before, it came with the
  silence at about 87 m.
- `ChapterCard.TitleThenStairs` now does nothing until the stairs are revealed, so the hum's call
  (`StairsHum`, unchanged) waits too.

## Tests
- **The full autotest's Act 1** (`--autotest --story-to=1`): 51/51. New checks: the fir comes down once they're looking,
  they stop to watch, their pace dies and comes back, the walls' first trees come down in the stop, no title from the
  fir, the falls ahead seen, the title only close and in view (17.9 m).
- **`--autotest --act1-approach=normal|wrong|side --test-out=act1/<mode>`** (new, `StoryTest.Act1Approach.cs`): three
  approaches with a frame every half second (a quarter during the stop) in `test-output/act1/<mode>/`.
  - normal: fir at 11 m in view, the stop 8.7 s with three down, ten seen, none unseen, title at 18 m.
  - wrong: off the trail (the fir waits), stood near it facing away (waits 15 s), turned to it (it falls, the stop),
    then off the way into the woods (a tree comes down across their way 7.6 m from them, in view; they pull up), back,
    on to the stairs; title at 17.7 m.
  - side: in from the woods to the side (walls begin), beside the stairs with their back to them (no title), turning
    round (the title, 9.6 m).
