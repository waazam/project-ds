# 2026-10-13j — Two hands: the lantern in the left, what you choose in the right; the wheel really switches; the things in them built to be held

The owner: "make items and inventory scrolling actually work and switch between items, allow for lantern to be in left
hand and other items to be in right. create objects that look good and fit in the hands correctly and face the correct
way, look up pictures if needed".

## What was wrong
- **The wheel only changed a word on the HUD.** `EquippedItemHud` kept its own "held" item and stepped it with the wheel,
  but nothing else read it. Everything carried still "worked at any time": right mouse always raised the camera, the
  axe and the hammer worked from the pocket, and nothing was ever in the hand. The one exception was the flamethrower,
  which read the HUD.
- **Nothing showed in the right hand.** It was seen only for a tool's moment of use (`HeldUse`), a burning flare or a
  tin. The camera was never in view, only its viewfinder overlay.
- **The lantern's beam could never be gathered.** Right mouse raised the camera, which the player carries from Act 1,
  and the camera going up lowered the lantern. So the "gather its beam" half of right mouse never happened.
- **The pad's bumpers.** LB was "next item" (the wheel had no "back" on a pad) and RB was the blacklight.

## Two hands (`PlayerHands.cs`, `PlayerInventory.cs`)
- **The left hand is the lantern's, always.** F and B work as before.
- **The right hand holds one thing** (`PlayerInventory.InHand`), or nothing.
  - **Holdable:** the camera, the axe, the hammer, the cap, the walkie-talkie, the knife, the lighter, the crowbar and
    the flamethrower.
  - **Pocketed:** keys, keycards, the compass and the station's three things (the eye, the hand, the step). The hand
    shows them only while they're being used (`HeldUse`, as before). Puzzle tools still work from the pocket.
- **Choosing:**
  - **The mouse wheel or RB / LB** go round what you carry, an empty hand among them.
  - **1-9** pick by place on the wheel: 1 is the camera, then the rest in the order the story hands them over. Pressing
    the number of what's already in hand puts it away. A number key you've bound to an action in Settings > KEYS does
    that action instead.
- **A pickup comes to the hand.** Taking the lantern (Act 3) puts the camera away, so right mouse gathers its beam. The
  lantern's how-to line now ends "Wheel: the camera."
- **A thing used up** (the axe on the boards, for example) leaves the hand empty.
- **Right mouse is the right hand's:**
  - with the camera in it, it raises the camera to the eye: the camera comes up into the middle and gives way to the
    viewfinder;
  - with anything else, or nothing, it gathers the lantern's beam.
- **The switch:** the old thing is lowered out of view, rolling away as the wrist turns, then the new one is raised and
  rolls in. Each way takes a quarter of a second. You hear a rustle of the sleeve as it goes down and the grip as the
  new one comes up, never the same sample twice running.
