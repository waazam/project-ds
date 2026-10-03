# 2026-10-02b — The stalker remodelled; light in the fog, the lantern's cage, bark and roots, stains, air, wet floors

Commit: this entry's commit on `main`.

The owner:
- "lets focus on the first monster in the game within the stairs sections, really enhance him on par to what we did
  with the wendigo";
- suggestions for him and for the game's look. They took all eleven: "do them in the most efficient order after you
  look around and see what are the most important details to nail down first, after all of this is done test, debug,
  and push it to the main."

The order: the stalker first (the model, its skeleton and clips, then the rags, the face and the close encounters,
all one pipeline). Then the two things that light everything: the fog and the lantern. Then the woods it hides in,
then the interiors, then the air.

## The stalker (`tools/Blender/stalker.py` → `assets/models/stalker/stalker.glb`)
Built, baked, rigged and animated by one script in Blender 4.0, run headless, as the wendigo was. It keeps the
design the owner signed off on (`StalkerBody.cs`'s proportions, so its sample points and placement rules are
unchanged):
- gaunt and hunched, about 2.4 m;
- heavy high shoulders crowned with gnarled root spikes;
- a small long head slung forward and low on a thin neck;
- arms hanging past its knees to long fingers;
- knees bent a little wrong;
- rags off the shoulders, arms and hips, and bark shards down the spine.

### The model
- **The hide:** one skin fused by a voxel remesh from:
  - the torso, lofted along a hunched spine, the belly sunk under the ribcage;
  - the limbs and neck as tubes, with the trapezius slopes to the shoulders;
  - the bones pressing through: eight ribs a side, 22 knuckles of the spine, the shoulder blades, the collarbones,
    the hip bones;
  - the cords of the neck, the sinews of the forearms, the Achilles tendons.
- **Finer in their own right:** the hands (long many-jointed fingers, knuckles, dark hooked nails), the feet (four
  long toes) and the head.
- **The face (suggestion 4):** a small stretched mask, too small for the long skull, its front pressed flat:
  - two deep sockets, the eyes far back in them;
  - a lipless slit of a mouth, straight across with its corners dragged down;
  - two slits for a nose;
  - stains run down from the eyes and the mouth.
- **The crowns and shards:** the shoulders' knotted roots and their spikes, splitting as they go (as the owner asked
  in September: spiked roots, not round and bubbly); bark shards down the spine and off the elbows.
- **The rags:** 45 torn strips (the mantle, the long ones down the back, the chest, the hip skirt, the sleeves), each
  with a torn end, bunched onto 23 chains of bones.
- **Detail:** about 85,000 triangles in the game, baked from a 420,000-triangle copy:
  - 2048 px albedo and normal maps;
  - the hide's micro-detail is a cracked, leathery, bark-like grain;
  - the game's tones (the face's paleness, the eyes) are carried in its vertex colours, as before.

### The skeleton and its clips (suggestion 2)
- **123 bones:**
  - spine, neck, head, a jaw for the mouth's slit;
  - collarbones, arms, hands and three joints in every finger;
  - legs, feet, toes;
  - three bones down every chain of rags.
- **The clips, posed with small IK solvers:**
  - **idle:** a slow breath through the hunch, the weight shifting, the arms drifting, the fingers working;
  - **peek_R / peek_L:** leaning out from behind a trunk on that side, the near hand up on the bark, the head tipped
    out toward you;
  - **duck_R / duck_L:** snatched back behind the trunk, low;
  - **walk:** one cycle of a low, long, stalking gait, the arms swinging loose;
  - **shove:** a wind-up, then both arms flung forward;
  - **loom:** drawn up out of its hunch, the arms reaching out, the fingers open, the mouth's slit opening;
  - **stare:** drawn up straight and dead still.

