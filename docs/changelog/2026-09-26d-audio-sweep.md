# 2026-09-26d — The audio sweep: spatial audio, rooms, occlusion, Foley, the oars, and the haunting

Commit: this pass's single commit on `main` (see `git log` for the hash).

The owner's brief: audit the audio emitters across every world object; fix mix bugs; make sure
dynamic scaling and spatial tracking are right; make every sound match what the player is doing and
what is going on; make the boat's paddling sound like paddling through water; polish everything,
and push the realism and spookiness.

## The audio director (new, `scripts/audio/AudioDirector.cs`, an autoload)
Every positional sound in the game now goes through one place, however the scene that made it set
it up. About seventy different places create sounds, each with its own settings.
- **Normalising**: each 3D sound is checked as it enters the tree:
  - an unknown bus (which silently went to Master) falls back to Events and is reported;
  - an unbounded sound gets a sane max distance;
  - every sound listens to the reverb zones;
  - the air takes a little more top end off over distance (distance low-pass at 6 kHz, -18 dB) than
    Godot's default.
- **Occlusion**: a sound on the far side of a wall is muffled (a low-pass down toward a dull 650 Hz
  thud) and about 8 dB quieter. A ray from the listener to the sound, a few sounds per frame, moves
  this smoothly as you walk round a corner. The last metre by the sound is ignored, so a sound set
  in a doorway or on its own prop isn't counted as behind it. It works on top of whatever the
  sound's owner does with its volume, so fades still work.
- **Rooms**: reverb spaces round every interior, in four kinds: small wooden room, hall, long tunnel
  and cavern. Sounds inside a space ring in it. While you're inside one, your own sounds (footsteps,
  cloth, the lantern) take on its reverb too, easing between spaces as you walk.
  - Small rooms: the cabin, the station lobby and its two rooms, the bunker's looping rooms, the
    library, the room at the top of the dais.
  - Halls: the basement, the gallery of burnt portraits, the bunker's CRT room.
  - Tunnels: the basement stairs, the gallery's corridor, the bunker's vaulted hall and exit hall,
    the long Act 15 hallway, the sewer pipe, the library's secret passage.
  - Caverns: the stairwell shaft, the sewer cistern, the pit, the round room.
- **Haunting**: sounds for the dark corners, played now and then at fixed spots in a space, only
  while you're near, so they pan and ring and occlude like everything else:
  - the station lobby: once it has started to rot, old boards taking a slow weight somewhere
    overhead;
  - the basement and the bunker's hall: water dripping in the dark;
  - the sewer's cistern: drips, and a heavy chain shifting far off in it;
  - the pit: a chain shifting high up in the dark, until the thing is dead;
  - the round room: air moaning through the bricked windows high up.
  - The library is left quiet on purpose, as the one respite.
- **Audit**, reported by the story test after every act:
  - sounds on unknown buses, over +8 dB, or never freed after finishing (leaks);
  - footsteps on surfaces with no footstep sound;
  - the most sounds ever playing at once.

## Sounds that now match what the player does
- **The oars** (the owner's note) are rebuilt as three sounds, each played at its moment and synced
  to the blade:
  - the catch as the blade goes in: a soft plunge, a deep bloop and gulps of air dragged down;
  - the pull as it's drawn through: a heavy swirl of water rushing past the blade, full of gurgling
    eddies, with the hull lapping as the boat surges. It is pitched to the stroke, so hurried strokes
    are shorter and sharper.
  - the release as it lifts out: a sheet of water pouring off it, then drips pattering back onto
    the lake.
  - Rough water plays them heavier, and the oarlock creaks as it loads more often.
- **Footsteps**:
  - gravel now crunches (a new set of six);
  - "rock" floors use stone (both used to fall back to dirt);
  - any floor with no footstep surface is reported by the tests.
- **Things that were silent**:
  - picking anything up (a scuff and a rustle into a pocket);
  - reading a note (paper unfolding);
  - the lantern on and off (the latch, the shutter, the flame catching or guttering);
  - a station door opening (a creak);
  - trying a locked one (the handle rattling against the latch).
- **Directional scares**: the three pounds on the cabin wall and the cabin door's slam were made
  flat at the ear earlier so they'd hit hard. They're still that loud, but now come from the wall and
  the door themselves (panned, full weight, never muffled), with a flat body under them so they still
  shake you.

## New sounds (AudioGen, `Paddle.cs` and `Foley.cs`)
- `oar_catch`, `oar_pull`, `oar_release` (four takes each);
- `paper_rustle`, `item_take`, `lantern_on`, `lantern_off`, `door_locked`, `step_gravel`;
- the haunting: `haunt_boards` (boards overhead, heard through a ceiling), `haunt_moan` (a tonal
  moan of air, never hiss), `haunt_drip` (drops ringing in a long hard space), `haunt_chain`.
- All are events: no sustained noise beds (the owner's rule).

## Tests
- Every act now ends with two audio checks from the director:
  - every sound on a real bus, none over +8 dB, finished ones freed;
  - a footstep sound for everything walked on.
- The first full run's audit found and fixed:
  - the round room's top floor had no footstep surface;
  - a flat, full-weight distant gunshot was flagged as too loud (sounds with no distance falloff are
    now exempt: they're loud by design);
  - the bird and loon emitters' reusable voices were counted as leaks (leaks are now one-shots left
    lying in the level);
  - hammering the oars could stack 70+ oar sounds at once; they're capped at six (the oldest is
    cut).
- An Act 1 album check that flaked under load (it pressed Tab while the last note was still closing)
  now waits for control first.
- Results:
  - Full `--autotest` (the whole game, start to finish): 521/523. The two failures were that flaky
    album check, since fixed; Act 1 passes 32/32. All the audio checks pass in every act: no bad
    buses, nothing too loud, no leaks, and a footstep sound for everything walked on.
  - `--continue-test`: 642/645. The same three failures predate this work.

## Also in this pass (the owner's lake screenshot)
- **The "eye" view**: the first climb's blackout blinks the vignette shut, and the level changed
  while it was still shut. The post-process material is one resource shared by every level, so the
  next level's wake-up read that heavy vignette as the resting look, and a black eye stayed round the
  view for the rest of the game. The blackout now puts it back behind the black, and a guard
  (`PostGuard`) eases the vignette back to rest whenever no cutscene is running and the player has
  control. The view is full-screen again.
- **The white trees across the lake**: looking into the low sunrise, the fog's sun-scatter lit the
  far treeline brighter than the sky. The scatter is down from 0.32 to 0.1 and the dawn fog thinned
  a touch (0.014 to 0.0105) for the doubled lake, so the far shore reads as a dark treeline softened
  by haze.
