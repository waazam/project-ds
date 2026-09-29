# 2026-09-28d — The winter's pink dawn, a winding save icon, the dining hall's walls

Commit: this entry's commit on `main`.

The owner's notes after Act 23, with a screenshot of the winter woods and two reference photos (a pink winter
sunrise over snow and firs):
- the trees looked white and bland, with no texture or flair;
- the scene needs contrast from the sky: make it early morning, with nice pink hues;
- animate the save icon so the film looks like it's winding into the canister;
- a black rectangle is clipping through the dining room's walls.

## The winter's early morning (`ForestAtmosphere`)
Acts 21 and 22 outdoors: Act 22 was a dark blue dusk and Act 21 a pale grey overcast. The trees went white in
the pale grey haze and a strong, flat ambient light.

- **The sky** (the sky shader's palette, in steps, only while it's winter):
  - a deep violet-blue zenith over a rose-pink horizon;
  - a salmon glow where the sun is coming up;
  - pink-lit cloud;
  - lilac ridges and haze.
- **The sun:** low (13° up, a little east of north, ahead along the road to the lodge) and rose-gold. It
  catches the snow on the boughs pink and throws long shadows. The light shafts are off while it's this low
  (as at the lake's sunrise).
- **The haze:** lilac, a step darker than the horizon, so the far trees fade toward the sky's colour
  instead of turning white.
  - The fog covers the sky less (0.45 instead of 0.92), so the pink shows above the treeline.
- **The ambient light:** lavender and lower, so the trunks and the undersides of the boughs keep their
  shading. The trees now stand dark against the pink sky, with pink-lit snow on them.
- Kept a step under the reference photos' brightness: the snow reads pink-lavender, never a glare.
- The trees' draw distance was fine: the flatness came from the lighting and fog, not from level of detail.

## The save icon (`SaveIndicator`)
While a save runs, the film winds into its canister:
- the sprocket holes run steadily into the canister's mouth;
- the leader draws shorter (30 px to 12 px across the save);
- a notch on the spool's knob turns with it;
- a dark slot (the canister's felt light-trap) marks where the film goes in.

It keeps the slow, soft brightness swell, with no flicker, and is still under three seconds.

## Fixes
- **The dining hall's walls:** the lodge's exterior timber sill (between the stone course and the logs) ran
  through the whole wall's depth. It poked 2 cm through the dining hall's panelling as broken black bands
  between the rows of windows. It now sits on the outside half of the wall only; the wings' rooms upstairs
  were affected too.

## Tests
- Acts 21–22 story test: 53/53 on the first pass of the dawn grade. After the tuning (more ambient light,
  the sun a little higher), Act 22 passes 25/25.
- Act 23 story test: 51/51. Continue test: 813/813 (30 scenarios).
- Lodge preview (`--lodge`): the dining hall's walls are clean.
