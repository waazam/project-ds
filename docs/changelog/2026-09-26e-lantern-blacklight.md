# 2026-09-26e — The lantern's blacklight, the code in invisible ink, the flame dying in the stairwell, and the blacklight's secrets

Commit: this pass's single commit on `main` (see `git log` for the hash).

The owner's new mechanic, with references (fluorescent footprints and handprints, a word in invisible
ink under a UV torch, a number smeared on a board). The bunker's code is no longer on obvious paper
notes: the numbers are on trees in ink only the lantern's blacklight shows, among marks that aren't
his, so the player has to look closely.

## The lantern
- **Further**: the flame now reaches 14 m round the player (was 9.5), eased a little in energy so it
  isn't brighter up close. The gathered beam (right mouse) throws 24 m (was 13).
- **The blacklight** (B, or the right shoulder on a pad): the lantern's second light, toggled with its
  warm flame.
  - It is a steady violet cone (electric, no flicker) with hardly any light round the player, so it
    helps little to see by.
  - It shows the ink: anything written in it glows where the beam falls on it, a cold cyan-white or a
    sick green, with the speckled, fibrous grain of real fluorescent residue. It is brightest at the
    heart of the beam and fades at its edge and with range (9 m).
  - Switching has its own sound: the shutter clack, and a tube's electric buzz swelling in.
  - The how-to line when the lantern is taken now mentions B.
- The beam's place and direction are published to the shaders as globals (`uv_light_pos`, `uv_light_dir`,
  `uv_light_on`, `uv_light_range`, declared in `project.godot`), so any surface can react to it.

## The flame dies in the stairwell (the owner)
- A quarter of the way down Act 14's stairwell, the lantern's flame gutters out: sputtering, the
  dips deeper and longer, fading to nothing. This is saved (`lantern_flame_dead`), and a Continue
  from Act 14's end on counts as dead too.
- From then until after Act 20, F won't light it: the striker clicks and a spark catches, nothing
  more. The first time, a line says so: *"The flame won't take. B: the blacklight still works."*
- The blacklight still works. It now gives a little more violet round the player, since in the
  stairwell's lower three quarters it is all the light there is.
- After Act 20 (the room at the top) the flame works again.

## The stairwell is shorter (the owner: about three minutes)
- 40 turns, down from 64 (about 3.3 minutes at a walk; it was about five).
- Its painted signs, the painted-shut door, the burnt portrait, the chair facing the wall, the echoes,
  the figure, the whispers and the question are all still there, each at the same fraction of the way
  down as before. The signs count this shaft's own turns until they start to go wrong.

## The blacklight's secrets (the owner: easter eggs and clues)
- 44 things written in the ink round the game, only the blacklight shows them (`BlacklightSecrets`,
  and the stairwell writes its own):
  - **the camp**: "HE COUNTS THE STEPS" by the fire, a hand on the stump.
  - **the cabin**: "IT KNOCKS THREE TIMES" on the back wall (it does, later), days tallied on the
    side wall, small hands low on the inside of the door.
  - **the bunker**: "IT'S ON THE CEILING" in the hall; a hand dragged along the wall; "THE STAIRS
    WANT YOU BACK" in the CRT room; "YOU'VE BEEN HERE BEFORE" in the rooms that repeat.
  - **the lake**: "ROW. DON'T STOP." on the end of the dock.
  - **the station**:
    - "IT WAS ALWAYS STAIRS" behind the desk;
    - **the cryptex's word, S T A I R S, faint on Room 2's ceiling** (a clue);
    - hands all over Room 1's ceiling;
    - "IT MOVES WHEN YOU LOOK AWAY" by the basement's drain.
  - **the stairwell** (where the violet light is all there is):
    - "COUNT THEM";
    - "THE LIGHT WON'T COME BACK", just past where the flame dies;
    - "IT'S BEHIND YOU";
    - "NOBODY COUNTS RIGHT DOWN HERE";
    - "IT WANTS YOU TO JUMP", and "JUMP" at the bottom;
    - hands down the rail all the way, as if someone felt their way down in the dark.
  - **the long hallway**: "DON'T MOVE IN THE RED" at its start, and near the end **"THE SWITCH IS BY
    THE DOOR"** (a clue for the dark closet), with a hand on the wall where it is.
  - **the sewer**: "DOWN" by the hole. **The pit**: "TURN THE LIT ONES" on the catwalk.
    **The library**: a "HERE" by the book that sticks out. **The round room**: "UP. ALWAYS UP." on
    the dais.
- Words are written in a scrawled hand and rendered once into an ink mask (`UvInk.Text`/`Write`), so
  any wall, floor or ceiling can carry them.

## The code
- R.H.'s four numbers (4 7 2 9, as before: two between the camp and the cabin, two between the lookout
  and the bunker) are **brushed big on bare trunks in the blacklight ink**.
  - They stand 3.4 m off the path, and each number is a little way round its trunk from the path side,
    so it has to be looked for.
  - Under the flame the tree is just a dead tree: no paper, no glint.
- **Decoys**:
  - Round every real one stand three more dead trees with marks that aren't his: handprints, smears,
    tally marks, an eye, scratches, and other digits scrawled and struck through.
  - Most trees have a mark or a print lower down too.
  - The ground round them is marked (hands, single prints, smears, struck-out numbers).
  - More marks are scattered along both stretches.
- **Reading one** takes the blacklight on the number, within 6.5 m, looking at it, with nothing in the
  way. The digit then shows big on screen and goes into the HUD's tracker, as before.
- **Footprints**: bare footprints in the ink lead from the lookout all the way to the bunker's door:
  left, right, a slightly wandering walk (about 345 prints).
- **His note** at the camp now says so: *"I put them on the trees on the way, in the ink only the
  lantern's black light shows. Look close. Not every mark out there is mine."* The bunker's card says
  the code was "marked on the way in (UV)".

## New
- `uv_ink.gdshader`, `UvInk.cs`:
  - hand-drawn stroke masks: the digits in his hand, handprints, bare feet, smears, tallies, an eye,
    scratches, struck-out digits;
  - marks wrapped round trunks, laid on the ground's slope, or scattered as one MultiMesh.
- A new input action, `lantern_mode`.
- A new sound, `uv_hum`.

## Tests
- Each number (in Acts 4 and 7):
  - it stands off the path among decoys;
  - it cannot be seen by the flame, even right up to it and looking at it;
  - B switches to the blacklight, and the number shows and reads;
  - it is saved and shown in the tracker;
  - B switches back to the flame.
- Footprints: more than 60 prints, ending at the bunker's hatch.
- The stairwell:
  - the flame guttered out a quarter of the way down (saved);
  - F won't light it again;
  - the blacklight still works.
- After Act 20 the flame works again, and the blacklight's secrets are written round the game (25
  or more).
- Screenshots: the number tree under the flame and under the blacklight, a decoy tree, the ground
  marks, and the footprints.
- The album check in Act 1 kept flaking under a full run's load: it pressed Tab while the note
  overlay before it was still letting go. It now waits for that, and presses again if a press is lost.
- Results:
  - Full `--autotest` (the whole game, start to finish): 533/535. The two failures were that album
    check, since fixed. Everything blacklight passes: every number unreadable by the flame and read
    under the blacklight, the footprints to the door, the flame dying in the stairwell (about 3.3
    minutes down now), F not relighting it, the blacklight working, and the flame back after Act 20.
  - `--story-from=14`: 29/29.
  - `--continue-test`: 642/645. The same three failures predate this work.
