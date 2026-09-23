# 2026-09-22a — Pre-rework checkpoint

Commit: `bd717f9` (tagged `pre-linear-rework`)

A deliberate revert point banked immediately before the linear story
rework below, so that work could be undone cleanly if it didn't land. Not
playtested as a whole — some pieces (bunker papers, Act 11 radio) were left
mid-flight when the pass stopped.

## Additions
- A readable-notes system (`Readable`, `NoteOverlay`, `PaperKit`); a modal
  input state on `PlayerInput` so reading a note properly captures input.
- Act 1's photo trip gains a shot list, a `PhotoSubject` framework, prints,
  and a notebook page (Tab).
- Cabin papers: a door note, a tongue-twister sheet, the friend's page, and
  prints.
- Bunker papers and Act 11 radio work started, but left incomplete this
  pass.

## Story Beats
- Act 2 now ends in a blackout, waking at the footbridge; the compass's
  sweep is limited.

## Bug Fixes
Fixes that came out of a systems review:
- A ref-counted cutscene lock (so overlapping cutscene requests don't
  release each other's lock early).
- A caption-echo line fix.
- The Act 6 climb properly gated.
- Stair length now driven from `StairsState` instead of being hardcoded in
  more than one place.
- The stalker stays dormant until the player is past the bridge and
  indoors.
- Indoor audio gating.
- Performance caching.

## Level Design
- A first pass at an open-trail atmosphere grade — noted in the commit as
  "too subtle," i.e. a known follow-up.
