# 2026-10-13d — The gloved hand: a real fist on a wooden grip, a strong wrist, and one hand for every pose

The owner's list, in order: the lantern's hand "still looks a bit blocky"; then "make the players hand look more
realistic and less robot"; then "make the hand less bendy at the wrist and more strong". And a design change: the
lantern goes in the left hand and the other things in the right, so the hand is now one reusable piece, left or right,
in a few poses.

## The lantern's hand (`tools/Blender/fp_arms.py`, `HeldLantern.cs`, `ItemMeshes.cs`)
- **What was wrong:**
  - The back of the hand was a long flat slab, its front end cut square.
  - The fingers were capsules on a chain with a ball at every joint.
  - The thumb was a lump.
  - The wrist bent like rubber where the sleeve met it.
  - The leather had one smooth plastic sheen.
- **The grip.** The bail now has a turned wooden grip across its top, 1.7 cm thick and 8.5 cm long. Its top runs
  straight under the grip and then rounds down to the cap. Only the lantern in your hand has it; one lying about is as
  before. The fist is built round that grip:
  - Each finger's three bones are bent to fit it. The middle and last bones lie against the wood and the tip curls in
    toward the palm.
  - The grip runs a little across the fingers, deeper toward the little finger, which curls tightest.
  - No two fingers are bent quite alike.
- **The fingers** are each one smooth skin from knuckle to tip, not a bead of capsules:
  - they taper, swell softly over the back of each joint and are fuller in the pads;
  - they're a little uneven along their length;
  - each tip is rounded over.
- **The thumb** comes from a full base low on the palm, at its true lengths. It lies out beside the first finger and
  across the front of it, with its tip on the middle finger.
- **The back of the hand** narrows to the wrist and widens to the knuckles:
  - it's domed across and bowed along;
  - it rounds over into a row of four knuckles;
  - the tendons are drawn tight under the leather, from each knuckle back toward the wrist;
  - the heel of the hand and the pad under the fingers' roots are filled out, so the fingers grow out of the palm.
- **A strong wrist.** The forearm leaves the hand almost in line, about 10° off (was 25° and soft). It's as thick as
  the heel of the hand, and the sleeve sits round it with no gap. The lantern's swing moves the wrist half as much as
  before (0.1 and 0.08 of the swing, were 0.22 and 0.18).
- **The glove:**
  - Worn dark-brown leather (the body's gloves), rubbed to a lighter tan over the knuckles and fingertips and darker
    down in its folds.
  - One soft crease inside each bent joint. Rings of wrinkles all round each joint had read as a robot's segments.
  - Seams down the fingers' sides and round the sewn-in thumb, three stitched lines down the back of the hand, the
    cuff's seam, and the wrist's shirring.
  - In the engine, a fine pebbled grain on the leather and a sheen that varies with it. The glove has no UVs, so the
    grain is mapped from its three sides.
- **Kept from the first pass:** the lantern's place in view, its size, and the 9 cm bail (`Rest`, `Gathered`,
  `BailRise`, `ModelScale` are untouched).
- About 40,000 triangles a hand, drawn without shadows.

## Its own life (`FirstPersonHand.cs`, `HeldItem.cs`)
- Walking, the hand shifts a hair on the grip with the stride.
- At a run, or after a fright (the breathing's tremble), it closes a little tighter and rolls over the grip. It grips
  quickly and lets go slowly. All of it is small: a firm hand, no flop.

## One hand, left or right, in four poses (`FirstPersonHand.cs`, `assets/models/player/fp_hand_L_<pose>.glb`)
For the left-hand lantern and right-hand items to come. Each pose is modelled once, as the left hand. The right hand is
the same mesh mirrored across X, so the two always match.

```csharp
var hand = FirstPersonHand.Create(HandSide.Right, HandPose.Stick);   // null if the model's missing
holder.AddChild(hand);       // the hand's socket (the grip's centre) at the holder's origin
hand.AddChild(flare);        // put the held thing's grip at the socket: this node is never mirrored
hand.Stride = sway;          // -1..1, the walk's sway times how much you're walking (HeldItem does this)
hand.Tension = 0.8f;         // 0..1, a sprint or a fright (HeldItem does this)
```

| Pose | Grip through the socket | Made for |
|---|---|---|
| `Handle` | along X, 1.7 cm thick | the lantern's bail: a fist, the thumb over the fingers |
| `Stick` | along Y, 3.2 cm thick | a flare, a tool's handle: a fist, the thumb over the fingers |
| `Camera` | along Y, 4.2 cm thick | one end of a camera or any box, from the side: fingers round its front, thumb up its back (one of each side for two hands) |
| `Relaxed` | none (the socket is the middle of the palm) | open and at ease, each finger curled a little more than the last |

- `GripAxis` and `GripRadius` give each pose's grip.
- `FirstPersonHand.Dress` gives any modelled arm the glove's and sleeve's materials.
- The models are drawn at a gloved size, 1.1 (`GloveScale`).
- `HeldItem.BuildArm(parent, side, pose, at)` makes one named "Arm".
- `BuildFist` (the flare, a carried prop, a tool in use) now uses the right hand's `Stick` pose. Its grip is sized for
  the flare (3.2 cm). The items' own placement in it is unchanged, for the item work to come.
- Not changed: the lantern stays in the hand it's in now. The body's own hands, seen looking down, are drawn only in its
  shadow, so they weren't touched.
- Rebuilt by `blender -b --python tools/Blender/fp_arms.py` (`-- handle stick` builds only those). It writes previews
  to `build/blender/`.
- The old `fp_arm_bail_L` and `fp_arm_fist_R` models are gone, and so are four unused bake images left beside them.

## The harness (`BodyPreview.cs`)
- `-- --hand=<tag>` shoots the lantern's hand to `test-output/hand/<tag>/`, from the eye and through a narrow lens at
  the eye:
  - light and dark, flame and blacklight, the beam gathered;
  - walking and running, looking up and down.
- `-- --hand=poses` shoots every pose on both sides, lit and in the dark.
- Before and after: `test-output/hand/before/` and `test-output/hand/after/`.

## Tests
- Build: 0 errors, 0 warnings.
- The lantern-hand harness and the pose gallery both pass (every model loads).
- Playtest beats 0, 3, 4, 21 and 24a (the systems; the lantern, its blacklight and gathered beam; the long stair; the
  flare and the crowbar in the right hand): 5 of 5 beats, 119 of 119 checks, no engine errors.
- The body preview: both checks pass.
