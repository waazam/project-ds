# 2026-09-22c — Story pass: one night in the Hollow, Act 6 stair loop, bunker rework

Commits: `c2f436d`, `f17445b`

A dense pass tightening Acts 1-11 into "one continuous night" and reworking
the Act 6 stair clearing and the bunker's back half into something scarier.

## Story Beats
**Acts 1-5**: the first stairs no longer pull the player up automatically —
you inspect the post yourself and take a one-way flight. Rain now starts at
the wake and continues as one unbroken night (no dawn). The stalker becomes
rattle-only and stalks hardest during the path walk (working in sectors,
answering the player's own footsteps, arriving in episodes). A code is
written as digits on notes nailed to trees, tracked by a HUD tracker. The
cabin now gets three pounds and a slam on entry; the newel cap and R.H.'s
page are already on the table on load; the axe and woodpile sit beside the
far wall; rain goes silent indoors; the shed key was removed from the
puzzle chain; self-talk captions removed.

**Acts 6-7**: the clearing becomes a ring with one way in, made of ten
48-step staircases; the loop can start on any flight, with up-only legs and
fog hand-offs between them. A "blood rain" effect causes a daze; walls are
one-way; falling is now a slow drop with a thump. "Come up and see" only
plays where stairs are actually standing; the stairs vanish at the wake.
The cabin is already burning from the wake — seeing it is checkpoint 6. The
giant now paces a "sky lane" with a deep, distant rattle. The trail runs
along the near bank past the fire to the bunker. The compass ribbon
direction, which had been mirrored, was corrected.

**Acts 8-11**: the vine door shuts behind the player and stays locked
until the CRT screens and the walkie are solved. The walkie is found dead
on the console and wakes back up outside with real FM static under every
line. Bunker rooms scare properly now: a blackout, eyes, a slam, a shove, a
40 m hallway chase, and a flash triggered by any look-back; steel doors
added; a hallway lunge event. Act 11 gains a look-up beat, trembling, a
blink-out, and a hum that swells then dies (the roar was cut). New radio
lines: "who's there" / "don't go up". The giant looks down with red eyes.

## Level Design
- The Act 6 clearing redesigned as a one-way ring of ten staircases
  (replacing the earlier fifteen small stair variations).
- The bunker hallway reworked around a 40 m chase sequence and steel
  doors.

## Subtractions
- The Act 11 roar cut, replaced by the swelling/dying hum.

## Audio Fixes
- Thunder is rolling-only now (no crack).
- Lighter rain.
- A sparser deep choir.
- New walkie squelch/tick sounds, steel-door and cabin-slam SFX, giant
  footfalls and rattle.
- The jumpscare sound rebuilt from an old walkie burst layered with a
  creature take.

## Additions
- `StoryTest` gains `--story-from`/`--story-to` per-act runner flags and
  `--start-act=2`/`--start-act=8` dev launch shortcuts.
- Lighting preview scenes; light shafts; a dimmer sky.

## Housekeeping
- `f17445b`: session test logs added to `.gitignore` (no gameplay change).
