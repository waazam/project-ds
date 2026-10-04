# 2026-10-04 — Act 23's ending reworked; the ground cleared for the last two acts; the dining hall, the pantry, the doorframes, the crawlspace and the stair runner

Commit: this entry's commit on `main`.

The owner, before giving the last two acts, approved five suggestions:
1. Stop one act's failure cascading through the autotest.
2. Separate the ending from Act 23.
3. Bring the story document up to date.
4. Check the late game's frame rate.
5. Add a final checkpoint.

They added: "the ending to act 23 when the wendigo jumps and busts open the door it looks way too fast and you barely see his character model interact with the door, it should be a longer more intimate scene where he jumps off the top balcony and crashes through the door and you see him fully do all of it."

Then, during the work:
- "some of the doorframes inside the ski lodge are still having some texture clipping issues where they go bright and dark";
- "polish the whole dining room table section, the models underneath the sheets look pretty bad now";
- "in the crawlspace section between the walls, you can see seams of light peaking through throughout";
- the pantry's shelves: "whatever items are on the shelves look horrible";
- the stair carpet: "shouldnt the carpet on the stairs have its left section flipped to mirror the right side better?"

## Act 23's ending: the wendigo, the balcony and the doors (`SkiLodge.Finale.cs`)

### What it was
It stood on the balcony, then covered the distance to the doors in 0.8 s. The doors vanished the instant it arrived, and so did it.

### The scene now (about twelve seconds longer)
- **On the balcony, across the hall.** It now stands on the west-back walk. The west-front one had been hidden behind the Christmas tree from where the player stands.
- **The speech.** It speaks as before: "STARVING..... FREEZING..... FOREVER....".
- **Onto the rail.** It comes to the edge and climbs up onto the log handrail in a crouch. The rail groans under it. It turns toward where it means to land and holds there for a breath.
- **Off the rail.** It comes off the rail at the player in a true throw: steady across, falling faster and faster, over the Christmas tree. The player throws themselves aside, and it lands where they stood, 3.5 m from them:
  - a heavy landing sound;
  - it comes down on its hands, claws in the boards (a new landing animation).
- **The stare.** It turns its whole crouched body round to them, slowly. The copied voices mutter in it, and its jaw works.
- **The first blow.** It turns to the doors, rises, and draws back with its arms cocked (a new door-ram animation). Then the first blow:
  - its shoulder and hands go into the doors;
  - they bow out against the snow and hold, dust and snow shaking off the frame;
  - its claws drag down through the wood with a raking sound.
- **The second blow.** It draws back and strikes again, and they go:
  - the left leaf is slammed open against the jamb, bounces off it and is left hanging crooked off its last hinge;
  - the right leaf is ripped off, tips over outward as it flies, slaps flat on the porch's stone and skids;
  - splinters and snow burst out.
