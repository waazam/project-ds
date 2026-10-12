# 2026-10-12 — Weight and flesh: the animation and creature overhaul

The owner asked for the animation overhaul and the creature models to be done "as efficiently as possible": weight
and inertia, feet planted on the ground, secondary motion, cleaner silhouettes. The approach: shared tools first, laid
over what each creature already does, then the creatures that still looked built from blocks rebuilt.

## The shared tools (`AnimKit`, `Loft`)
- **Springs.** Critically damped springs for one value or a vector: they ease into a target, carry their momentum
  through a change, and settle without snapping or overshooting.
- **Two-bone IK.** A limb solved so its end lands on a point. The knee or elbow keeps the side it already bends
  toward, so it never pops backward, and the foot or hand keeps its own orientation.
- **Body dynamics.** Weight laid over a skeleton driven by clips, every frame after the clip:
  - the spine leans into the body's acceleration (forward as it sets off, back as it stops) and rolls into its turns,
    with the head taking back half so its gaze holds;
  - the upper arms hang back as it sets off and swing on past a stop, like a pendulum;
  - **the feet are planted on the ground under them.** Each foot finds its own ground; only the difference from the
    ground under the body counts, so a foot the clip has lifted keeps its lift. The skeleton is lowered to the lower
    foot and each leg is bent to land on its own ground: slopes, steps, the uneven snow.
  - Any bone a clip doesn't key is put back before the next frame's weight goes on, so nothing piles up.
- **Skinned chains.** A mesh built straight in its rest pose (a tentacle, a tail) is skinned to a chain of bones, each
  vertex eased between the two bones it lies between. It bends as one smooth thing, with no seams and no rigid pieces.
- **Loft.** One smooth tube along a curve: rings round a spline through the points, each its own size (round or an
  ellipse), with no twist and smooth normals all the way. For bodies and limbs that must read as one piece of flesh.

## The creatures
- **The wendigo and the stalker** carry their weight now. They lean into their starts, stops and turns, their long
  arms swing behind them, and their feet land on the ground under them. Off during a leap and while a held pose plays.
- **The thing in the lake**: each limb is one smooth skinned tube with its suckers, not a rigid cylinder to every
  joint with a knuckle over the seam. **It follows through**: each joint eases toward its bend, looser toward the tip,
  so the tip trails the root through a sway and whips on past it, as flesh does.
- **The shadow man**, rebuilt from smooth lengths of flesh. Before, he was six-sided cylinders creased at every joint.
  Now: starved thighs and the knee's knob, shins wasting to the ankle, a waist you could close a hand round under a
  ribcage that flares (the ribs just showing), the back bowed forward, sloping shoulders, arms that bend twice with each
  length its own taper, long flat palms, and fingers with knuckles. His silhouette reads as a figure, not a stick.
- **The doe in Act 1**, rebuilt. One smooth barrel of a body, deep at the chest and the haunch with the waist between,
  the dark back and pale belly laid over it. One smooth neck to the skull, the muzzle drawn out. Legs tapered, the hind
  thighs heavy. **Its legs fold now**: a second joint at the knee and hock, so as it bounds the fore hooves fold back
  under and the hind ones forward, then reach to land. It used to swing four stiff sticks from the hips.
- **The eyes** on the lake's limbs and the pit's leviathan were bright ping-pong balls. Now they're a dull, sickly,
  bloodshot yellow, sunk in the flesh behind a fuller lid that hides their roundness.
- **The skull on the platter** already turns to follow your head (2026-10-11), slowly and heavily, tipping back to look
  up at you; it's three-quarters of its full size.

## The lodge's walls
The owner: "throughout the ski lodge there are walls with the greyish wallpapers ... do a more simplistic design." The
patterned papers are gone: the corridor's and the dining hall's medallions, 201's stripes, 202's roses and 203's grey-blue
trellis. Every wall above the wood panelling is plain painted plaster now, one quiet colour to each part of the lodge,
with only the plaster's faint texture showing:
- the corridor and the dining hall: warm cream;
- 201: sage;
- 202: dusty rose;
- 203: ochre.

## Fixes
- **A pickup made while a trigger's signal was running** (Act 20's round room) changed its physics flags then, and
  the engine logged an error. They're set deferred now.
- **The Act 24 test's "creeping behind it" step** stood the wendigo 3.5 m off and let it roam. Now and then it turned
  and caught them, and the next check couldn't run. It now stands 5.5 m off, and the creep is shorter.

## Tests
- **Acts 1–8:** 289 of 289.
- **Acts 11–13:** 134 of 134.
- **Acts 14–20:** 195 of 195.
- **Continue test:** 1086 of 1086.
- **Acts 23–24:** 162 of 162 (run again after the walls and the fixes).
