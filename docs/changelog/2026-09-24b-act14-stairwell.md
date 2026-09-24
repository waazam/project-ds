# 2026-09-24b — Act 14: the stairwell

Commit: this pass's single commit on `main` (see `git log` for the hash).

Built from `project_ds_act14.docx` and the owner's reference photos:
- square fire stairs winding round an open well, seen from above;
- a concrete trench of steps going down into the ground;
- five concrete textures, ranging from clean, speckled concrete to oily, cracked grime.

The brief asks for the longest descent in the game, one that feels maddening and is too dark to count
the stairs.

## Story Beats
- **Room 3** (behind Act 13's iron door) is the building's opposite: clean, cold, riveted steel plate,
  steel floor, pipework and caged lamps under a 7 m ceiling. The walls are hung in the old salon
  style with 42 portraits in ornate frames (gilt with bosses, black lacquer, walnut, tarnished
  silver). Every portrait has its face burnt out of the canvas. Stepping through the door ends Act 13
  and saves Act 14's start (checkpoint 13).
- **The hole**: a rectangular trench cut into the back of the floor, edged with hazard paint, with a
  sagging chain between two bollards. Block-lined concrete steps lead down into the dark.
- **The descent**: steel stairs wind down a square shaft round an open well, four flights to a turn,
  64 turns, 256 flights, about 295 m down. At a walk it takes about 5.3 minutes (measured by the
  test).
  - It is nearly black. Underground, the world's sky, sun and ambient light go and the fog turns
    black, swallowing the well a couple of turns down.
  - A caged bulb hangs every third turn, and fewer than half of them work, all dim. The lantern is
    what you see by.
  - The walls go from clean poured concrete, to drip-stained and cracked, to dark, oily and grimy,
    and in the last stretch they lose their flatness into a slight uneven wave. The stairs stay the
    same steel throughout.
- **Running is punished**: sprint on these stairs for under a second and you are moved back up the
  shaft, two to four turns from the top. The move is seamless:
  - It is a whole number of turns (a multiple of the bulbs' spacing), so everything around you is
    identical.
  - Your place on the turn, your speed and your view are all kept.
  - The walls' grime is held where it was, then allowed to drift back more slowly than it builds on
    the way down.

  Only the numbers painted on the landings give it away.
- **What happens on the way down** (each once, by depth). Nothing chases and nothing attacks; the
  stairwell wears the player down:
  - The stencilled floor numbers count B1, B2... then stick on B9, count backwards, turn into tally
    marks, and later read TURN BACK, B-1, DOWN, YOU WERE TOLD, NEVER.
  - Footsteps sound across the well two turns below, in time with the player's, and carry on for a
    step or two after they stop. Later the same happens one turn above, following.
  - A steel door marked 3 on a landing: someone knocks on it from the other side.
  - A chair tumbles down the middle of the well from somewhere above and never lands.
  - A burnt-face portrait hangs on a landing. Further down, a chair sits set to face the wall.
  - A tall figure stands at the rail two turns below, looking up. It is gone when you get there.
  - The structure groans, a far clang comes up from below, whispers come from above, and a thought
    reads "How far down does this go?".
  - Near the bottom the shaft goes silent for a long stretch, and the last bulbs are out.
- **The bottom**: the next flight has fallen away. The landing beyond it still hangs off the wall, a
  jump away. Walking up to the edge asks **Jump across?** or **Jump down?** (A/D to choose, E to
  jump, S to step back and think about it).
  - **Across**: the player makes the jump and lands on the far landing. It holds for a moment, then
    the bolts shear and it drops with them. They look up at the stairs going away above, and then it
    goes black. They come to lying on their side among the wreckage, roll onto their back, sit up,
    breathe, and get up with a stagger. It is slow on purpose.
  - **Down**: they step off into the drop and land on their feet with a hard bend of the knees,
    control back at once, ready to run.
- Either way they are in the chamber under the shaft, where **Act 15 begins**:
  - black wet concrete, the wreckage of stairs that fell before, and one low passage with a cold
    glow somewhere down it;
  - the save is Act 14's end (checkpoint 14), and how they came down is remembered for Act 15
    (`act14_jumped_across` / `act14_jumped_down`);
  - Act 15 isn't built yet, so after a few seconds the credits roll there, as they did at the end of
    Act 13 before.

## Act 13 change
- Act 13 no longer ends up the wooden staircase to the white light. It now ends as you step through
  the iron door into Room 3, which is Act 14's start. The staircase, the flesh and the eyes of the
  old finale were removed from Room 3.

## Additions
- `Stairwell` (with `Stairwell.Events.cs` and `Stairwell.Bottom.cs`). The shaft, the stairs, the
  walls and their collision are built in chunks that only draw within about 34 m. The flights have
  ramps underfoot and thick guards on the well side, so nobody hops or is pushed through the rail.
- `stairwellparts/StairwellTextures`: procedural clean, stained and grimy concrete, cinder block
  with salt blooms, diamond-plate treads, painted steel, riveted plate, and the burnt portraits.
  `PortraitKit` builds the ornate frames.
- `stairwell_concrete.gdshader`: the depth blend, the oily sheen, the late-depth wave (kept out of
  the corners so walls never part), and the grime hold for the loop.
- `ChoicePrompt`: a two-way question overlay (modal, keyboard).
- `ForestAtmosphere.Underground`: no sky, sun or ambient light, and a black fog below ground.
- `PlayerCameraRig.ShiftBy` moves the eased eye with a teleport, so the loop never glides.
- A `metal` footstep surface (steel stairs and Room 3's floor).
- New checkpoint: `Act14Finished` = 14, with a Continue marker in the chamber. `Act13Finished` now
  respawns just inside Room 3.
- `--story-from=14` starts in Room 3.

## Audio
- New synthesized sounds: six boot-on-steel-tread steps, the shaft's low tonal drone (loop), three
  structural groans, something falling past down the well, two far clangs from below, and the flight
  giving way.

## Comfort
- Nothing flashes or strobes; the dead bulbs are simply dead. Camera motion in the jump and the fall
  is eased and brief, and the landing's lurch is a small, short shake.

## Tests
- `StoryTest` Act 14 covers:
  - the gallery;
  - going down the trench into the dark;
  - walking eight turns, then sprinting and being taken back up (whole turns, same place on the
    turn, grime held);
  - walking all the way down (timed);
  - the question at the edge, stepping back from it, and asking again;
  - jumping across: the fall, lying on the floor, and getting up;
  - the Act 14 save.

  It then Continues from Act 14's end and takes the other way: jumping down onto their feet, with
  control back straight away.
- `ContinueRoundTripTest` has new scenarios for Act 14's start (Room 3) and end (the chamber).
- Results: `--story-from=13 --story-to=14`: 76/76. Full `--autotest`: 370/370. `--continue-test`: 456/459. The same three
  failures (the Act 6 loop's fire state, the Act 10 mood, the clearing stairs) predate this work.