### In the game (`StalkerBody.Model.cs`, `stalker_skin.gdshader`)
- **It runs on the skeleton.** It is still drawn black, dissolving and fogged as before. On top of the clips, every
  frame:
  - **the head:** the drift and the twitch, and the look (the giant's and the pursuer's stare);
  - **the rags (suggestion 3):** each chain swings on a damped spring under gravity and its own movement, with a
    breath of wind, and is never folded back up through the body. Walking, peeking or ducking, they follow late and
    settle.
  - **the grip:** when it peeks from behind a trunk, the near hand goes onto the bark at the trunk's edge, its long
    fingers lying round toward you. `Stalker.FindGrip` finds the edge with rays stepped out sideways; a two-bone
    reach puts the wrist there to the millimetre.
- **Up close (suggestion 5):** I took my recommended answer to the question I asked you, since there wasn't a reply:
  - far off it is pitch black, a hole in the scene, as you asked in September;
  - within about 2.5 m (gone by 6.5 m) a very faint cold edge light traces its outline, its ribs and knuckles and the
    grain of its hide, from the baked maps;
  - in the Hollow it never comes that close, so it stays a silhouette there;
  - the bunker's scare and Act 11's shove are where it shows.
- **The eyeshine (suggestion 4):**
  - its eyes catch the lantern like an animal's: the flame lit, within 28 m, and its head turned toward you;
  - two points answering your light, brightest in the middle of each eye;
  - eased in and out, never a flicker;
  - the blacklight doesn't do it.
- **Where it's used:**
  - **the Hollow:** it is found in its peek, the hand on the bark. Caught looking, it ducks as it dissolves.
  - **the bunker's hallway scare:** it stands in its loom, the arms out at you, the mouth open, the eyes lit.
  - **Act 11's pursuer:**
    - it walks its stalking gait, its steps the clip's length, so the feet don't slide;
    - the shove is both arms flung at you;
    - at the foot of the stairs it draws up and stares after you.
  - **the giants (Acts 7 and 11):** the same model and gait at their size, in their haze.
- **No stutter at first sight:** the shader warm-up draws the stalker itself (skinned, fully dissolved, in front of the
  camera) before the fade-in, though it starts unseen (`ShaderWarmup.IWarmUp`).
- **The old figure built in code** is kept for the editor (and if the model is ever missing); `--old-stalker` brings it
  back for a run (measuring).

## The game's look

### Light in the fog (suggestion 6: `ForestAtmosphere.ApplyVolumetric`)
- **Volumetric fog, thin:** only what's lit shows in it.
  - The lantern's gathered beam is a shaft.
  - The lamps have haloes.
  - The sun gives shafts between the trunks (only a share of its light goes into the fog).
- **Its density by place:** open woods, underground (thickest), interiors, winter.
- **Never the sky or the ambient:** that would grey the whole view.
- **The lantern's own glow is barely in it,** so there's no haze over everything at the eye (no glare).
- **Settings:** "Light in the fog" in the menu (on by default). `--no-volfog` turns it off for a run.

### The lantern's cage (suggestion 7: `Lantern.BuildCage`)
- **What it is:** four thin struts, a wire ring above and below the glass, the hood and the base. They are drawn only
  into the shadows (never seen themselves).
- **What it does:** the glow casts them round the walls as soft dark bands that turn with the lantern. The flame has
  a size, so their edges are soft.
- **Where they fall:** the struts stand 40° either side of straight ahead, so what you look at is always clear
  glass. The hood dims the ceiling a little.
- **The projector I tried first:** a projected texture for the cage didn't map onto the light's directions the way I
  expected. Measured, it smeared the struts into the corners of the view, so I built real geometry instead.

### The woods it hides in (suggestion 8)
- **The bark:** Poly Haven's furrowed, mossy brown bark at 256 px with its relief as a normal map (worked out per
  pixel, since the trunks are built without tangents). It replaces the 32 px drawn bark on every trunk in the
  Hollow and Act 1's woods.
- **Rounder trunks:** firs have 12 sides (were 8), the deciduous trees and snags 10 (were 7).
- **Root flares:** four to six roots spread from every trunk's foot. Each rises out of the trunk a little above the
  ground and dives back into the slope a metre or so out.
