# 2026-10-15f — Act 1 walked again

The owner's play-through notes on Act 1, the hands and the breath.

## The trailhead and the edges
- A real forest lot: log rails on posts, an open forest-road gate, a state-highway sign and a curve warning; the access road leaves the lot and bends away behind the trees (204 m, nothing across it: it's the road they drove in on). An invisible wall crosses it before the bend, tied into the forest.
- Every other edge of Act 1 and the Hollow thickens into impassable forest (young firs, snags, saplings, bramble, deadfall; `ForestScatter.Edge.cs`), the walls hidden inside it. The Hollow has woods behind the wake. `LookShots --look-walk` tries to walk out in six directions per level.

## The woods
- Tree trunks: the seam line gone (rings shared between segments, normals averaged), rounder (16/14/8 sides, lobed bases), roots that sink into the ground; fir sprays widen at the tip instead of spear points, soft tufted tops, no beads on young firs, rounder broadleaf crowns.
- Wildflowers (7 patches, four kinds), mushrooms (7 clumps, four kinds) and frogs (4) spread along the trail; five squirrels that forage, dash, climb and bolt up a trunk; the photo birds on a branch, the ground, a rock and the omen's post (`Bird.Perch`).

## The tree falls
- Once the fir is down and the stop is over, every wall tree still standing comes down one after another, wherever the player is.
- The first fall turns the view to it from anywhere, and holds while it falls.
- Fewer creaks: most of the later falls come down on their cracks; a waiting tree groans at most four times, 6-11 s apart; eight creak takes, no two alike.

## The stairs
- The drone, the ringing and the pressure rise along the walk through the fallen trees; at the foot every sound cuts (`StoryMute`) and stays off until the Hollow.
- In the silence the player is held looking up the flight while the game's title shows; the chapter title comes when they step onto the stairs.

## Sound
- The player's breath rebuilt from horror-game research (Outlast, The Last of Us Part II, INSIDE): nose breaths walking, mouth breaths locked to the footfalls running, winded recovery, shaky scared breathing and a gasp on a fright, a stillness when afraid and standing (`Breath.cs`, 88 takes).
- The far rifle: six distant takes built from how far-off shots really sound (no crack, cut highs, smeared hill echoes, a rolling tail; `Gunshot.cs`).
- The trail walk: far woodpeckers, crows, a squirrel's scolding, trunk creaks, the walker's strap and zip, brush off the trail (`Trail.cs`).
- The omen bird screams like a raven as it flies at you (`RavenAttack.cs`, five takes); the old scream is kept in `assets/audio/storage/` for Alex.

## Hands and camera
- Thumbs wrap round every handle opposite the fingers, never through it; the camera's thumb sits on the back under the advance lever; the axe and hammer held halfway up the handle in a loose, low carry; the walkie lower in the hand.
- Photographs leave out every on-screen word (titles, subtitles, thoughts, prompts, the HUD).

## Tests
- `--autotest --walkthrough`: the story test walks where it used to teleport, at the game's real speed.
- The Act 1 checks follow the new rules (the walls don't wait; titles at the foot and on the stairs; sound muted and back).
