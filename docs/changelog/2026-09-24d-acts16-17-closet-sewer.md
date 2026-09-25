# 2026-09-24d — Acts 16-17: the closet, the sewer, and the hole

Commit: this pass's single commit on `main` (see `git log` for the hash).

Built from `Project_DS_The Real Act 16.docx` and `Project_DS_The Real Act 17.docx`, with the
owner's references:
- a round brick sewer pipe;
- a green-stone pillared passage;
- a vaulted brick cistern;
- concrete pipe mouths with graffiti;
- a black round hole with smoke rising from it.

## Act 16: the closet
- **The save** is Act 15's end (checkpoint 15), shut in the janitor's closet. The placeholder credits
  are gone, and a thought line reads "Pitch black in here. There was a switch by the door."
- **The door won't open in the dark**: "Can't find the handle in the dark." First, the light switch
  by the door.
- **The light comes on with the hallway's low siren**: red, then green, then an ordinary yellowish
  white, in about six seconds (5.5 s measured). Each change is eased, never a flash.
- **The door** they came in by opens on pitch black where the hallway was. Stepping into it covers
  the move to Act 17: black, then the sewer, and the black goes all at once.

## Act 17: the sewer
- **The save** is Act 16's end (checkpoint 16), on a landing just inside a steel door.
- **The pipe**: four steps down into ankle-deep grey water in a long round pipe of small glazed
  brown-orange brick, 100 m long. Planks, bottles and bags float in it, and two old sodium lamps
  light the way.
- **Wading**: the water drags at every step. The player moves at 55% speed while in it (1.46 m/s
  measured, against a 2.7 m/s walk), with wading footsteps.
- **The cistern**: the pipe opens into a great flooded room, 44 × 44 m.
  - Rows of brick pillars carry barrel vaults about 14 m up, with ribs over each row.
  - The round mouths of other pipes (concrete lips, black inside) run all round the walls. One has a
    light far down it.
  - Old graffiti near the pipes, heaps of broken brick and bags against the walls, and junk floating
    everywhere.
  - Sodium lamps on the walls and dim light up in the vaults let the size of the place be felt.
- **The platform**: a square of concrete in the middle, with a clean-cut square hole in it, black
  all the way down. A thin smoke rises out of it, and one light falls on it from far overhead.
- **The question**: going up to the hole prompts "Drop down into the hole?" Yes / No. A yes asks
  again: "Are you sure?" Yes / No. It takes two yeses.
  - **The first no**: "come and see" (the owner's recorded takes) is whispered from one pipe mouth,
    then another, round the room.
  - **The second no**: the room turns a hazy teal that is hard to see through, and the whispers
    multiply. They keep coming from anywhere, overlapping, and every further no brings them faster.
  - **Yes, and yes**: to the edge, a look down, a step, and the black. That is Act 18's save
    (checkpoint 17). Act 18 isn't built yet, so the credits roll from the black.

## Fixes (from the owner's playtest)
- Thin horizontal bars across the sewer pipe (meant as pipe joints) are gone.
- Clipping in the pipe: its collision was two flat side walls, so low down, where the round brick
  curves in, you could walk into it. It is now a ring of slabs following the curve all the way round.
  The test now shoves the player against both sides and checks that body and eye stay inside the
  brick.
- The hole showed grey: the room's water, its floor slab and a step under the platform ran beneath
  it. All are now built around it, and the pit's black walls sit just in front of the concrete.

## Act 15 changes (owner's playtest)
- The red-light-green-light hall is tighter: 3.8 m wide instead of 7, still 34 m tall. It is also a
  little shorter (950 m), keeping the section at about 8 minutes (7.9 measured) with the longer reds.
- The red lasts 5-10 s (was 4-6).
- Being taken now reads "The Shadowman Taketh".
- In the red, only moving counts. Pressing WASD at all, a jump, or still sliding is death; looking
  round with the mouse is safe.
- He now closes in during the red, so looking round is worth it. He appears about 6 m behind, then
  jumps closer in visible teleports (about 4 m, then 2.5 m) until he is right at your back, 1.3 m
  away.

## Additions
- `Act15Hallway.Closet.cs` (Act 16), `Sewer` with `Sewer.Hole.cs` (Act 17), and
  `sewerparts/SewerTextures` (glazed sewer brick and cast pipe concrete, world-projected).
- `PlayerController.WadeScale`: water drag on speed.
- `ChoicePrompt` can show a question line and its own key hint.
- New checkpoints: `Act16Finished` = 16 (the sewer door) and `Act17Finished` = 17 (by the hole, until
  Act 18 moves it), with Continue markers.
- `--story-from=16` and `--story-from=17`.

## Audio
- New synthesized loop for the sewer: water dripping into water all round, near and far, and a very
  low hollow tone. The whispers are the owner's "come and see" recordings.

## Tests
- `StoryTest` Act 16 covers:
  - the closet save;
  - the door refusing in the dark;
  - the switch, and the red/green/white cycle (timed);
  - the door opening on black;
  - stepping through into the sewer, and the Act 17 save.
- `StoryTest` Act 17 covers:
  - the sewer save and the steps into the water;
  - wading: the drag, and measured speed;
  - the clipping check against both sides of the pipe;
  - the pipe and the cistern, and up onto the platform;
  - the hole's question, the first no (whispers), and a yes then a no (the teal haze and more
    whispers);
  - yes and yes, down the hole, and the Act 18 save.
- `ContinueRoundTripTest` has new scenarios for the sewer door (checkpoint 16) and the platform
  (checkpoint 17).
- Results: `--story-from=15 --story-to=17`: 39/39 (including the new check that he closes in while
  you watch: 6.0 m, then 1.3 m). Full `--autotest`: 406/406. `--continue-test`: 537/540. The same three
  failures (the Act 6 loop's fire state, the Act 10 mood, the clearing stairs) predate this work.
