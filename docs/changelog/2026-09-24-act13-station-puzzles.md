# 2026-09-24 — Act 12 death, Act 13 rebuilt: the station puzzles

Commit: this pass's single commit on `main` (see `git log` for the hash).

Two briefs. Act 12: a checkpoint at the start of the lake, and a death if the player rows too slowly.
Act 13 was rebuilt from `Project_DS_The Real Act 13.docx` into a roughly 30-minute puzzle section.
The rescue station starts kept and tidy, and each solved room leaves it more wrecked.

## Act 12: the lake can kill you
- **The checkpoint**: Continue from Act 12 now puts the player on the lake shore by the dock. A
  stale marker used to send them back to the Act 6 clearing, and a terrain snap used to drop them
  33 m up.
- **The hunt**: after the breach, a hunting limb follows the boat. Its gap closes by
  `HuntSpeed` a second, against whatever the player's rowing gains. If they row too slowly and it
  catches up (`CatchGap`), it takes the boat: the boat capsizes, the player is dragged under, a dead
  eye drifts up to look at them, and the screen fades to "The lake took you." The game then reloads
  the Act 12 checkpoint.
- Tuning: the hunter follows at 0.9 m/s. It catches a player who stops rowing or dawdles below
  about two strokes a second, while a steady rhythm slowly pulls away. At the first value tried
  (1.15 m/s), a steady 2.2 strokes a second got caught.
- New shared death flow `PlayerDeath` (fade, caption, reload the checkpoint) and `UnderwaterView`
  (a tinted, darkened, low-passed underwater view). Act 13's Room 2 uses both.

## Act 13: the station
- **The lobby**: a desk with a knife stuck in it (E). The basement door is duct-taped shut. Cut it
  open with a close-up minigame: hold the mouse button and drag down the slit.
- **The basement**: red brick stairs that feel wrong, humming. At the bottom the lights sputter and
  die over about ten seconds. The sputter is slow and soft, never a strobe, and with Reduce Flashing
  on it is a single fade. The room is flooded, with debris floating in it. Turn the giant hatch wheel
  three times (E) and the water drains in a seven-second whirlpool. A dead, droopy-lidded eye swirls
  down and sticks in the grate. It never moves while watched, but it always faces the player again
  when they look back. The grandfather clock gurgles a noon chime, spits out a key and bursts.
- **Room 1** (left of the desk): one line scrawled in red on each wall: DO NOT LOOK AT THEM / DO NOT
  TOUCH THEM / NEVER GO UP THEM. A duct-taped cigar box needs three cuts: left, right and front.
  Under the tape is a button. Pressing it melts the words down the walls into blood puddles, opens
  Room 2, and saves a checkpoint.
- **Room 2** (right of the desk): striped red damask wallpaper, an off-white ceiling, a black and
  white diagonal marble floor, and heavy crimson velvet drapes on a brass rod. A table in the middle
  holds a brass cryptex over the plaque "WHAT YOU WERE TOLD NEVER TO GO UP". The door slams shut
  behind the player, and five seconds later someone starts knocking softly. The first touch of the
  box breaks the window: the lake pours in, the drapes blow into the room, and the player is knocked
  flat. The room fills in 80 seconds, and at knee height the water turns to blood. The cryptex is
  six sticky brass rings: A/D picks a ring, W/S turns it, and a ring sometimes sticks. The word is
  STAIRS. Solve it and the blood is drawn back out through the window, the glass flies back into the
  frame, and the box opens on an old lighter. Fail and the player drowns and returns to the Room 1
  checkpoint.
- **Door 3** (behind the desk): burn a thick spiderweb away with the lighter. Behind it is a rusted
  iron door with three hollows: LOOK, TOUCH, CLIMB. This is the backtracking scavenger hunt, about
  10-15 minutes, or 5-7 for someone who knows it:
  - **LOOK**: the dead eye is out of the grate now and moves around the dark basement, but only
    while nobody is looking, with a wet slap from wherever it lands. It can only be taken while the
    player is looking straight at it from close by.
  - **TOUCH**: a handprint appears on Room 1's door. Of the three blood puddles, only the one under
    DO NOT TOUCH THEM holds the pale hand. In the others, something takes the player's wrist.
  - **CLIMB**: stair marks appear on Room 2's door. Next time the player goes in, the table is gone
    and a short wooden staircase stands there, going up to nothing. At the top it goes dark, there
    is a knock right behind them, and they come to holding one of the steps.
- **The finale**: set all three and the iron door grinds up. Past it the station becomes industrial
  and hellish: pipes, chains, meat growing from the walls, eyes in the flesh that follow the player.
  A long wooden staircase climbs to a white light, and the final checkpoint is saved at the top.
- **The lobby decays** through five stages as rooms are solved: kept, then stained, then rotted,
  then industrial, then flesh. Each change happens only while the player is out of the lobby.

## Additions
- `stationparts/`: `StationTextures` (procedural wallpapers, rotten boards, brick, marble, rust,
  meat, velvet, and decals), `StationProps`, `LobbyDecor`, `DeadEye`, `Cryptex`, `IronDoor`.
- `CryptexOverlay` (the modal close-up for the cryptex). `StationDoor` gains `SlamShut` and an
  `Opened` event.
- Inventory: Lighter, The Eye, The Hand, The Step. `Pickup.TakenId` lets several pickups of the same
  kind each remember that they were taken.
- New checkpoints: `Act13Room1Solved` = 12 and `Act13Finished` = 13, each with a Continue marker.
- Shaders: `basement_water` (the swirl and the drain), `torrent`, `underwater`.
- Wading footsteps in water.

## Audio
- Owner playtest: the rowing "sounds very wrong". The oar stroke was rebuilt as a light, bright
  splash: a soft high plish, a spray of small bubbles, and drips off the blade. The deep slap and
  gurgle are gone, the stroke plays a little quieter, and the oarlock creak comes half as often.
- 26 new synthesized station sounds: bulb buzz, sputters and pop, drain gurgle, clock tick, glass
  crack and shatter, the blood suck, cryptex clicks, sticks and opening, lighter flick, web burn,
  four wading steps, a door creak, and loops for the torrent and the industrial drone.

## Bug Fixes
- The basement's "lights die" beat and its hum were placed by depth alone. A player dragged under
  the lake in Act 12 sank past that depth and set the Act 13 lights-dead flag from out on the lake,
  and that flag was auto-saved. Both are now bounded to the basement and need the tape cut first.

## Tests
- `StoryTest` Act 12 now plays the lake twice. The first pass lets the limb catch up and checks the
  death and the reload to the lake shore. The second rows across.
- `--story-from=12` now starts at the lake (the same save the drowning reload restores).
- `StoryTest` Act 13 was rewritten for the new act, including a Room 2 drowning and reload before
  the solve. It passes 52/52 (`--story-from=13 --story-to=13`).
- `ContinueRoundTripTest` now checks that Act 12's checkpoint respawns on the lake shore. Its
  "standing on something" check no longer compares the lake and the station against the forest's
  heightfield, which they are not built on.
- Results: `--story-from=12`: 118/118. `--story-from=13`: 52/52. Full `--autotest`: 349/349.
  `--continue-test`: 402/405. The three failures (the Act 6 loop's fire state, the Act 10 mood, and
  the clearing stairs) predate this pass and are the same three noted in the earlier Act 12 entries.
