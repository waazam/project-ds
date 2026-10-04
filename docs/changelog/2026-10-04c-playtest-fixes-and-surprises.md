# 2026-10-04c — The owner's playtest: trees down flat, the shadow man's twelve turns, the crawlspace's posters and lights, a faster, closer wendigo, a green tree, snow with body, the frozen remains and their flies, the pantry's bottles, the church's grey ring

Commit: this entry's commit on `main`.

The owner's notes from playing the last build, and their direction: "we should find a way to make more surprises happen throughout the game for more replay value."

## Act 1: the trees come down flat (`FallingTree.cs`)
- **The problem:** "the trees arent falling all the way down in act 1, they are getting caught by the leaves and branches." They stopped when the trunk was about a metre off the ground, leaving room for the boughs, so they came to rest propped on their own branches.
- **Now:** they come down until the tip lies on the ground. The boughs beneath press into the earth, and where the ground rises under the trunk or crown, the tree presses into the rise rather than being propped up by it.
  - First the upper half decided where a tree came to rest, then the crown. Either way, a rise under the tree still held one tip up, at 3.1 m and then 0.99 m; the autotest caught both. Now the tip alone decides.
- **The autotest** checks that each lies within half a metre of the ground nine tenths of the way to its tip.

## Act 1: the birds fly off (`Bird.cs`, `tools/AudioGen/Forest.cs`)
- **What it was:** "The birds in act 1 need better flight animations and they need to disappear by flying away further behind the trees out of view, right now they just disappear in the air." After its picture, a bird slid two metres up and back in half a second and was deleted in plain sight.
- **Now it flies:**
  - **Open wings:** an arm out to the wrist and long, darker primaries swept back past it. They beat on the shoulders, about 17 times a second getting up and 13 once away.
  - **Getting up:** it springs up and away from the player (turned a little to one side or the other) on a long first burst of beats.
  - **Away:** it goes out through the trees in bounding flight, the way small birds fly: a burst of beats, rising, then a moment with the wings shut tight along the body, dropping. It weaves a little and climbs to about treetop height, up to 9 m/s.
  - **Faces its way**, nose up as it climbs.
  - **Wingbeats you can hear:** a new takeoff sound, `wing_flutter_01..03`, a flurry of soft feathered whirrs slowing and fading as it goes.
- **Gone only out of sight:** it's removed only once it is more than 22 m off and behind a trunk or out of view, or more than 55 m off (lost in the fog), or after 16 s.
- **The black bird** still lunges at the player as before.
- **The autotest** checks that the photographed bird is in the air, and that it goes only far off and out of sight.

## Act 15: the shadow man's turns (`Act15Hallway.Variants.cs`, `ShadowMan.cs`)
The owner: "Can we vary this so it is more interesting ... make 6 - 12 unique variations ... the goal is that a typical playthrough of the game won't trigger every variation unless the player dies a lot."

**How a turn is picked:**
- In the hall's second half, each red light picks one of twelve turns, weighted, never the same twice running.
- The plain inch is the commonest (about a quarter of reds); the strangest are the rarest (one red in twenty-five or thirty).
- The dice are fresh each playthrough.

**Rules every turn keeps:**
- he stays in front of them (or, in one turn, alternately behind);
- he stays short of the door;
- each turn ends with him standing frozen in the hall, to be walked round in the green.

**The twelve turns:**

| Turn | What happens | Weight |
|---|---|---|
| Inch | a jump at a time closer, as before | 26 |
| Far lunges | thirty metres down the hall, then three lunges that cover metres in a blink (17 m, 8 m, then right in front of them) | 12 |
| From the wall | out of the wall beside the way in jerks, turning from facing across the hall to facing them as he comes clear | 11 |
| From the floor | rising out of the floor in front of them, a lurch at a time | 10 |
| Stillness | far off, not moving at all the whole red; then, at its very end, right in front of them | 9 |
| Looming | closer and taller each time, until he stoops over them | 8 |
| Zigzag | from one side of the hall to the other as he comes | 6 |
| Melting eyes | right in front of them (sunk so his eyes are level with theirs; standing his full height that close, his face was off the top of the screen), unmoving, his eyes melting slowly from colour to colour (white, amber, blood red, violet, sickly green) and running down his face; slow, never a flash or a cycle | 6 |
| From above | hanging upside down out of the dark overhead, lowered a jerk at a time to look them in the face; with the green he drops to his feet | 6 |
| Many | three of him down the hall, all coming at once; with the green, only one is left | 5 |
| Slither | face down on the floor, head first, dragged toward them in jerks | 4 |
| Side to side | in front, then behind, then in front, then at their back | 3 |

**The autotest** takes the turns in order and screenshots each it meets. `--act15-turn=N` starts it at turn N, so successive runs can see them all.

## Act 23: the crawlspace (`SkiLodge.CrawlDressing.cs`, `WendigoArm.cs`)
- **Its arm through the wall is slower and deliberate.** The owner: "will look scarier if he is intentionally trying to grab you and not just flailing as fast as he can". Changes:
  - its aim shifts a few times a second, not every few hundredths;
  - it follows at half the rate;
  - its reaches are longer, and its drags back slower;
  - it trembles slower and claws slower.
