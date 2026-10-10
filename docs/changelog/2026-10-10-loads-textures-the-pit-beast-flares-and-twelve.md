# 2026-10-10 — Faster loads; real wood and concrete; the pit beast rebuilt; the catwalk breaks; green flares in the snow maze; and twelve things to make it scarier and more alive

The owner's list for the night:

> "We are going to do a big overhaul sweep in the loading between acts ... the textures yet again. The wood in the cabin and
> the ski lodge looks pretty bad. The repeating patterns in the red light green light hallway on the walls needs to be
> varied and the texture for all the concrete needs to be drastically upscaled ... the lovecraftian octopus monster be
> better connected and less blocky looking ... the catwalk in act 18 get visibly damaged ... guide the way in the snow
> tunnel in act 24 with flares."

and then: "implement all 12 of your things. Then do a debug and test then push it to the main."

## The loads between acts

- **Textures drawn once.** Every texture that was drawn in code as a level loaded (pixel by pixel: the bunker, the station,
  the church, the lodge, the stairwell, the sewer, the props) is now kept on disk after the first time, in
  `user://texcache/` (one folder per build; an old build's is cleared). A second launch reads them back instead of
  drawing them. `--no-tex-cache` turns it off.
- **Scenes kept.** A level's packed scene is loaded once and kept, so a death or a reload doesn't read it again.
- **The winter built later.** The winter woods and the lodge were built as Act 18 loaded. They're now built in the black
  after the drop into the sewer's pit (the heartbeat in the dark covers it). Their ground is computed on all cores at
  once (3.0 s down to 0.3 s).
- **Measured.** Every load prints `[load] ... ms to play`, with how long its textures and meshes took.
  - Act 21: 25.8 s down to 17.6 s.
  - Act 15's reload after a death: 9.4 s.
  - The Hollow's acts: about 6.7 s.

## Wood and concrete (`tools/textures/surfaces.py`, `SurfaceSets.cs`, `concrete_varied.gdshader`)

- **New surfaces.** Nine high-resolution surface sets are baked offline: 1024 px albedo, normal and roughness each,
  mipmapped and compressed.
  - Wood:
    - `wood_log`: the cabin's logs, with real grain running along them and checks in the ends.
    - `wood_boards` and `wood_floor`: the cabin's boards and its floor.
    - `wood_dark`: the lodge's dark woodwork.
    - `wood_pine`: the lodge's log walls.
  - Concrete:
    - `concrete_a` and `concrete_b`: newer pours and older, stained ones.
    - `concrete_clean` and `concrete_grime`.
- **The red light, green light hallway.** Its walls no longer repeat. A new shader projects the two concrete sets onto them
  and blends between them over the length of the wall. The walls are cast in pours a couple of metres wide, each a slightly
  different tone, with faint cold joints between. Water has run down from the top in streaks, and grime has gathered at
  the foot.
- **Everywhere else.** All the stairwell's and the bunker's concrete uses the baked sets.
- **Round logs.** The lodge's beams, rails, rafters and posts, the firewood, and the ending's cabin are round logs, and
  their texture runs round them and along them. On those, the log texture had shown its pale chinking rows repeating down
  the length of every beam, and at a distance a bluish moiré. They now have their own set, `wood_round`: the same wood
  without the chinking, turned so its grain runs along the log.
- **Warmer logs.** The logs are warmed toward old brown pine; the baked grey read white at dusk.

## The pit beast (`Leviathan.cs`, `octopus_flesh.gdshader`)

- **Every limb one smooth tube.** Each limb is a single skinned mesh on a chain of 37 bones, one ring of 18 sides to each
  bone. Before, each limb was 18 separate cylinders with a ball at every joint, which looked blocky at each one. The frame
  is carried along the limb without twisting, so the suckers stay underneath as it arches. The flesh's pattern is read
  from the limb's rest pose, so it stays on the skin as the limb bends instead of swimming over it.
- **Sizes.** Of the fourteen limbs:
  - four are thick, rising from low round the body;
  - five are middling;
  - five are skinny whips with no mouth, from up near the crown, tapering to a fine point. They writhe faster and further,
    and reach further or shorter than the others.

  The thick ones do the slamming; a skinny one slams only when no other limb is near the target.
- **The body.** It's finely rounded now (it was about forty facets across ten metres), with flatter lumps and folds round
  the waterline. Each limb leaves the body through a swollen collar of flesh, and swells out into the body at its root.

## The catwalk takes the slams (`BossRoom.Damage.cs`)

Where a limb comes down:
- the grating is dented: a dark, crumpled dish pressed into it (a decal with its own normal map), scraped bright round the
  rim;
- torn strips of grating curl up round the dent;
- the railing's span beside it bends, sagging down and out over the pit, with a groan of steel;
- a short spray of sparks comes off the steel.

It all stays walkable: nothing new collides, and the guard over the pit is untouched. The marks stay for the fight (the
oldest go after 22).

## Green flares in the snow maze (`Flare.cs`, `HeldFlare.cs`, `MazeExtras.cs`)

- **Along the way.** Seventeen road flares are stuck in the snow along the one right way through, 27 m apart, each burning
  green.
- **Taken (E).** A flare is struck and held low in the right hand, and its light lights the tunnel. It burns for four-fifths
  of the walk to the next flare at a walking pace (8 s for 27 m), so a steady walk gets there just after it gutters out. It
  gutters over its last three seconds and goes out.
- **Thrown (G, or B on a pad).** It's lobbed ahead, tumbling, bounces off the walls, and lands burning. The wendigo goes to
  it along the tunnels and stays crouched over it, sniffing, as long as it burns.
- **A risk.** A flare in the hand lets the wendigo see you from further away.
- **Its look.** The light is a soft, steady green, wavering a little (never a flicker). It fizzes, smokes a little, and
  sparks drift up off it. New sounds: `flare_strike_01/02`, `flare_burn_loop`, `flare_out`.

## The twelve

### 1. Hiding in the maze, and holding your breath (`MazeExtras.cs`, `WendigoHunter.Lure.cs`, `PlayerBreathing.cs`)
- **The hollows.** At seven of the maze's dead ends, snow has slumped from the roof into a heavy drift hanging across the
  tunnel. Its underside is crouch-high, so you can only get under it crouched. Behind it is a hollow.
- **Hidden.** Crouched in a hollow with no flare showing, the wendigo can't see you unless it's right on you, and it won't
  go in.
- **Sniffing.** If it comes past the mouth, it stops there and sniffs for five or six seconds.
  - Hold your breath (V, or up on a pad) and it moves on.
  - Breathe, and it hears you.
- **Holding your breath.** You can hold it anywhere. The view's edges close in a little. It lasts up to nine seconds, less
  the more winded you are, and then it's let go in a gasp, which carries.

### 2. A tin to throw in the crawlspace (`Knockable.cs`, `HeldProp.cs`, `WendigoArm.cs`)
- An old tin lies on the boards three cells before the first arm, and another before the third. Pick one up (E) and throw
  it (G).
- Where it clatters down, the arm in the wall claws after the noise for a few seconds. It doesn't reach for you and doesn't
  grab, so you can get under it.

### 3. Notes in the album (`PhotoCatalog.cs`, `PhotoLogPage.cs`)
- After the album's prints, there's a page of notes in pencil: a line for each thing on the list the first time it's on
  film. For example: "Stairs in the woods. Nobody builds stairs out here." and "It wears antlers. It talks in voices."
- There are 69 lines, getting drier, more tired and more frightened as the game goes on.
- They're in the photographer's own hand. R.H. is a stranger in the story, so the notes are yours.
- The album still opens on the newest prints; the notes are the next page.

### 4. The dial and the switches under the hand (`CodeLockOverlay.cs`, `RoundRoomPower.cs`, `PlayerCameraRig.LeanIn`)
- **The bunker's dial.** The view leans in over it while it's held up, and back after. Each wheel rolls round under the
  thumb and seats with a click.
- **Act 20's switches.** They're heavy: a stiff first inch, then the lever gives and snaps over into its notch with a
  clack. The view leans in to the switch and back.

### 5. The woods react (`AmbientLife.cs`)
- **Birds.** When the thing that follows you takes a new tree within 30 m, now and then a few birds go up out of the canopy
  over it, wings clattering. It's rare: a minute apart at least, a third of its moves, and never anywhere else. When the
  birds go up, it's there.
- **Frogs.** Along the lake's near shore, frogs croak. They go quiet as you come near, and come back slowly once you've
  gone. All of them go still for two minutes when something rises out of the water. New loop: `frogs_loop`.
- **Moths.** On summer nights in the woods (Acts 2 to 11, outdoors, not in the rain), two or three moths find the lantern
  and circle it, darting in to bat at the glass.

### 6. After the rain (`RainVfx.Puddles.cs`, `tree_solid.gdshader`)
- **Puddles.** As it soaks in, puddles fill in the hollows of the ground: still, black and glossy, with the lantern's light
  lying on them. They shrink away as it dries. They're fixed in the world: each 6 m patch of level ground has its own or
  none, the same every time.
- **Trunks.** The trees' bark goes darker and glossier as it gets wet, in runs (a new global, `wet`).
- **Drips.** The boughs keep dripping after the rain has stopped, slower. Water drips off the edges of the roofs in a slow
  line of drops while it rains and for a while after.

### 7. Things to knock over (`Knockable.cs`, `SkiLodge.Loose.cs`, `Cabin.cs`)
- **Where.** Bottles, a tin and a chair stand loose along the lodge's upstairs corridor walls. In the cabin there's a tin
  under the shelf and a whiskey bottle by the boots.
- **Walked into,** a prop is shoved the way it was hit and rolls or topples. It clinks (glass), rattles (tin) or scrapes
  and knocks (a chair). You walk through what you've knocked over rather than catching on it.
- **It's heard.** In the lodge during Act 23, something in the walls near the noise knocks back three times (at most once
  every 25 s). In the maze, the wendigo comes to look. New sounds: `can_clatter_01-03`, `bottle_clink_01-03`,
  `chair_scrape_01/02`.

