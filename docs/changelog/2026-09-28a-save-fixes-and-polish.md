# 2026-09-28a — Save/load fixes, the crawler's steps, and a second polish round

Commit: this entry's commit on `main`.

The owner asked to close out the day on the list of things needing attention (the load bugs, frame
hitches, the crawler's piled-up footsteps, the iron door's eye, the Act 6 flaky check, the hatch climb),
and "5-10 more things" to fix and polish.

## Bug fixes (gameplay)
- **Loading a save from Act 10 on brought Act 6's clearing back.**
  - Cause: a Continue after the clearing's loop put the clearing away, but never marked it as
    revealed. The next story flag (the radio's, in Act 11) re-ran the reveal live: the fifteen little
    staircases came back up, and the light turned "menacing" when it should be night.
  - Now the clearing counts as revealed, and a reveal can never run once the loop is done.
- **Two trail markers stood on the path.** On two bends the markers' fixed 1.75 m offset put them 0.3 m
  from the tread (markers 5 and 7 on the sign audit). Each marker now steps out from the trail until
  nothing of it stands within 0.6 m of the path. The sign audit is clean.

## Audio
- **The crawler's steps no longer pile up.** Every plant of its four feet started a new sound, so
  hurrying down the metal stairs there could be nine copies of one step at once (78 sounds playing at the
  peak). It now rings round a pool of three step voices of its own: the same sound, the same variety,
  a fraction of the voices.

## Tests
- **The continue test's three "failures" were two bugs and one wrong expectation.**
  - The two bugs: Act 10's clearing stairs and lighting mood, fixed above.
  - The wrong expectation: the cabin burning on an Act 6 save after the fall. The cabin is meant to be
    burning when they wake (the owner, 2026-09-22), and the test now expects that.
  - The continue test now passes 701/701.
- **Act 6's "cannot back down" check was flaky.** With the movement's run-up, 0.7 s of walking was
  sometimes a tread short of the point where the way back shuts. The test now walks until the one-way
  wall arms.
- **The iron door's eye is now photographed close** once it's set in the LOOK hollow
  (`iron_door_eye_set`), so the fix to it can be seen on every run.
- **The Act 4 blacklight footprints shot** was taken four frames after aiming, before the camera's new
  look-smoothing had settled, so it showed nothing. It now waits 0.6 s.
- **The systems preview** (`systems_preview.tscn`) had gone stale since the story rework.
  - The stalker now wakes when the lantern and compass are taken, not at the footbridge.
  - The cabin lives in the Hollow, not the trail level this harness loads. Its checks now skip,
    saying so.
  - It passes again.

## Frame hitches: a logger, and what it found
- **New:** the story test logs every frame over 100 ms (`[hitch]`), with the act and the check or
  screenshot just before it.
- **What it found** (the full run: 324 frames over 100 ms in 37 minutes):
  - 178 come straight after a test screenshot: reading a 1440p frame back from the GPU. Test-only.
  - Almost all the rest are 120-150 ms and follow a test teleport, where the next area streams in.
    Also test-only: in play you walk there.
  - Two are real one-offs in play, at about 0.45 s each:
    - the last staircase rebuilding itself at 220 steps (Act 11);
    - the first red light in Act 15's hallway.
  - Both happen once, at a story beat, and are left for a performance pass with the profiler.
- Walking average: 99-109 fps.

## Test results
- Full autotest: 580/580 (before the Act 22 work).
- Continue test: 701/701.
- Sign audit: clean.
- Systems preview: all checks pass.