- **Missing posters and newspaper pages,** pinned to the boards all along the way, each readable:
  - a lodge guest who went out on snowshoes;
  - the hiker R.H., his name torn off and "the steps" pencilled over it;
  - a family whose tent was found standing;
  - a bartender who heard someone calling her name;
  - a hunter whose dog came home alone;
  - the whole winter staff;
  - a scout leader;
  - an old woman whose walking stick was found at the foot of some steps no ranger can find.
  - The papers' headlines include "HIKERS STILL MISSING", "SEARCH CALLED OFF" and the old stories of the hungry thing in the woods.
  - Posters have a red headline and a grey photograph with a face only just made out.
- **Christmas lights** over the ceiling from a third of the way in to the end. The owner, watching the run: "there should be a bunch of different lines of lights on the ceiling in different jumbles for variation, also the lights should really glow and illuminate the crawlspace with their different colors."
  - **Four strands**, joining one after another so the jumble thickens as you go. Each wanders its own way across the boards from staple to staple, so they cross one another. Most sag a little between staples, and about one span in seven droops long.
  - **Bulbs** every few inches in red, green, amber, blue and violet, a few dead. They glow brighter now, and each has a soft halo, which the screen's halation picks up.
  - **The colours on the boards:** a coloured light every cell and a half, going round the colours, so the crawlspace is lit red, then green, then amber as you go. They are steady; none of them blink. Only those within about 15 m are drawn.
  - The autotest now looks along the way and up at the strings for its screenshot, and checks for more than 600 bulbs and at least 12 lights.

## Act 23: the wendigo at the doors (`SkiLodge.Finale.cs`)
- **A quarter faster:** every move and hold from the speech on takes 80% of its time.
- **More intimidating:** after it lands and turns to them, it:
  - rises off its hands to its full height over them with a snarl;
  - comes at them a step, and another;
  - leans its head down into their face, its jaw working, one of its stolen voices close enough to feel.
  - Then it turns to the doors.

## The lodge
- **The Christmas tree is green:** the needle photo is a very dark teal, and faded to olive it read grey. Its needles are now a deep fir green.
- **Snow with body** (`fresh_snow.gdshader`), replacing the flat indoor snow:
  - bright on top, going a cold shadowed blue down its sides;
  - the snow's grain crisp over a coarser crust;
  - here and there a single crystal catching the eye, a different one as you move. It's tied to where the eye is, not the clock, so nothing twinkles on its own.
- **The frozen remains** (`frozen_remains.gdshader`, `tools/Blender/dining.py`):
  - **The skeleton remodelled** with what's left on it: the back muscles shrunk to dark leather along the spine, rags of tissue hanging off the ribs, a mass of it at the hips, half of one thigh still on, the neck's stump torn and shredded, sinew strung across the knees, elbows and wrists.
  - **Its shader:** its bone grimed in blotches, darker underneath, the meat's sheen dulled; a pale frost crust over what faces up, with the odd glinting crystal.
  - **The flat ice sheet under it is gone.** Snow is heaped in it instead.
- **Flies** buzz about it once its sheet is off: a dozen darting and circling over the ribs and hips, with their buzz, a new seamless 20-second loop.
- **The pantry:** more bottles and cans in groups, fewer jars.
  - New models: a huddle of six bottles (long-necked, squat, a milk bottle, one fallen on its side; corks and labels), and a pyramid of nine cans.

## The lodge's room doors: the two dark lines (`LodgeDoor.cs`)
- **What it was:** the owner, watching the run: "there are like two black lines on the screen, you can see them on the wallpaper but they are everywhere the player looks left and right of the middle of the screen."
  - These were the upstairs room doors, which face each other in pairs across the corridor (201 and 204, 202 and 203). Seen straight down the corridor, each pair's casings stood out from the wallpaper as two tall dark posts on either side of the middle of the screen.
  - The casings stood 5 cm proud of the wall.
  - A centimetre's gap each side of the door leaf looked through the wall's hollow to the lit room, so a dotted line of light ran down each post.
- **Now:**
  - The casings are slimmer (10 cm) and stand only 2.5 cm proud.
  - The opening is lined through the wall's thickness, and the leaf fills its opening, so no light shows round it.
  - Each card reader sits on the wall; it had floated 4 cm off it.

## The church: the grey ring round the player (`Weather.cs`)
- **What it was:** the falling snow's far curtain is drawn on rings round the camera, for the open air. The check for "under a roof" was a ray straight up for 40 m, and the church's vault, 36 m up, has no collision for it to hit. So in the nave the rings stood round the player as a grey wall.
- **Now:** the ray reaches 90 m, and indoors by the atmosphere's word (the church, the lodge, underground) counts as under a roof.

## Tests
These three entries (2026-10-04, 04b, 04c) went up together.
- **The full autotest:** 742 of 743. The one failure was the Act 1 trees lying propped (highest tip 3.1 m), fixed as above. Afterwards:
  - **Act 1** alone: see the last line here.
  - **Act 15**, from the melting eyes on: 25 of 25.
  - **Act 23:** 98 of 98, with 34 posters and papers, 1,566 bulbs and 88 lights in the crawlspace.
- **The continue test:** 981 of 981, 36 scenarios.
- **The clip audits:**
  - the trailhead: 0 z-fighting pairs;
  - the forest acts: 40, the known station door frames;
  - the winter: back to its known pairs once the doors' new linings were lifted clear of the floor, the dining doors' linings stopped at their meeting, and the bulbs' sockets left open-ended.
  - No walk-through props.
- **The sign audit:** 0 blocking.
