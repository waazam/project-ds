# 2026-10-13 — The hand on the lantern; your body looking down; a brighter flame; one wake; the code from memory

The owner's list: "Fix the hand holding the lantern", "the cutoff body when looking down", "the regular lantern should
be brighter, but the black light lantern should remain the same", "show text that nudges the player to use the
blacklight to find the code", "when you wake up after falling off the first set of stairs it wakes you up then blacks
out again. It should just wake up from the black out", and, during the work, "remove the UI for the bunker code, the
player should have to remember themselves".

## The hand on the lantern (`tools/Blender/fp_arms.py`, `assets/models/player/fp_arm_bail_L.glb`, `HeldLantern.cs`)
- **What was wrong:**
  - The hand was a flat paddle laid on the wire. The palm was level and the back of the hand faced the eye, and the
    fingers hooked straight down under it. From the eye it read as a black pancake sitting on the bail.
  - The sleeve was a wide open tube, its inside showing at the cuff.
  - The lantern's foot was cut off by the bottom of the view.
- **Now:**
  - A closed fist round the wire. The knuckles face forward, the back of the hand is up and rolled a little out to the
    left, and the fingers wrap right round the wire with their tips in the palm. The thumb closes over the first two
    fingers.
  - The back of the hand is fuller and rounder, with no tendon grooves (a glove hides them).
  - The forearm rises to the fist from the elbow below it, instead of lying across the view like a pole.
  - The sleeve fits the wrist: oval and close at the cuff, the glove's cuff showing under it.
  - The glove is a shade lighter (still dark leather), so its shape reads in the dark. The right fist shares it.
  - **A normal bail** (the owner: "the lantern should be higher up on the screen, the handle is too long"). It stands
    0.09 m over the cap (was 0.22), so the fist sits just above the lantern's top. The hand rests a little lower
    (y -0.07, was -0.02), so the whole lantern sits in the lower left of the view, clear of the centre, its foot no
    longer off the bottom edge. Gathering the beam lifts it a touch less (y -0.02, was 0.03). The swing is a little
    quicker for the shorter bail.
- Blender 4.0.2 is now in `~/tools/blender-4.0.2-windows-x64` (it wasn't on this machine).

## Your body, looking down (`PlayerBody.cs`)
- **What was wrong:**
  - Only the jacket below the belly was drawn, under a domed lid. Looking down you saw that lid: a dark stump with legs
    under it.
  - The body was pushed so far back by 60° that only the toes showed.
  - Crouched, the whole torso went behind the eye, leaving two thighs alone in the view.
- **Now:**
  - The jacket's whole front is seen: its chest, zip, pockets and belly, down to the belt, the jeans and the boots.
  - The tops of the sleeves are seen too, so the shoulders close round (the sleeves' cut edges had shown as ragged
    flaps). They're a trimmed copy carried rigidly by the chest, so the arm's swing and the raised lantern arm never
    bring their edge into view. The whole arm is still in the shadow.
  - The body steps back from the eye later (from 35° to 83° down, was fully back by 60°), and stays back while
    walking.
  - Crouched it steps back less (0.12, was 0.28), so the knees, the belt and the belly are seen together.

## A brighter flame (`Lantern.cs`, `HeldLantern.cs`)
- **The glow:** 21 m and 4.3 (was 16 m and 2.9).
- **The gathered beam:** 30 m and 3.6 (was 24 m and 2.5).
- **In the hand:** the glass glows 1.2 (was 0.9), and the flame lights the hand a little more.
- **The blacklight is exactly as it was:** its glow, its beam, its glass and its hand-light all keep their old values.
  The setting's Lantern slider still scales both.
- In a dark test scene, the flame's frame is about 25% brighter on average (0.111 to 0.137), and the blacklight's is
  unchanged (0.053 and 0.054).

## One wake (`GameFlow.WakeUp`)
- **Before:** after the black in the Hollow, the screen opened to half, shut again, opened, half-shut, then cleared. It
  read as waking and blacking out again.
- **Now:** the black holds while the level is ready, then lifts once, slowly at first, as the head comes up over 8 s.
- No other system fades the screen during the load. Frames captured through the wake show the black only ever going
  down.

## The code from memory (`CodeLockOverlay.cs`, `SurveyLot.cs`, `TreeNote.cs`, `UI/KeyHint.cs`)
- **No code on the HUD.** The "CODE _ _ _ _" tracker is gone.
- **The dial isn't filled in.** It starts at 0 0 0 0 every time, and the four in order open the door. It can be tried
  from Act 7 on, as before.
- Reading a digit under the blacklight still shows it big for a moment. That's how it's learned.
- **Two nudges, each once in a game,** in the hint line under the picture, with the blacklight's key as the player has
  bound it:
  - "His numbers are on the trees.   B: the blacklight shows them." This comes once the camp note's read and the
    lantern's own lesson at the camp is over, while no digit's been found.
  - "Something on a tree back there.   B: the blacklight." This comes after walking on past one of the number trees
    (within 9 m, then 16 m on) without the blacklight on near it.
- The lantern's other hints now name the keys as bound too, not always F and B.

## Tests
- The story test's camp and bunker checks are updated: the tracker checks are gone, and the dial now starts at nought
  and the remembered code is dialled through the player's own keys.
- **Acts 3-8:** 226 of 226.
- **The body preview:** both checks pass.
- **The nudges,** checked in the Hollow: the first came after the camp note and the lantern's lesson, and the second
  after walking past the first number tree with the flame.
- **The wake,** with frames captured through it: the black only ever goes down.

## The lantern's shadow on the stair walls (`Lantern.cs`)
The owner: "lantern causes weird shadow on wall when walking up stairs in tunnel".
- **The cause:** the cage. Four wire struts, drawn only into the lantern's shadows, stood a hand's width from the flame.
  With a wall close by (Act 21's long stair, any narrow flight), they threw thin dark lines that swept across it with
  every swing of the lantern. They also streaked the ground at your feet.
  - They're gone from the lantern's light.
  - The world's shadows from it (rails, frames, treads) stay.
- **Kept out of walls:** if a wall is between the eye and the held flame (the lantern swung against the wall), the light
  sits just this side of the wall. It no longer lights the wall from inside, or from behind.
- **Checked on Act 21's long stair:** the bottom, the middle and the top, walking and facing each wall, before and
  after.
