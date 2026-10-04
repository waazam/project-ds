# 2026-10-03h — The lake's shore, the trail and the ground from the owner's photos, sharper bark and leaves

Commit: this entry's commit on `main`.

The owner, with photos taken that day (a lake: cypresses with their roots heaved up into the water, dead reed straw
washed up on pale sand, a birdhouse on a trunk, tall reed beds, marsh grass lying over the water, a warning sign out in
the marsh; a gravel trail with puddles and leaves; the woods off it, thin trunks, palmettos, pine straw and oak leaves,
a heap of old boards): "The lake needs some love ... include that around the lake to give it some character. Also the
2 other pictures ... use these as reference to spice up those open act forest sections too ... increase the
resolution on any textures and models that are most important."

## The lake (`LakeDressing.Shore.cs`, `lake_shore.gdshader`)
- **Bald cypresses (41) along the shore**, some standing out in the water:
  - trunks flaring wide and fluted at the foot;
  - roots heaved up in a tangle, running down into the mud and the water, knees poking up round them;
  - a crown of feathery sprays on sixteen limbs all the way round, in their own two-sided foliage, lit through from
    behind (seen from below against the sky, the fir boughs read black);
  - moss hanging thick.
- **The wrack:** dead reed straw washed up and dried pale along the waterline (370 patches, drawn from its own
  straw-and-leaves texture), with bleached branches and root wads stranded in it.
- **Pale sand** washed up above the mud in drifts, muted.
- **Reed beds:** tall, dense stands out in the shallows (316), green at the foot and gold at the top, with plumes.
- **Marsh grass:** bright mats lying out over the water, the blades' tips trailing on it.
- **A birdhouse** on a cypress by the landing, as the photo.
- **A warning sign** out in the marsh grass on two posts: "WARNING / DO NOT ANCHOR OR DREDGE / GAS PIPELINE CROSSING".

## The opening woods (`terrain.gdshader`, `ForestScatter.Growth.cs`)
- **The trail's open first stretch has pea gravel** laid on it, leaves over it, giving way to bare dirt as the woods
  get deep (the trailhead level only).
- **Puddles** stand in the trail's dips: dark, still, glossy.
- **Saw palmettos** in clumps under the trees in the open woods: stiff fans on stems, the old ones gone brown.
- **Three heaps of old boards** dumped a few metres off the trail, a couple of rusted sheets of tin among them
  (solid).

## Sharper textures (`tools/Textures/make_surfaces.py`)
- **The ground's fallen leaves are a photo now:** Poly Haven's `forest_leaves_02` (oak leaves, pine needles, twigs,
  moss, like the owner's photo) at 512 px, in place of a 64 px drawing. It's tinted down to the woods' light, so it
  sits like the old ground with real detail up close.
- **The trunks' bark and the forest floor's patches** are at 512 px (from 256), still area-downscaled from the 1k
  photos, so they stay soft.
- **The tool:** each surface can have its own size, and it only uses photos already downloaded (never fetches).

## Tests
- **Looked at in the previews and tuned:**
  - the lake (the new `creature_preview -- --lake`): the cypress crowns made fuller and given their own lit-through
    foliage (they read black), the root wads laid low;
  - the trail and the ground (`forest_world_preview`): the photo litter's tint cooled and darkened (it came out a flat
    orange at a distance).
- **The full autotest:** 734/734. New: the lake's shore has its character (41 cypresses, 316 reed stands, 370 patches of
  wrack), and its view of the nearest cypress (`lake_cypress`).
- **Frame rates while walking:** 155 fps on average over the whole game; Act 1 to the stairs 99 (as before, with the
  undergrowth, the palmettos and the photo litter), the lake 177.
- **The continue test:** 925/925, 34 scenarios.
- **The clip audits:** the trailhead 0 z-fighting pairs, the forest acts 40 and the winter 14 (both as before), no
  walk-through props.
- **The sign audit:** nothing blocking. The new signs are clear of the trail: the fork post by 0.48 m, the closed sign
  by 0.97 m.
- **Act 18** passed twice on its own after one flaky run (the bot crushed by a slam at the tenth valve, the valve order
  being random).
