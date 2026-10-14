# 2026-10-13h — Joints and gaps: parts that meet

The owner: "lets try and fix any objects that have disconnects or gaps between joints. this includes objects and
characters". Every modelled piece, the creatures in their poses, and the levels as the playtest leaves them were
measured for parts that should meet and don't; the ones a player can see were fixed at their source (the Blender
script or the builder), keeping every silhouette and the recent changes (the clawed hands, the shoulders without
spikes, the player's body, the lantern fist).

## What was wrong, and the fix

| Item | Where | The gap | Fix |
|---|---|---|---|
| The stalker's toes | every stalker and the giant, at the feet | toes and foot modelled end to end at the ball; the remesh's smoothing pulled the two caps apart: a 5 mm slot that opened as the toes bent (it read as a separate toe block) | each toe rooted 3 cm back inside the foot (`stalker.py` `build_foot`) |
| The stalker's back shards | down its spine, in silhouette | up to 3 cm of air between each bark shard's root and the hump | each sunk into the hide along its normal (`sink()` in `stalker.py`) |
| The stalker's shoulder knots | both shoulders | the knotted root masses up to 8 mm off the shoulder | the same, sunk 1 cm into the hide |
| The wendigo's fangs | the upper jaw, cheek to mid-snout | ten fangs hanging 4-15 mm under the snout's underside | rooted 3.5 cm up into the snout (`wendigo.py`) |
| The wendigo's lower teeth | the man's jaw | several stood 6-8 mm proud of the jaw's bar | roots sunk 1.5 cm into the bar |
| The wendigo's neck | the back of the skull | the neck ended at the head's joint in a blunt cut; with the head hung forward the stump stood out bare, 2-3 cm clear of the skull | the neck carried on up into the braincase |
| The wendigo's jaw | under the skull | the jaw's hinge ends stopped 2-3 cm under the skull: the whole jaw hung in the air | its ends carried up into the skull's sides |
| The wendigo's antlers | the skull's crown | the big antler's burr 6 mm off the bone | set 2 cm lower |
| Station desk | Act 13, the station | both pedestals stood 2 cm over their plinths | each pedestal runs from the plinth to the top (`interiors.py`) |
| Footlocker | the bunker, Act 24 | its side handles 7 mm off the box | the handles meet the box on two iron brackets each |
| Rifle | Act 24's remains | a 6 mm slot through the stock at the wrist | the fore-end begins inside the butt (`act24.py`) |
| Bed | the lodge's rooms | the pillows' puffed bellies 18 mm over the mattress | 2 cm lower (`furniture.py`) |
| Ottoman | the lodge's lobby | its bun feet ended in points; the body curved away 16 mm above them | a dowel from each foot up into the body (`christmas.py`) |
| Wreckage | Act 14, the stairwell's bottom | the fallen treads and beams set at random heights and tumbled: 2-23 cm off the floor | each let down onto the floor, its lowest corner just in it (`Stairwell.Bottom.cs`) |
| The cigar box's lid | Act 13, Room 1, after a Continue | the open lid was lifted 16 cm and floated over the box | open on its hinge, as the opening leaves it (`StationRoom1.cs`) |
| The front doors' chain and padlock | Act 23, inside the lodge | 7 cm bars with 2 cm of air between each, the padlock 12 cm under the chain | links along the sag, each into the next; the padlock on a shackle through the lowest (`SkiLodge.Finale.cs`) |
| The porch doors' chain and padlock | the lodge's porch | the same: 1.5 cm between links, the padlock 9 cm below | the same (`SkiLodge.cs`) |
| Bare bulbs | the lodge's service corridor and pantry | hung 10-15 cm under the ceiling on nothing | a brass socket and a flex to a ceiling rose (`SkiLodge.Furnish.cs`) |
| Room 203's icicles | the open window | "icicles off the curtain rail": there was no rail; they hung 4-7 cm under nothing | an iron rail on two brackets |
| Vanity mirrors | rooms 201-204's bathrooms | 1 cm off the wall | flat on the wall |
| The bar's ceiling lights | the bar | 1 cm under the ceiling | up against it |
| Room 203's plaster rubble | the hole into 204 | 8 mm over the tile | on the tile |
| Room 201's snapped laths | the hole in 201's bathroom | 2 cm proud of the wall, the short ones beginning in the air | in the wall's thickness, from 3 cm inside the broken edge (`SkiLodge.Room201.cs`) |
| The banner's nail | the lobby, "MERRY CHRISTMAS" | its one nail began 5 cm off the wall | driven into the wall (`SkiLodge.Christmas.cs`) |
| The round table's glasses | the lobby | the glasses, bottle, mug and hat 1 cm over the cloth | on it |
| Crawlspace pipes | Act 23, under the lodge | ran 6 cm clear of the wall on nothing | a strap off the wall under them at each panel (`SkiLodge.Crawlspace.cs`) |

Shots: `test-output/joints-before-after/` (the stalker's foot and toes, the wendigo's neck and jaw, before and after)
and `test-output/gaps/lodge_fix_*.png`.

## Found and left
- **Act 24's snow maze** (another pass is on the snow tunnels): snow lumps in the trenches 2-13 cm over the floor, and
  the hole's icicles hanging 5-15 cm under its edge (`Act24Maze/SnowMaze/TrenchSnow`, `Act24Maze/Hole/Icicles`).
- **The lake and the rowboat**: not touched (another pass).
- **The first-person arms and the lantern grip**: not touched (another pass). The player's body seen looking down
  (jacket, belt, jeans, boots) showed no gaps.
- Small and loose by nature, or under a centimetre: the lodge's random room clutter can stand up to 2 cm over a
  table (it is placed by ray against the furniture's collision boxes, which run a little above the modelled tops);
  holly sprigs in the crawlspace 5-14 mm over the boards; the stocking's hanging loop, the wreath's bows and berries,
  the Christmas tree's baubles off their needles; the deer's pale belly, throat and rump laid 6-9 mm over its hide;
  the sandbags (separate bags on the ground); the bed's headboard buttons (one 1 cm proud); the chesterfield's seat
  cushion (7 mm); keys on their hooks and the wardrobes' knobs (5 mm); the headless skeleton's bones (scattered, as
  bones are); the station basement's papers (they float on the flood, on purpose).
- `assets/models/creatures/stalker.glb` (the procedural stalker's remodelled parts, only used when the rigged model is
  missing) has 23 small floating pieces; left.

## Tools
- **`GapAudit.cs`**: splits every visible mesh under a node into its islands (skinned meshes baked at their current
  pose, so a gap that opens mid-stride shows) and lists each that touches nothing but has something within 25 cm, to
  `test-output/gaps/`. `Precise` also measures corner to corner (the gaps a box test can't see).
- **`gap_audit.tscn`** (`GapAuditPreview.cs`): `-- --models` (every .glb alone), `--creatures` (the stalker walking,
  shoving, looming, peeking; the crawler walking; the wendigo standing, leaping, crouched, climbing; the doe), `--lodge`
  (and close shots of its fixes), `--joints` (every joint of the stalker, the crawler and the wendigo up close in plain
  clay, in two poses each), `--precise`, `--shots=N`, `--sheet=<prefix>` (contact sheets of the shots).
- **`-- --playtest --gap-audit`**: each beat's screenshot scans the level as it stands then.
- **`tools/Blender/gap_check.py`**: the model check in Blender, corner to surface, inside or out (a fang sunk in its
  jaw counts as joined): `blender -b --python tools/Blender/gap_check.py -- <glb ...>`.

## Re-exported
`stalker.py` → `stalker/stalker.glb`; `wendigo.py` → `wendigo/wendigo.glb` and `wendigo_head.glb` (the crawlspace arm
re-exported identical and was left as it was); `interiors.py` → `station_desk`, `footlocker`; `act24.py` → `rifle`;
`furniture.py` → `bed_17`; `christmas.py` → `ottoman`. Every other part of those models is as it was: the random
draws were kept in their order.

## Tests
- `--playtest-only=13a,13b,14,23a,23b,23c,23d,23e` (and 3, 4, 5, 8, 15, 17, 19, 20, 21, 24a with the audit): all pass.
- `gap_check.py` on every re-exported model: no floating groups (the wendigo was three groups, its head apart; now one).
- `dotnet build`: 0 errors.
