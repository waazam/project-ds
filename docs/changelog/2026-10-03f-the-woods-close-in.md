# 2026-10-03f — Act 1: the woods close in (the falling trees)

Commit: this entry's commit on `main`.

The owner: "right now in act 1 how the player find the staircase is by following those stone paths by the fallen down
tree, I would like that tree to originally be standing upright normally, but then it falls down blocking the players
path when they start getting close, we should use this tree falling mechanic to guide the player to the stairs. Let's
remove the stone pathway, and have trees fall left and right along that path to the stairs forcing the player to go
into that direction ... we can even have trees fall behind them after they pass the first fallen tree ... The falling
trees need their own sound design to have a creaking noise when falling then an impactful thud when they hit the
ground. The physics of them should drop them like a dead weight and not have them floaty or rolling around, they need
to seem heavy to the player."

## What it was
- The trail ended at a fir already lying across it.
- Pale stones led from it round its crown to the stairs.

## The dead fir (`FallenTree.cs`)
- **It stands at first:** a dead grey fir at the trail's end, its root plate under the ground.
- **When the player comes within 22 m** (within sight in the trail end's fog):
  - it creaks (2.8 s, leaning a few degrees in fits as the hinge wood gives);
  - it cracks;
  - it comes down across the trail, tearing its root plate up out of the crater, and lies as it always did.
- **Solid:** while standing, its trunk is; once down, all of it is (as before).

## How they fall (`TreeFallMotion`, `FallingTree.cs`)
- **Not the physics engine** (it floats and rolls them): a hinge at the foot.
- **The fall:** a rod pivoting on its foot under gravity, its angular acceleration 3g/2L times the sine of its lean
  (half again heavier than true), slow at first, then faster and faster.
- **It stops dead where it meets the ground,** found along its length on the real terrain.
- **Only a short, damped jolt** back off the ground, no rolling, and it lies there.
- **The ground shudders under you,** a little, by how near it came down (gentle, the owner's motion comfort).
- **A low burst of dust and needles** settles where it landed.
- **Never pinned under one:** a player inside its length when it lands is put out beside it.

## The sound (`tools/AudioGen/TreeFall.cs`, new)
- **The creak** (3 takes): the trunk giving way at its foot, a deep stick-slip groan through the wood's low modes,
  faster as it goes, fibres ticking more and more.
- **The crack** (2): the hinge wood splitting, a hard report and a splintering tail.
- **The rush** (2): the crown coming down through the air, a swelling roar of boughs.
- **The impact** (3):
  - a sub-heavy slam, the trunk's dull knock, and the crown's smaller slam a beat after;
  - branches snapping all along it;
  - debris and earth pattering down after.

## The way to the stairs (`Act1TreeFalls.cs`, new)
- **The stones are gone.** The old path's line (`FriendTrail`) is still the way, from the trail's end round the fir's
  crown to the stairs' foot.
- **Once the fir is down, trees beside the line come down as the player nears them,** 15 m ahead:
  - eight of them, alternately left and right;
  - lengthwise along its edges, a little outward, so each lies as a wall;
  - near the stairs they fall outward, never into the clearing.
- **Behind them:** once the player is 12 m past each of three points, a tree comes down across the line, closing the
  way back.
- **None comes down within 7 m of the player.**
- **The scatter's trees are kept out of where they'll lie.**

## Tests
- **Looked at in the new `forest_world_preview -- --falls`** (frames through the fir's fall and a side tree's) and in
  Act 1's screenshots (`fir_falling`, `fir_down`), and tuned:
  - the fir's trigger brought in from 34 m to 22 m (at 34 it fell unseen in the trail end's fog);
  - the impact brought down from +10 dB to +8 dB (the audio check's ceiling).
- **The full autotest:** 750/750. New checks:
  - the dead fir stands at first;
  - it creaks and starts down as they near;
  - it lies across the trail;
  - trees came down beside the way (at least 5 of 8);
  - and across it behind them (at least 2 of 3);
  - they still walk to the stairs.
- **Frame rates:** Act 1 to the stairs 99 fps (was 84), the whole game 154 on average.
- **The continue test:** 925/925.
- **The clip audits:** the trailhead 0 z-fighting pairs, the forest acts 40 as before, no walk-throughs.
