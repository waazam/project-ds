# 2026-10-14g — The forest's weak spots

The survey's "Forest improvements" list, worked through without touching the forest's character (the dark frame, the
lantern's pool, the fog, the muted olive, brown and blue-grey palette). Before/after shots from the survey's spots
(Acts 1, 3, 6, 7, 8, 22, 24 and the blizzard) are in the worktree's `test-output/contact_forest_before_after.png`.

## Visual polish

- **Snowflakes.** The six-armed crystals in the snow's atlas read as clip-art stars a metre from the eye. The generator
  (`snowflake_generator.gdshader`) gains two modes: a soft *aggregate* (a lumpy clot of crystals summed like a metaball,
  its rim ragged, its heart denser) and a plain *soft disc* (the out-of-focus flake, no bokeh ring). The atlas is
  re-baked (`--bake-snow`): cells 0-7 soft clots, 8-11 soft discs, 12-15 unchanged (the clumps and specks the dust and
  spores use). The far curtain is re-baked too, small soft clots and specks only. `Weather`: the near flakes are now
  1.2-3.6 cm (were 2.5-5 cm crystals), the middle layer takes the soft discs and clumps a little smaller, and a new
  **lens layer** (90 big, faint, out-of-focus flakes within 1.6 m of the eye) gives the air depth. In the blizzard
  the lens layer thickens and everything streaks a little along the gale.
- **Winter ground** (`winter_ground.gdshader`). It read as smooth clay. Now the wind's work is in the shading: broad
  drifts lying across the wind, sparse sastrugi running with it, soft lumps. Their lee sides and hollows go a cold
  blue-grey and their crests catch a little more light. The photo textures' dark crack lines are softened toward their
  average, and a broad mottle and grain carry out to the fog. Now and then a crystal glints as the eye moves (never
  on its own, never a glare). The road's ribbon now carries UVs across and along it in metres (vertex blue marks it),
  so the plow's packed tyre ruts and two lines of old boot prints, half filled in, are drawn along the road. Low drifts
  are heaped against the broadleaves' trunks on their lee side (a MultiMesh per tree chunk in the ground's own
  material, no collision). Collision and the walkable surfaces are unchanged.
- **The storm's far light** (Acts 3-5, `storm_horizon.gdshader`, driven by `RainVfx`). The frames were almost wholly
  black. Low over the woods ahead (down the trail), the cloud is thinner and a dim cold light comes through it, on a
  ring inside the fog's reach so the near trunks stand black against it. Every 6-16 s lightning flares in that cloud
  far off, with no light on the scene and no thunder (one slow swell with Reduce Flashing). It shows only in the storm,
  out of doors.
- **Act 1's sky.** Between the trunks it blew out to white through the post's halation. The open grade's sky colours
  are a good third down (a hazier, greyer afternoon) and `OpenSkyBoost` is now -0.3 (was +0.12). The peak in the sky
  gaps over the trail went from 208 to 150 (of 255). (ForestAtmosphere: only the `OpenSky` table and that export.)
- **The trailhead toilet block** (`weathered_block.gdshader`). It is now old painted cinder block: 40 x 20 cm blocks
  in running bond with sunk, shadowed joints, park-brown paint gone chalky and flaking to grey block, rain streaks
  from the eave with algae in them, rust bled down from the vents and the door frame, and splash, grime and moss at
  the foot (more on the shaded back). The footing, cap course and pad are darker, and the RESTROOM board is weathered.
- **Trail signs** (`sign_weather.gdshader`, `SignKit.Weather`/`Split`). A film over every board's face (the
  trailhead sign, the directional posts, the low signs, the toilet's board) adds grime blotches, rain run from the top
  edge, moss along the foot and up the ends, and pale lichen spots. It lies over the routed letters, so they fade and
  break up. The letters' white fill is a dirty cream, the sign paints chip more and carry more grime, and some boards
  have a split running in from the square end.

## Tools

- `LookShots` prints the sky's energy, exposure and horizon colour, and takes `--look-noshafts` and `--look-nosky`
  for A/B probes.
