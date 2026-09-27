# 2026-09-26g — The library: the door and passage flicker fixed, a blacklight handprint on the secret book

Commit: this entry's single commit on `main` (see `git log` for the hash).

## The flicker at the library's door (the owner: crazy flickering and texture pop-in at the door)
- The cause was z-fighting: surfaces built twice in exactly the same place.
  - **The front wall**: the library's own front wall was the same slab as the pit's north wall
    (same faces, same doorway sides). Now the pit's wall is the only real wall. Inside it, round
    the door only, there is a thin plaster skin 5 cm in, with no collision.
  - **The doorway floor**: the library's floor started 0.8 m back inside the pit's doorway floor.
    It now starts where that floor ends.
- **Shadow atlas contention**: four lamps plus the fire all cast shadows at the door, on top of the
  pit's lights. Now only the banker's lamp on the table casts them. The standing lamp, the
  sconces and the fire light without shadows.

## The flicker past the secret bookcase
- The passage's walls and ceiling ran on into the round room's wall and doubled its entry sides.
  They now stop at the wall, and the entry sides sit inside the wall's thickness.
- The passage floor ran under the round room's floor disc. The disc now has an exact radius
  (`RoundRoom.FloorR`, 7.45 m), and the passage floor runs from the library's floor to that edge.
- This also closes the 10 cm slit in the floor under the bookcase.

## The secret book (the owner: a blacklight handprint instead of the "S")
- The gold "S" on the jutting book's spine is gone.
- The book is now a thicker old tome that barely stands out from the rest (4 cm, was 7 cm). By
  lamplight nothing marks it.
- Under the lantern's blacklight:
  - a palm print glows on its spine, fingers up, the way you'd tip a book out;
  - there are fingertip smudges along its top.

## Tests
- `--story-from=19 --story-to=20`: 34/34 passed.
- New screenshots:
  - the door from inside, twice a few frames apart;
  - the passage, ahead and back;
  - the jutting book under the blacklight.
- All the new screenshots are clean: no doubled walls, no gaps, and the handprint is readable.
