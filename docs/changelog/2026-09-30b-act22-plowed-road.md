# 2026-09-30b — Act 22: the plowed walls, deep snow, winter firs, and the photo subjects remodelled

Commit: this entry's commit on `main`.

The owner:
- **The winter fir:** yes, make the winter fir too.
- **The road:** give it much higher plowed walls of snow, like the reference photos; and make them lumpy, clumpy
  and layered, not straight and smooth.
- **Deep snow:** wandering off the road should slow the player down, so they want to stay on it; but not round
  the lodge, which should count as plowed.
- **The props:** were too high up after the road changed.
- **The photo subjects:** far too low in detail; use Blender (4.0 is installed).
- **The clothes on the road:** put the "man" lying face down into the plowed wall instead, with just his arm
  sticking out, and make the arm realistic, not blocky.

## The plowed walls (`WinterWoods.Path.cs`, `WinterWoods.cs`, `winter_ground.gdshader`)
- **Their height:** the road runs between near-sheer walls of snow 1.6–3.6 m high, varying along each side. They
  rise out of the church's yard and sink into the lodge's shelf. They drop low only where something stands out
  in the snow beyond them (the antler tree, the ski tracks), so it can be seen and waded to.
- **Their shape:** plowed a while ago and slumped:
  - a long low toe sloping up off the road's edge;
  - steepening to a near-sheer upper face;
  - a rounded, lumpy lip.

  The faces bulge softly in and out. Clumps of packed snow are heaped along the tops.
- **What went:** a first pass also stood slabs out of the faces and dropped chunks at the walls' feet. They read
  as stones sticking out of the snow, so they're gone, and the faces' layering is fainter.
- **Their texture:** the snow is laid on the faces from the side, not stretched down them, with faint bands of the
  plow's layers and a touch bluer.
- **Lighting:** the walls' normals come from their own surface, so a face is lit as a face, not a slope.
- **Collision:** the old ground under the road and walls is pushed well down out of the way. Its coarse 4 m
  cells, spanning a wall's foot and top, had poked up through the road.

## Deep snow off the road (`WinterWoods.cs`)
- **Off the plowed road:** the player wades, dropping to 42% of their pace.
- **The church's yard:** trodden, 78%.
- **Round the lodge:** plowed, full pace, and it now looks plowed too.
- **Coming back onto the road** brings them back up to full pace within a second.
- **Fixed alongside:** the sewer (Act 9) and the crawlspace (Act 23) reset the player's pace every frame, wherever
  they were. That would have cancelled the deep snow, so they now only touch it while the player is in them.

## The winter firs (`WinterTreeKit.WinterFir`)
- **The shape:** a pine-bark trunk, bare and stubbed low down, then tiers of drooping boughs narrowing to a spire.
- **The boughs:** each bough is a V of two cards showing a fir bough drawn by the texture script, so it has body
  from any side.
- **The snow:** it lies heavy on every tier, a lumpy cap with a lip at its edge.
- **The ice:** near the lodge the firs ice over like the bare trees.
- **Where they're used:** they replace the old blocky firs (about one tree in eight). There are three sizes,
  14–26 m.
- **The fir at the end of the ski tracks** is one too.

## The photo subjects, modelled in Blender (`tools/Blender/`)
- **How they're built:**
  - `winter_props.py` builds each model (`props_<name>.py`, on the helpers in `kit.py`) headless in Blender 4.0;
  - each is exported as a `.glb` to `assets/models/winter/`;
  - previews are rendered to `build/blender/`.
- **The look:** the game's PS2 look, a few thousand to about 30,000 triangles each. They use small soft textures:
  the game's own snow and bark, and a few drawn by the script.
- **The snowman:**
  - three hand-rolled, lumpy snowballs on a skirt of trodden snow, with chunks round it;
  - skinned stick arms, one ending in a six-fingered twig claw;
  - a stiff, frost-frozen red scarf with frayed tails;
  - seven coal eyes down one side of its face and a grin of real teeth, its head turned back up the road.
- **The snowplow:**
  - an orange municipal plow truck, paint chipped and spotted with rust;
  - a chrome grille, headlamps, and plow lights on stalks;
  - treaded front wheels and dual rear wheels with rims and lug nuts;
  - mirrors on their arms, an exhaust stack, a fuel tank on its straps;
  - a ribbed sander bed heaped with snow, its spinner beneath;
  - a curved, ribbed moldboard angled into a bank of snow, on push arms and hydraulic rams.

  The cab's door hangs open on an empty seat, the key still in it, and snow lies on everything that faces up.
- **The antler tree:**
  - a dead, leaning tree, its limbs broken short, snow along them;
  - eighteen antlers (burr, beam, brow tine and tines) and deer jawbones (with their teeth) hung on twine;
  - a deer's skull nailed to the trunk facing the road, its antlers still on.
- **The ski gear** at the end of the tracks:
  - two poles planted crossed, each with its grip, wrist strap and basket;
  - one ski stuck upright beside them, its tip curled and its binding open.
- **The arm in the wall** (replaces the empty clothes):
  - a puffy, quilted parka sleeve breaks out of the plowed wall at chest height, bent at the elbow, reaching up
    and out toward the road;
  - frost along its top, a knitted cuff, then a bare man's hand.

  The hand is built from its bones out: a palm, four jointed fingers clawed in by different amounts, and a thumb.
  The last joints are frostbitten black-purple, with dark nails. The snow round where the arm comes out is broken,
  with chunks fallen at the wall's foot. Its photograph is "an arm in the snow".
- **The frozen deer:**
  - a young buck, deep-chested and narrow, mid-step with one foreleg lifted;
  - hind legs with their backward hock, black hooves;
  - a pale rump, throat and belly, ears up, antlers;
  - eyes gone white, frost along its back, icicles off its belly and chin.

  The old glassy ice shell over it is gone: on a model this detailed it read as plastic.
- **The lodge sign:**
  - three routed planks with a raised border on battens, hung between two log posts on iron straps and bolts;
  - a shingled gable over it with snow lying thick on top;
  - icicles off the eaves and the bottom board.

  Its lettering is the game's own, as before.
- **Placement:** the props stand on the road between the walls, against one of them. The antler tree and the ski
  tracks are out in the snow beyond, where the wall sinks low. The plow is laid along the road, pulled over to
  one side.

## Other
- **The wendigo:** when the walls hide the trees from the road, it can appear up on top of a wall behind the
  player, looking down.
- **Tests:**
  - new checks: the walls are higher than a head; wading off the road drops the pace; it comes back on the road;
  - the props' screenshots are taken from a few metres back, so the whole of each is in frame;
  - Act 22's story test: 28/28.
  - The full autotest and the continue test landed with 2026-10-01 (the same commit); see that entry.
