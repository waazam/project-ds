# 2026-10-07 — The storm walk lit and its stalker hidden longer; the load from Act 1 cut to a fraction; frost, breath and fire in the ice; footsteps in the mud; a lantern setting; the maze kept on disk; a sweep

The owner, after playing: "in act 2 it is so dark even with the lantern i cant see, so we either got to make the lantern more bright or the environment. Next, the creature following the player is like extending his head ... he should be hidden until much later in act 2, it looks really bad whatever he is doing ... do a full sweep of the game looking for more bugs and polish what you can." Then: "the loading time between act 1 and 2 took a very long time." Their "act 2" is the rainy night after waking in the Hollow: the camp, and the storm walk to the cabin (STORY.md's Acts 3 and 4).

## The storm walk: light to see by (`Lantern.cs`, `ForestAtmosphere.cs`)
- **What made it so dark**, measured with a new brightness audit (`tools/qa/brightness_audit.py`, every act's screenshots ranked by brightness, too-dark ones flagged). The storm walk's frames averaged 2.4 out of 255. Three causes:
  - **The lantern's cage:** its shadows were cast by a cage that included a solid base disc under the flame and a hood over it. The disc threw a shadow over the ground for a metre all round you. They're gone; the struts' soft bands stay.
  - **The Hollow's night values** are set in its scene, not in the code, so the night was darker than intended: an ambient of 0.19, a moon of 0.1, an ambient floor of 0.27, and near-black blue fog.
  - **The lantern** itself was modest (1.35) for 14 m in the rain.
- **Now:**
  - The lantern is stronger (2.3) and falls off more gently.
  - The Hollow's night: ambient 0.3, moon 0.2, an ambient floor of 0.55 (no trunk or ground goes fully black), and a slightly lighter, thinner blue fog. The woods keep their shapes against it past the lantern.
  - It's still night, dark and blue, with no glare.
  - In numbers: the storm walk 2.4 → 6.0, the lookout (the giant above the trees now shows) 3.1 → 8.7, waking in the clearing 1.6 → 11.7, the camp 12 → 16. The blacklight shots stay dark on purpose.
- **A lantern setting** in the menus (70–160%), next to brightness, for a dark monitor.
- **A brightness guide** under the brightness slider: six squares from black up through the darkest greys, drawn through the same setting as the picture, and "set it so the second square is only just visible".

## The stalker (`Stalker.cs`, `StalkerBody.Horror.cs`)
- **Its neck no longer draws out.** Unwatched, it had crept its head out round the trunk on a neck that grew longer than any neck should; the owner found it looked bad. Now its head only leans over a little, and its mouth opens the longer you look.
- **Heard, not seen, for most of the storm walk.** From the camp until you're 60% of the way to the cabin, it is only sound: its clicking rattle from its tree and its steps answering yours, from behind, from either side. Only then is it drawn.
- **Its glimpses, with rare variations** (for replay, kept grounded):
  - Most are as before: caught, it's gone in a blink.
  - About one in eight is a stare: it holds your eye for a second or two, its mouth opening, then slides back behind its trunk over most of a second.
  - Far ahead at the fog's edge, about one in three sightings just stands there watching for a few seconds, and goes only if you come on.

## The load from Act 1 to the Hollow (`WinterWoods.cs`, `StationInterior.cs`, `Stairwell.Bottom.cs`, `LakeCrossingEvent.cs`)
- **Where the time went** (a new `[build]` log line for anything that takes long): the Hollow built every act's place at once. That was about 20 seconds of building, most of it hours away from where you wake.
  - the winter woods and the ski lodge: 8.3 s;
  - the church: 1.7 s;
  - the station and everything under it (the rooms, the stairwell, the hallway, the sewer, the pit, the library, the round room): 4.4 s;
  - the bunker: 1.9 s;
  - the forest: 2.5 s.
- **Now they're built when the story gets near them, behind blackouts the story already has:**
  - **The station and everything under it**, in the black of the lake crossing's end, as you step into the station.
  - **The winter woods and the lodge**, in the black after the fall down the stairwell in Act 14, while you lie out cold.
  - Each time, the render budget and the shader warm-up are brought up to date while it's still black. The blacklight's writing and the camera's subjects for those places are hung then too.
  - A save from further on builds everything at load, as before.
- **The load from Act 1** now builds about 5 seconds of the level instead of about 20. Act 3's test run is 38 s, down from 46.
- `--no-defer` builds everything at load, as before.

## Act 24 (`AirParticles.cs`, `FrostEdge.cs`, `frost_edge.gdshader`, `NapalmStream.cs`, `SnowMazeCave.cs`)
- **Your breath** shows in the ice maze, as in the winter woods.
- **Frost at the screen's edges** as the wendigo comes near (from about 16 m) and as the hunt's menace rises: feathery crystals growing in from the corners and sides, never past the middle third. It eases in over about four seconds and thaws over three. The crystals are still: nothing in it moves or flashes.
- **Fire on the ice:** a second warm light where the napalm has splashed and clings, so the ice walls round it glow orange, wavering slowly with the burning (never strobing).
- **The maze kept on disk:** built once, the cave's mesh and icicles are saved (`user://snowmaze_cache.bin`, keyed to the game's build). Loading a save in the maze reads it back in a fraction of a second instead of six seconds of building. `--no-maze-cache` ignores it.

## Act 25's wendigo
Its walks in the endings (to the wall, burning; off the crushed car at them) play their stride at the pace they actually cover the ground, as in the maze. Act 22's wendigo only leaps and holds; it has no walk to fix.

## The storm walk's sound (`PlayerFootsteps.cs`, `RainVfx.cs`, `tools/AudioGen/Foley.cs`)
- **Footsteps on the wet forest floor**, in the rain:
  - patches of mud (a soft slap, a suck as it sinks, the wet pull out);
  - sodden leaves (a pat and a damp, matted rustle);
  - here and there a root (a hollow knock, a scuff of bark).
  - The ground is fixed by place, so the same spot always sounds the same.
  - What follows you steps in kind: its steps answer yours on the same ground.
- **The boughs dripping:** fat drops of gathered rain falling off the branches round you, now near, now a few metres off, more in heavier rain. Never under a roof.
- New sounds: `step_mud_01..06`, `step_leaves_01..06`, `step_root_01..04`, `canopy_drip_01..06`.

## The sweep
A full run of the game, its log and its screenshots gone through for bugs, and polished where something was off:
- **The game had no credits.** Every one of Act 25's five endings stopped at the black: the credits (the end card, your pictures played back as polaroids, the score, the thanks) were only built into the Hollow's level, and the story now ends at the trailhead. They now run wherever the story ends (`GameEnding.Credits`).
- **A crash at the bottom of the stairwell.** A winter-woods instance from an earlier load of the level was still listening to the story after it was freed; the checkpoint at the end of the fall woke it and the fall's cutscene failed. It now stops listening when it's freed.
- **The dark nights** (above), found by the brightness audit.
- **The underground** (Acts 14 to 20, the flame dead): the sewer's vaults and the stairwell read as pure black even round their torches. A little more of the ambient is kept down there (8%, was 3%). The sewer's median frame went from 3.1 to 6.3; it's still near-black, and the blacklight is still what you see by.
- **The clip, sign and ground audits** now build the whole level at load, so the station and the winter are still checked now that a new game leaves them for later.
- Everything else in the run passed; no hitch over 150 ms in play.

## Tests
- **The full autotest:** 787 of 787, start to credits (Acts 1 to 25), with the station built at the lake crossing and the winter at the stairwell's fall, as a new game does.
  - An earlier full run had found the missing credits (780 of 781) and the stairwell crash (in its log).
  - After the last changes: Acts 3 to 7, 171 of 171; Act 14, 36 of 36; Act 17, 15 of 15; Act 24, 39 of 39.
- **The continue test:** 1058 of 1058, 40 scenarios. The Act 24 saves load the maze from its cache.
- **The clip audits:**
  - the trailhead: 0 z-fighting pairs;
  - the Hollow: its known 40 (station door frames);
  - the winter: 10;
  - no walk-through props.
- **The sign audit:** 0 blocking.
- **The load from Act 1:** the Hollow builds about 5 s of its level, down from about 20.
