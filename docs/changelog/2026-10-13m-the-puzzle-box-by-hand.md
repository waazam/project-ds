# 2026-10-13m: The puzzle box by hand

Dan: "make the block puzzle drag n drop with mouse left click. and right click to rotate pieces". Act 19's bamboo
puzzle box (`PuzzleBox`, `PuzzleOverlay`) was played by keys only: pick a piece with A / D, take it with E, walk it
over the box a cell at a time. It now plays with the mouse, and the keys and the pad still work as before.

Commits: see `git log --grep "puzzle box"` on this branch (the work, and this entry).

## How it plays
- **The cursor comes out** when the close-up opens and is captured again when it shuts (or put back as it was). The
  piece under the cursor lifts a little, and the cursor turns to a hand over it.
- **Left mouse, hold and drag.** Press on a piece to pick it up by the cube under the cursor (out of the box, if it's
  in it). It rides up clear of the rim with a soft shadow under it and follows the cursor over the table. Over the
  box it snaps from cell to cell, kept inside the box. A ghost on the cells it would take shows where it lands: a warm
  outline where it fits, a red wash (and the piece tinted red) where it doesn't.
- **Let go** over the box and it drops in if it fits. If it doesn't, it knocks the rim and goes back where it came
  from (its cell in the box, or its spot on the table). Let go over the table and it goes back to its spot there with a
  soft wooden clack.
- **A click on a piece in the box** (no drag) takes it out, back to the table.
- **Right-click or the wheel** turns the held piece a quarter turn about the cube you hold it by, easing round. With
  nothing held it turns the piece under the cursor where it lies: about its middle in the box (nudged a cell if it
  must be), or on the table if it still has room clear of the other pieces. If it hasn't, it knocks.
- **Keys and pad unchanged:** A / D (stick, LB / RB) pick, E (A) take and set in, W A S D (stick) move, right-click /
  wheel / RT / shoulders turn, Esc (Start) puts it back or straightens up. A piece held by the keys can be taken over
  by the mouse with a click. The hint line names both.
- While it's open the wheel, the shoulders and 1-9 belong to the puzzle (`PlayerHands.Hold`): they never switch the
  item in hand, and right mouse never raises the camera or gathers the lantern's beam.
- Every piece eases to where it belongs (no jumps, no jitter), whether it was dropped, turned, knocked or put back. The
  solved logic, the `Solved` event and everything after it (the glow, the burning away, the bookmark, the checkpoint,
  the save) are as they were.

## Details
- The cursor's ray is cast from the close-up camera onto the plane the held piece rides in, in the box's own space, so
  it holds at any resolution and window shape. Pieces are picked by the ray against their cubes as drawn.
- `PuzzleBox` gained what the held and ghost looks need: a soft shadow decal under each cube (shown only when held),
  a tint on the bamboo shader's existing `tint`, `ToRest`, and turning a piece where it lies on the table
  (`TurnAtRest`, checked cube by cube against the others). The short piece's spot on the table moved 4 cm so it no
  longer sat inside the S piece.
- New sound key `clack` (Library): a quiet `wood_place` variant at a random pitch.

## Tests
- The playtest's Act 19 beat drives the overlay's own pointer path at viewport points: a left-click picks the first
  piece by its cube, right-click turns it, a drag across the table (no snap, no ghost), then over its cell (snapped,
  ghost fits), let go: it's in. A second piece dragged onto the first: red, and let go it knocks and goes home. A
  click takes a piece out. Real right-click, wheel and number-key events don't switch the item or raise the camera.
  The cursor is out while it's open. Screenshots: dragging (fits), blocked, solved.
- `--playtest-only=19`: 30/30 checks pass. StoryTest's Act 19 still uses the unchanged key-path API (not run).
