# 2026-10-01b — Church, station and bunker pieces; footprints in the snow; the wendigo's new moves; the round room's web; calm tablecloths; Christmas in the lodge

Commit: this entry's commit on `main`.

The owner:
- "do the church, station and bunker interiors next" (after recommendations, all approved: "yes do all of
  those");
- "if there is anyway to have interactable snow on the ground so the player leaves footsteps behind that would
  be great to show wendigo footprints".
- While playing (Act 20):
  - the round room's webs had a red speckle and the room felt buggy and laggy;
  - riding the dais up, the view walked and bobbed as if the player were walking;
  - the web looked like a geometric shape and should be all over the floor, so it burns all round the room.
- The tablecloths:
  - they should all use the Quatrefoil Jacquard fabric the owner supplied;
  - they spasmed and could end up across the room, and should come off slowly and fall naturally to the floor
    beside their tables.
- "the ski lodge lobby just has 1 couch in it": more furniture, and Christmas. A tree and decorations, as if a
  party happened years ago and the remnants are still around (with reference photos).

## The church, the station and the bunker (`tools/Blender/interiors.py`, as in the fidelity pass)
Each piece is modelled in Blender by script, baked with a cavity map, and given the room's own materials
through `FurnitureKit`. Where a piece is missing, the old built-in shape is drawn instead.
- **The church:**
  - **The pews:**
    - carved bench ends with a poppyhead finial and pierced quatrefoils;
    - raked back boards and a book ledge with hymnals on it;
    - a red kneeler on its rail.
  - **The vestry:**
    - books, a stack of papers and a row of books on its table;
    - a crate by the door.
- **The station (Act 13):**
  - **The front desk:** a ranger's pedestal desk: a moulded top with a leather writing inset, two pedestals of
    drawers with brass cup pulls and label frames, a raised-panel modesty panel, a plinth. A mug and papers sit on
    it.
  - **The lobby:**
    - the dining chairs are the modelled ones;
    - a coat stand with a ranger's jacket and a campaign hat hung on it.
- **The bunker (Acts 8–10):**
  - **The CRT room:**
    - modelled filing cabinets, one with a drawer pulled out and its hanging files showing;
    - the toppled office chair (five legs on casters, a padded seat and back on a steel spine, padded arms);
    - the console's mug.
  - **The first room's leftovers:** stacked supply crates and a footlocker (steel-bound, with a hasp and padlock), with collision.

## Footprints in the snow (`SnowPrints.cs`)
- **The player's:**
  - every step on snow outside lays a boot print where the foot came down, left then right, along the way they
    walk;
  - prints are deeper and darker in the deep snow off the road;
  - the newest 480 prints stay, and the oldest fade out before they're reused, so nothing pops.
- **The wendigo's:**
  - a long narrow pad with three long toes splayed forward, each ending in a claw's gouge;
  - laid wherever it stands, where it pushes off into the trees (driven deep), and where it lands;
  - four lines of its tracks cross the road on the way to the lodge, out of the trees, over one wall and up the
    other: it was here first.
- **How they're drawn:**
  - each print is a small card on the snow's surface, multiplied over the snow, so it darkens the snow into a
    hollow and the snow's own grain and light show through;
  - a cold blue-grey hollow, never black;
  - the side facing the toe is a little lighter, as if it catches the light;
  - each is drawn from its shape, blurred soft at the edge.
  - The first attempts were pale, outlined cards that read as stickers lying on the snow.
- **Cost:** two multimeshes (one draw each).

## The wendigo's new moves (`WinterWoods.Stalking.cs`, `Wendigo.cs`)
Behind the player it now takes turns between four moves. A move that can't be done at that spot falls through to
the next. It still never touches them, and it still goes the moment it's looked at.
- **By a tree:** standing beside a trunk (as before).
- **On a plowed wall:** crouched low on the wall's top behind them, looking down (the crouch pose, held).
- **Dropping onto the road:**
  - it springs off a wall's top behind them and drops into the middle of the road, 12–20 m back (the pounce
    clip);
  - the landing is loud: a whump and a burst of snow;
  - it rises out of its crouch there and waits to be seen;
  - its claws mark where it pushed off and where it landed.
