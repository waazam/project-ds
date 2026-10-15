# 2026-10-15c — Three-quarters, and a thumb on the button

A follow-up to 2026-10-15b. The fix is in the same commit as this file.

## Changes (`HeldModels.cs`)
- **Axe and hammer at three-quarters.**
  - **The problem:** with the bit pointing straight ahead, the axe head read as a thin sliver from the eye, and the
    hammer head lost its shape.
  - **What POV photos show:** the head at about a three-quarter angle, because the wrist rolls a little and the tool is
    carried slightly across the body.
  - **Now:** the bit and the face point forward and to the left, about 32° off straight ahead (was 8°). The flat of the
    bit is partly seen and the head reads as an axe or a hammer. It still points away, not side-on facing the eye.
  - The fist, the grip height and the lean are unchanged.
- **The crowbar's hook** is turned 20° (was 0°), so its curve reads as a hook rather than a stick.
- **The walkie's thumb on the push-to-talk.**
  - The thumb no longer lies across the speaker face.
  - The radio sits with its back in the palm, the fingers round its left side and the thumb along its right side.
  - The push-to-talk moved to that right side, under the thumb.
  - The hand is turned so the speaker faces the eye.
  - `HeldModels`' grip table can now turn an item in the grip (`ItemTurn`), for the walkie.

## Verified
- The hands look-book (29/29 checks: day, dark, blacklight, walking), `--grips` and `--stride`. Each was shot before and
  after.
