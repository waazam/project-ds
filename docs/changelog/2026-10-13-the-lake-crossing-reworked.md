# 2026-10-13 — The lake crossing reworked: hold W to row, a shorter crossing, and the dread from the first stroke

The owner: "The paddling across the lake feels way too cumbersome and takes too long; we need to make that more fun,
scary and less boring."

## What was wrong (measured)
- **About 250 key presses.** Every stroke was a press of A or D, and it only counted if it was the other key from the
  last one. The calm half took 42 s at a brisk three strokes a second (137 presses). After the breach, the current
  held the boat still at two strokes a second; it took three to get anywhere, kept up for more than half a minute.
- **About two minutes**, boarding to beach: 12 s boarding (5 of them drifting off the dock), 42 s calm row, 22 s
  breach, 35 s or more against the current.
- **A long empty first half.** Nothing happened until 9 m before the breach.

## Rowing
- **W rows.** Both oars pull together on their own rhythm for as long as it's held, with no timing to keep. Each
  stroke surges the boat (the bow lifts, the view widens a touch) and then she glides.
- **Shift rows hard**: quicker, heavier strokes, for about six seconds of breath. Breath comes back while rowing
  easy. A thin line under the key shows it.
- **A and D steer.** The boat slides across and turns into it. Let go and she eases back onto the line for the
  station's beach, and over the last stretch she's brought in to the landing whatever the keys say.
- **The old way still works.** Alternating A and D on their own rows as before.
- **The prompt shows all of it**: [W] row, [Shift] row hard, [A][D] steer. The key being used is lit. "Row hard" goes
  red as the thing behind closes in, and when a limb strikes, the steer key that gets you out from under it is lit.
- Pad: the stick rows and steers, the left trigger rows hard.

## Shorter
- About **78 s from boarding to the bow grounding** (84 s to standing on the beach) for a steady rower: 6 s
  boarding, 23 s calm row, 17 s breach, 32 s through the hunt. About 75 strokes, all of them W.
- The lake and the shores are where they were. The boat is quicker (easy rowing 3.3 m/s, hard 5), the breach comes
  sooner (45% of the way, was 55%), its camera beats are a few seconds tighter, and the push-off is quicker.

## Scarier
- **The fog closes in** on the water from the push-off: the dawn haze turns grey and thick, then thins over the last
  stretch so the station comes out of it.
- **Something passes under the boat** early on: a dark shape slides beneath, a dull knock on the planks from below,
  the boat lifted and rocked.
- **The foghorn, far off**, the first time it's heard.
- **A limb comes up out of the fog** to one side, every eye on it open and turned on the boat, watches it go by, and
  slides back under. Then knocking on the hull, then the hush and the breach as before.
- **After the breach, the hunt reads clearly.** Its wake churns behind the boat, a low undertone rises under the boat
  as it closes, and the heartbeat with it. It's a little slower than easy rowing. Every few seconds it comes for the
  boat: it rears, the water boils, there's a thud from below. Easy rowing loses ground to it then; hard rowing
  holds it off. The first time, "It's coming. Row hard."
- **Limbs surface closer and closer** (22 m off, 14, 10).
- **Two strike at the boat.** A limb erupts ahead and to one side, rears, holds a moment looking down, and slams down
  where the boat will be. There are about two and a half seconds to row hard or steer away. A hit throws the boat and
  all but stops it (the hunter gains), but never takes it on its own. A near miss soaks and rocks it.
- **The final dash.** Over the last stretch it gives chase all the way to the beach: the foghorn behind, "Row!".
- **Fair warning before a take.** Inside 5 m the prompt goes red, it knocks on the hull and rears up behind the
  stern, and only after a second and a half of that does it take the boat. Stopping after the breach still drowns
  you, about six seconds in (four of grace, then it closes).
- The oars' sounds are unchanged. Both oars together play a little quieter each. The crossing's sounds never play the
  same take twice running.

## Tests
- Act 12's StoryTest rows the new way at real speed. It checks the old A/D way, W, Shift, steering and the drift back,
  the bump, the first limb, the fog, the knocking, fair warning before the deliberate drowning, the strikes, the
  surges, the final dash, and that the crossing takes 45–95 s. New screenshots: `lake_bump`, `lake_glimpse`,
  `lake_hunter_close`, `lake_strike`, `lake_final_dash`.
- `--lake-steady` makes the second crossing a W-only rower (never Shift, never steering, so both strikes hit), to check
  the slowest player who keeps rowing still makes it.
