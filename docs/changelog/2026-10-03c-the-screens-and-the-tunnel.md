# 2026-10-03c — The CRT room's screens, the tunnel, the vine door's ivy, and the HUD's rule

Commit: this entry's commit on `main`.

The owner, from a screenshot of the CRT room: "the tunnel and the crtv room need some work. lets make the tvs larger
and have more of them to cover the walls more. This should allow us to see whats on the tvs better, may have to bump
that resolution up a little". And: "lets take the top line off of the inventory ui there, it serves no purpose". Then,
with two reference pictures of ivy-grown doors: "the overgrown door leading to the tv room desperately needs the vines
too look way better and more tangible", and "i like the idea of some of the tunnel before the vine door is mossy and
viney too".

## What it was
- **The sets:** small (46–72 cm), on 2.4 m shelving units with gaps between them: five along the back, three a side,
  bare concrete showing all round.
- **What they showed:** four camera views at 96 x 72, upscaled (blurred, the atlas's cells bleeding into each other)
  and blurred again by the tube shader's fixed mip. Up close it was grey mush.
- **The tunnel:** ninety metres of the same bay: ribs, conduits, lamps, stains, and nothing to pass on the way down.
- **The vine door's ivy:** flat 32 px cards, each a blob of three tiny leaves, on thin dark sticks. From the hallway it
  read as dead thorny branches.

## The CRT room (`CrtRoom.cs`)
- **Bigger sets:** a new size of set (90 x 74 cm) with the 58 and 72 cm ones on the lower shelves, mid sizes on the
  top shelf, small ones only to fill a shelf's end and stacked on the back wall's tops.
- **More of them, wall to wall:**
  - the shelving is taller (four shelves to 2.8 m, spaced for the big sets);
  - the back wall's five units stand shoulder to shoulder;
  - down the sides, one unit fills each bay between the pilasters, and a narrow one the back corner;
  - the sets sit at the back of the shelves, the big ones standing out over the front edge.
- **The marked set on the console** is bigger too (84 cm), the switch on it as before.

## What's on them (`BunkerPictures.cs`, the tube shader)
- **The resolution:** every picture is drawn at 256 x 192 now (was 96 x 72) and kept at that size (no upscaling).
- **Nine cameras** (a 3 x 3 atlas, was four), drawn as real night-vision footage: the camera's infrared lamp lights
  what's near it pale, and the woods behind fall away into the dark. Each burns in its number, REC and the time
  (09/21, around 03:14, the night of the station log's last entry):
  1. a trail winding away between the trunks, ferns along it;
  2. the footbridge, its boards and rails running out over the black stream to the far bank;
  3. trunks close to the lens;
  4. the trail sign at a fork, the paths parting either side of it;
  5. someone in the trees: tall, thin, half behind a trunk, arms past its knees, its head tipped out toward the lens
     and its eyes catching the lamp (on about a ninth of the sets);
  6. the bunker's own door, in its concrete face in the mound, the ivy hanging over it;
  7. the tunnel, from high on its wall (ray-cast through the hallway's own profile: the ribs, the conduit, the lamp
     cages, the far end black);
  8. the lake shore: the black water, the far firs mirrored in it, stones and reeds at the lens;
  9. a fallen tree across a clearing, its roots torn up.
- **The trunks** are round, with furrowed bark, root flares and dead branches; the ground has leaf litter and twigs.
- **The cabin smouldering and the stairs** are redrawn at the new size (shingles, round logs, glazing bars, tiered
  firs; the stone stairs mottled).
- **The tube shader:**
  - picks the cell from any grid (`atlas_grid`);
  - chooses its mip as usual and only adds blur as the picture fuzzes, so the far sets don't shimmer and the near ones
    are sharp;
  - the picture's own grain is fine (it was the snow's coarse cells);
  - the scanlines finer and fainter.

## The tunnel (`BunkerHallwayDressing.cs`)
- **Markings:**
  - the distance in, stencilled every ten metres on the left wall;
  - BULKHEAD 90M at the entrance;
  - RESTRICTED in red on both walls at the far end, with chipped hazard stripes low down the last bays;
  - the paint worn through, run below the letters.
- **On the walls** (the kick wall is only 1.2 m high and the vault curves in above it, so everything taller stands
  out from the curve):
  - two sealed steel side doors on the right, AUX 2 and AUX 3, in concrete surrounds of their own (piers and a
    lintel), the second chained shut from this side, a padlock on the chain;
  - three junction boxes (440V) with their conduit up to the tray;
  - a fire-hose cabinet, the hose still coiled in it;
  - a wall telephone with its handset off the hook, hanging by its cord to the floor.
- **The vault spalling** in three places: the scar with cracks running off it, a grid of rusted rebar showing, the
  chunks that fell still lying at the wall's foot.
- **On the floor:** a drain every ten metres, crates by the left wall (with collision), papers, and a pair of boots
  standing by the right wall with nobody in them.
- Everything stands within the walls' last 0.2 m, outside the walking collision, or has its own.

## The vine door's ivy (`BunkerVineDoor.cs`, `ivy_leaf.gdshader`)
- **The leaves** (`BunkerTextures.IvyLeafAtlas`, 256 px, four leaves):
  - five-lobed, three-lobed, an old one going brown and holed, and the unlobed heart of a mature stem;
  - dark waxy green, pale veins fanning from the stalk, each with its stalk.
- **Each leaf is a card folded along its midrib**, its tip curled back, held off the stem on its stalk. The two halves
  catch the light differently, so the growth has depth.
- **The leaf shader:**
  - a soft waxy sheen where the lamps and the lantern catch it;
  - paler, duller undersides;
  - a little light coming through;
  - a faint tremble in the draught.
- **The stems:** woody, thick at the foot and thin at the tip, wandering, branching. Their leaves alternate along them,
  big near the foot and small and crowded at the tips, so the leaves hide the stems.
- **Laid out like the references:**
  - two old trunks up either side of the frame, arching over the lintel to cross in the middle;
  - a second, thinner trunk twisting away from each, up to the vault;
  - climbers over the rest of the bulkhead;
  - a fringe hanging over the doorway, stopping above head height (2.05 m and up);
  - thinner ivy on the door's own leaf (so the door still reads), and the strands across the gap that tear when it's
    pushed.
- **The tunnel before it, grown over:** the last eighteen metres, thicker toward the door:
  - creepers out along the floor by the walls;
  - climbers up the walls and over onto the vault, following its curve;
  - a fringe hanging from the vault, kept above the head down the middle;
  - moss on the floor, up the walls and on the vault;
  - the far end's hazard stripes and RESTRICTED stencils half under it.

## The HUD
- **The item in hand** (bottom right): the thin line above its name is gone.

## Tests
- **Looked at in the bunker preview** (`bunker_preview`, new shots `crt_feeds_*`, `hall_*`, `vine_door_*`,
  `vine_tunnel_*`, the atlases `surv_atlas.png`, `ivy_atlas.png`) and tuned:
  - the bark's camouflage blotches made long furrows;
  - the lake feed lifted;
  - the diagonal hatching across every tube fixed (the sine hash, now an integer one);
  - the ivy's leaves made broader, bigger, twice as many, the stems thinner, the veins and sheen quieter;
  - the side doors and wall boxes brought out of the curving vault (they were sinking into it);
  - the spalling scars made larger, with cracks and a rebar grid;
  - the hazard stripes muted.
- **The full autotest:** 735/735 on its own and 728/728 with the next entry. New checks: the tunnel dressed
  (17 stencils); the CRT room's walls covered (172 sets, 65.7 m² of them, the atlas 768 px).
- **The continue test:** 925/925, 34 scenarios. One run's Act 22 halfway respawn found no floor under the player for
  a moment; it passed on the rerun.
- **The clip audits:**
  - the forest acts: 40 z-fighting pairs and the winter: 14, both as before;
  - no UV faults;
  - the one walk-through they found (the marked set on the console, big enough now to count) has its collider;
  - no walk-through props.
