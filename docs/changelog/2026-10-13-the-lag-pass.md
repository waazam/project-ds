# 2026-10-13 — The lag pass: what froze the game as things were about to happen, measured and moved out of the moment

Commit: this entry's commit on the lag-pass branch.

The owner: "The game feels very laggy when certain events and things are about to happen; we need to fix that."

## Measuring it (`PerfProbe.cs`, `-- --hitches`)
- **The old logger under-reported.** The story test's `[hitch]` line read the engine's frame delta, which the engine caps
  (about 150 ms): every "120–150 ms" hitch it ever logged could have been seconds. The giant's first step in Act 7 logged
  as 149 ms; it was 3.2 s.
- **New: a lag meter** (`-- --autotest --hitches`). Every frame's real wall time, and each one over 33 ms logged as `[lag]`
  with what it can be blamed on:
  - where the time went: the awaits resumed on the frame (most story beats), the nodes' `_Process`, the physics, the
    physics server's step with the trigger volumes' signals, and the rest (drawing, deferred calls); the renderer's own
    CPU and GPU times; draw calls and triangles;
  - the story event just before it (checkpoints, flags, the test's checks) and how long before;
  - pipelines compiled in it (a first sight), garbage collections, nodes and engine objects made (a build or a burst),
    timed builds that ran in it (`BuildTimer` now reports to it);
  - whether the screen was black (a hidden load), or it was the test's own screenshot or teleport (left out of the counts).
- A table per act (frames over 33, 50 and 100 ms in play, the worst and its cause, the split between first sights /
  builds / collections / drawing, the garbage made a frame and which part of the frame made it) is printed as each act
  ends and written to `hitches.txt` beside the test's report.
- `--soak=60` (with `--story-from=N`): the act's save loaded, the test taken off, the game left standing and measured.
  `--alloc-types`: what the garbage is made of (the runtime's own sampling). `--perf-classes` now also reports each
  class's garbage and engine objects a frame.

## What it found, and what changed
- **Things built on the spot at their moment, their shaders compiled as they first showed:**
  - Act 7, the giant walking out of the murk: a 3.2 s freeze. Its skin is now drawn once in the load's black
    (`GiantStalkerEvent.cs`). Now 55 ms at worst in that act.
  - Act 14, the crawler coming down the stairs after them: 1.8 s. The same (`Stairwell.cs`). Now 72 ms at worst.
  - Act 12, the lake thing's breach: 1.6 s (23 pipelines, and a quarter second building it). Its shaders are warmed at
    the load (`LakeCreature.cs`, `LakeCrossingEvent.cs`), and its limbs are built a limb a frame while the eye opens in
    the deep (the boat has stopped by then: the same arc, the same limbs), then brought up.
  - Act 11, the thing that follows them out of the bunker, its eyes lit: 140 ms. Warmed at the load (`Act11Ending.cs`).
  - Act 8's hallway scare: the stalker built in the frame it appears (60 ms). Built with the bunker, hidden and still
    till its moment (`BunkerFlow.cs`).
  - Act 13, the clock bursting: its dust's shader and its sounds read ahead (`StationBasement.cs`).
  - Act 23: the first sheet off a dining table read four models and lifted their maps on the spot (a third of a
    second); they're read as the lodge builds (`SkiLodge.Act23.cs`). The lodge's own warm-up (90 ms) ran as you came in
    the back door; it runs in the black the lodge is built in.
- **The warm-up itself** (`ShaderWarmup.cs`):
  - it drew each copy with the mesh's own materials, not the ones set on each surface, so a creature whose skin is set
    on its model (the crawler, others) was never really warmed; it now draws them as they are;
  - hidden shadow-casting lights (a closet's bulb) are lit for the warm-up's frames, in the black, so the shadows round
    them are drawn once then;
  - `ShaderWarmup.Prewarm` / `WarmSubtree`: for a thing an event makes later (a throwaway copy drawn with the load's
    warm-up), or one built ahead during play.
- **A late save faded in while the level was still building.** The warm-up gave up waiting after 6 s; loading a save
  from Act 14 on builds the whole underground and the winter, so the picture came up and then froze for 2–5 s while the
  lodge was dressed (and the warm-up had missed all of it). It now waits, in the black, for the building to finish (up to
  50 s; the fade-in waits up to 60). Act 19 from its save: 5.3 s frozen on the first view, now nothing over 55 ms.
- **Garbage collections, the commonest hitch in play** (30–100 ms every few seconds in most acts). The sound director
  built the names of every playing sound into strings every frame (for a test summary), and made a new ray query for
  each of up to twelve occlusion rays a frame; the player's legs made four ray queries and exclude lists a frame; the
  creatures' planted feet one per leg per frame (`AudioDirector.cs`, `PlayerBody.cs`, `AnimKit.cs`, `Crawler.cs`). Each
  is now made once and reused. The garbage made a frame fell from about 150 KB to 15–20 KB in most acts, and the
  collections' long pauses with it. The collector's full sweeps are set to run alongside the game (`PerfProbe.cs`).
- **The first picture of a new game** waits for the warm-up (`OpeningAtCar.cs`): on a slow machine the intro cards could
  end first.

## Not changed (yet)
- The draw itself: some views are simply heavy (Act 15's chamber, Act 24's napalm, Acts 18 and 20 in places), 50–140 ms
  frames with the GPU's own time to match. A graphics question, not a hitch at an event.
- Act 23 into Act 24: pushing the lodge's front door builds the snow tunnel and the maze's props there and then (about
  0.2 s, then 0.5 s). Building them earlier means starting Act 24's systems (its music, its hunter) earlier; left for a
  decision.
- The pit (Act 18) still makes a lot of garbage in places; what's left of its collections are under 50 ms.
- Measured with other copies of the game running on the same machine (other work in parallel): the drawing-bound
  frames vary with that; first sights, builds and collections don't.

## Before and after
(see the hand-off report for the full table)