- **No switching** (the wheel does nothing):
  - while the story has the controls (a cutscene, a note, the pause menu);
  - with the camera at the eye (the wheel zooms it) or the album open (the wheel turns its pages);
  - with a burning flare or a tin in the hand, or a tool in use (each takes the right hand for its moment, and the
    chosen thing is lowered meanwhile);
  - in the rowboat (the right hand's thing is lowered while rowing);
  - while the flamethrower is firing;
  - while a beat holds the hands (`PlayerHands.Hold(key)` / `Release(key)`, for story beats).
- **Beats still put things in the hand.** Act 24's flamethrower is put in the hand when it's taken, and again on a
  Continue (`EquippedItemHud.Prefer`, now a wrapper over `PlayerInventory.Select`). Act 25's endings taking the camera
  empty the hand.
- **Saved.** What's in hand is saved with the rest (`;hand=Axe`). An older save has the camera in hand before the
  lantern, and nothing after it.

## Both hands
- **The flamethrower takes both hands:**
  - the right hand on its rear pistol grip, the left on the front grip;
  - the lantern hangs at the belt meanwhile, its light still with you (in Act 24 it's out anyway);
  - the wand is raised and lowered like the rest (a third of a second each way), and only fires once it's up;
  - choosing anything else lowers it and brings the lantern back up.
- **Rowing** lowers the right hand's thing. The lantern is left as it was: the oars' hands belong to the lake crossing's
  own rework.

## The HUD (`EquippedItemHud.cs`, `NeonKit.cs`)
- The neon item tag at the bottom right (its look unchanged) now names what the right hand really holds, and the next
  one along.
- It strikes up on a switch or a pickup and fades after about three seconds (with the contextual HUD, the default).
- It shows only once there's something to hold.
- **New icons:** the cap (a ball on a neck over its plinth) and the empty hand (an open palm).

## The things, built to be held (`HeldModels.cs`)
- **Built in code** at their real sizes, in grip space: the origin in the fist, the handle up through it, forward away
  from the eye.
- **Worn materials,** procedural:
  - oiled hickory, darker where it's handled;
  - forged steel under forge scale, with a honed bright edge;
  - rusted iron;
  - black pebbled leatherette;
  - dulled satin chrome;
  - tarnished brass;
  - stacked leather;
  - the walkie's olive-black plastic;
  - a slotted speaker grille;
  - yellowed paper.
- **The camera:** a 1970s all-mechanical 35 mm SLR (the size of a K1000 or an AE-1).
  - Black leatherette between chrome ends, and a chrome top plate with the prism's hump.
  - The eyepiece on its back.
  - A 50 mm lens: a bayonet ring, a knurled focus ring and coated glass.
  - The speed dial, the shutter button and the advance lever on the right; the rewind crank on the left.
  - Strap lugs with stubs of the tan strap.
  - Held by its right end, the lens away from the eye.
- **The axe:** a 26" camp axe.
  - An oval hickory handle with a knob at the butt, and the wedge's kerf across its top.
  - A forged head: poll, cheeks and a flaring bit, the edge honed bright.
  - Held low on the handle, the head up, the bit forward.
- **The hammer:** a 16 oz curved-claw hammer. The chamfered round face is forward, and the claw's two prongs curl back.
- **The knife:** a fixed-blade hunting knife: stacked leather washers, a brass guard, and a drop-point blade ground to a
  bright edge, its edge down.
- **The crowbar:** a 24" wrecking bar of hexagonal, rusted stock. The gooseneck's split claw is up and forward, and the
  chisel end is below the fist.
- **The lighter:** a brass flip-top lighter, upright in the fingers, the lid above them.
- **The walkie-talkie:** an '80s handheld radio.
  - The speaker grille in a raised rim, and a channel sticker.
  - A ribbed rubber antenna, two knurled knobs and the push-to-talk bar.
  - The battery pack and the belt clip.
  - Its speaker is turned to the eye. Its LED is dark until the radio comes alive outside the bunker (checkpoint 8),
    then glows red.
- **The cap:** the newel post's stone cap, held by its neck, the ball up.
- **Lighting:** the held things are lit the way the lantern's hand is.
  - The lantern's warm fill reaches across to the right hand when the lantern is up; with the blacklight on, it's
    violet.
  - A faint cool rim lights them always.
  - Their own soft fill lights them when the lantern isn't lighting them.
  - The world's lamps are kept off them, as before.
- **Motion:** they bob with the stride and lag behind the view's turns, the same as the lantern (`HeldItem`).

## The hand (`HandRig.cs`): waiting on the new one
- The held things are built against a small interface, `IFirstPersonHand`, which has a `Side`, a `Pose`, a `Root` and a
  `Socket`. A hand comes from `FirstPersonHands.Factory`.
- **For now it's the Blender fist** (`LegacyHand`: `fp_arm_fist_R`, mirrored for the left), with its socket turned to
  its knuckles.
- **Each thing's hold is data** (`HeldModels.SpecOf`: the pose, where the hand is and how it's turned, and where the
  thing sits in it). That's so the realistic hand being built alongside (grip poses for the lantern's bail, the camera,
  a flare or tool, and relaxed) can be plugged in and the holds re-tuned.

## Controls (`GameSettings.RegisterInputActions`, `PlayerInput.cs`, README)
- **Pad:** RB / LB go next / back round the wheel, and also zoom the camera and turn the album's pages. The blacklight
  moves to D-pad down.
- **Keyboard:** 1-9 added.
- **Right mouse** is the right hand's (see above).

## Tests
- **The inventory test, `hands_preview.tscn`:** 29 checks of the switching, then a look-book of every held thing from
  the eye (ahead, up, down, in the dark by the lantern, by the blacklight, walking). Shots go to `test-output/hands/`.
  `--hands-studio` shoots each model alone, turned. All 29 pass.
- **The playtest's Act 3 beat:** it now checks that the lantern puts the camera away and the wheel brings it back to
  the hand, and it puts the camera away again before testing the beam.
- **Photographs in the tests:** the test bot's and the story test's picture-taking now put the camera in the hand
  first (`TestBot.CameraInHand`).
