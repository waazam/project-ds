# 2026-09-22e — Restore the automatic float up the first stairs

Commit: `b796411`

Playtesting feedback: the first staircase used to lift the player off their
feet automatically; a later rework (2026-09-22c) changed it to an ordinary
walked climb. This pass brings the float back, but look-free rather than
fully hands-off.

## Bug Fixes
- Stepping onto the first flight now lifts the player off their feet again:
  a straight, silent glide to the top landing with no footsteps and no
  WASD, but the mouse (or stick) stays live so they can still look around
  on the way up. Movement comes back the instant they land, at which point
  the broken newel post's E-point appears and the rest of Act 2 (inspect
  the post, the collapse, the wake in the Hollow) plays out unchanged.
  Implemented by freezing the player body's physics for the glide rather
  than disabling input outright, which is what leaves look free — a
  different mechanism than the original 2026-09-18 version of this beat,
  which disabled input wholesale (killing mouse look too).
- `StoryTest`'s Act 2 checks updated for the new sequence (movement taken
  away and handed back, landing position verified against the top marker)
  in place of the old assertion that the player climbs the flight
  themselves.

## Test Fixes
Two unrelated issues surfaced while re-running the full 250-check suite to
validate this change:
- A stray `NullReferenceException` from `StoryTest._Process` reading the
  static session object one frame after `Finish()` had already nulled it
  (`Quit()` only schedules the exit; a frame or two of processing can still
  land after).
- A stalker footstep-echo check divided an already-halved counter by two
  again: `ShadowStepsHeard` already only counts every second real player
  stride (the "one answer per two strides" rule lives in the parity check
  itself), so it should track `ShadowStepsAnswered` roughly 1:1, not 2:1.
  This had been silently wrong since it was written on 2026-09-22, and
  only started actually failing here because the widened footstep stride
  from the sound pass shifted the walk's timing enough to cross the old
  formula's tolerance.

Both fixes brought the full `--autotest` suite from 249/250 to 250/250.
