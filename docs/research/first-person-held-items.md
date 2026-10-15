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
