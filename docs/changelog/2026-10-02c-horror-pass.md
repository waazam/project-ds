# 2026-10-02c — The horror pass: the stalker made as horrific as possible, the crawler remodelled, the loose ends

Commit: this entry's commit on `main`.

The owner: "i am going to test it myself tomorrow, do all of your recommendations right now and make things as
horrific as possible for the stalker. also the game is called Dead Silent now not the hollow."

## The name
- **Every player-facing title already said Dead Silent:** the window, the title screen, the end card, the trailer and
  the exe's product name.
- **"The Hollow" was me:** it's only the forest area of Acts 2–11 (its scene and code), and I had been calling the
  game that in chat. I won't any more.
- **One internal name left alone:** the project's internal name ("Project DS" in `project.godot`) stays. Changing
  it would move everyone's saves to a new folder, and the window already shows Dead Silent.

## The stalker: as horrific as possible

### Its face and mouth (`tools/Blender/stalker.py`)
- **Needle teeth:** two crowded rows of them behind the lipless slit of its mouth, their tips showing through the
  closed slit. The lower row rides the jaw, so they bare as it opens. A dark throat behind them.
- **A wider gape:** the jaw opens far wider (the loom's gape nearly doubled).
- **The face, fixed:**
  - the jaw stood out under the flat mask like a beak, so the mask is now pressed flat over the jaw too;
  - the throat had bulged through the slit like lips, so it now sits well inside.

### New ways it's found
After its introduction (the storm walk to the footbridge stays the plain peek round a trunk, so it escalates):
- **Low** (new clip `peek_low_R/L`): folded down at the foot of the trunk, knees up by its shoulders, the near hand on
  the bark low, its head round the trunk at a child's height, tipped right over.
- **Clinging** (new clip `cling`): up the trunk, a metre and a half off the ground, pressed to its far side, a hand on
  either edge of the bark, the head out to one side. Only on a trunk tall enough to hold (checked first; otherwise
  round the side).
- **The owl:** its body turned away from you, its head right round on its twisted neck, looking at you over its back.
- **Round the side,** as before, but three times in ten the arm round the trunk has its elbow bent the wrong way.
- **The visibility rules:** they still use the tuned fixed points round the side. The new poses use the figure's own
  bones, so a low peek or a cling is judged where it actually is.

### What it does while it's there (`StalkerBody.Horror.cs`)
- **Its head follows you.** The eyes, and their shine in the lantern, stay on you as you move.
- **Unwatched, it creeps.** Over about nine seconds:
  - the neck draws out, the head sliding a quarter of a metre further round the trunk than any neck should allow;
  - the head tips over onto its shoulder;
  - the teeth bare a little.
  
  Look back, and it's further out than it was.
- **Watched, it doesn't move, except its mouth:** the slit opens slowly the longer you look, and the head goes on
  tipping. It still goes in a blink when its time is up, as before.
- **Its fingers drum on the bark,** one after another, while you're not looking. They stop dead when you look.
- **It's heard at the bark:** now and then, unwatched and within 18 m, a creak of bark under its weight or the drag of
  its nails, from where its hand is. Still never a voice, and never a sound as it goes (your rules).

### The Act 11 pursuer
- **It freezes when looked at.** Within 26 m it stops dead where it is, its head following you, tipping over, its mouth
  opening.
- **Look away and it comes on faster** than it walked (×1.35).
- **Its rags whisper** with its steps.
- **The steps match its stride** (the clip's length).

### The bunker's hallway scare
- It looms with its face straight at yours, the needle teeth bared.

### Under the hood
- **The bug I hit:** a held pose was being rebuilt on top of last frame's result, and the overlays compounded until the
  figure went to NaN. The animation player stops writing bones once a clip that holds its last frame has ended. Now
  the clip's pose is snapshotted and put back every frame before the overlays.

## The crawler, remodelled (`tools/Blender/crawler.py` → `assets/models/crawler/crawler_rig.glb`)
- **What it is now:** the owner's design (a pale, starved humanoid on all fours, its elbows and knees riding up higher
  than its back, its head hung low) as one hide over its bones:
  - the spine a ridge of knuckles;
  - the shoulder blades and hip bones standing up;
  - the ribs under the skin, the belly sunk to nothing;
  - pale skin with dark veins, bruised and grimed at the joints;
  - long hands and feet with long knuckled fingers and toes, black nails.
- **Its head:** long wet black hair hanging over its face; the sockets sunk to hollows; the jaw dislocated, hanging far
  too low, full of crowded teeth.
- **Detail:** about 79,000 triangles, 2048 px baked albedo and normal maps, a 35-bone skeleton.
- **In the game (`Crawler.cs`):**
  - the same limb solver as before plants every hand and foot on the steps, and now turns the skeleton's bones to it
    (one skin bending at the elbows and knees, not separate sticks);
  - the hands lie flat toward where it's heading, the fingers curling as a limb lifts and splaying as it lands;
  - the spine heaves with its breath and rolls with its gait;
  - the head snaps to its twitches;
  - the jaw hangs and works, gaping wider as it hurries;
  - the blacklight's glow still lies over it;
  - `--old-crawler` brings the old pieces back for a run.

## The other recommendations
- **The Act 4 stutter:**
  - The hitch log now counts pipeline (shader) compiles.
  - The ~135 ms hitch has none and little CPU time. It comes right where the test jumps the player along the trail
    (`WalkAlongTrail`'s teleport), and happens with the old stalker and with the fog off.
  - It is the test's jump, not something a player walks into.
- **Thin stains:**
  - The decal dresser now also treats a corridor (walls close on both sides) and a flight of stairs as indoors.
  - The long hallway and the stairwells had been left almost bare because their ceilings have no colliders.
- **The clip audit's walk-throughs:**
  - the ski gear at the end of the ski tracks gets a collider;
  - the other six flagged were meant to be so and are now listed as such: room 201's hacked doorway, the crawlspace's
    connector, its broken brick and light leaking out, and the turned-down bed's cover.
- **The trailhead deer's stretched fur:** projected from its own three axes now. Its sphere-wrapped UVs stretched the
  texture 49 times from the belly to the back.
- **The forest-acts preview tool** (`hollow_preview`): its six out-of-date checks brought up to the story as it is now:
  - the axe leans by the cabin;
  - the clearing's path can be walked round its stairs;
  - the bunker opens to its code;
  - checkpoint 9 wakes on the lake's shore at sunrise.
  
  It passes all of them.
- **A test's margin:** Act 14's "never far behind" check allowed a gap under 8.5 flights. The crawler's farthest gap
  varies with its random surges (8.2 and 8.5 seen on the same code, and the same with the old crawler's logic), so it
  now allows under 9.

## Tests
- **The full autotest:** 719/719.
  - New checks: the crawler is the remodel, planted on the steps.
  - The run's tally: the stalker peeked 15 times before the bunker, found round the side and clinging, with 8 creaks
    from the bark and 32 hands on the bark.
- **Frame rates while walking:** 151 fps on average over the whole game, as before; the stairwell with the new crawler
  170 (its old pieces ran at 157).
- **The continue test:** 925/925, 34 scenarios.
- **The clip audits:**
  - the forest acts: 40 z-fighting pairs as before, no walk-through props, and no UV faults (the deer's fixed);
  - the winter: 14, as before, and no walk-through props (was 7).
- **The forest-acts preview tool:** all checks pass (6 were failing).