- **Mid-climb:** caught halfway up a bare trunk, clinging to it with its back to them, 2.4–3.4 m up the bare trunk, below the boughs (higher,
  among the branches, it couldn't be made out at all), 10–15 m back (further off, against the trees, the haze swallows it whole; by a
  tree or on a wall it stands out dark against the sky). Seen, it goes
  on up into the crown.

## A save halfway down the road (Act 22)
- **What changed:** passing the road's midpoint saves, and Continue comes back there, on the road, rather than
  at the church door.
- **How it's done:** the save is a flag on Act 22's checkpoint, with its own respawn spot. It isn't a new
  checkpoint because checkpoints are compared by their order all through the game, and a new one couldn't go
  between Act 22 and Act 23.

## Sound (`tools/WinterAudio/make_detail_audio.py`)
- **Deep snow:** off the plowed road, each step is a muffled, packed crunch with the leg dragged out after it,
  a little quieter.
- **The wind over the walls:**
  - wind whistling over the plowed walls' crests, from a source on each wall's top beside the player that
    follows them down the road;
  - it is as loud as the walls are high, and dies away in the still, frozen stretch by the lodge;
  - it is a 12-second seamless loop: a breathy hiss with a thin, wandering whistle that rises in the gusts.
- **The grandfather clock:** the lobby's clock ticks, a slow tick-tock with its case's wooden knock. When the
  lodge freezes over, it stops.
- **The bar:**
  - now and then, a glass on the back bar's shelves touches the next with nobody there;
  - it's quiet, and only heard near the bar.

## Things to pick up and look at (`Curio.cs`)
- **What it is:** some of the lobby's clutter can be picked up with E: the whiskey on the low table, the books
  beside it, the mug by the wing chair, and the trail guides on the mantel.
- **What happens:** each makes its sound (glass, pages, china) and gives a line of what they see. Pressing
  again gives a second line.
- **What it doesn't do:** nothing is taken.

## Settings and the debug readout
- **Brightness** (the settings, main menu and pause menu):
  - a gamma over the finished picture, from 70% to 150%;
  - it lifts the darks and the mids, not the whites, so a dark screen can see into the dark without the
    brights glaring.
- **F3 (dev builds) per-room cost:**
  - the frame's triangles, draw calls, objects, video memory, and the lights switched on within 40 m;
  - it's for checking each room as more detail goes in.

## The round room (Act 20)
- **The red speckle:**
  - the cause was the ring's red light, inside the dais, lighting the fine strands from below;
  - the web textures had no mipmaps, so at a distance the strands broke up into sparkling red dots;
  - the textures now have mipmaps (smoother and cheaper to draw), and the webs are on their own render layer,
    which the ring's light leaves out.
- **The web, all over the floor:**
  - the tent drawn up over the dais is gone;
  - old web now lies everywhere: a grey film across the floor from the dais out to the walls, banked up where the
    floor meets the wall, hanging on the lower walls (clear of the way in and the switches);
  - the dais is still thick with it: sheets slung low between its posts, a mat over the top, strands down to the
    floor;
  - every card is its own photographed web.
- **The burn** (`web_burn.gdshader`):
  - it catches where the lighter touches and spreads out in every direction, over the dais, across the floor and
    up the walls, over eight seconds;
  - a ragged ember-orange edge eats the web as it goes;
  - a few low fires ride the front outward and gutter out at the wall;
  - it is slow and steady, with no flash and no flicker;
  - the fires' smoke and embers are off (the smoke caught the red light as a red cloud, and the embers drew as
    square specks).
- **The lag:**
  - the room measures 179 fps facing the webbed dais, with a worst frame of 23 ms (a new test check);
  - nothing in the room itself was costly; the first measurements were wrong, because the test's frame helper
    waits on physics ticks (60 a second);
  - the mipmapped webs are cheaper to draw at any distance.
- **The ride up:**
  - riding the dais (or any scripted glide), the footsteps and the walking bob carried on;
  - the cause: the body is frozen for a cutscene with the speed it last had, and both go by that speed;
  - freezing the body now stands it still (`Cutscene.Lock`);
  - the test checks that no footsteps play on the way up.

