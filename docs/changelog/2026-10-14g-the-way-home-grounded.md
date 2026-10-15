# 2026-10-14g — The way home grounded: Act 25's snowbank laid over the ground with a real tunnel in it; the old cabin by the trail is the cabin, on its own footings

The owner: "the whole snow part, the burnt cabin or whatever it is, just floating there. We need to fix that according
to the story and make it all look good."

## What it was
Both pieces are Act 25's, built by `scripts/world/snowmaze/EndingDirector.cs` when the player comes out into the trailhead
(STORY.md Act 25: "They come out of a hole in a snowbank, the snow thinning into the autumn woods ... Down the trail they
go past an old cabin, its door hanging open"). Surveyed from the player's path with the look shots, and every winter act
(22 from the church door down the whole plowed road to the lodge, 23, 24) from its spawn: nothing else floats in the snow
acts. The two offenders (`test-output/look/ending_before/`, `ending_before_sheet.png`):
- **The snowbank** was three jittered spheres set at the spawn's height whatever the slope did under them. They read as
  faceted pale boulders, and the hole was a dark lump stuck on the side of one.
- **"The burnt cabin"** was the old cabin: a ring of logs set on the ground at its centre point, no footing, no corners
  joined, open gables, and its roof two loose slabs hovering over the walls. In the dark, its roof was a black shape over
  the logs.

## The snowbank
Built as one surface over the ground in three sheets that share their edges, every point the terrain's height plus the
snow's, the edges 14 cm down into the ground everywhere (no gap and no cut edge anywhere on the slope):
- **The face** is a grid over (along the bank, height). It stands near sheer round the hole and slumps into drifts toward
  its ends, its line and its top wandering. The hole is cut out of it cleanly.
- **The top** falls away behind into the woods.
- **The apron** in front is a skin of old snow round its foot that breaks up into patches over a few metres, the leaves
  showing through. There are low drifts heaped at the bank's two ends, and the way out of the hole is trodden thin.
- **A real tunnel** bores into it from the hole: its walls grey and glazed at the mouth (the burning thing came through
  here), black a metre or two in, running down. A lip of broken crust hangs over the top, and a few lumps lie fallen at
  the foot.
- A trimesh collider replaces the box, and a stop a step inside the tunnel. The spawn stands on the snow's skin. The
  ground decals thinning out to twenty metres are kept.

## The old cabin
It is now the Hollow's cabin itself (`Cabin.cs`, new `Derelict` flag: theirs, if they'd stayed). It grounds itself as it
does in the Hollow: fieldstone footings down to the lowest ground, porch skirt, as many steps as the slope wants, the
gable ends closed and the roof on its walls. Derelict, it is dark inside, with no lamp and no R.H. papers. Its door is off
the top pintle, swung in, its free edge sagged to the floor (with its own collider). It stays out of the "cabin" group, so
nothing in the story mistakes it for the Hollow's. The site is the flattest ground beside the last stretch of trail that
is clear of the trail's other bends and the creek, with the porch toward the trail. On the first spot the porch stood a
man's height up on ten steps. The woods' scatter is kept off it.

## Tools
- `GroundAudit`: a fourth pass, `trailhead_act25`, loads the trailhead with the ending's pieces and audits only those (the
  cabin by its outline; the snowbank by its edges, since a skin of snow has no underside).
- `LookShots`: a pose y of 9999 or more drops onto whatever is under that x, z (long roads with unknown heights).

## Verified
- Look shots before/after: `test-output/look/ending_before/`, `ending_after/` (and the two `_sheet.png`s).
- Ground audit: `trailhead_act25` 2 objects, 0 floating, 0 hanging. The 8 remaining hangs elsewhere are older (Hollow and
  trailhead rocks, logs, the Act 6 stairs), outside the snow acts.
- `--playtest --playtest-only=25 --gap-audit`: 6/6 beats, 70/70 checks, 0 near misses. `--playtest-only=5,7` (the
  Hollow's cabin, untouched): 3/3, 43/43. `--autotest --story-from=25 --story-to=25`: 16 pass, 0 fail.
