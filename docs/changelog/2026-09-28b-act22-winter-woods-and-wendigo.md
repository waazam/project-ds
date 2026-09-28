# 2026-09-28b — Act 22: the winter woods, the wendigo, the ski lodge

Commit: this entry's commit on `main`.

The owner's "Real Act 22" document, their wendigo, winter forest and ski lodge references, and: make the
wintery forest feel isolated and creepy; stylize the wendigo and make it ours; use Poly Haven for the snow;
keep the PS2 horror style.

## Story beats
- **Act 21 now ends by leaving the church through its entrance** (the owner: through the puzzle door once
  the puzzle is done; the church will be refined later).
  - The chalice goes into the great door's first niche.
  - The gold shapes in the other three niches take its light one by one, the bars inside draw back, and
    the two leaves swing in on the snow.
  - That's Act 21's save; the credits no longer play here.
  - The great door is now two hinged leaves (it was one mesh); the chalice rides on the left one.
- **The compass dies on the stairwell** (Act 14) and stays dead through the church.
  - The strip drifts and hunts on its own, and now and then swings round and settles somewhere new, as
    if something else were pulling it.
  - Turning does nothing to it, and it points at nothing.
- **Act 22: out of the great door, the compass lives again.** A plowed road snakes 1.34 km through the
  winter woods (a five-minute run, seven or eight minutes' walk), and the compass points down it to the
  ski lodge.
- **The lodge's front doors are chained shut from inside.** Trying them turns the compass round to the
  back.
- **The back door stands open a crack, drifted up and iced into its frame.** "Force the door" plays a
  short cutscene:
  - three shoves (a small lunge in and back: gentle, for the owner's headaches);
  - the ice cracking louder each time, and chips bursting off it;
  - the third splits the ice off the frame, the icicles over the door drop one after another, and the
    door swings in on the dark.
  - The player steps into the mudroom: skis still in the rack, boots under the bench, an inner door
    (Act 23).
  - That's **Act 22's save** (a new checkpoint, `Act22Finished`). Then the fade, and the credits: the
    demo's end, for now.

## The winter woods
- **One ground from the church's walls to the lodge.**
  - The clearing round the church and the new woods are one height field.
  - A coarse 4 m grid in 64 m chunks, each with its own collision, and faded out past the murk.
- **The plowed road:** a ribbon laid along the centreline, every 1.5 m a cross-section with two tyre
  ruts in the packed snow, a hard edge, the windrow the blade threw up either side, then the deep snow.
  - Its own collision.
  - The grid ducks under it so the ruts and banks are exact.
  - It starts at the great door's threshold (a first version began 3 m out and left a step nobody
    could climb).
- **Trees, about 4,700:**
  - snow-laden firs, grey snags and black bare broadleaves;
  - thick right up to the windrows, and on into the dark;
  - MultiMeshes in 96 m chunks that fade out past the murk;
  - a trunk collider on every tree within reach.
- **Colder toward the lodge:**
  - the snowfall thins and stops;
  - the ground glazes to a frozen crust (and the road to ice);
  - the trees are caked in a glassy ice shell, hung with icicles;
  - a few ice crystals hang in the still air, glinting faintly (slow: never a flicker);
  - the wind dies to a frozen hush, and icicles knock together now and then.
- **The light: a dusk under a low snow sky.** A dark blue-grey murk that closes in at about a hundred
  metres, with the sky's warm glow swallowed too.
  - It gets colder and clearer blue by the lodge.
  - The snow is toned down to a cold grey-blue, so a field of it never glares (the owner finds bright
    white scenes painful).
- **Snowfall:** follows the player, never over the church (its own sheets fall round that).
- **Sound:**
  - the summer forest's sounds are silenced out here;
  - a thin winter wind on the weather bus instead.
- **Keeping to the road:** past ~95 m from the road the player is gently pushed back (the woods go on
  forever in the dark; the ground doesn't).

## The snow textures (Poly Haven, CC0)
- `tools/Textures/make_snow.py` (new) makes three 256 px ground textures from Poly Haven's snow sets,
  toned to a cold blue-grey with a brightness ceiling (no glare):
  - fresh snow (`snow_03`);
  - the plowed road (`asphalt_snow`: packed snow worn to dark grit in the ruts);
  - the frozen crust near the lodge (`snow_floor`).
- `winter_ground.gdshader` (new) blends them per vertex:
  - road and frozen weights;
  - two scales of the snow so its tiling never lines up;
  - a faint sheen only where it has frozen.
- Two new micro-detail kinds (`snow`, `ice`) for DetailKit, from `snow_03` and `snow_floor`.

## What there is to photograph (nine new pictures: the list is now 69)
- **A snowman** built facing back up the road at the church: seven coal eyes down one side of its face,
  a grin of real teeth, a twig hand with too many fingers.
- **The snowplow** that cut the road, pulled into the windrow, its cab door hanging open, nobody in it.
- **A dead tree hung with antlers** and jawbones on twine.
- **Ski tracks** that leave the road and run straight into the trees, and stop dead at a trunk, the poles
  planted there in an X.
- **A whole set of winter clothes** laid out empty on the snow in the shape of a person.
- **A deer frozen where it stood**, glazed in ice, one foreleg lifted.
- **The lodge's sign.**
- **The lodge.**
- **The wendigo** (a monster picture: good luck).

## The wendigo (new: `Wendigo.cs`, stalking in `WinterWoods.Stalking.cs`)
- **Read up on first** (the Wikipedia article, as the document asked). What we took from the Algonquian
  accounts:
  - a giant, and yet starved: ash-grey skin drawn tight over the bones;
  - sunken eyes, torn lips;
  - a heart of ice;
  - it keeps human speech, and taunts.
- **The model, ours, after the owner's references:**
  - About 3.6 m, hunched.
  - Lofted limbs and trunk (ellipse rings along a path, knobbed joints, wasted muscle), ash grey,
    bruised, going black-blue with frostbite toward the ends.
  - Every rib standing out, with a gap torn in them over a shard of blue ice that glows faintly where
    the heart should be.
  - A row of knuckles down the spine.
  - Arms far too long, black hands with long three-jointed fingers hanging past the knees.
  - Legs that bend backward like a deer's, to long black toes.
  - A shaggy black mantle over the shoulders and down the back, hanging in ragged locks whitened with
    frost at the ends.
  - **Its head is an elk's skull:**
    - bleached and long, the sockets on its sides, a pinprick of cold light deep in each;
    - a man's jaw hung loose under it, with long uneven teeth and torn lips in strips;
    - a great rack of antlers, the left snapped off short, strips of dead velvet hanging from the
      tines.
  - The skull and antlers carry a faint pallor so they show in the murk.
  - The head hangs to one side and snaps to new angles.
  - Frost-breath from the snout.
- **In Act 22 it never touches them** (the document).
  - **The first time:** it's standing in the middle of the road ahead, at the edge of the murk. The
    moment they look at it, it's gone.
  - **After that:** it takes its place behind them, beside a trunk 12-28 m back, facing them.
    - Look anywhere near it (38 degrees, with a clear line) and a few frames later it leaps: a crouch
      of a few hundredths of a second, then up into the crown along an arc in 0.17 s, stretched with
      the speed, arms thrown up.
    - The bough dumps its snow, a thud, a creak.
    - If they never turn, it slips away and comes back later, closer.
  - **Voices:** from the trees, off to one side or behind, it speaks in copied human voices, and they
    come out wrong. Fourteen lines, made by `tools/WinterAudio/make_winter_audio.py`:
    - plain lines ("hello?", "is someone there?", "help me", "wait for me", "over here", "I'm so cold",
      "come back", "don't leave me out here", "I can see you", "it's warm in here", "are you hungry?",
      "turn around");
    - and the game's own recorded "come and see" and "turn around";
    - each pitched down (the women's lines lower still), a grain of it caught and repeated like a skip,
      the end dragged slow, a dry rasp breathing under it, a faint ring on it, a reversed ghost of the
      last word after it, dulled by distance.
  - **Its steps:** barely there. A few soft presses into the snow behind them while they walk, stopping
    when they stop.
  - New sounds, all made in the same script: the leap, snow sliding off a bough, the ice splitting off
    the door, icicles knocking.

## The ski lodge (new: `SkiLodge.cs`), "HOLLOW PEAK LODGE"
- **A great hexagonal hall** at its heart:
  - stone to the sills, dark board-and-batten above;
  - a steep six-sided roof deep in snow up to a windowed lantern;
  - an iron spire and weathervane;
  - a great stone chimney.
- **Two long wings:**
  - steep snow roofs, gables, three dormers each, stone chimneys;
  - rows of small-paned black windows with snow on the sills.
- **The front:** a stone porch with two squat piers, the name carved over it, and the great doors
  chained through their handles.
- **Winter on it:**
  - icicles all along every eave (a few hundred);
  - snow drifted against the walls, shoulder-deep either side of the back door;
  - one window over the doors lit dimly, as if by a candle: the only light at the road's end.

## Bug fixes found on the way
- **The walker stuck in the church's doorway.** The road began 3 m outside, leaving a step. Fixed (above).
- **The wendigo's director read a stale cutscene count.** After a test's save-skip the count was left
  at 2, and the wendigo never showed. It now asks whether the player has control instead.
- **Tests:** `--story-from` now reaches Act 22.

## Tests
- New story-test act, "Act 22: the winter woods". It checks:
  - the woods, the road and the lodge exist;
  - Act 22 starts at Act 21's save, out of the open door onto the road;
  - the compass lives again and points at the lodge;
  - dusk, and snow falling;
  - the road's first 150 m on foot;
  - the wendigo on the road ahead: looked at, it leaps into the trees;
  - behind by a tree, and it stays while they look away: turn round and it's up the tree;
  - it never lays a hand on them;
  - a copied voice from the trees;
  - a picture of the snowman, and a look at every other oddity on the road;
  - frozen by the lodge (no snow, ice, the frost);
  - the chained front doors, and the compass turning to the back;
  - round the west wing to the iced-in door, three shoves, inside, the save;
  - the credits.
  - Act 22 alone: 25/25.
- Act 14 now checks the compass has died, and Act 21 that it is still dead in the church.
- The continue test:
  - a new scenario, Act 22's end, which respawns in the mudroom with the back door open;
  - Act 21's end now respawns outside the great door, standing open, on the road.
- The creature preview has a `--wendigo` mode (front, side, back, three-quarter, the head close).

## Test results
- Acts 19-22: 103/103, no engine errors. Act 22 walks at an average of 142 fps.
- Continue test: 729/729 (27 scenarios, the new Act 22 one included).
- The full run failed at Act 18's boss fight, and everything after it failed with it.
  - The test bot was crushed on all five tries (the valve order is random each try).
  - Act 18 alone passed twice (crushed three and five times first, then won), and earlier full runs
    passed it too.
  - So it's the bot's dodging that is chancy, not the fight. The bot could be made steadier.
- The wendigo seen close in the creature preview (`--wendigo`).
