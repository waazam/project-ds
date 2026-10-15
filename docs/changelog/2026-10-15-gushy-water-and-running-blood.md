# 2026-10-15 — Gushy water, and running blood

The owner asked: "make water flowing more gushy and less flat". Then: "by water I mean the blood too". Every bit of
flowing water and flowing blood in the game used to be a flat plane or strip with one scrolling texture on it. Now it
moves, pours, breaks up and splashes. The fix is in the same commit as this file.

## The kit (`FlowFx`, four shaders)
Every pour, splash and run is made from the same few parts, so they all move the same way.
- **`stream_water.gdshader`**: a creek's running water. It replaces `terrain_water.gdshader`, which is deleted.
  - **Flow:** two layers of noise are dragged down the flow and blended, half a cycle apart (the flow-map trick). The
    middle runs about three times faster than the banks, and the pattern never shears apart over time.
  - **Steep runs:** the ribbon's vertex colour carries how steep the bed is. Steep runs flow faster and comb out white
    streaks along the flow.
  - **Waves:** the surface heaves in small standing ripples and in lumps carried downstream. They are bigger in the middle
    and on steep runs. The ribbon now has nine vertices across and a row every 0.75 m (it had two across and a row every
    1.5 m).
  - **Foam at rocks and banks:** a broken white lace wherever the water is shallow over something. It's worked out from
    the depth buffer, so the footbridge's rocks, the waterfall's pool stones and the banks all get it with no extra
    setup.
  - **Colour:** olive and slightly see-through at the shallow edges, peat-dark where it's deep.
  - **Gloss:** kept low. A sharp sky reflection turned the whole run into a pale grey sheet.
  - **Debris:** now and then a dark leaf rides the middle of the current.
  - **`blood`:** runs the creek red (see Act 6 below).
- **`pour.gdshader`**: anything pouring (a curtain, a sheet or a tube).
  - **Shape:** the body is combed into strands. Its edges (a sheet's sides, a tube's silhouette) are eaten into fingers
    and gaps that open and close as it falls.
  - **Surges:** swellings travel down it with its fall (vertex displacement along the normal), and the whole pour sways
    a little.
  - **Aeration:** it whitens as it falls (`aerate_start`, `aerate_len`). Highlights slide down with the strands.
  - **`viscous`:** turns it to blood. It slows to about a third of the speed, the lumps get heavier, the white water goes,
    and it turns dark and glossy with thicker edges.
- **`churn.gdshader`**: the boil where a pour lands. A disc of froth rings running outward and breaking up, the middle
  heaving and swirling. With `viscous`, blood lands in blood: dark glossy rolls instead.
- **`blood_surface.gdshader`**: wet blood on a surface, either a pool (`spread` grows it) or a run (it creeps along).
  - **Depth:** dark where it's deep, thinner and browner at the edge.
  - **Rim:** raised, a darker band just inside it. Pools have a lobed rim with tongues reaching out.
  - **Movement:** slow heavy creep, darker clots drifting slower than the rest, rings rolling out from `source` when
    something pours into it, glossy highlights that move.
- **Particles:**
  - `FlowFx.Spray`: droplets thrown up where a pour lands, stretched along their flight. Water drops have a faint glint
    of their own, so they read at 640x360 in the dark.
  - `FlowFx.Drips`: drops that swell, stretch long as they let go, and fall (a scale curve with Y aligned to the fall).
  - `FlowFx.Mist`.
  - Each emitter has a few dozen particles at most.
- **Warm-up:** `FlowFx.Prewarm` runs from `ForestTerrain` (in both levels). It draws one of each part in the load's
  warm-up, so a pour or a pool an event makes later (the skull, the bowl, the stairs) doesn't hitch. Room 2's hidden
  torrent is in the `warm_up` group.

## Water
- **The creeks** (the trailhead woods and the Hollow, `ForestTerrain.BuildWater`): `stream_water.gdshader`, as above.
- **The waterfall** (`Waterfall.cs`):
  - **The water:** dark glassy water over the ledge, then a curtain combed into ropes that whiten as they fall. It surges
    (0.11 m), and its sides and foot are ragged. Three strands of it stand out over the curtain.
  - **Where it lands:** a churn at the foot and a spray of droplets thrown up off it. The mist is fainter: its unshaded
    puffs glowed as white balls at night.
  - **Removed:** the old flat white foam patch below the fall. The creek's own steep-run streaks carry the white water
    on down.
  - **Fixed:** the strip's fade in at the lip and out at the foot was set per triangle, which cut a sawtooth across the
    foot once the curtain was more visible. The strip is now opaque; the shader rags the foot and the churn covers it.
  - **Sound:** the fall's sound is where it was, at the foot.
- **Room 2's torrent** (Act 13, `StationRoom2.Pour.cs`; it replaces `torrent.gdshader`, which is deleted):
  - **The pour:** a thick pour over the sill, cut into columns so it can swell. Three ropes of it, each its own thickness,
    wander a little and start at different heights.
  - **Where it lands:** a churn and spray that ride up on the flood's surface as the room fills. The flood's surface
    (`flood_water.gdshader`, now subdivided 24 x 24) heaves in rings out from where the torrent lands, and a current
    spreads from there.
  - **When it's drawn back out:** the churn and spray stop, and the pour runs backward as before.
  - **When it turns to blood:** see below.
