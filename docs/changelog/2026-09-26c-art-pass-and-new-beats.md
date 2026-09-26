# 2026-09-26c — Art pass: textures, wind, fog, the monsters remodelled; a bigger lake; new beats in Acts 11 and 15

Commit: this pass's single commit on `main` (see `git log` for the hash).

From the owner's list, their references (octopus tentacles with toothed mouths; ink-splatter and
frosted-glass figures; smoky aura art) and their notes during the work.

## Art
- **Texture pass (the PS2 horror look)**:
  - Every textured material in the game now carries a fine grime detail layer: stains, speckle,
    pitting and hairline scratches, multiplied over its own texture at a finer grain. The flat,
    low-res surfaces now read as dirty and worn, like the era's horror games.
  - The forest ground gets fine grit and dark wet pockets close up.
- **The post-process finish** (`ps2_post.gdshader`):
  - a sickly split-tone grade (murky green-teal shadows, dirty warm highlights);
  - a soft halation bleeding off anything bright;
  - lens softness and a touch of colour fringing toward the frame's edges;
  - faint static scanlines (the interlaced look, without any flicker), with the dither, grain and
    vignette as before.
  - Nothing flashes: the glow only follows what is on screen.
- **Wind through the forest**: every tree now leans a little with a breeze from one direction. The
  lean grows with height, so the base never moves, and it breathes with slow gusts rolling across the
  woods. Trunk, stubs, boughs and leaves all move together (`tree_wind.gdshaderinc`). It is subtle:
  about a third of a metre at the top of a giant fir.
- **Trunks and stumps**:
  - Each tree's trunk is now one smooth, continuous shape from its root flare in the ground to its
    top, with bark running on unbroken. Before, a separate flare cylinder left a hard seam, a shading
    crease and a texture jump where it met the trunk. This applies to firs, snags and the deciduous
    trees.
  - The odd stump beside a tree is placed clear of the trunk and its flare, so the two never run
    into each other.
- **Act 1's fog**: it is hardly there at the trailhead, and closes in steadily the further you walk.
  At the fallen tree you can see about twenty feet: a see-through bubble round you, and past it a
  dense wall of fog that swallows everything. This is depth fog, ramped by how far along the trail
  you are. Other acts keep their fog as it was.

## The monsters
- **The tentacle monster** (the lake's limbs and the pit's leviathan), after the octopus
  references:
  - Gooey, grimy octopus flesh (`octopus_flesh.gdshader`): maroon-crimson underneath, a slick dark
    green-teal down the back, dark veins, grime in the folds, pale stretched patches, and uneven wet
    slime (clear-coat gloss) over it. The surface is bumpy, and a little red light comes through the
    thinner flesh. The pit's rot is built in, as before.
  - Raised ring suckers in two staggered rows down each limb's underside: a glossy pink-red rim
    round a dark wet hole.
  - At each tip, a mouth: a swollen head opening into three ribbed jaws round a dark throat, ringed
    with hooked teeth.
  - The eyes (the owner's note) are huge and crowded at the roots where the limbs meet the body,
    and shrink toward the tips. They sit in wet, swollen lids now rather than on the skin, and their
    glow is toned down.
- **The Shadowman**, rebuilt twice after the owner's notes ("not that scary"):
  - A man-shaped absence that is wrong in every proportion: too tall and starved thin, hunched, a
    small head hanging forward and to one side off a long neck. It snaps to a new angle every few
    seconds, which is wrong-looking but never a flicker.
  - His arms bend twice and hang past his knees, and his splayed fingers are as long as forearms.
  - Ink runs off him in strings and spatters round his feet.
  - His light-swallowing black (`ink_shadow.gdshader`) has a ragged, fraying outline that slowly
    "boils".
  - **The aura** (the owner's references): dark smoke rolls off him and hangs where he was, black
    flakes tear loose and drift away, and faint specks of light swim slowly in and out of the black.
    A blur behind him and a thin veil in front breathe in and out, so you never quite see his
    outline, though it is there.
  - Two pinpricks of light for eyes, barely there until he takes someone.
- **The forest creature (the stalker)**: its shoulders were round, bubbly lumps in side profile. They
  are now knots of root with crowns of gnarled, jointed, splitting root spikes twisting up, out and
  back, to match the rest of it. The rounded hunch of its back is reduced to go with them.

## Story and levels
- **Act 11**: the thing from the bunker's rooms follows them out.
  - After the radio, it comes out of the bunker's door and keeps to their trail, slowly at first. It
    is only ever seen when they turn round.
  - Near the last staircase it matches their pace.
  - If it gets close, it shoves them hard, spinning them round to face the stairs and throwing them
    a few metres toward them.
  - It cannot climb: it stops at the foot of the flight, to one side, and stands there staring up
    after them, eyes glowing. It is gone once the cap is seated.
- **Act 12**: the lake is doubled (the owner: massive, and a crossing to remember). It is 300 m by
  192 m, about 190 m of rowing, where it was 84 m. The ground, water, shoreline dressing and mist all
  follow, and the station now stands on the far shore of the bigger lake.
- **Act 15**:
  - From halfway down the hall, a red light puts the Shadowman in front of you, square in your path
    and closing in a jump at a time: stand still until it's green, then go round him. This repeats
    to the end.
  - He never stands in front of the closet door: he always stays at least 7 m short of it, in front
    of you or behind.

## Checks
- The missing-assets sweep found every referenced resource and sound present (only test-output
  folders, which are made at run time).
- A creature preview harness (`creature_preview.tscn`, dev only) renders the leviathan (healthy
  and rotting), the lake's limbs, the Shadowman and the stalker into `test-output/creatures/`.

## Tests
- Act 15:
  - the Shadowman in front of you from halfway, reds survived by standing still and going round him;
  - never in front of the door.
- Act 11:
  - the thing follows them out, keeping to their trail (never far behind);
  - stuck at the foot of the stairs, staring up.
- Act 12: the rowing timeouts doubled for the longer crossing.
- Found in testing and fixed:
  - The first version of the Act 1 fog set the fog mode every frame outside Act 1, which washed the
    lake at sunrise out to a flat orange haze. It now keeps the level's own fog settings and only
    touches them in Act 1, putting them back after.
  - The pursuer could fall far behind a fast player; now, when it is well back and out of view, it
    is simply nearer on their trail the next time they look round.
- Results:
  - Full `--autotest` (the whole game, start to finish): 466/467. The one failure was the pursuer
    falling behind, fixed after; `--story-from=11`: 24/24 with the fix.
  - `--continue-test`: 642/645. The same three failures predate this work.