- **Into the storm.** It goes on through the doorway, crosses the porch in long strides, and leaps off it into the storm. A far howl follows.
- **The doorway.** They get up, walk to the splintered doorway and look out, left and right, as before.
- **Following it.** From the speech on, the view follows the wendigo smoothly. The pull is eased, and the turn is capped at a gentle rate (the owner's comfort): no shake, no snap.
- **On a load,** the doors are restored as they were left: one leaf hanging, the other lying on the porch, stumps on the torn leaf's hinges, and boards strewn across the floor.

### New animations and sounds
- **The wendigo's animations** (`tools/Blender/wendigo.py`):
  - **land:** touching down with its arms out for the floor, the weight taken deep, its hands slammed flat with the claws in, its head up on its prey;
  - **smash:** drawn back, shoulder and hands into the door, then its claws dragged down and apart.
- **The posing:** the posing gained a twist of the chest and arm targets relative to the shoulder's real place. The model was rebuilt and re-baked; the head and the arm are unchanged.
- **`Wendigo.cs`:**
  - leaps can be true throws (a parabola), and can land into a clip;
  - clips can be played and held from outside, with blends.
- **`StoryBeat.Follow`:** keeps the view on a moving target, eased and rate-capped.
- **Sounds** (`tools/AudioGen/LodgeDoor.cs`):
  - the rail groaning under its weight (`lodge_rail_groan`);
  - the landing (`wendigo_land`): the body's thump through the joists, the boards groaning, the claws clattering;
  - the blows on the doors (`door_ram`): a deep boom through the leaves, the iron straps rattling, the frame cracking, the doors groaning back against the snow;
  - the second blow's break (`door_ram_burst`), with three cracks and a run of splinters;
  - the claws through the wood (`claw_rake`).

## Ready for the last two acts
- **The autotest no longer cascades.** When an act fails, the next act starts from its own save, the save a run from the start would have left. Previously one flaky slam in Act 18 had failed every act after it.
- **Act 18's test bot:**
  - keeps a little wider of the slams (0.95 m past a circle's edge, was 0.7);
  - picks escapes clearer (1.05 m, was 0.8);
  - is allowed eight tries, not five.
- **The end of the story is its own step:**
  - `GameEnding` holds the save that says the story is done, the fade, the credits and the menu;
  - the last act calls it once its own end is saved (for now Act 23, through `GameEnding.LastAct`);
  - the autotest checks it as its own step, "The end: the credits".
- **A final checkpoint, `GameFinished`:**
  - It is held at 1000, far above the acts' numbers. The checkpoints Acts 24 and 25 add will number in before it, so every "at least this far" test stays in story order.
  - Continue after the end puts the player back where the story ended, at the splintered doorway.
  - The continue test has two new scenarios: Act 23's end, and the game finished.
- **`docs/STORY.md`, revision 3:**
  - Act 1's way to the stairs is now the falling trees.
  - Act 11 ends with the giant rising out of the woods and grabbing the player, who wakes at the lake.
  - Acts 12 to 23 are written up as built.
  - Acts 24 and 25 are placeholders, followed by *The end*.

## The dining hall and the pantry (`tools/Blender/dining.py`, `SkiLodge.Dining.cs`)
Everything here is modelled in Blender with baked cavity maps (1024 px), in the lodge's own materials.

- **The tables:**
  - a top of planks with breadboard ends and pegs, and a moulded edge;
  - a beaded apron, four heavy turned legs, and an H-stretcher with a long rail.
- **The collapsing table:** it breaks into modelled halves with a jagged split and splinters, and its turned legs kick out. On a load it lies the same way, on its apron.
- **The first sheet:** a place laid at every seat and left for weeks, alternating two settings:
  - a rotted joint on its bone, furred with mould;
  - soup dried to a skin in a soup plate, a crust gone green, a wine glass on its side and the spill.
  - Each setting has a rimmed plate, a knife, fork and spoon, a wine glass with the dregs dried in it, and a crumpled napkin.
  - Two silver candelabra burnt down to stubs with their wax run down them, and a picked carcass of the roast on a serving dish.
- **The third sheet:** a whole skeleton laid on its back, skull gone and neck snapped:
  - vertebra by vertebra, with the ribs half fallen in, the breastbone, collarbones and shoulder blades;
  - the arms with radius and ulna and splayed hands;
  - the pelvis, the femurs, the kneecaps, tibia and fibula, and the feet fallen open.
  - It has snow caught in it and frost glazed over it.
- **The fourth sheet:** an oval silver platter with a gadrooned rim, a chased border and handles. Its cloche cover has been lifted off and lies tipped beside it.
- **The sixth sheet:** a footed porcelain bowl with a silver band. The snow and the blood now fit inside it.
- **The pantry's shelves:**
  - rows of preserving jars behind see-through glass, holding dark fruit and pickles in brine, with zinc bands and labels;
  - ribbed tins, some stacked and one tipped over;
  - stoneware crocks, and a bottle here and there;
  - iron brackets under the shelves;
  - burlap sacks slumped along the floor under them, tied with twine.

## Act 1: the fog thickens toward the stairs (`ForestAtmosphere.cs`)
The owner: "in act 1 we should make the fog more dense the closer you get to the staircase, it needs like a 35% increase."
- **Before:** the fog closed in along the trail to the fallen fir and then held there all the way to the stairs.
- **Now:** past the fir it keeps closing in, from 60 m out to the staircase's foot.
  - At the foot it is 35% denser: its depth distances are drawn in by 1.35, so you see about 24 m where you saw 32.
  - It is eased in the same way as the trail's own fog.
- **The autotest** checks that the fog by the stairs is thicker than at the fir.

## Fixes
- **The doorframes flickering light and dark** (`LodgeDoor.cs`):
  - **The casing pieces overlapped flush:** the jambs ran up into the head. Now the jambs' tops tuck inside a slightly prouder head.
  - **The casing was sized for the 14 cm partitions.** In the lobby's 0.8 m walls (the pantry's door, the dining room's double doors) it sat buried in the wall. It showed only inside the opening, where it lay flush with the soffit and reveals. Each door now knows its wall's thickness, and the casing stands just proud of both faces, a few millimetres clear of the opening's edges.
  - **The dining room's two doors** overlapped each other's casings. They are now a pair: no jamb where they meet, and the heads join.
  - **The clip audit:** every casing z-fight is gone from the winter audit.
- **The crawlspace's light seams:** the thin bright strips of "light between the boards" read as gaps in the walls, and broke up into dashes at a distance. They are gone.
- **The stair runner:** the carpet's two rows of arches put a black line down its middle and only one at an edge. Each tread's runner is now two halves, one mirrored, so both black lines run down the outsides.

## Tests
See [2026-10-04c](2026-10-04c-playtest-fixes-and-surprises.md#tests); all three went up together.
