# 2026-10-15g — The stalker heard right, and Act 1 played again

The owner's second play-through of Act 1 and the Hollow.

## Act 1
- The dead fir: its creak starts the stop and the turn (within 26 m), it cracks after 1.8 s wherever they look (never on them): it can't be run past. The view holds one way, toward the falls; the wall trees all come down from the moment the fir lands, half a second to a second apart; the first ones in the stop need not be in the middle of the view.
- The stairs: the drone, the ringing and the pressure build all the way, surge over the last 6 m, and the instant they step into the clearing round the stairs (11 m from its centre) every sound stops dead. No game title, no hold; THE STAIRS on the first step.
- The bridge: a pocket of extra ambient light round the footbridge (ForestAtmosphere.PocketCentre/Radius/Lift; it was the darkest place on the early trail, unchanged in code for days).
- The black bird: Alex's saved scream (kept in assets/audio/storage) brought up close and sudden (AlexScream.cs, raven_alex_01-03): a 6 ms attack, a hit under it, mostly dry, loud; fired the frame the shutter snaps.
- The far rifle reworked again (Gunshot.cs): a sharp report with real 1-3 kHz, the pop and boom, echoes that are the same shot coming back off the hills, a tail made of the shot reverberated.
- The breath ~5.5 dB quieter and deeper. The lantern prop's glass and hand light dimmed (its light on the world unchanged).
- Tree creaks gone from the forest ambience (and the storm walk's trunk groan, and the stalker's own trunk creaks; its claw scrapes stay).

## Into the Hollow
- A loading veil (LoadingVeil.cs: black, tape grain, a drifting tracking band) over the whole level load, scatter build and shader warm-up; the window kept answering (LoadPump.cs); the sound eased back in over ~6 s. The first trip on a fresh build still holds ~28 s while the Hollow's textures are drawn.

## The stalker (research: Alien: Isolation, RE2's Mr. X, Left 4 Dead's director, Slender, Amnesia, Lethal Company)
- Its steps answer between the player's (0.44-0.56 of their stride, never within 150 ms of one), a few dB under the player's own, heavier, occluded per step; never reusing the player's last sample.
- The rattle: 48 short phrases (Creature.RattlePhrase: ticks, runs, long runs, stutters) with a chitin edge for direction, no repeats soon, louder (UnitSize 14, cap +8), fading as it's seen; steps and rattle take turns; the off-tree rattle drifts round behind instead of being pinned to the camera.
- Its own Stalker bus: duller when it's behind, a little room further off.
- A director (Stalker.Director.cs): build (trees, short spells, sparse ticks), peak (episodes, full spells, the stop beat), relax (nothing of it).
- Every walker-like sound is its own: the storm walk's mud steps, twig snap and thump behind removed; the pack's buckle/zip clinks removed; the canopy drips (2-4 soft pats at a walking pace: the "extra footsteps in the rain") now single light plips, sparser, from above, quieter. Rain 3 dB quieter.

## Tests
- `--soundlog` (story test): every sound as it starts, with its bus, source and distance.
- The checks follow the new rules (the fir trigger, no title, the clearing's silence, the lantern without cage lines).
