# 2026-10-15b — Held the way hands hold them: the axe, hammer, knife, crowbar and walkie

The owner: the axe and the hammer were "STILL not held right — look up references on how a human holds an axe in that
position". The knife, the crowbar and the walkie-talkie weren't right either. Each grip was checked against references
first. The references and the full before/after comparison are in `docs/research/first-person-held-items.md`
("2026-10-15: the grip pass"). The fix is in the same commit as this file.

## Changes (`HeldModels.cs`'s carry table)
- **Axe and hammer:**
  - The bit and the hammer's face now point forward, away from the eye, so it sees the poll and the top of the head.
    They had been yawed 30–35° in toward the middle of the screen, showing the head side-on.
  - The fist is raised into the lower right, so the knuckles and fingers round the handle show. It had been at or under
    the bottom edge.
  - The handle is rolled 10° in toward the middle, the way a relaxed wrist holds it.
  - The grip height was already right and is unchanged: the little finger at the knob or butt, only the knob below.
- **Crowbar:** at the same place as the axe and hammer. The gooseneck's hook is up top and curls forward and down, not
  toward the middle of the screen.
- **Knife:**
  - The guard now sits against the index finger. It had been 2.5 cm clear of it.
  - The blade leans forward and up at 45°, edge down. A steeper lean swung the forearm and cuff up into the view.
- **Walkie-talkie:**
  - It's now held in the palm by its side, using the `Camera` pose's box hold: the fingers round its back and the belt
    clip, the thumb up the front edge, the lower half in the hand, the speaker grille clear above the thumb.
  - It's carried a little higher and nearer than the tools, as a radio is held up to be heard.
  - It had been in the round-handle fist by its bottom 6 cm, the 6 cm-wide case standing on the fist with the fingers
    through it.
  - The model's proportions (6 × 3.4 × 15.2 cm, 12 cm antenna) match a Motorola Talkabout's (6 × 3 × 16 cm) and are
    unchanged.

## Verified
- `--grips` and the hands look-book (29/29 checks): every item ahead, up, down, dark, blacklight and walking.
- `--hands-studio`: each item square on, from the front, side, top and three-quarters.
- `--stride`: walking and running.
- Sheets were shot before and after.
