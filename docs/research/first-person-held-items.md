# How first-person games hold things (and how Project DS does)

Research for the 2026-10-14g arms and held-items pass. The owner's notes that led to it:
- "the arm itself is too long and unrealistic, especially while walking"
- "the axe and the hammer are not angled forward"
- "crowbar isn't held correctly"
- "the items in hands still look funky and like they're facing the wrong way"
- "the knife blade is too short"

**How reliable this is.** Only a few facts below could be checked against a source; the source is linked next to each.
Several reference pages refused automated fetching (the Valve Developer Community viewmodel page, the Amnesia wiki, the
TWHL view-bob tutorial). Bullets marked **(seen)** come from memory of the games and from footage descriptions, not
from a source that could be checked; check them against footage before treating them as fact. The **Ours** bullets
are what the game now does. Each was checked from the player's eye with the hand tools (`--grips`, the look-book, the
pose gallery, `--stride`).

## The rules most games share
- **Placement (seen).**
  - The main item sits lower right, its middle about 60–80% across the screen and 65–90% down.
  - An off-hand light sits lower left.
  - The bottom and side edges of the screen cut the item and the hand. The bottom middle stays clear.
- **The arm (seen).**
  - Only the wrist and the last 10–20 cm of the forearm show, coming in from the bottom edge or a bottom corner.
  - The elbow and upper arm are never on screen, and the arm never reaches further out than an arm can.