## The tablecloths (`VerletCloth.cs`, the owner's jacquard)
- **The cloth:** every tablecloth is the owner's Quatrefoil Jacquard Fabric (Poly Haven, CC0):
  - a crimson jacquard with its quatrefoil figure in the weave's relief, a metre a repeat (1024 px, mipmapped);
  - this covers the dining hall's six cloths (Act 23), the library's (Act 19), the church's altar cloth, and the
    lobby's party tables.
- **The motion:**
  - Jolt's soft-body cloth is gone;
  - each cloth is now our own small simulation: a grid of points joined along, across, diagonally and with a soft
    bend, stepped several times a frame and heavily damped;
  - it touches only the table's block (where it slides) and the floor (where it stops);
  - no point may move more than 3 cm a step, so it can never be flung;
  - once it has lain still a moment it stops for good.
- **The pull:** a handful of the near edge is lifted, drawn slowly back over the near edge and down to the floor
  beside the table (2.6 s, eased), then let go from the ends in; the rest slides off after it and settles there.
- **The test:** it checks that every dining-hall cloth comes to rest on the floor beside its own table.
- **When the fifth table collapses:** its cloth comes down with it.
- **The cost, and a bug found on the way:**
  - a cloth costs nothing until it's pulled, and nothing once it has settled;
  - the first build had every cloth in the game simulating from the moment it loaded. Godot turns physics
    processing back on for any script that has a physics step as it enters the tree, whatever was set before;
  - that made a 60 ms physics tick, and everything ran in slow motion after a load;
  - the full test run caught it: after Act 13's drowning the player crawled, and Room 2's door never slammed;
  - a cloth now stays asleep until it's pulled.

## Christmas in the lodge (`tools/Blender/christmas.py`, `SkiLodge.Christmas.cs`)
A party happened years ago and nobody cleared it away.
- **The tree:**
  - a 6.2 m fir in the open hall, full to the floor;
  - its needles dried a dull olive (the forest firs' spray photo on every bough), shed in a carpet round its foot;
  - old glass baubles all over it, a tinsel garland spiralling down;
  - a string of lights almost all dead, with the few still alight glowing steadily (nothing blinks);
  - the star knocked askew, a felt skirt, presents under it (one torn open).
- **The decorations:**
  - garland swags along the gallery's railing, three to a side;
  - a garland and four stockings on the mantel;
  - wreaths either side of the front doors;
  - a MERRY CHRISTMAS banner over them, come loose at one end and hanging from its one nail.
- **The leavings:**
  - the buffet along the front wall under its cloth: the punch bowl dry with its ladle, the cake grey on its stand
    with a slice out, plates, empty bottles (one on its side), coupes knocked over, the candelabrum burnt down,
    crumpled napkins;
  - a round table in the middle of the hall under a cloth, glasses and a bottle left on it, chairs pushed back, one
    knocked over;
  - party hats, broken baubles and dropped tinsel on the floor.
- **More furniture:**
  - a second seating group on the west side (clear of the gallery's corner post, which the clip audit found the
    chesterfield running into at first): a chesterfield facing in, two leather club chairs across a tufted
    velvet ottoman, a rug, a floor lamp;
  - a chess table by the east wall with a game left half played, a club chair either side;
  - the new pieces are all modelled (club chair, ottoman, chess table) and have collision.

## Tests
- **Acts 19, 20, 22 and 23 on their own:**
  - Act 19: 23/23;
  - Act 20: 30/30 (with the new checks: the burn spreads out to the walls, no footsteps riding the dais up, the round
    room runs smoothly);
  - Act 22: 40/40 (the prints, the wendigo's moves, the halfway save, the deep-snow steps, the wind);
  - Act 23: 88/88 (the cloths come to rest beside their tables, the clock, the whiskey).
- **The full autotest:** 719/719.
- **The continue test:** 925/925, 34 scenarios (the halfway save is a new scenario).
- **The clip audit:** no new faults from the Christmas pieces once the west seating group was moved off the
  gallery's corner post.
- **These land with the 2026-10-02 entry,** in the same commit.
