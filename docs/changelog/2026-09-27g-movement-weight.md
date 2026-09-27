# 2026-09-27g — Movement with weight (the owner's movement overhaul)

Commit: this entry's commit on `main`.

The owner: "we need to give it a floaty feel … make the character feel like they have some weight and
acceleration". The owner's notes and a reference controller set the tuning:
- acceleration and friction curves, no snapping;
- momentum carried;
- slower backwards and strafing;
- camera inertia;
- a bob tied to the real velocity and the footfall;
- a roll into a strafe;
- look smoothing;
- physics-driven peeking;
- a held interaction locking the stance and slowing the look.

All the head motion is kept gentle (the owner gets headaches from strong camera motion), and it can be
turned off.

## The body (`PlayerController`)
- **Speed builds up and dies away** instead of snapping. It eases toward the target at 3.5/s (about
  70% in a third of a second, all of it in about a second), and without input it decays at 5/s.
  Letting go carries the player on a little, so a corner can't be pixel-peeked in and out of in an
  instant.
- **Direction penalties:** walking backwards tops out at 60% and strafing at 80%, blended for the
  diagonals. Both gather speed more slowly (75%) than walking forward.
- **Running is forward only:** backing away or sidling is never at a run.
- **Changing direction** bends the momentum round instead of reversing it at once (the same eased
  blend).
- **A held interaction plants the feet:** while holding E on something (reaching into the blood,
  working a lock), the body stays put.
- The automated tests keep the old, snappy response and a direct look, because they steer by the
  frame.

## The head (`PlayerCameraRig`)
- **Inertia:** the head trails the body by up to 5 cm as it sets off, and runs on a little as it stops
  (from the body's actual acceleration).
- **The bob is the stride's:** it is driven by the real footsteps (`PlayerFootsteps`). The head dips
  on each footfall, exactly when the step sounds, and sways toward the foot that's down; its size
  follows the body's real speed, not the keys.
  - Before, the bob ran on its own 0.72 m cycle while the steps fell every 1.3 m, so the two drifted
    apart.
- **A roll into a strafe:** about 1.25° at walking speed, eased.
- **Look smoothing:** the view eases after the mouse at a sharpness of 24/s (about 40 ms), so a snap
  round to look behind takes a beat to arrive.
- **Peeking (new):** Q leans left and R leans right (E is use; the d-pad on a controller). The head
  moves 32 cm out to the side and tilts with it, eased. A ray keeps it from going through a wall.
- **A held interaction:** the look slows to 40% while it's held.

## Settings
- **Head motion** (on by default): off, there is no bob, strafe roll, head lag or look smoothing. The
  view is steady and the look direct; the body keeps its weight.

## Tests
- The tests keep the snappy movement and direct look (they steer by the frame), so they measure the
  story, not the feel. Acts 1–2 56/56, Act 11 26/26, Acts 13–14 90/90, Acts 19–21 79/79, Act 21
  29/29. `--continue-test` 698/701 (the same three).
- The feel itself is the owner's to judge in play. The numbers are exports on `PlayerController`
  and `PlayerCameraRig`: Acceleration, Friction, BackwardScale, StrafeScale, LookSharpness,
  HeadLag, StrafeRoll, PeekReach and PeekRoll.