### 8. Breath and the hand (`PlayerBreathing.cs`, `HeldItem.cs`)
- **Breathing.** The breathing is heard again, softly, but only once you've really run. (It had been muted while it was
  reworked.)
- **After a fright** (the wendigo's shriek, the arm bursting through, a slam near you on the catwalk, the safe light's
  visitor), the breath catches. The hand holding the light shakes gently, settling over about six seconds.

### 9. The safe lights (`SaveLamps.cs`)
- **Where.** A caged work light hangs on its cord at four saves in the dark:
  - the station's second room;
  - the sewer's way in;
  - the lodge's upstairs corridor;
  - halfway through the crawlspace.

  Each is warm, humming, and swaying slowly. New loop: `save_hum_loop`.
- **The visitor.** On about one run in three, and then at only one of the lights, once: stand under it a few seconds and
  there's a creak behind you. Something stands in the dark past the light's edge, watching. Turn to look and it's gone.
- `--fake-safety` forces it, at the crawlspace's light.

### 10. Wrong on a second look (`SecondLook.cs`)
- **The portrait's eyes.** On about one run in four, in Room 3, one of the burnt-out faces has eyes, wet and pale,
  following you down the hall. Stare straight at it and they close, and aren't there.
- **Someone in the picture.** On about one run in four, once, a photo taken out in the dark woods comes out with someone
  standing faint at the tree line at the edge of the frame. Nobody was there. It's only seen in the album.
