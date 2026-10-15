# 2026-10-15e — Snow that lies on the ground

Group (e) of the 2026-10-15 repetition, clipping and clutter scan (`test-output/scan/SCAN.md`): Acts 22-25, the winter
woods, the church glade, the snow maze and trenches, and the Act 25 ending. The fixes are in the same commit as this file.

## Act 25, the snow's edge (`snowmaze/EndingDirector.cs`)
- **Snow patches (scan #4).** The 46 patches were decals 1.6 m deep. They painted every fern, bush and log inside them
  flat white, and the car and the player's legs too. Now each patch is a thin sheet of old snow laid on the terrain: every
  vertex sits at the ground's height, a few centimetres thicker toward the middle. Its ragged edge and melted holes are cut
  by alpha, so ferns and logs come up through it. No patch lies under the bank (one used to lie on the tunnel floor).
- **The exit mound (scan #11).**
  - The nine identical, evenly spaced "petals" round the hole are gone. In their place:
    - a few crust plates of different sizes and shapes, at uneven gaps along the arch, each tilted and sagging differently;
    - an uneven roll of crust round the cut's edge, which also hides the saw-tooth where the face's grid meets the hole;
    - eleven broken pieces at the foot of the face and just inside the mouth.
  - The bank uses the maze's packed snow, greyed (pitted crust, grit, glaze streaks) instead of the greyed fresh snow,
    which read as smooth clay. The face has more relief: lumps, a finer crust and the wind's layers. It now fades out only
    at the hole's own edge, not across the whole width round the mouth.

## Act 24 (`snowmaze/Act24Maze.cs`, `SnowMazeDressing.cs`, `packed_snow.gdshader`)
- **Vault smears (scan #6).**
  - The triplanar weights come from the facet's real normal (screen derivatives), not the CSG's smoothed normal, and are
    sharper (pow 8).
  - The glaze-streak noise is sampled per projection. A single diagonal x + z sample went flat along walls running across
    that diagonal, which made the long horizontal bands.
  - The stone jambs in those shots are the lodge's porch piers, so they are not changed here.
- **The way down.** The three stacked black cards are gone. A tube now lines the bore. It flares out from inside the cut's
  wall, so it shows no seam, and its packed ice goes grey and then black with depth, closed by black at the end.
- **Trenches.**
  - **Posts:** spaced as they were driven (0.7-1.75 m); some missing; most cut off near the lip, some rotted below it, a
    few tall; leaning in; some split at the top. Snow sits only on the exposed tops, and not on all of them.
  - **Planks:** some bays lose their top boards. The boards are a little out of true, and some lie fallen on the
    duckboards.
  - **Sandbags:** the single 4-3-2 pyramid model is replaced by procedural heaps of 2-7 bags. Each heap sits on the
    surface where it lands, at its own angle, with snow on top, and now and then one bag has tumbled into the trench.
  - This variety draws from its own random sequence, so the crowbar, the crate and the war's leavings lie exactly where
    they did.
- **The crowbar** lies on the duckboards. It was 3 cm above them.

## Winter woods and church glade (`WinterTreeKit.cs`, `WinterWoods.Trees.cs`, `WinterGlade.cs`, `WinterWoods.Props.cs`, `winter_ground.gdshader`)
- **Roots.** The pale, thin, flat spikes are replaced by short, thick flares that dive under the snow within a hand or two.
  The roots are darker than the trunk.
- **Grounding on slopes.** Trees are set at the lowest ground round their foot, not at their centre's height.
- **Tree variety.** There are 16 tree shapes, up from 10 (five more bare broadleaves and a fir). They are built once and
  shared by the glade and the road; before, they were built twice. Each tree also gets its own width and depth (±15%).
- **Trunk drifts** each get their own yaw and stretch, mostly downwind. Before, they all faced one way.
- **Fir icicles** hang from the underside of the tree's own boughs (each fir's tiers are recorded when it is built), with
  varied yaw. Before, they were placed at a random radius and height.
- **The snowplow** sits on the ground under its four corners, pitched and rolled to the road's slope.
- **The lodge sign's** "->" is now a real arrow (→).
- **The open snow's normal map** is blurred, two scales are averaged, and it is laid in shallower, to soften the
  "crumpled bedsheet" creases. The road keeps its crisp normal map.

## Not changed (other owners)
- The flat grey wedge at the church flank is `Church.Shell.cs` (`BaseCourses` and `Drift`).
- The snow pillows along the lodge walls and the lodge's piers in the vault belong to the lodge.

## Verified
- Before and after shots at the scan's poses (Acts 22, 23, 24 vault, maze and trenches, the hole, Act 25), with a
  contact sheet.
- Beats 22, 24a, 24b, 24c, 24d, 25-1 to 25-5 and 25c all pass, with 0 engine errors.
