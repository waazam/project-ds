# 2026-09-30 — Act 22's winter forest overhauled

Commit: this entry's commit on `main`.

The owner, on the woods between the church and the ski lodge:
- **Looks terrible.** The fixes they asked for:
  - make the ground hilly;
  - have the path snake through the hills (to hide the trees' pop-in);
  - make it dark, so the lantern is needed and only a radius round it is seen;
  - past that radius, a whitish haze rather than black.
- **The trees and snow need love:**
  - use better textures (their Poly Haven snow sets, Poly Haven bark);
  - most trees should be bare, just trunks and branches.
- **Winding like the references:** the path should wind and snake like the reference photos, not run straight to
  the lodge.

## The road and the hills (`WinterWoods.Path.cs`)
- **The road snakes:**
  - It swings left and right round a hill every hundred metres or so, like the references' paths.
  - It used to run nearly straight with a few long, lazy bends.
  - It's now 1,450 m (was 1,300).
- **Its limits:**
  - The tightest bend is 23 m in radius, still wider than the road's banks (so the road surface never folds on
    itself).
  - No two stretches of it come within 90 m of each other.
- **The hills rise away from the road on both sides.**
  - From about 10 m out they climb 6–17 m, highest in the ridges between the road's legs.
  - Every bend has a hill on its inside, so a bend hides what's round it, trees included.
  - Knolls and hollows break up the slopes.
  - The church's yard stays level (the hills start 40 m out), and so does the lodge's shelf.
- **The road still rises and falls** over the long swells, so a crest hides what's beyond it too.
- **Everything placed along the road moved with it:** the lodge at its end, the photo subjects (snowman,
  snowplow, antler tree, ski tracks, clothes, frozen deer, lodge sign), and the clearing's trees.

## Dark, the lantern, and the white haze (`ForestAtmosphere.cs`)
- **Before first light out in the woods:**
  - the ambient is low and cold blue;
  - the sun is down to a faint glow;
  - the lantern does the lighting (its glow reaches 14 m, its beam 24 m).
- **Past the lantern, a pale haze rather than black:**
  - a depth fog that leaves the near dark clear from 4.5 m;
  - it closes in white-grey by about 46 m (a little further near the lodge, where the air is clearer and
    colder);
  - it takes the sky too, so the trees go into it as dark silhouettes, like the misty reference;
  - it's held a step under white, so it never glares.
- **The woods only.** The fog switches off inside the church, the lodge and underground, and each place's own
  fog comes back.
- **The falling snow is lit now**, so it shows in the lantern's light and faintly past it. Unlit in the dark it
  had been a static of white dots.

## The trees (`WinterTreeKit.cs`, `WinterWoods.Trees.cs`, `WinterGlade.cs`)
- **Mostly bare broadleaves now** (about three in four), from a new generator:
  - a leaning, wandering trunk with its root flare in the snow and a few roots showing;
  - a leader up through the crown;
  - five to seven scaffold limbs spiralling up it, which fork and fork again, kinking at every fork;
  - sprays of fine bare twigs at every tip and in the forks: crossed, alpha-cut cards drawn by the texture
    script, with a little snow along the twigs.

  Six shapes, 9–22 m.
- **The bark** is Poly Haven's bark_brown_02 (grey-brown) and bark_willow_02 (pale grey), with the moss and the
  warmth taken out for winter.
- **Snow on the trees:**
  - A bark shader lays snow along the top of every limb and plasters it up the windward side of the trunks, like
    the references.
  - Near the lodge the same shader glazes each tree in a dull, cold ice, set per tree, never glossy.
- **A few firs** (about one in eight) and dead snags remain. The firs' ice shell is duller now, so it doesn't
  glare.
- **The church's clearing** uses the same trees.
- **Drawn to 82 m.** The fog is solid well before that, so trees come out of the haze instead of popping in.
- The antler tree on the road is one of the new trees.

## The snow (`tools/Textures/make_winter_forest.py`)
- **From the owner's snow sets:**
  - snow_02 (smooth fresh snow) for the deep snow off the road;
  - snow_01 (trodden, boot prints) for the packed crust near the lodge and for the plowed road, with a little
    road grit in it. The road had read as brown dirt in the lantern's light.
- **Kept in the game's look:** 256 px, soft, a cold blue-grey, never white glare.
- **Credits:** the diffuse maps come from the owner's zips; the normal maps are Poly Haven's 1k (CC0).

## Performance
- **Less to draw:**
  - trees are drawn to 82 m, down from 175 m;
  - the ground and road are drawn to 170 m, down from 300 m or more.

  With the fog solid past about 50 m, nothing beyond shows anyway.
- Act 22's story run averaged 143 fps while walking.

## Other
- **The wendigo's first appearance** on the road ahead is at 24 m, down from 36 m, so it shows as a dark shape in
  the haze rather than being lost in it.

## Tests
- Act 22's story test: 25/25.
- The full autotest: 685/685 (some checks vary with where a run's randomly hidden things fall).
- The continue test: 897/897, over 33 scenarios.
