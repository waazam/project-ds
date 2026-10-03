# 2026-10-03d — The giant rises and grabs you; the stalker's face, its torn jaw alive, its breath and its rags

Commit: this entry's commit on `main`.

The owner: "for the long stairs part at the end of act 11, the giant should slowly raise itself up from the woods
beyond the stairs then reach out and grab you, then the screen blacks out and you wake up at the lake like normal for
act 12. We need the giant's movements to be more horrific and unnatural feeling. He needs to grab the player with a
janky haste that will freak us out. I think we also should do more work on the monster's face so that when you see it
in the bunker it is more horrific as well, creepier eyes and facial features and a jaw that is barely hanging on from
one side." Then, seeing the new face: "love the new face model, lets add some animations do the body with the shreds
of cloth moving around and the jaw needs some animation work to move it around so it feels more real and scary".

## What it was
- **The ending:** the cap seated, the giant was simply standing in front of the landing. The view was driven up its
  body, its eyes opened, it leaned in slowly while the view trembled, and the player blinked out into black.
- **The face:** a small pale mask with two matching deep sockets, small eyes far back in them, two nose slits and a
  lipless slit of a mouth with needle teeth.

## The giant's rise and grab (`GiantRise.cs`, `Act11Ending.cs`)
- **It rises out of the woods beyond the landing,** forty metres out:
  - head first, from wholly under the trees to standing;
  - sized so its eyes end well above the player's (about a 38 degree look-up), whatever the drop to the woods' floor;
  - the view follows its head up.
- **Never smoothly:**
  - it holds, lurches up several metres in a tenth of a second, and holds again;
  - now and then it sinks back a little before the next lurch;
  - once it oozes up slowly and smoothly, where nothing that size could move like that.
- **All wrong as it comes:**
  - stooped and listing to one side;
  - its head upside down on a drawn-out neck;
  - the spine straightening in jerks and the head snapping over a quarter turn at a time;
  - a shoulder hitched;
  - its head jolting out of place for a frame while the rest hangs still.
- **The sound:** a deep moan as it starts, and the creak of something that size bending at the lurches. The hum rises
  under it all to its maximum.
- **The stare:**
  - it stands still, its torn jaw hanging;
  - its eyes open red (eased in, not a flash);
  - the view trembles (gently);
  - the radio asks "Do you see him now?".
- **The grab, in stop-motion:**
  - its whole figure is frozen between poses;
  - the arm, its elbow bent the wrong way, comes at you in five juddering poses over half a second, with the body
    lurching in behind it;
  - a scream sting, and its hand fills the view;
  - the screen cuts to black and the hum cuts dead.
- **Over black,** after a silence: the radio's last "Did you see them?", the checkpoint, and they wake at the lake as
  before (Act 12).

## The face (`tools/Blender/stalker.py`, `StalkerBody.Giant.cs`)
- **The sockets don't match:**
  - its left is small, high and narrow, the eye far back in it;
  - its right is wide, deep and dropped lower, its eye too big for it and bulging half out.
- **Pinprick pupils** (dark against the red when the eyes glow): the big eye's turned up and off to the side, so it
  doesn't look where the other does.
- **The rest of the face:**
  - creases drawn into the sockets;
  - the cheeks hollow under the bone;
  - the nose gone, a cavity in its place.
- **The jaw torn loose on its left:**
  - there the face below the slit belongs to the jaw alone (a clean tear), raw and dark along the edge;
  - sinews still span the tear and stretch as it hangs;
  - the right hinge holds, and the game hangs the jaw from it (`JawHang`), swinging a little as the head moves, on
    every stalker: the bunker's, the woods' and the giant.
- Its needle teeth and its opening slit (the stare, the loom) work as before, on top of the hang.

## Alive (`StalkerBody.Giant.cs`, `StalkerBody.Model.cs`)
- **The jaw moves on its own:**
  - it lags behind the head on springs, swinging on as the head stops and back again;
  - it twists sideways on its one hinge;
  - it lolls;
  - spasms kick it now and then;
  - every few seconds, a short burst of chattering, the teeth knocking.
- **Its breath:** the chest heaving and the shoulders lifting, slow and ragged, the in-breath catching halfway every
  third breath or so.
- **The rags:**
  - gusts roll through them: the strips lift and fall, each chain catching the gust a little after the last;
  - their torn ends flutter, more in the gusts;
  - they swing looser and answer the body's own movement more.

## Fixes
- **The poses start from rest every frame:** channels the clips don't key (the head's and the jaw's positions) had kept
  the last frame's overlays, so a stretched neck would have compounded frame on frame.
- **The big eye** was placed a metre in front of the face, its socket carved too deep for the floor search. It now
  sits in its socket.

## Tests
- **The previews:**
  - `creature_preview -- --giant-grab`: frames through the rise, the stare and the grab;
  - `-- --stalker`: the face square on, with the jaw unhung, hung and hung far, and the eyes lit;
  - `-- --stalker-motion`: the face and the body every quarter second for six seconds;
  - each looked at and tuned: the giant's bow made a stoop (bent double at its size, its head came right over the
    landing), the compounding poses fixed, the big eye put back in its socket.
- **The full autotest:** 728/728. The ending's checks rewritten:
  - it rose out of the woods and they looked up into its eyes (46 degrees, size 30);
  - trembling under its stare, then its hand reached them and the screen cut to black;
  - the hum cut dead with the grab;
  - they wake at the lake.
- **The continue test:** 925/925.
- **The clip audits:** as before.
