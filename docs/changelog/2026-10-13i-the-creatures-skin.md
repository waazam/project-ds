# 2026-10-13i — The creatures' skin: wrinkles, cracked dry hide, veins, scars and pores on every monster, black in every crease

Commits: `784c548` (the detail), and this entry's.

The owner: "the monster models look too smooth, add more detail". Up close, every creature read as a smooth CG shape:
the crawler's baked colour stretched into bark-like streaks, the wendigo's and its arm's hide like wet plastic under the
lantern, the giant a smooth black rubber figure. All of it is now done on the surface (textures and shaders); no model,
skeleton, weight, clip or part was touched, and no vertex was added.

## The skin's detail, shared
- **Two maps of our own** (`tools/Textures/make_creature_detail.py`, drawn from noise; 1024 px, tiling): a normal map
  and a mask (how deep each point lies, the veins, the wet). In them, from coarse to fine: loose folds and sag, crossing
  wrinkles in patches (each patch its own direction), the hide's cells cracked like old leather in its dry patches,
  raised branching veins, a few long welted scars, and pores.
- **Riding the flesh** (`CreatureDetail.cs`, `creature_detail.gdshaderinc`): each model gets its rest pose in its spare
  vertex channels once, as it first loads, and the maps are laid on three ways in that pose, at two sizes (the fine
  skin, and its folds three times larger), so the wrinkles stay on the flesh as it moves: no swimming, no seams, no
  stretching at the joints. Laid over each model's own baked normal map.
- **Its creases**: the curvature of each model worked out from the mesh itself, so the hollows between the ribs, round
  the knuckles, the joints and the folds of the neck go dark (grime and shadow), and the skin bunches into wrinkles
  there and is drawn tight and smooth over the ridges where a bone pushes through.

## The creatures
- **The crawler**: its hide drawn (`creature_flesh.gdshader`): a corpse's pallor, grey-green and sallow in patches,
  bruised, the veins blue-black under the thin skin, a few raw sores; dull and dry on the cracked skin, slick in the
  creases; light through the thin flesh (the terminator warmed and softened) so it reads as flesh, not plastic. Under
  the blacklight it still glows its cold violet-white where the cone falls, but the veins and the wet places brightest
  and the creases dark (it was a flat sheet of glow).
- **The wendigo** (and its head on the platter): the skin's detail under its frost, frostbite and blood; grime in every
  crease; dry and dull on the open hide, wet only in the creases and the wet patches (it shone like plastic). The skull
  and the rack get the bone's version: shallower, pitted and crazed rather than wrinkled.
- **Its arm through the crawlspace wall**: the crawler's flesh shader in the arm's ash-grey: sinewed, wrinkled, veined,
  old blood run down it, its cold rim kept.
- **The stalker** (and the Act 11 giant, the same skin): still a hole in the scene, still unlit, still nothing far off.
  Up close its faint light now finds its hide: the wrinkled, cracked skin over the skull, the ribs and the spine's
  knuckles a breath brighter, black down in every crease. (Found on the way: its baked map's depth was lost in the
  import, so that faint light had lit the whole figure evenly; its overall pallor is kept as it was, now shaped.)
- **Act 7's giant** (`giant_skin`): the skin stretched paler over its bones, black in the creases, a dull sheen so its
  wrinkles catch the light that grazes it; all of it fading as the haze takes it.
- **The lake thing and the pit's leviathan** (`octopus_flesh`): the skin's creases, cracks and veins worked into their
  bumps, grime and slime, finer than their noise.
- **The shadow man** is left as he is: a shape of ink and blur, not a hide.

## The cost
- Textures: two 1024 px maps (about 2 MB on the card, mipmapped, compressed), shared by every creature. No creature's
  own textures changed.
- Geometry: unchanged (crawler 83,504 triangles, wendigo 116,190, stalker 81,552, the arm 19,760, the head 13,650).
  The spare channels add 48 bytes a vertex (about 2.8 MB for the crawler or the stalker, 4.2 MB for the wendigo).
- Load: 25 to 125 ms the first time each model loads (its rest pose and creases), once a session; the wendigo already
  paid most of that for its rest pose.
- Drawing: 12 more texture reads a pixel on a creature's hide, only where it's on screen.

## Tools
- `creature_preview.tscn -- --detail[=tag] [--detail-only=crawler,wendigo,head,arm,stalker,giant,lake,leviathan,shadow]`:
  every creature close up and whole, lit, by the lantern, by the blacklight and in fog, to
  `test-output/creatures/detail_[tag]_*.png`.

## Tests
- `--playtest --playtest-only=7,11,12,14,18,22,23,24` (where the creatures are): 257 of 258 checks, 16 of 17 beats.
  The one miss: in Act 18 the bot was caught by a slam before the last valve (its second go in the same beat turned all
  ten untouched); `--playtest-only=18` again: 13 of 13. No shader errors anywhere.
- No full-game test run (the owner's instruction).
