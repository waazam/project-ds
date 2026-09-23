# 2026-09-22b — Linear story rework: two levels, the Hollow, R.H.

Commit: `a6b3c91`

A structural rewrite of the story (`docs/STORY.md`, Revision 2), removing
backtracking entirely and replacing the friend character with a stranger
known only through what he left behind.

## Story Beats
- No more backtracking: Act 1 plays out on the Blackfern Trail; the first
  climb blacks out and loads a new level, "the Hollow," where Acts 2-11 run
  along one forward route — camp, storm and giant, cabin, bridge, clearing,
  a lookout over the burning cabin, the bunker, the last staircase.
- The friend character is replaced by a stranger, "R.H.," known only from
  the trail register, his camp note, and his page and prints found in the
  empty cabin.
- New opening: title cards, then the player takes the camera out of their
  car's trunk.
- The park is renamed "Overlook Park"; the stairs are given no backstory
  or explanation.
- A stone newel post becomes a story thread: a broken cap on the first
  stairs, the cap turns up on R.H.'s table ("It's warm."), and it's fused
  back together in the clearing.
- New lines: "This isn't the trail.", "Old stones. Someone's been this
  way.", "Someone's camp.", "The rain stopped. Like it wanted me to take
  it.", "Everything he wrote is in there."

## Level Design
- Two-level structure: the Blackfern Trail (Act 1) and the Hollow (Acts
  2-11).
- A car, a trail register, and a bird card added near the start; pavers
  placed past the fallen fir, with the left side blocked off.
- Weather: a sunny, clear start that fogs in by the footbridge.
- The stalker now appears only in the Hollow; trail signs moved off the
  actual trail.

## Additions
- A photo album (Tab) showing only the pictures actually taken, persisted
  across Continue — replacing the old checklist. New photographable
  subjects: deer, frog, wildflowers, waterfall, holed stone.
- `StoryManager`-driven level selection and travel; `GameFlow` wake-up and
  opening sequence.
- Deep voices routed through pitch-shifting on the Voice bus.

## Subtractions
- The old checklist-style photo tracking, removed in favor of the album.
- The old `AutoTest` harness removed, replaced by `StoryTest`.

## Bug Fixes
- Note overlay centered and made scrollable.
- Lighter rain.
- The nearest staircase's hum now picked correctly (it could pick the
  wrong staircase before).
- A "grounding" pass fixing objects that floated slightly above the
  terrain.
- New dev audits added to catch these classes of bug earlier:
  `ground_audit`, `sign_audit`, `note_preview`.

## Tests
- New `StoryTest` (`--autotest`) walking Acts 1-11 across both levels:
  89/89 checks passing.
- `ContinueRoundTripTest` made level-aware: 310/310 checks passing.
