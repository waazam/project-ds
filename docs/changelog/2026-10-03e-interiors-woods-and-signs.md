# 2026-10-03e — Less boxy interiors, a thicker undergrowth, and the park's painted signs

Commit: this entry's commit on `main`.

The owner: "any other suggestions you can make for graphical fidelity increase again, objects, clutter, anything that
sticks out to you? ... we need to make all the interiors feel more natural and less boxy, base boards and crown
molding can go a long way", then "do all of your suggestions after the push, i trust you ... make the overhaul pop".
Later, with photos from the woods near them (vine-smothered thickets, thin lichen-streaked trunks and rotten logs,
moss hanging from dead limbs in the mist) and of a state park's sign: "add some more tree/growth density in the
opening acts forest ... if you want to model our signage in the woods more like that".

## What it was
- **The station's rooms:** skirting, but walls meeting the ceiling bare, the wallpaper flat to the floor.
- **The bunker's room of doors:** a bare concrete box.
- **The library:** a flat lid of a ceiling over its shelving.
- **The station's basement:** bright, even orange-red brick.
- **The lodge's cream wallpaper and daylight windows** read as a glare round the lamps.
- **The opening forest:** firs, snags and ferns, the space between the big trunks empty.
- **The park's signs:** unpainted grey wood with pale routed letters.

## The interiors
- **The station (lobby, rooms 1 and 2):** crown mouldings where the walls meet the ceiling, a 22 cm cove in three steps,
  running on over the doorways' lintels.
  - Each wall trims only the face its room sees: the rooms' thin skins in front of the lobby's walls stay clean.
  - The runs along X and Z sit 4 mm apart in height, so where two meet in a corner they never share a plane.
- **Rooms 1 and 2:** a dado rail at hip height, and under it a row of moulded panel frames on the wallpaper, dark wood
  like the skirting. The lobby keeps its own wainscot, so it gets the crown only.
- **The lobby's lamp:** a plaster ceiling rose in rings round its rod, and a brass canopy.
- **The station's basement:** the brick old, damp and sooted, dark red-brown, a burnt one here and there, the mortar
  darker.
- **The room of doors (the bunker):**
  - painted to shoulder height in an institutional grey-green with a dark stripe at its edge;
  - a concrete plinth along the walls' foot;
  - pipes on brackets along the top of the long walls, under the beams;
  - a vent grille high on the far wall;
  - all of it round the doors' frames.
- **The library:** two beams along the room cross the old ones, coffering the ceiling.
- **The lodge:** the wallpaper aged and smoke-yellowed, the daylight and ice windows dimmed (no glare round the lamps).
- **Cobwebs** in the corners of station rooms 1 and 2, the basement (low ones too) and the room of doors.
- **What collects along a neglected room's walls** (`DebrisKit`): crumbs of plaster, brick or concrete fallen from the
  walls, thickest at their feet, heaped here and there, and a few scraps of paper. In the station's rooms, the
  basement and the room of doors, the doorways kept bare.

## The opening woods (`ForestScatter.Growth.cs`)
- **Saplings between the firs:**
  - thin trunks, pale with lichen in patches, leaning and bending a little;
  - twigs with loose clusters of leaves, a share of them yellowing;
  - crowding in the clumps, thinner in the deep woods' shade.
- **Vine mounds:** whatever stood there smothered under ivy into a lumpy dome, big leaves all over it hanging down its
  sides, a dark mass inside so it never reads hollow. On the woods' edge by the trail and round the clearings, where
  the light gets in. Solid (you can't walk through them).
- **Hanging moss:**
  - grey-green beards off the dead stubs of a share of the big firs and off the snags' branches, more of them in the
    deep woods, swaying a little;
  - the trees' shapes are the same with or without (its own random stream).
- **Kept clear, by the trees' own rules:** the trail, the stream, the test route, the clearings and the stairs' ring.
- **Not done:** a straight 12% more trees. It closed the lane to the stairs' approach, so the density comes from the
  undergrowth instead.

## The park's signs (`SignKit`, `SignPost`, `ParkProp`)
- **As in the owner's photo:**
  - chocolate-brown painted timber, the grain still showing through, worn to grey at the edges and in patches,
    chipped;
  - the routed letters and arrows filled white;
  - flat-topped square posts.
- **The trailhead:**
  - a header beam across the posts' tops, its ends run out past them and cut away underneath in steps (a rafter
    tail);
  - a green plaque, OVERLOOK PARK, over two brown boards (Blackfern Trail 2.1 mi; Clearwater Loop CLOSED).
- **The low signs at forks and bridges:** the same header across their two posts.
- **The directional posts:** painted, white letters and arrows.

## Tests
- **Looked at in the previews** and tuned:
  - single acts 13 (60/60), 8-10 (80/80), 19 (23/23) and 1-2 (56/56), their screenshots;
  - `forest_world_preview`;
  - the new `creature_preview -- --growth` (a moss-hung fir and snag, the saplings, the mounds).
- **What tuning changed:**
  - the crown made a real cove (the first, 6.5 cm deep, barely read);
  - the lobby's doubled panels taken off;
  - the moss thinned (from a distance it filled its cards in);
  - the mounds given a green core and twice the leaves (their orange core showed);
  - the saplings' orange lollipop crowns made loose leaves.
- **The forest preview's walker** reaches the stairs' approach with the undergrowth in (the straight 12% more trees
  blocked it). Its climb check fails as it did before this pass.
- **The full autotest:** 732/732. The Act 6 clearing check now counts the saplings and mounds too: none.
- **Frame rates while walking:** 149 fps on average, as before (Act 1 to the stairs 84, the station 164, the library
  120).
- **The continue test:** 925/925, 34 scenarios.
- **The clip audits:**
  - the trailhead: 0 z-fighting pairs;
  - the forest acts: 40 and the winter: 14, both as before;
  - no walk-through props;
  - no UV faults.
