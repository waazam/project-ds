# 2026-09-26 — Act 13 fixes: walls, doors, the cryptex, and see-through blood

Commit: this pass's single commit on `main` (see `git log` for the hash).

From the owner's playtest of the demo build, with screenshots. They reported clipping and missing
walls in the station, broken doors, the cryptex letters overlapping, and the cryptex vanishing once
the blood covered it.

## Bug Fixes
- **Walls fighting each other**: Room 1's and Room 2's walls on the lobby side were built in exactly
  the same plane as the lobby's own wall. The lobby's stained wallpaper and Room 2's red paper (and
  Room 1's) flickered through each other depending on the angle. Each room's shared wall is now a
  thin skin just inside the lobby wall, with no collision of its own (the lobby wall has it).
- **The way in was a hole**: the lobby's front doorway (where the player comes in off the lake)
  was an empty gap onto nothing. You could see the grey void and the outside of the basement
  stairwell through it, and walk out. It now has a shut, panelled front door with collision
  ("It won't open.").
- **Doors**: every doorway off the lobby now has a wooden frame, with linings inside the opening and
  casings on both faces. The cut ends of the wall slabs no longer show, and the slits of light
  round a shut door are closed. The door leaves are a touch bigger to sit snug in the frames.
- **Basement stairs**: the landing inside the basement door was a thin slab. Looking up the stairs,
  you saw a pale slit of void under its lip, so it is now solid brick right down. The stepped
  ceiling over the stairs had thin gaps between steps; each step of it now overlaps the next.
- **The cryptex's letters overlapped**: each letter was about twice as tall as its twenty-sixth of
  the ring, so neighbours overprinted each other. They are now sized to fit one per slot, crisp and
  readable.
- **The cryptex close-up** now looks down onto the reading line along the top, where the letters
  that count sit between the two brass guides. Before, the camera looked at the rings' fronts, so
  the letters in the middle of the view weren't the ones being read.
- **See-through blood**: Room 2's flood (lake water, then blood) is now translucent (new
  `flood_water.gdshader`). The cryptex stays readable once the blood is over it. While bent over the
  cryptex, the underwater murk is kept light enough to read through.

## Tests
- The Act 13 test now tours the station on camera:
  - each doorway from the lobby and from inside its room;
  - the shut front door;
  - looking up the basement stairs;
  - the cryptex close-up, and the cryptex again with the blood risen over it.
- Results:
  - `--story-from=13 --story-to=13`: 53/53.
  - `--continue-test`: 642/645. The same three failures predate this work.
- The Windows demo build was re-exported with these fixes.