- `--second-look` forces both.
- Not done: the third variant from the suggestion (a closed door found open). The lodge's four room doors all belong to the
  story, and opening a decor door would show an unbuilt room.

### 11. The dread (`DreadDrone.cs`)
- A low drone of two notes not quite agreeing (`dread_drone_loop`). It's heard only when something is near (within 20 m)
  and you can't see it. It swells as it comes closer unseen and falls away once you've looked at it. It fades in over about
  four seconds and out over about three.
- What it listens for:
  - the wendigo in the maze;
  - the crawler;
  - the lake's pursuer;
  - the thing in the woods, while it's really out at a tree within 14 m.

### 12. The dark woods (`Stalker.cs`)
- Leave the lantern off out there for more than half a minute and the thing that follows you comes on up to three times as
  fast. It takes its next tree sooner, and closer.

## Also
- **Held items and the world's lamps.** Held items are no longer lit by the world's lamps. Right up against the pit's red
  lamps by the catwalk's rail, the held lantern and its glove had lit up solid red. Held items are now lit by their own
  soft fills and the sky only.
- **Removed:** the temporary load prints in `StationInterior`.

## Tests
- **Act 18:** 20 of 20, start to the clean room.
  - 104 slams, 37 dodged.
  - The catwalk is dented and its rails bent along the fight.
- **Act 24, new checks:**
  - 17 flares;
  - a flare burns 8.0 s of the 10 s walk;
  - taken and burning;
  - thrown, it draws the wendigo;
  - hidden in a hollow, unseen;
  - holding the breath, it sniffs at the mouth and moves on.
- **Act 23, new checks:**
  - the tin taken, and thrown past the first arm, which claws after it;
  - a corridor bottle walked into and knocked over.
- **Photo preview:** the album's notes page.
- **The full autotest:** 795 of 796, start to credits, in 57 minutes.
  - The one failure: the test waited only 12 s at the drop into the sewer's pit, and on a cold start the winter is built in
    that black (5.7 s, plus its shaders). The wait is now 30 s.
  - Act 17 again: 15 of 15.
  - Act 25 again, with the round logs: 15 of 15.
  - Act 23 again, with the round logs: 97 of 97.
- **The continue test:** 1058 of 1058, 40 scenarios.
- **The clip audits:** no walk-through props.
  - The only pairs from tonight's work are the railing's spans meeting at the catwalk's corners, as the continuous rails
    did before.
  - The Hollow's other pairs are the pit's pipe walls, as they were.
- **The sign audit:** 0 blocking.
- **The settings' round trip:** 16 of 16.
