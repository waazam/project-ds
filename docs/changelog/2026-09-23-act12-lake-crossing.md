# 2026-09-23 — Act 12: the lake crossing

Commit: this pass's single commit on `main` (see `git log` for the hash — noted here as a range
would just go stale across amends).

A brand-new act inserted right after the Act 11 ending, replacing the immediate credits roll:
the player wakes at a lake, rows across it in a small boat, and is attacked by something in the
water halfway over.

## Story Beats
- After the giant's touch (checkpoint 9) the player no longer cuts straight to credits: they're
  carried, still blacked out, to a lake's near shore and wake at sunrise (a short "coming to"
  beat — vision clearing, camera levelling — mirroring GameFlow's own hollow wake-up).
- A rowboat sits right there. Pressing E boards it (a short glide into the seat, the same
  frozen-body/free-look mechanism the first stairs' climb uses).
- Paddling is on-rails but player-paced: alternating A (left oar) and D (right oar) — a stroke
  only counts if it switches side from the last one, so holding one key does nothing.
- Midway across, tentacle limbs covered in bloodshot eyeballs breach the surface: a red screen
  flash (respecting Reduce Flashing), a low foghorn-like blast, and the water turning
  white-capped for several seconds.
- The water stays rough afterward: a current now drags the boat back unless the player keeps
  alternating strokes fast enough to outpace it.
- Reaching the far shore lands the boat and reveals a large, dilapidated forest rescue station.
  Control comes back; walking up to its doorway is the act's real end — checkpoint 10, then the
  credits (unchanged) roll.

## Level Design
- A new lake area bolted onto the Hollow scene the same way the bunker interior and the final
  stairs are: its own remote coordinates, self-contained ground and water, no dependency on the
  main terrain's heightfield.
- A still water plane (a new shader, gentle multi-directional ripples at rest, whitecaps that
  bleed in as `wave_intensity` rises for the breach), two raised shorelines, a small dock at the
  near shore, and the rescue station (board-and-batten walls gone grey, a sagging half-stripped
  roof, boarded windows, a lopsided porch) at the far one.
- Checkpoint 9's Continue/respawn point now lands at the lake's wake spot instead of near the
  final stairs — anyone who quits between the giant and finishing this crossing resumes exactly
  where the live version would have put them.

## Additions
- `Lake.cs`: the passive environment (water, shores, dock, mist, ambient sound, the rescue
  station).
- `LakeCrossingEvent.cs`: the boat, the boarding interactable, the paddle mechanic, the breach
  sequence, and the arrival trigger that hands off to the credits.
- `LakeCreature.cs`: the tentacle-and-eyeball creature, built from chained tapering cylinders and
  jittered eyeball blobs, that rises and sinks around the boat during the breach.
- A dawn lighting mood (reusing an existing but previously-unused `ForestAtmosphere.Mood.Dawn`)
  now applies globally from checkpoint 9 onward, since the Hollow's night is never seen again
  after this point.
- `--start-act=12` dev launch shortcut, matching the existing `=2`/`=8` ones, straight to the lake.

## Audio Fixes
- New procedurally-synthesized sounds, built the same hand-written-DSP way as every other sound
  in this project: a calm, deliberately quiet lake-water ambience (`lake_loop`, mostly under
  100 Hz, no bright droplet ticks) and a low, mournful "foghorn" one-shot for the creature's
  breach (a pulse-train pipe resonance with a second, slightly-detuned throat underneath, in a
  large hall reverb).

## Test Fixes
Two race conditions surfaced while getting the new Act 12 StoryTest step to pass reliably:
- A polled `WaitUntil` on the checkpoint could lose the race against the real credits sequence
  completing and the main menu restarting a fresh game under `--autotest` (the same class of bug
  `Act2Climb`'s test already worked around) — fixed the same way, by subscribing directly to
  `StoryManager.CheckpointReached` so the test's response runs synchronously, before the credits
  task gets a chance to start.
- Added a `PaddleUntil` test helper (alternates scripted A/D input) alongside the existing
  `PushFor`, for driving the new paddle mechanic.

Full `--autotest` (all 12 acts): 265/265. `--continue-test`: 401/404 — the 3 failures are
pre-existing, in systems this pass never touched (cabin fire restore, Act 6 clearing-loop
restore, and a lighting-mood mismatch below checkpoint 9); flagged separately rather than fixed
here.
