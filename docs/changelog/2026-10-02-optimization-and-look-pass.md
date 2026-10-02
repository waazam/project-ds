# 2026-10-02 — The optimization and look pass

Commit: this entry's commit on `main`.

The owner asked for every suggestion I'd made, "in the most efficient order possible taking importance into
account". Running faster: streaming by area, occlusion culling, baked lighting, no shader-compile hitches,
instancing and levels of detail, merged colliders. Looking better: mipmaps, dust and grime, roughness maps,
architectural detail, baked occlusion on the architecture, the creatures remodelled, and consistent texture
resolution. Also:
- "when you just went up the stairs from the hotel lobby there were gaps into the hallway on both side where i can
  see outside of the map";
- "remember to push it to the main repo when you finish".

## The gaps at the top of the lodge's stairs (`SkiLodge.Interior.cs`)
- **Where they were:** the doorway from the gallery into the upstairs corridor is cut through the hall's west
  corner, where two of its walls meet at an angle.
- **The cause:** the corridor's walls ended square at the corner wall's face, and the cut's sides ran off at an
  angle. That left an open wedge either side of the doorway, with the outside through it.
- **The fix:**
  - the corridor's walls now carry on through the corner wall's thickness to the doorway (wallpaper, wainscot,
    skirting, crown);
  - there is a ceiling under the doorway's lintel.

## Running faster

### Where the time actually went (measured first)
- **How it was measured:**
  - a controlled benchmark in the lodge on its own (`creature_preview -- --bench`: fixed views, vsync off, each
    variant in the same process);
  - a profile of the real game standing in the lodge (`--autotest --story-from=23 -- --profile-nodes`): each node
    that runs every frame switched off in turn, each part of the world hidden in turn.
- **What it showed:** drawing the lodge itself takes about 1.5 ms. Its real frame was about 7 ms, and most of that was
  the rest of the world:
  - **The surface's sun:** about 1.5 ms. Underground its light is at nothing, but its shadows were still on, so it
    drew a shadow map of everything round the player every frame. A sun with no light to give is now switched off,
    and back on the moment it has any (`RenderBudget.DarkSuns`).
  - **The pit's leviathan:** about 2 ms. Its tentacles were posed every frame wherever the player was. It now rests
    while the camera is over 300 m off.
  - **The lake and the sewer:** about a millisecond together. The lake's floating dressing rode its waves and the
    sewer's rubbish bobbed every frame from anywhere; both now only while the camera is near.
- **The result:** the lodge's frame went from 7.0 ms to 4.8 ms (142 to 208 fps standing), and Act 23's walk from
  132–137 fps to 166.

### Occlusion culling, tried and switched off (`MeshKit.Occlude`, `LodgeKit.Wall`, `StationKit`)
- **What was built:** every solid wall span the lodge and the station build registers itself as an occluder down its
  middle. The occluders come from the walls, not from colliders, so nothing invisible (barriers, glass) could ever
  hide anything. The crawlspace's walls are left out, because the arms come through them.
- **What the measurement said:** occlusion culling added about 0.1 ms to the lodge's frame and saved nothing, because
  drawing the lodge is cheap and wasn't the bottleneck. On the surface, where there's nothing to occlude, it cost
  about 2 fps.
- **So:** it's off in the project settings. The occluders are still built, ready for a place that needs them.

### The underground's areas, gated (`RenderBudget.cs`)
- **The problem:** the areas (the station, the stairwell, the long hallway, the sewer, the pit, the library, the
  round room, the long stair, the church, the winter woods, the lodge) lie within a few hundred metres of each other,
  walled off only by rock. Nothing culls behind rock.
- **Now:** only the area the player is in and the ones either side of it in the story's order are drawn. The rest
  are switched off entirely: their meshes' visibility ranges shut and their lights faded out, however near they lie.
- **How:**
  - it uses the ranges and fades the render budget already owned, so it never fights the story's own showing and
    hiding of things (like the lodge putting the woods away while the player is inside);
  - the player's area is the smallest one whose own things' bounds hold the player;
  - the F3 readout shows the area.

### No more shader-compile hitches (`ShaderWarmup.cs`)
- **The problem:** the full test run's hitches of 120–150 ms came each time something was first seen: the renderer
  building that thing's pipeline. About half of the 407 were the test's own screenshots; the rest were these.
- **Now:**
  - once a level has built, one of every kind of thing it will draw is drawn once, tiny, in front of the camera,
    while the screen is still black;
  - "kind" means each pair of material and vertex layout (a pipeline is built per pair), each multimesh and each
    particle system;
  - then they're cleared and the fade-in goes ahead (it waits for this, at most a few seconds);
  - the lodge's own warm-up stays, for the things that only appear later.

### Instancing and levels of detail
- **The dining chairs:** all 72 are one model, drawn as one instanced set instead of 72 copies merged into the
  room's mesh. The frost of Act 23 still glazes them.
- **The Christmas tree:** its own instance, so it keeps the import's levels of detail. It's 90k triangles up close
  and a fraction of that across the hall (`FurnitureKit.Place`).

### Textures (`tools/Textures/import_settings.py`)
- **Mipmaps on everywhere:** without them a texture seen small or at a slant shimmers and sparkles, and costs far
  more memory bandwidth.
- **Compressed in video memory:**
  - the furniture's 54 baked cavity maps alone were about 216 MB uncompressed;
  - normal maps are flagged as such;
  - the surface photos the game reads on the CPU stay lossless.
