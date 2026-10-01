# 2026-09-30c — The wendigo, remodelled and rigged; display settings

Commit: this entry's commit on `main`.

The owner:
- **The wendigo, the game's main monster:**
  - it isn't tall enough; it needs very lanky, long arms and long legs;
  - a huge modelling pass, very detailed: ribs exposed from the chest, gaunt;
  - far more polygons and baked maps, a micro-surface pass that holds up close;
  - a real skeleton, so its legs bend when it jumps and lunges;
  - real hands and clawed fingers, so it can grab at the player through the wall.
- **Display:**
  - the bottom letterbox bar doesn't reach the bottom of the screen; a thin line of the game shows under it;
  - add resolution settings to the menu.

## The model (`tools/Blender/wendigo.py` → `assets/models/wendigo/`)
Built, baked, rigged and animated by one script in Blender 4.0, run headless.

- **Its size:** about 4.7 m to the antlers' tips (was 3.6), hunched; the hips at 2.4 m.
  - The arms hang past its knees to its hands, more than 2.2 m from shoulder to fingertip.
  - The legs bend backward like a deer's: thigh, a forward knee, the shin back up to a high hock, a long foot, three
    clawed toes.
- **The flesh:** one skin, fused by a voxel remesh from:
  - the torso, lofted along a hunched spine, the belly sunk to the backbone under a flared ribcage;
  - the limbs, neck and tendons as skinned tubes;
  - the bones under the skin: ten ribs a side, the spine's knuckles down the back, the shoulder blades,
    collarbones, the breastbone, the hip crests, knees, elbows, heels, every knuckle;
  - the sinews down the forearms, the cords of the neck and thighs, the Achilles tendons.
- **The chest:** a hole torn in its left side below the breastbone, torn flaps of skin hanging into it. The ribs
  show through it as bare bone, two of them snapped. Behind them is a dark red cavity, and in it the heart of ice,
  glowing faintly blue.
- **The head:**
  - an elk's skull, long and narrow: the braincase, the temples pinched behind the orbits;
  - a ring of bone round each deep eye socket, a pinprick of cold light in each;
  - the long face tapering to the nose's hollow;
  - long uneven teeth along it;
  - a man's jaw hung under it, too big, full of teeth, strings of dried lip between;
  - a great rack of antlers, the left snapped short.
- **The hands:**
  - a long palm, four long fingers and a thumb, each knuckled three times;
  - frostbitten black from the wrist out, ending in hooked black claws.
- **The mantle:** about 300 clumped ribbons of matted black hair off its shoulders and down its back, frosted at
  the tips.
- **Detail and maps:**
  - the in-game mesh is about 110,000 triangles, baked from a 195,000-triangle high-detail copy;
  - 2048 px albedo and normal maps;
  - the albedo is the colours painted on the high copy (ash grey mottled with bruise purple, frostbite at the
    extremities, sore red round the tear, bone with hairline cracks and stains) multiplied by baked ambient
    occlusion;
  - the normal map is the high copy's micro-detail: pores, fine wrinkles, raised veins, cracks in the bone.
- **The skeleton:** 64 bones.
  - The torso: hips, spine, chest, two neck bones, the head, the jaw.
  - Each arm: the collarbone, upper arm, forearm, hand, and three joints in every finger and the thumb.
  - Each leg: thigh, shin, the long foot, the toes.
  - The skin is weighted to its nearest bone, blended across each joint; the skull, teeth, claws and antlers move
    rigidly with their bones.
- **The animations,** posed with leg and arm IK in the script:
  - **idle:** hunched, breathing slow and deep, its arms swaying, its fingers working;
  - **crouch:** down hard to spring, the hips dropped, the knees jutting, the hocks raised, the arms swept back;
  - **air:** launched with its legs straight; they fold up under it as its arms reach up and out, claws spread.
    It ends with the legs reaching down;
  - **pounce:** flung flat at its prey, arms and claws thrown forward, jaw open, legs trailing.

## In the game (`Wendigo.cs`)
- **Animation:** it runs on its skeleton.
  - It stands in the idle, each appearance at a different point in its breath.
  - A leap crouches it, then plays "air" (up into the trees) or "pounce" (down at someone), stretched to the
    leap's length.
- **On top of the animation:**
  - its head still hangs to one side and snaps to new angles;
  - its jaw works when it speaks. In the finale it mouths "STARVING..... FREEZING..... FOREVER....".
- **Lighting:** the bone keeps a faint pallor and the skin a cold rim light, so it reads in the dark without
  glaring. The eyes and the heart glow.
- **The skull on the dining hall's platter** is the new head, on its own model.
- **The finale:** the cutscene's "turn and look up at the balcony" now really looks up. The camera only turned
  side to side before, so the wendigo stood above the frame.

- **The finale:** the player now stumbles back out from under the balcony over the doors as they turn at the howl.
  From under it, the balcony's own edge hid the wendigo on the far side; only its antler tips showed.

## The arm through the wall (`WendigoArm.cs`)
- **The model:** the wendigo's own arm and hand (`wendigo_arm.glb`), about 17,700 triangles with 1024 px baked
  maps, skinned to its upper arm, forearm, hand and every finger joint.
- **How it moves:**
  - the two-bone solver drives the real skeleton, so the elbow bends as the arm reaches;
  - every finger joint curls toward the palm as the hand clenches and opens;
  - blood still runs off it, worse each time.
- **Unchanged:** its colliders, so it's still a wall to someone standing and passes over someone crouched.
- **Frantic** (the owner: desperate to get at the player, starved):
  - its aim jerks to a new spot every few hundredths of a second, and it trembles all the time;
  - its fingers claw open and shut fast;
  - every half second to a second it lunges straight at the player's head with the hand splayed, snaps it shut
    at the end, and drags it back toward the wall, scraping.
- **Grabs:** only someone standing can be grabbed; under its lowest reach, crouched, it can't get them.

## The crawlspace's walls
- **The bug:** the owner could see through them in the stud space. Each wall panel stopped at its cell's edge,
  set 4 cm in from it, so where the cavity turned or branched, two neighbouring panels left a 4 cm slit at the
  corner with the outside showing through.
- **The fix:** every panel now runs 6 cm past both ends of its cell, overlapping at the corners, and its
  collision does the same.

## Display (`CinemaFrame.cs`, `GameSettings.cs`, `UiKit.cs`)
- **The letterbox gap:**
  - The cause: the bars are drawn at the game's 640×360 and scaled to the window. When the window isn't a whole
    multiple of that, a bar sized exactly to the screen's edge could stop a fraction of a pixel short.
  - The fix: each bar now runs well past its edge of the screen and is rounded up to a whole pixel.
- **New settings (main and pause menus):**
  - **Windowed:** fullscreen (the default: borderless, at the screen's own resolution) or a window;
  - **Window size:** the window's size when windowed, from 1280×720 up to 3840×2160, offering only sizes that fit
    the screen.

  Both are saved. F11 / Alt+Enter still switch fullscreen and windowed, and now remember the choice.

## Tests
- Acts 22–23 with the new wendigo, the arm and the finale: 109/109.
- The full autotest and the continue test landed with 2026-10-01 (the same commit); see that entry.
