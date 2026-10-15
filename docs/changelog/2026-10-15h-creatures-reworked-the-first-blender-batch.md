# 2026-10-15h — Creatures reworked, the first Blender batch

The owner's pass over every monster's model, and the start of moving every model to Blender (his rule from now on: every model is built in Blender, tools/Blender/*.py → assets/models/**/*.glb).

## Creatures
- Act 15: the stalker replaces the shadow man in red light, green light and at the save lamps (HallStalker.cs; held poses, its head following, ember eyes, the take). "It Taketh". The woods stalker goes inert indoors and underground; the hall's rattle is occasional.
- The wendigo scarier and fuller (wendigo.py): a broader ribcage over a starved middle, a long tongue, hide in strips, frostbite and blood, a slack quivering jaw, pinprick eyes; clips lurk, sniff, approach, crawl, lunge, grab; owl head-rolls and stop-motion jerks.
- The giant (Act 7) walks in step with its speed (a 15 m stride every 6 s, the feet locked to distance, footfalls on the plants delayed by distance), hunched with a long neck and arms, and sits in the scene's own fog with soft edges and mist banks before it (GiantStalkerEvent.Air.cs, fog_bank.gdshader).
- The crawler: bone under skin, bony joints, a brow and sunken cheeks; under the blacklight the teeth glow, the hair doesn't. Still unseen without the blacklight.
- The doe and the squirrels reshaped (a Blender pass to follow).
- The octopus boss's eyes pop quicker at the end (first gap 0.4 s, was 0.75).

## Blender
- Minor figures (figures.py): the stairwell rail figure, the lodge's sheet figure (two poses), the crypt's hooded statue, the station's flesh growths, the lantern moths.
- Tentacle creatures (tentacle.py): the lake creature's limbs, the window tentacle and the octopus boss's limbs and hide (46 eye sockets), with sucker cups, mouths of three curled lips and lidded eyes; the game still drives the limbs (TentacleModels.cs). `--no-tentacle-model` forces the old code flesh.

## The world
- Tree lines gone: no bare branch sticks past the needles, needle cards fading edge-on (foliage.gdshader edge_fade), ragged tier cones.
- Act 1's woods thicken off the trail (ForestScatter.Corridor.cs): open to ~12 m, hard to push through by ~25-35 m, props, wildlife and the tree-fall lanes kept clear.
- Smoother joints on code-built props and held items (MeshKit: sides by radius, joint balls; `--rough-kit` to compare).

## Act 6
- The loop's end: a blink 90 m above the clearing, a 4.6 s free fall, and on impact straight into the void at full force (orange streaks, flashes, swirl) slowing into the dark (red eyes, the slowed voice, the rattle far below), a slow fade to black, the usual wake (FallVoid.cs; Reduce Flashing and head motion respected).

## Sound
- The original far rifle back (the file itself, not regenerated), +16 dB.
