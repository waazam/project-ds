# 2026-10-13b — The fir always falls, the descent's music lands at the bottom, a near miss at the lake, the player's own shout

The owner's decisions on four points from the polish pass.

## Act 1: the fir never keeps anyone waiting (`Act1TreeFalls.cs`)
- **The 7–9 s stop is unchanged.** The fir still waits to be seen and its crack still stops the player to watch.
- **What was there:** a fallback after 25 s unseen, but only with the player within 15 m of the fir. Anyone further
  off with their back to it, or who had wandered away after its creak, could leave it hanging there.
- **Now:** 25 s after its first creak it comes down wherever the player is, with its whole creak, crack, fall and
  impact. The one condition left is that it never lands on them. `FirUnseenRange` is gone. It is seen only within its
  usual 22 m. Further off, the fall counts as unseen, and the stop to watch only happens within 34 m, as before.

## Act 14: the descent's music (`Stairwell.Crawler.cs`)
- **The track:** the file is 4:12, but its music is only the first 1:48 (the rest is silence). It runs a steady first
  minute, breaks at 1:00, builds at full from 1:04 to 1:35, then fades out on its own by 1:48.
- **Measured** (`--measure-walks`): walking, the music starts 2.5 s down and the edge of the fallen flight comes 87 s
  later. From the top, the edge used to fade it at 87 s into the track, 8 s before its build ended.
- **Now it starts 8.5 s in** (`DescentMusicOffset`). Walking, the break comes a little past halfway down, the build
  carries the rest, and the edge arrives at 96 s, just as the track starts its own fade. Our 5 s fade rides on top.
  Sprinting (67 s), the edge comes at 74 s, in the build. Either way it is the edge that fades it, wherever the track is.

## Act 12: the near miss (`LakeCrossingEvent.cs`)
- **Why:** a good rower never let the hunter close in, so they never felt it.
- **Now, on every crossing:** once the first strike is over (from 0.655 of the way, never while a strike's key is lit,
  always before the final dash), the hunter comes for the stern.
  - A thud under the water, and its wake races up behind.
  - Then it breaks the surface right behind the boat, about a metre and a half off the transom: the creature's
    foghorn bellow up close, the breach, a column of water and spray over the stern, and rings spreading out.
  - The boat rocks: the stern drags down, the bow lifts, she rolls and shakes, the heartbeat at its loudest, and the
    prompt goes red.
  - It rears there for about two seconds, every eye on the boat, in plain view for anyone who looks back.
  - Then it sinks with a thud and drops back to the hunt's usual 11 m. The surges wait until it is over.
- **Fair:** it is scripted from start to end. There is no warning, no knock count and no take, and the boat keeps its
  speed. The hunt, the surges, the strikes, the warning before a take and the drowning all work as before around it.
- **The second staring limb** comes up a little later (0.72 of the way, was 0.69) and stares a little less long, to
  make room.

## Act 11: "Who are you!" is the player's own (`Act11Ending.cs`)
- **What was wrong:** it played like the radio's lines: squelch, the channel's static and crackle on the Radio bus.
- **Now it's their own shout,** the only line the player says. There's no radio sound under it. A quick, hard breath
  in comes first, dry and close on the Player bus (non-positional, at -7 dB), then the words come up fast (0.15 s, not
  0.6). The game has no recording of the player's voice, so the line is still text on the subtitle band.

## Tests
- **New playtest beat `1b`:** the player stands 11 m from the fir with their back to it, then walks off 30 m down the
  trail, never looking. It waits 15 s, then comes down unseen after 25.0 s, and they're free to go on. 7/7.
- **The Act 12 StoryTest** checks the near miss: it rears behind the stern (3.4 m, rail to rail) and sinks back past
  8 m, with no warning in it and no take. There's a new screenshot looking back at it, `lake_near_miss`. The Act 12
  playtest beat checks that there is exactly one near miss and that it sank away.
- **`--measure-walks`** now plays the music on both passes. It checks that walking reaches the edge at the end of the
  build (96.0 s into the track) and that the edge fades it when sprinting too (74.2 s).
