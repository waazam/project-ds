# 2026-09-26i — The camera: sixty subjects, scored pictures; the credits montage; the save indicator

Commit: this entry's commit on `main`.

## Sixty pictures to take (`PhotoCatalog`)
- The owner asked for 60 important pictures across the game. They are listed in the order the game
  meets them:
  - **Act 1 (11)**: the four birds, the deer, the frog, the wildflowers, the mushrooms, the
    waterfall, the strange stone and the stairs.
  - **The Hollow**: the camp, the cabin, the shed, the woodpile, a trail sign, a number on a tree
    (under the blacklight), the footbridge, the fallen tree, him, the bunker door, the walkie, the
    screens, the vine door, the lake and the forester station.
  - **The station and down**: the lobby, the writing, the red room, the cryptex, the basement, the
    stairwell (shot down the well), the long hallway, the closet, the sewer, the hole, the pit, a
    valve, the puzzle box, the fireplace, the bookcase, the journal, the green ring, the bricked
    windows, the switches and the room above.
  - **The church (Act 21)**: the nave, the font, the altar, the crypt and the great door.
  - **Nine monsters**, worth double points: IT (the stalker), the giant, the thing in the room, the
    bunker creature, the lake colossus, the lake's tentacles, the dead eye, the shadow man and the
    beast in the pit.
- Where the subjects come from:
  - `PhotoSubjects` hangs the landmark subjects round the Hollow once it has built. Repeated
    dressing (every sign, every valve) all counts.
  - The monsters carry their own. The lake thing's subjects ride its limbs while they are up.
- **Retakes count**: a subject already photographed can be shot again for a better score. The album
  keeps the best score for each subject.
- **Bug found and fixed**: the Hollow had no album node, so no picture after Act 1 was ever kept.
  It has one now, and it restores the Act 1 pictures.

## Scoring (clarity, focus, framing, zoom)
- **Zoom**: while the camera is up, the mouse wheel steps through x1, x1.5, x2, x3 and x4 (35mm to
  140mm), with a click. A zoom-step ladder and the focal length show in the viewfinder.
- **Autofocus**: it pulls in on what's in the middle over about half a second. A bar beside the
  focus square fills and turns yellow when focus locks.
  - The more the view is swinging (magnified by the zoom), the slower it pulls in.
  - A new subject starts it over; a zoom step sets it back.
- **Clarity**: a steady camera and a still subject (both magnified by the zoom), in enough light.
  Monsters move and live in the dark, so they are hard.
- **Framing**: the whole subject inside the frame (a subject bigger than the frame is judged on how
  central it is), near the middle or a rule-of-thirds crossing.
- **Zoom**: the subject fills a good share of the frame, not a speck.
- **The print shows it**: a shaken or unfocused shot comes out soft.
- **Stars and points**:
  - Stars (1 to 5) go on the print's bottom border.
  - The breakdown and the points show beside the print as it slides in; a monster shows "x2".
  - The album shows stars on every print, and "N/60 found, points" in its footer.
- **Film**: four rolls, 144 exposures, enough for the sixty and for second tries.
- **Prints** are now 192x128, so they hold up in the credits.

## The credits montage (`PolaroidMontage`)
- After the end card, every picture taken comes up in order as a polaroid on the black:
  - a white card with a wide bottom border, the pencil caption, stars and the print's number;
  - tilted and dropped a little over the ones before;
  - each fades in, sits and fades away as the next ones come.
- The last seven gather into a collage, hold, and go together.
- Then one card: "N of 60", and the points.
- A big album never drags: the montage is capped at 80 seconds. Slow fades only, nothing flashes.

## The save indicator (`SaveIndicator`)
- Every save shows a translucent grey-white 35mm film canister in the top-right corner: the spool
  knob, the lips, a label band, and the leader strip with its sprocket holes.
- It fades in, swells slowly brighter and darker in contrast, and fades out, all within
  2.8 seconds (the owner: 3 seconds max).
- Every save plays it from the start.

## Tests
- `--story-from=1 --story-to=2`: 56/56 passed. The bird's picture is scored (clarity 89, focus
  100, framing 98, zoom 8: 4 stars), and the list is 60 long.
- `--story-from=20`: 27/27 passed, including:
  - the ring photographed (4 stars);
  - a switch shot zoomed x2;
  - the save indicator shown;
  - 62 subjects hung round the Hollow;
  - the credits playing the pictures back as polaroids.
