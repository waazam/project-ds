# 2026-10-13 — Act 6: the fall goes off the edge, and the clearing comes in at the cabin door

Commit: `4bdc7a1`.

The owner: the fall off the last landing "looks funky, like he falls through the top", and "the rain and everything
should spawn in when you open the cabin door to leave, but the player won't see all the blood rain till he's over the
bridge."

## The fall (`Act6ClearingEvent.cs`)
- **What was wrong:** the fall's tweens all ran at once (a `Chain` followed by `SetParallel` cancels the chain), so the
  drop began at the front of the landing and the eye sank down through the stone.
- **Now:** one path in three parts, planned before it plays, about three seconds in all:
  1. **The stumble:** to the edge, gathering pace (no jolt), the view turning out over it and down to the stone at their
     feet, while the landing sags a few centimetres under them.
  2. **The tip:** the body pivots on the edge, a little slower than a real topple, carried over by the stumble's pace.
  3. **The drop:** falling free and accelerating, with a slow roll. The black comes in over its last 0.6 s, and there's
     a soft thump in the dark.
- **Swept against the world first.** The way off the back is simulated whole and the eye's path swept against the
  world's collision, from the first stumbling step until the eye is near the ground. If the back is blocked (or the
  ground behind comes up too near on a slope), it goes over one of the side walls instead, pivoting on the coping.
- The eye rides the path with no lag. The player's own body is hidden through the topple (it's drawn standing and
  can't fall with them), and the view is level again when they wake on the trail.

## The clearing comes in at the cabin door (`Act6ClearingEvent.cs`, `BloodRain.cs`)
- With the cap in hand, the moment the slammed cabin door is opened again, the woods' giants, the clearing's fifteen
  stairs, its lights and its ring of deadfall go into the world, a piece a frame (so there's no hitch). The blood rain
  starts then too. On a Continue with the cap already taken it all goes in at once, under the load.
- **The blood rain is unseen by the cabin.** `BloodRain.Presence` (0 to 1) scales its drops, its sound and the red in
  the fog. It's 0 by the cabin and comes in along the way to the footbridge, all of it there once the creek is crossed.
  The loop's own rise (a few drops to heavy) goes on from there.
- **Unchanged at the bridge:** the clearing's turn (the menace, the fog, the voice) and Checkpoint 5.
- `docs/STORY.md` Acts 5 and 6 updated to match.

## Tests
- The story test: the clearing isn't there while the cap is on the table; its stairs, ring and blood rain come in as
  the door opens; none of the blood rain reaches the player by the cabin, and the storm doesn't come back with it; over
  the bridge it's all there; the fall's eye path meets nothing on the way down.
- `--continue-test`: a Continue with the cap taken and before Act 7 expects the clearing's fifteen stairs.
