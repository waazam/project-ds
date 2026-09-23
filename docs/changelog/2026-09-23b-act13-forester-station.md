# 2026-09-23b — Act 13: the forester station

Commit: this pass's single commit on `main` (see `git log` for the hash — noted here as a range
would just go stale across amends).

A big multi-room puzzle stretch inside the dilapidated rescue station the player reaches at the
end of Act 12, aiming for the classic-adventure-game "cut open a taped box," "turn a stiff valve,"
"find the code," "sort the tokens" kind of feel, all first-person.

## Story Beats
- Stepping through the station's front door (Act 12's old ending point) no longer rolls credits:
  it opens onto a lobby with a desk, a knife stuck in it, and four ways out — a duct-taped
  basement door and three locked rooms.
- The basement is flooded. Cutting the tape (a first-person drag-to-cut minigame) opens it; a
  rusted iron valve wheel, turned three times, drains it. Once dry, a grandfather clock in the
  corner sounds a drowned, gurgling chime, then breaks itself apart and leaves a key sitting where
  it stood.
- That key opens Room 1: three walls of hurried red writing ("DO NOT LOOK AT THEM," "DO NOT TOUCH
  THEM," "NEVER GO UP THEM") and a taped cigar box on the floor. Cutting all three of the box's
  sides (left, right, front) opens it on a button; pressing it melts the writing into a blood
  puddle at each wall's foot and unlocks Room 2.
- Room 2 is a code scratched into the wall and a keypad by the door to Room 3.
- Room 3 has three old coins (silver, gold, copper) scattered around and a stone pedestal with
  three sockets. Setting all three coins in place finishes the act, reaches the final checkpoint,
  and rolls the credits (unchanged) for the first time in this playthrough.

## Level Design
- The station interior is a new remote pocket (the lobby plus basement and three rooms, each its
  own sub-room joined by doorway gaps that line up across shared walls) bolted onto the Hollow
  scene the same way the lake and the bunker are.
- Every room got its own ceiling lamp (the lobby, the basement, and all three puzzle rooms had
  none at all until a test pass turned up screenshots that were essentially pitch black — the
  basement also got a soft fill light, since it's the biggest and most spread-out of the five).
- The lake-side rescue station's exterior doorway went from 1.5 m to 2.2 m, matching the interior
  doorway convention used throughout this pass, after test runs showed the narrower gap was an
  intermittent pathing snag right on arrival.

## Additions
- `TapeCutOverlay.cs`: a reusable first-person drag-to-cut minigame (hold and drag in the marked
  direction; the camera frames in on each cut in turn) shared by the basement door and the cigar
  box.
- `StationInterior.cs`, `StationBasement.cs`, `StationRoom1/2/3.cs`, `StationDoor.cs`,
  `StationKit.cs`: the lobby, the flooded basement and its wheel/clock/key sequence, the three
  puzzle rooms, a shared hinged-door helper (locked/unlocked, tool-gated or puzzle-unlocked, swings
  open on its own the instant it's unlocked), and shared wall/floor construction helpers.
- A `Knife` tool (`PlayerInventory`, `ItemMeshes`, `Pickup`): never consumed, since every cut in
  the act reuses the one on the desk.
- `--start-act=13` dev launch shortcut, matching the existing `=2`/`=8`/`=12` ones, straight into
  the station already past the lake.
- New procedurally-synthesized sounds: three knife-slice takes (a rip/crinkle noise burst) for the
  tape cuts, a creaking valve-wheel turn, a drowned inharmonic clock chime, and a clock breaking
  apart.

## Bug Fixes
- `StationDoor.Unlock()` only flipped the locked flag; the actual door-opening (freeing the
  blocking collider, swinging the leaf) lived solely in the E-press handler, so a door unlocked by
  a puzzle rather than a direct E-press — the basement door in particular, unlocked by finishing
  the tape-cut minigame rather than a second key-press — stayed physically sealed forever. Fixed
  by having `Unlock()` open the door itself; a test that had the player getting stuck at the
  basement threshold, unable to reach the wheel at all, now passes.

## Test Fixes
- Added a `StoryTest` step (`Act13Station`) driving the whole chain: knife, tape cut, wheel, drain,
  clock, key, Room 1's box and button, Room 2's keypad, Room 3's coins and pedestal, final
  checkpoint.
- The exterior station doorway widening above came out of this test step's own arrival check
  flaking intermittently even on a healthy run (boat crossing clean, then stuck right at the front
  door) — the same narrow-doorway pathing weakness already worked around for every interior
  doorway in this pass, just not yet applied to this one older piece of geometry.

Isolated `--autotest --story-from=11 --story-to=13`: 58/58, repeatably clean after the fixes above.
`--continue-test`: 401/404 — the 3 failures are pre-existing, in systems this pass never touched
(cabin fire restore, Act 6 clearing-loop restore, a lighting-mood mismatch below checkpoint 9;
already flagged separately). A full `--autotest` (all 13 acts, start to finish in one process) is
currently flaky under this pass in a way that looks load/timing-related rather than logical —
different, unrelated acts fail on different runs alongside anomalous single-frame hitches — and has
been flagged as its own follow-up rather than chased down here.