- **The viewmodel's own FOV.** Many games draw the held things through a narrower camera than the world's:
  - TF2's default is 54 (https://wiki.teamfortress.com/wiki/Viewmodel).
  - CS2 allows 54–68 (https://csdb.gg/commands/fov-and-viewmodel).
  - Fallout and Borderlands each have their own weapon FOV setting.
  - Converted to our vertical FOV, 54 is about 42°.
  - **Ours:** the held things share the world camera (70° vertical). To make up for it, they're kept close and small.
- **Real proportions.**
  - Hand: about 19 cm.
  - Forearm: about 25–28 cm. One sample gives forearm plus hand as 46.8 cm
    (https://journals.sagepub.com/doi/pdf/10.1177/0971097320100414).
  - Upper arm: about 30 cm.
  - The shoulder sits about 20 cm out from the eye, 25 cm below it, and slightly behind it.
  - A relaxed reach from that shoulder puts the hand 40–55 cm in front of the eye.
- **The walk's bob.**
  - Half-Life's defaults are `cl_bob 0.01`, `cl_bobcycle 0.8` and `cl_bobup 0.5`
    (https://developer.valvesoftware.com/wiki/HL_cvars).
  - Quake clamps its bob to between -7 and +4 units (https://ara-mdk.googlesource.com/platform/external/quake/+/9fd67c44777b350dc56f3e70c88963b0d966ffc7/quake/src/WinQuake/view.c).
  - Typical values (seen): 0.5–1.5 cm up and down and 0.5–1 cm side to side, one cycle every two steps; the view's turns
    lag by 2–5°, easing back over about 0.1–0.2 s.
  - The Long Dark hides an equipped light while you sprint (https://worthplaying.com/article/2016/9/23/news/100764-the-long-dark-all-early-access-update-adds-new-modes-features-skills-tweaks-fixes-trailer/).
  - **Ours:** the arm swings from an off-screen shoulder (`HeldItem.Shoulder`), so the hand moves on an arc and the sleeve
    leaving the view hardly moves. The dip is 0.024 rad, the sway 0.018 rad, and a small swing fore and aft at a run.
    The lag behind the view's turns is clamped (0.08 / 0.06 rad) and eased back. None of these are translations.

## The lantern
- **Sourced.**
  - The Long Dark had to move its lantern "to work better with first-person hands" (worthplaying, above).
  - Amnesia: Rebirth carries its lantern as a toggled item (https://primagames.com/gaming/amnesia-rebirth-beginners-guide-fear-survival-tips).
- **Seen.**
  - Amnesia holds the lantern by its bail in the left hand, low left, the body often partly under the bottom edge.
  - It swings like a pendulum.
  - Only the wrist and a little forearm rise from the lower left.
- **Ours:**
  - The fist is round the bail at (-0.22, -0.085, -0.48) m from the eye. It was (-0.23, -0.07, -0.64): the arm was
    stretched straight out past its own length.
  - The lantern's foot is just under the bottom edge.
  - It still swings as a damped pendulum.
  - The forearm, only 25 cm of sleeve, comes in from the lower left corner.

## Melee tools: the axe, hammer, crowbar and knife
- **Seen** (Half-Life's crowbar, The Long Dark's hatchet, hammer, crowbar and knife, Sons of the Forest's axe, Dying
  Light, BioShock's wrench):
  - A hammer grip near the bottom of the handle, the fist low in the lower right, often at or under the bottom edge.
  - The handle rises up and to the left out of the corner.
  - The head is up and away, leaning forward and in toward the middle by about 30–50°. The bit or face is turned in
    toward the middle so its outline reads.
  - Heavy tools bob the most.
- **The owner's suggestion for the axe** was to show only its top: the head and upper handle rising from the lower
  right edge, not the whole arm. The research agrees, and from the eye it reads best.
- **Ours.** Every hammer-grip item uses one convention (`HeldModels`):
  - The socket is the middle of the grip.
  - **+Y** runs up the handle.
  - **-Z** is the working end's direction.
  - The `Fist` pose closes its knuckles toward -Z with the back of the right hand out to +X.
  - Each item model is built to that frame, so it sits in the fist with no turn of its own. Previously the Stick pose's
    knuckles and the tools' working ends were some 70° apart, which is why the tools "faced the wrong way".
- **How each tool is carried:**
  - Held near the butt.
  - Leaned 30° forward in the hand's own frame. The Fist's forearm is built turned back by that much, so only the wrist
    cocks.
  - Turned 30–35° in toward the middle.
- **Placement:**
  - The axe, hammer and crowbar: the fist is down at the bottom edge, so the handle and head rise into the view.
  - The knife is shorter, so it's held a little higher, its blade forward and up (40°) and the edge underneath.
  - The crowbar is held by its straight end, just above the chisel; the gooseneck's claw is up at the far end, forward
    and down.

### 2026-10-15: the grip pass (axe, hammer, knife, crowbar, walkie)
The owner: the axe and hammer are "STILL not held right — look up references on how a human holds an axe in that
position". The knife, the crowbar and the walkie weren't right either.

**How real hands hold them (sourced).**
- **Hammer:** grip near the end of the handle, not choked up near the head, for leverage and balance. Make a fist with
  the thumb wrapped across the index and middle fingers, firm but not rigid
  ([Bob Vila](https://www.bobvila.com/articles/978-how-to-use-a-hammer/),
  [Bunnings](https://workshop.bunnings.com.au/t5/How-To/How-to-choose-and-use-a-claw-hammer/ba-p/229659)).
  Choking up toward the head is only for starting a nail. A variant has the thumb and index finger toward the head
  ([Apex](https://www.apexinds.com/blog/hammer-safety/)).
- **Hatchet:** the swinging hand goes at the end of the handle, just above the knob
  ([Crate Club](https://crateclub.com/blogs/loadout/how-to-use-a-hatchet-safely-a-comprehensive-guide-for-outdoor-enthusiasts),
  [Tools Trunk](https://thetoolstrunk.com/how-to-use-a-hatchet/)). Carried one-handed, the same hand stays there, the
  little finger against the swell of the knob.
- **Knife, hammer grip:** the fingers wrap the handle's belly and the thumb presses against the forefinger, locking the
  wrist. A variant lays the thumb along the spine
  ([Recoil](https://www.recoilweb.com/knife-hands-avoid-these-common-grip-mistakes-to-sharpen-your-edge-weapon-skills-153220.html),
  [Personal Defense Network](https://www.personaldefensenetwork.com/post/conventional-and-unconventional-knife-grips)).
  The guard or bolster sits against the index finger, the edge opposes the little finger (down when the blade points
  forward), and only the pommel shows below.
- **Walkie (consumer FRS/GMRS):**
  - Size: a Motorola Talkabout T55-series is 160 × 60 × 30 mm and a TLKR T5 is 140 × 55 × 30 mm
    ([manual](https://www.manualsdir.com/manuals/116305/motorola-t5522.html),
    [icecat](https://icecat.co.uk/p/vendorName/mpn/desc-1343018.html)).
  - Use: held a couple of inches from the mouth to talk, the push-to-talk held down throughout
    ([Outdoor Empire](https://outdoorempire.com/how-to-use-a-walkie-talkie),
    [Hytera](https://www.hytera.com/en/blog/how-to-talk-on-a-walkie-talkie)).
  - Hold (seen in photos): upright in the palm, the palm behind or beside the case, the fingers round it, a thumb or
    finger on the side's push-to-talk, the speaker toward the face and the antenna up.
- **Crowbar:** no source describes a carry; product pages only describe the parts (a flattened chisel end, a gooseneck
  with a claw). Seen: held one-handed near the straight end like a long hammer, the hook up top.
- **First-person games (seen, not sourced).** No page describing the viewmodels could be found (the searches turned up
  store pages only), so these are from memory of The Long Dark, Sons of the Forest, The Forest, Dying Light, Green Hell,
  RE7/8 and Half-Life:
  - Axes and hatchets are carried low right in a hammer grip, the hand near the butt, the head up and forward. The
    blade points away into the scene; the poll and the top of the head face the eye, never the cheek turned side-on.
  - Knives point forward and up out of the fist, the edge down.
  - Radios are held up and close, the face toward the eye.

**What ours did, against that (before → after).**
| | Before | After |
|---|---|---|
| Axe, hammer: head | Yawed 30–35° in: the bit's cheek and the hammer's side faced the middle of the screen | Yaw 8°: the bit and the face point forward; the eye sees the poll and the top of the head |
| Axe, hammer, bar: fist | At or under the bottom edge, only the top knuckles showing | Raised about 4 cm into the lower right, the knuckles and fingers round the handle in view; the same place for all three |
| Axe, hammer: grip height | Little finger at the knob / butt flare, only the knob below | Unchanged (it was right: knob centre 5 cm below the socket, the fist ±4.5 cm) |
| Handle angle | 30° forward, no roll | 30° forward (the Fist forearm's own lean, so only the wrist cocks) and rolled 10° in toward the middle, as a relaxed wrist holds it |
| Hand | Back of the hand out to the right, palm in, thumb over the index and middle fingers (the `Fist` pose) | Unchanged, now visible |
| Crowbar | Yawed 30° in: the gooseneck curled toward the middle | Yaw 0°: the hook up top, curling forward and down; fist on the tape, only the chisel below |
| Knife | Guard 2.5 cm above the index finger; 40° forward | Guard against the index finger; 45° forward, edge down. 55° was tried: it swung the forearm and cuff up into the view |
| Walkie | In the round-handle `Fist` by its bottom 6 cm, the case standing on the fist, the fingers through the 6 × 3.4 cm case | In the palm by its side (the `Camera` pose's box hold): the fingers round its back and clip, the thumb up the front edge, the lower half in the hand, the grille clear; a little higher and nearer (0.2, −0.19, −0.38) |

- **Walkie model:** 6 × 3.4 × 15.2 cm with a 12 cm antenna. It matches a Talkabout (6 × 3 × 16) and is unchanged. Its
  push-to-talk is on the left side, where a right hand's fingertips would reach round to it.
- **Placement across the set:**
  - The axe, hammer and bar fists sit at (0.235–0.24, −0.225 to −0.235, −0.41 to −0.42), all at 30° forward lean.
  - The knife is 1–2 cm higher, its shorter blade leaning further forward.
  - The walkie is higher and nearer still, as a radio is held up to be heard.
- **Checked** from the eye (`--grips` and the look-book, day and dark, ahead, up and down, walking) and square on
  (`--hands-studio`).

## Lighter and matches
- **Seen** (The Long Dark, Amnesia, RE7):
  - One hand, held close (30–40 cm from the eye) and fairly high, so the flame sits at about 55–65% down the screen.
  - A lighter upright in the fist, the thumb on the wheel.
  - Very little sway.
- **Ours:**
  - The `Small` fist in the same frame, upright.
  - At (0.17, -0.155, -0.36) m, lid up.

## Camera
- **Sourced.**
  - Fatal Frame raises the camera into a full-screen viewfinder with no hands drawn (https://www.gamerevolution.com/guides/30329-fatal-frame-walkthrough).
  - Outlast's camcorder is raised to the eye or carried "down by your side" (https://godisageek.com/2013/09/outlast-review/).
- **Ours:**
  - Lowered, it's carried low right by its right end, the lens away and the thumb up its back. It used to lie over the
    top.
  - Raised, it gives way to the full-screen viewfinder, which already worked that way.

## Flare
- **Seen** (The Long Dark, Metro):
  - A fist near the base.
  - Upright, tilted forward 10–30°, the burning end up and away.
  - The forearm shows.
- **Ours:**
  - The `Fist` pose round the stick.
  - Leaning 30° forward with the wrist, at (0.23, -0.2, -0.45) m.

## Flamethrower
- **Seen** (RE7's burner, Far Cry, Wolfenstein):
  - Low right, the nozzle forward and converging on the middle of the view.
  - A pistol grip.
- **Ours:** unchanged this pass. The wand (about 1 m, like the M2's) is held in both hands with the Stick pose.

## Keycards, keys and notes
- **Seen:**
  - Most games don't hold these as a held model at all: they're used from the inventory or read full screen
    (Firewatch, RE7).
  - When one is shown, it's pinched between the thumb and the first finger.
- **Ours:** they show only in a use (HeldUse) and still use the Stick fist. A pinch pose is left for later.

## The cap (the newel post's stone cap)
- No reference; this one is our own call.
- The ball is 20 cm across and hides any hand round its 7 cm neck from the eye.
- **Ours:** it's carried on the open hand under its plinth (the `Cradle` pose, palm up, the fingers cupped round the
  edge). The forearm is seen leading to it.

## Sizes (cm): before, the real thing, after
| Item | Before (held / world) | Real reference | After (held / world) |
|---|---|---|---|
| Knife blade | 11.8 / 23.5 | Hunting knife: 10–20 blade, 11–12 handle | 16 / 16 (handle 12.7 / 11) |
| Knife blade depth | 2.5 / 2.6 | About 3 | 3.0 / 3.0 |
| Axe, overall | 64 / 75 | 26" boy's axe: 66 | 64 / 66 |
| Axe bit (edge height) | 13.8 / 16.6 | About 10–12 | 11.5 / 12.4 |
| Hammer | 33.6, head 14 | 16 oz claw: 33, head 13 | Unchanged; held by its butt |
| Crowbar | 57 / 81 | 24" wrecking bar: 61 | 61 / 61 (world model at ×0.75; the prying motion now ×1, was ×0.9) |
| Lighter | 3.8 × 5.8 × 1.3 (flick shown ×1.25) | Zippo: 3.8 × 5.7 × 1.3 | Unchanged; flick ×1 |
| Compass (world) | 10 across | Pocket compass: 5–6 | 6 |
| Camera body | 14.3 × 11.3 (with the prism) × 4.2 | K1000: 14.3 × 9.2 × 4.8 | Unchanged (a little tall) |
| Lantern | 26.8 to the cap, 30.6 to the bail top | Hurricane lantern: 25–35 | Unchanged |
| Flare | 24, 3.4 across | Road flare: 23–30 | Unchanged |
| Walkie | 6 × 3.4 × 15.2, aerial 12 | '80s handheld: about 6 × 4 × 17 | Unchanged |
| Flamethrower wand | About 103 | M2 gun: about 1 m | Unchanged |
| Keycard | 8.6 × 5.4 | 8.56 × 5.4 | Unchanged |
| Keys (Act 1, the font's) | 15.7, 22.4 | Old church keys: 10–20 | Unchanged (old iron keys) |
| Cap | Ball 20, neck 7.2 | Newel ball: 10–20 | Unchanged (level geometry) |
| The arm | Sleeve 48 cm straight back from the wrist; fist 64 cm out | Forearm 25–28 | Sleeve to the elbow (about 31 cm from the wrist); fist about 50 cm out |