- **One resolution per kind of texture:**
  - the architecture's surface photos at 256 px, the PS2 look;
  - the furniture's baked cavity maps at most 512 px (they're soft shading, so more is wasted);
  - the hero textures (the wendigo's, the tablecloth's jacquard) as made.

### Measured, and not changed
- **Baked lighting:** Godot can only bake lightmaps in the editor, and the game builds its geometry at runtime, so
  there's nothing for the editor to bake. Lighting isn't where the time goes either (the round room's GPU frame is
  about 2 ms). Its look comes instead from the baked-occlusion grime below.
- **Merging colliders:** the physics step is about 1 ms in an ordinary frame, so merging the static colliders would
  gain nothing measurable. Today's real physics cost was a bug (in the 2026-10-01b entry: the tablecloths all
  simulating from the start) and is fixed.

## Looking better

### Dust (`furniture_dusty.gdshader`, `FurnitureKit`)
- **What it looks like:** years of dust on every modelled piece of furniture and clutter in every interior. It lies
  only on what faces up (table tops, seats, mantels, the tops of frames and books), thick in patches and thin
  between, pale and matte.
- **How:**
  - each piece's material and its dust are one shader: the room's photo, relief and roughness, the piece's baked
    cavity map, and the dust;
  - it began as a second pass over every piece, which cost an extra draw for each one, so it was folded into a single
    pass. The benchmark shows it costs nothing now;
  - never on glass, glows or cut-outs.
- **Room 201:** kept perfect, so there's not a speck in it.

### Grime at the walls' feet, and their trim (`Weathering.cs`, `LodgeKit.Wall`, `StationKit`)
- **The grime:**
  - where a wall meets its floor, there is a soft shadow on the floor and a darker band up the wall's foot: the dark
    gathers in corners, and scuffs and wet were always worst low down;
  - it gives the look of baked occlusion for the price of two thin strips per wall, multiplied over what's behind
    them;
  - this covers every wall the lodge and the station build.
- **The trim:**
  - skirting boards along the walls' feet (the lodge and the station);
  - crown mouldings where the lodge's walls meet its ceilings;
  - both are set a few millimetres off the floor, the ceiling and the wall faces, after the clip audit found trim
    lying in the same planes as the floors, door casings and the wardrobes against the walls.

### Roughness (`tools/Textures/make_surfaces.py`, `SurfaceKit.Relief`)
- **The maps:** every surface photo now has its own roughness map, fetched from Poly Haven with its colour and
  normal maps.
- **How they're applied:** scaled so their mean is 1, so each material keeps the roughness it was tuned to and only
  gains the variation: worn, handled places smoother and catching the light.

### The creatures, remodelled (`tools/Blender/creatures.py`, `CreatureModels.cs`)
- **Which creatures:** the owner asked for the stalker, the crawler and the friend. The friend is no longer a body in
  the game (what he left on his table is all there is of him), so it's the stalker (and the giant, which is the same
  body, larger) and the crawler.
- **How:**
  - each body's own parts are exported from the game (`creature_preview -- --export-bodies`), each in its pivot's
    space with its tone colours (the stalker's shader reads paleness, brightness and wetness from them, and the eyes'
    glow from their alpha);
  - Blender rebuilds every part dense and smooth with a fine skin grain, then they're loaded back in place of the
    built ones. Proportions, the pivots its idle and its walk turn about, and its tones all stay exactly as they were.
- **The stalker:**
  - its body, hips and arms are remeshed into one continuous sculpted surface (about 37k triangles, from a few
    thousand), with the tones carried over;
  - its tattered strips and bark shards are smoothed, not remeshed, because they're too thin;
  - its head is smoothed in place: a long grey skull of a face, the yellow eyes, the nostrils;
  - remeshed, the face's tones had broken up into a mosaic, which is why the head isn't remeshed.
- **The crawler:**
  - its body, skull, hands and feet are smoothed and grained: the spine's knobs along its back, its ribs under it, the
    neck craning down to a skull with sunken sockets and a hanging jaw;
  - its limbs, plain cylinders until now, are modelled: a long starved bone under thin skin, a knob of joint at each
    end, cords of tendon along it;
  - three tries to get here: remeshed, its open-tube neck and ribs vanished and the head floated free; smoothed
    as-is, its faces culled from the front (the winding is the other way round in Blender) and shrank into islands
    (each face had its own corners). Now it's welded, turned outward, then smoothed.

## Bugs the test runs found along the way
- **The lantern's flame lit again after dying** (`Lantern.cs`):
  - the lantern's own clock only ran while the flame was shown;
  - if the flame guttered out while the blacklight was on (or the lantern was off), it stayed "dying" for ever, and the
    moment the blacklight went off it was lit;
  - the clock now always runs. The full run's "F won't light the flame again" check caught it.
- **A press of E lost at a level's start** (the lodge's mudroom door, now and then): the test presses again and says
  so in the log.

## Tests
- **The full autotest:** 722/723. Its one failure was the lantern bug above, since fixed: Acts 13–14 then passed
  92/92.
- **Frame rates while walking:** 148 fps on average over the whole game (yesterday's run: 116–129).
  - the station 151, the stairwell 160, the long hallway 166, the round room 154, the church 170, the winter woods
    160, the lodge 163;
  - most of the underground was about 100 before.
- **The continue test:** 925/925, 34 scenarios.
- **The clip audits:**
  - the Hollow: 40 z-fighting pairs, the same known station door frames as before;
  - the winter: 14, fewer than before today's trim;
  - no new walk-through props.
- **The benchmark (lodge alone, ms a frame):** dusty and plain both 1.50; with occlusion culling 1.60–1.62.
