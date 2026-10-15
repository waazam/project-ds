# 2026-10-15 — The dead fir's base: a real foot standing, a torn-up root plate and its hole once down

The owner asked: "make sure the falling tree in Act 1's base doesn't look so fake", and then "something about it looks
off". The tree is Act 1's dead fir (`FallenTree`), the one that comes down across the trail's end. The fix is in the same
commit as this file.

## What looked off (shots before and after, side by side with the woods' own firs)
- **Not the woods' bark.** The fir was drawn with the old 32 px `BarkMat`, tinted pale. In the game it showed as
  blotchy red-brown patches, not the dark, furrowed photo bark (`TreeBarkMat`) on every fir around it.
- **A post standing on the ground.** A nine-sided tube went straight down into the ground with no flare and no roots.
  Every fir around it has a flare and surface roots.
- **A bare disc at its foot.** Standing, the root plate lay just above the ground round the foot as a flat brown lens.
- **Stacked segments.** Each piece of the tube was its own cylinder, so lit bands and seams ran round the trunk every
  metre or so.
- **The plate, once down, was a shield.** It was a smooth faceted lens of earth about 5 m across, standing on edge. It
  had a handful of twigs on its rim and nothing on its back.
- **The hole was a brown blob.** It was a convex lump, not a hole, and it appeared at the crack while the plate was
  still flat in the ground.
- **A hedgehog trunk.** Three long thin points stuck out at every metre of the fallen trunk, and the top ended in an
  open tube.
- **Nothing came off it.** No earth flew, and nothing broke off and lay round the fallen tree.

## Changes
- **The foot is made as it stands, then laid down with the tree.** The trunk, the flare, the roots, the plate and the
  hole are built in the standing pose against the real ground. Then the inverse of the standing transform turns them
  into the lie. The hinge, the fall angle, the timing and the sounds are unchanged.
- **The trunk** is one smooth 12-sided loft like the woods' firs (`FirMesh`).
  - The flare starts 0.5 m under the ground and narrows to the trunk by about 1.3 m up.
  - Each ring is framed once, square to the tube and turned the same way, so the bent axis doesn't open cracks. (A
    first pass with `ForestScatter.TrunkLoft`, which frames each piece on its own, showed white seams.)
  - It uses the woods' bark, held still: the breeze sways a tree by its height in the mesh's space, which for this one,
    laid out as it lies, isn't its own height. It is greyer than the living firs (dead) and a little paler up the trunk.
  - The top ends in a splintered point.
  - The branch stubs are thinner, two to a metre, and the ones on top are snapped short and blunt.
- **Seven surface roots** come out of the flare and dive into the ground before the plate's rim, so once it is down none
  sticks out past the edge as a spike.
- **The root plate** is a ragged slab of 22 sides under the ground round the foot, its top 10 cm down.
  - Its top is the old forest floor (leaf litter). Its underside is dark churned earth, bellied up to a metre deep
    under the trunk. Its torn rim is jagged.
  - It carries clods on the rim, and clods and stones caught in it.
  - 34 roots come out of the underside and the rim, with rootlets. They hang the way "down" will be once it lies, and
    their snapped ends are pale raw wood.
  - Its earth is mapped in the mesh's own space, so it rides with the plate and doesn't swim as it rises.
  - Standing, none of it shows. Leaning, the far side heaves the ground a little. Down, it stands on its near edge (the
    hinge), the flare toward the crown and the torn earth toward the hole.
- **The hole** shows once the plate is up off it (0.32 rad into the fall), not at the crack, with a dark burst of earth.
  - It's dark at the bottom, so it reads deep (it can't go under the ground's own surface). It has a heaved lip with
    clods on it, a skirt that sinks into the ground, and eight torn roots the ground kept, with pale snapped ends
    sticking up out of it.
- **On landing:** a puff of earth where the plate thumps down (the old dust burst at the crown is still there). The
  debris shows: 14 broken limbs with twigs either side of the trunk, nine slabs of knocked-off bark by it, and 12 clods
  thrown behind and beside the hole.
- **The dead crown and the stubs** use the same bark as the trunk.
- **`TreeFallMotion.Dust`** takes an optional colour, spread, count and lift. Called as before, it is the same burst
  for the falling trees beside the way.

## Unchanged
- The fall timing, the creak, crack and impact and their sounds, the 25 s wait, the hinge and the angle it comes to rest
  at.
- All collision: the standing cylinder, the trunk's boxes, the plate's box, the crown's boxes and the left barrier.
  The trail is blocked exactly as before, and the crown comes out of its random draws as it always did.
- The left barrier's boulders still sit partly in the hole. That is where they were before, and moving them would move
  their collision.

## Checks
- `forest_world_preview.tscn -- --fir-shots [--fir-out=dir]` (new in `ForestPreview`): the fir's base standing, held
  leaning, frozen mid-fall, landing and down, each from the trail, close, the side, the hole's side and low, plus
  from above with the woods hidden and across the trail. Shots in `test-output/forest/<dir>/`.
- Act 1 beats `1,1b,1c`: all pass (43/43 checks), 0 engine errors.

## Not done
- The trees beside the way (`FallingTree`) still turn on their foot as a whole, roots and all. Their roots swing up
  with them, and they leave no hole.