- **Leaf litter and mud:** soft-edged patches under the trees, wherever the ferns grow. The photo is projected from
  above so the patches run into each other, and darkened to the woods' own floor. They have their own dice, so no
  fern or tuft moved.
  - Their first version stood out as pale discs in the dark; I darkened them and softened their edges.

### Stains and rot (suggestion 9: `DecalDresser`, `tools/Textures/make_decals.py`)
- **How they're placed:** found, not placed by hand. Over each interior, rays find every floor under a ceiling
  (or walled in on three sides, since some ceilings have no colliders; outdoors is left alone), then the walls round it, and a share of them get a decal. It is laid a few dozen columns
  a frame the first time the area is drawn.
- **What there is:**
  - water risen from the floor and dried in tide lines;
  - drip streaks down from the ceiling;
  - black mould;
  - dried pools on the floors;
  - standing water and wet seeps below ground;
  - spilt wine in the lodge (the party);
  - old blood in the bunker.
- **By place:**
  - **the station:** its lobby's stains only come in with its stained wallpaper (decay stage 2); it starts kept, as
    Act 13 has it.
  - **the stairwells, the long hallway, the library, the church:** their own mixes.
  - **the sewer:** wet.
  - **the lodge:** room 201 is left perfect.
  - **the bunker:** the hall and the CRT room. The looping rooms change, so they're left alone.
  - **left alone:** the pit, the round room (its webs), the woods.
- **Rules:** never on a door or anything that moves, never two of a kind side by side. They only darken, and they fade
  out with distance.

### Wet surfaces (suggestion 11)
- **Puddles:** decals with their own roughness map, smooth, so the lantern glints in them. They are in the sewer, the
  station, the long hallway, the church's crypt and the bunker.
- **Seeps:** wet streaks down the sewer's walls, done the same way.

### What hangs in the air (suggestion 10: `AirParticles`)
- **Indoors and underground, dust:** fine specks, lit like anything else, so they only show where a light falls (the
  lantern's glow round you, a lamp's pool).
- **In the woods, spores and seed fluff:** a little larger, drifting slower.
- **In the cold (the winter woods, the cold lodge), your breath:** a faint puff below the view with each breath,
  quicker when you're winded.
- **All slow and faint:** nothing flickers, nothing crosses the view fast.

## Tools
- **`creature_preview -- --stalker`:** the stalker's clips, the grip on a trunk, the edge light, the eyeshine, the
  loom in the dark.
- **`creature_preview -- --lantern`:** the cage's shadows in a dark room, and the beam in the fog.
- **`--no-decals`, `--no-volfog`, `--old-stalker`:** measuring switches.

## Tests
- **The full autotest:** 718/718.
  - The first run was 725/728. Its three failures were fixed:
    - the stalker's clip read empty after a held clip ended (now its assigned clip);
    - the station laid one stain: its ceilings have no colliders, so "indoors" is now also "walled in on three
      sides";
    - the dust underground went by the atmosphere's place flags, which the station doesn't set (now a roof or walls
      round you).
  - New checks:
    - the stalker is the remodel, peeking on its clip;
    - its hand is on the bark (three grips in three peeks, the hand on its mark);
    - its rags swing;
    - the pursuer walks on its legs and stares from the stairs' foot;
    - light in the fog in the Hollow;
    - the lantern's cage;
    - spores in the woods, dust underground;
    - the bunker's and the station's stains, and the station lobby's held back while it's kept.
- **Frame rates while walking:** 151 fps on average over the whole game (148 before this pass); the Hollow's acts
  82–112, the underground 107–175.
- **The continue test:** 925/925, 34 scenarios.
- **The clip audits:**
  - the Hollow: 40 z-fighting pairs, as before; no walk-through props;
  - the winter: 14, as before;
  - the one UV fault flagged is the trailhead deer's, an old model.
- **A hitch noted and ruled out:** a ~135 ms hitch on Act 4's walk happens with the old stalker and without the fog,
  at a test teleport. It isn't from this work.
