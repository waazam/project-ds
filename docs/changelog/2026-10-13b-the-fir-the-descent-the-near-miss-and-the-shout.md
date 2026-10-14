# 2026-10-13b — The fir always falls, the descent's music lands at the bottom, a near miss and a sudden breach at the lake, no clipping in the rowboat, the player's own shout

The owner's decisions from the polish pass.

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

## Act 12: no eye before the breach (`LakeCrossingEvent.cs`)
- **The owner:** "remove the giant eyeball before the kraken jumps out of the water, just have it jump out of the water
  and scare the player."
- **Removed:** about five seconds of build-up with the input taken.
  - The boat coasted to a dead stop and the view was drawn down over the side into the water.
  - An eye the size of a house opened in the deep (the lake's `EyeOpen` / `EyePupil` / `EyeLook`, with two whispers
    and a thud), and its pupil snapped to a slit.
  - Then it shut, the water domed up ahead (`Bulge`), and the foghorn sounded.
- **Now:** mid-stroke, out of the hush, it bursts out of the water 10 m ahead.
  - The sound: the eruption, its foghorn bellow from under the surface and a slam.
  - A column of water and a burst of spray, with spray and a wave slap over the bow.
  - The boat is thrown back, rolled and stopped dead in 0.9 s, with a hard shake and a heartbeat.
  - The view snaps up to it in a third of a second, then is dragged up the colossus.
  - The rest goes on as before: the red wash, the ring wave, the others all round, the limbs' eyes opening and blinking
    together, the one that looms, the slam, and the letting go.
- The creature is built a limb a frame from the hush on (9 m before the breach point), unseen, where the boat will be,
  so the burst doesn't hitch.
- **Kept:** the hush, the shadow sliding under and the stir under the boat. They play while the player is still rowing.
  The drowning's eye (it rises out of the dark as they're dragged under) is a separate scene and is unchanged.
- The breach is 11.5 s with the input taken (measured; the rework measured 17 s).

## Act 12: the rowboat no longer shows the inside of the jacket (`PlayerBody.cs`)
- **The owner:** "looks like the heads or something's clipping in the rowboat part" (maybe the beard).
- **What it was:** the player's own body, not a head or a beard. Seated, the body came down a fixed 0.5 m, which left
  the chest 0.2 m too high for the boat's 0.92 m eye, with no offset back from it. The eye sat inside the jacket's
  collar. Looking forward, the jacket's insides filled the lower two-thirds of the view: a black, jagged shape with the
  zip up through it. Looking down or round, the collar showed cut open. The body has no head or beard in view; the head
  only casts a shadow.
- **Now:**
  - Seated, the whole upper body comes down with the eye (by the drop from standing height, 0.5 to 0.8 m).
  - It sits back from the eye as it does standing.
  - The chest and the shoulders' caps cast a shadow only while seated, so the lap, the jeans and the boots are still
    seen looking down.
- **Before and after:** `test-output/boat-before/` and `test-output/boat-after/` (forward, back, left, right, down,
  back-down).

## Act 11 (`Act11Ending.cs`)
- **"Who are you!" is the player's own.**
  - *What was wrong:* it played like the radio's lines: squelch, the channel's static and crackle on the Radio bus.
  - *Now it's their own shout,* the only line the player says. There's no radio sound under it. A quick, hard breath in
    comes first, dry and close on the Player bus (non-positional, at -7 dB), then the words come up fast (0.15 s, not
    0.6). The game has no recording of the player's voice, so the line is still text on the subtitle band.
- **Under the stairs, not 70 m out.** The stairs' first "come up and see" and the radio's "Don't go up." were meant for
  coming under the stairs, but both played at the fog's edge, 70 m from the flight. The fog still thickens there; the
  call and the line now wait until the player is within 14 m of the flight's foot (`UnderStairsRadius`).

## Tests
- **New playtest beat `1c`:** the player stands 11 m from the fir with their back to it, then walks off 30 m down the
  trail, never looking. It waits 15 s, then comes down unseen after 25.0 s, and they're free to go on. 7/7.
- **The Act 12 StoryTest** checks the near miss: it rears behind the stern (3.4 m, rail to rail) and sinks back past
  8 m, with no warning in it and no take. There's a new screenshot looking back at it, `lake_near_miss`. The Act 12
  playtest beat checks that there is exactly one near miss and that it sank away.
- **The breach** (StoryTest): the creature is out of the water within a second of the breach starting, with no eye
  opened. This replaces the old "an eye opens in the deep" check and its `deep_eye` screenshot, now `breach_burst`.
- **The rowboat** (the Act 12 playtest beat): seated and mid-stroke, it takes a picture looking forward, back, to
  each side and down. Looking forward, the middle of the view must be the lake (bright), not the body (it measured 0.00
  before the fix and 0.41 after).
- **`--measure-walks`** now plays the music on both passes. It checks that walking reaches the edge at the end of the
  build (96.0 s into the track) and that the edge fades it when sprinting too (74.2 s).