- **The sewer** (Act 17, `Sewer.Flow.cs` and `basement_water.gdshader`):
  - **The pipe:** the water now runs on toward the cistern. Its scum drifts with the current, streaks are combed along
    it, and small ripples travel down it. The tunnel's water plane is 3 x 160 segments (it was 1 x 20) so the ripples
    show.
  - **Three pipe mouths in the cistern now run.** The one by the entry and the one on the west wall pour out thick and
    ropey: murky brown-green with grey froth, a churn and a little spray. The high far one, with the light down it, lets
    out a thin trickle that catches its light, with drips off its lip.
  - **Sound:** the two pours have the creek's water loop under them, pitched down (0.7 and 0.86) and quiet (-14 and
    -19 dB). That's a steady bed, not a repeating sound. No new sound assets.
  - **Untouched:** the Station basement's standing water uses the same shader with `flow` and `viscous` at 0, so it looks
    exactly as it did.
- **Drips in the pit** (`BossRoom.Wear.cs`): the water dripping off the pipe joints now swells on the joint and
  stretches as it falls (`FlowFx.Drips`). Before, it was a fixed small quad.

## Blood
- **Act 6's blood rain** (`BloodRain.cs`):
  - **The creek runs red.** It reddens slowly while it rains (about fifteen seconds to its full 0.9), never pales while
    it's raining, and runs clear again over three times the rain's fade once the rain stops. It's thicker, slower and
    glossier than water, with no white water.
  - **Splashes:** the drops now throw up little red splashes off the ground round the player, as dense as the rain is.
- **Room 2's water turning to blood** (Act 13):
  - **Timing:** the torrent goes thick, slow, dark and glossy along with the colour's own 3-second tween.
  - **The surfaces:** its ropes and its churn follow, and the spray turns from water drops to blood drops.
  - **The flood:** heavier, glossier and darker in its troughs, and slightly less see-through (+0.08). The cryptex stays
    readable.
- **The pit's blood** (Act 18, `basement_water.gdshader` `viscous`):
  - **Movement:** slow, heavy swells (6 cm, about 6-8 m apart) that tilt the surface, so the lamps' highlights roll
    over them. The highlights are broad and soft (roughness 0.1-0.22); the old ones were pin-sharp water glints.
  - **Look:** glossy, darker in the troughs, clotting in darker patches.
  - **The drain:** its spiral lines are dark red rolls, not pale foam.
  - **Unchanged:** the glow lights and the levels.
- **The head on the platter** (Act 23, `SkiLodge.Blood.cs`). It used to have red beads falling.
  - **The rope:** a thick, slow rope of blood out of its jaw down onto the silver. It hangs from the head, so it turns
    with it.
  - **Drops:** they stretch off the teeth.
  - **The pool:** it spreads over the platter for nine seconds, lobed and glossy, with a raised rim. Rings roll out from
    where the rope lands, and blood rolls in blood there.
- **The blood dragged down the table** (Act 23):
  - **The run:** it creeps along toward the end, glossy and clotting.
  - **The end of the table:** it pours over the end in a heavy sheet with two rivulets, and drops fall off the lip. A
    pool spreads on the floor under it. The old drip sheet stays under the pour as the stain.
- **The bowl** (201's card, Act 23): the snow slumps as before. Now a surface of blood wells up from under it, spreads
  to the rim and rises up the bowl (2.6 s), rippling as it comes. It was a red cylinder scaling up.
  - **Fixed:** that cylinder (and the melted bowl's blood on a reload) sat at 6-8.5 cm, under the tureen's thick base
    (it's 11.8 cm tall), so no blood ever showed in the bowl. It now rises from 9 to 11 cm, and the reloaded state sits
    at 10.4-10.9 cm.
- **The arms in the crawlspace** (`WendigoArm.cs`): the blood running off them now falls in stretching drops, not beads.
- **Ending 4's stairs** (`EndingDirector.Blood.cs`):
  - **Down the steps:** from the top step down, the blood wells up on each tread and spreads in a lobed, glossy pool.
    It reaches the nosing and pours thick over it down the riser, in a sheet with two rivulets, onto the step below,
    which fills in turn. Drops hang off some of the nosings.
  - **At the bottom:** it spreads out on the ground.
  - **Same as before:** each step takes the same 0.45 s, and each still has its drip one-shot (the existing set). It
    used to be a flat red box on each tread.
  - **Fixed:** the boxes were placed for 0.2 m rises and 0.3 m runs, but the staircase is 0.17 by 0.28, so they sank
    into the steps and showed only as thin red lines. The blood now reads the staircase's own `Rise` and `Run`.
- **The church** (Act 21, `Church.Blood.cs`):
  - **The altar:** the torn altar cloth's blood is still wet. Drops gather on its edge and fall onto the step, and a
    thick run goes over the step's edge to the floor.
  - **The pool:** live and glossy over the old stain, with rings where the run comes in.
  - **Unchanged:** the smear to the vestry (dragged, drying).

## How it was checked
- **Look shots:** before and after, with a few frames 0.12 s apart to show motion. `LookShots` has two new dev-only
  options:
  - `--look-seq=N`: N more frames of each pose.
  - `--look-fx=name`: puts on an event's effect for the shots: `torrent`, `torrentblood`, `drain`, `lodge`, `stairs`,
    `bloodrain`, `where`. `FlowPreview.cs`, by reflection; nothing there runs in play.
- **Playtest beats** 1, 1b, 6, 6b, 12, 13b, 17, 18, 21, 23c and 25-4: all 11 passed (195/195 checks), with 0 engine
  errors.
  - **Frame rate:** a minimum of 1 fps shows in beats 1 and 17. HEAD without this change shows the same, so it's a
    load stall that was already there.
  - **The sewer:** its average frame rate is about 10 % lower (130 on HEAD; 115 and 121 in two runs with this change).
    That's the pours, churns and spray, all transparent.

## Not changed
- The lake's still surface.
- The Station basement's standing water.
- The static blood decals and stains (the lobby, the smears).
- The lake creature's and the shadow man's drips.
