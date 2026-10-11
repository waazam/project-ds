# 2026-10-10c — The book's chapters; the lodge rebuilt (tall snowed-in windows, a stair that turns round stone, warm wood, its own wallpapers, 95 public-domain paintings) and burned (the lantern thrust in its face, struck away into the tree); flames with depth; the crawlspace that rots as you go; a hand that hunts; the wendigo reskinned; the Leviathan's five ways to strike; a neon inventory; the forest kept outside; the lighting

The owner, over the night: "finish your to do list, really focus on the details of making this game have a fantastic
atmosphere", and with it the outline's chapters, "to make it feel like a book".

## The chapters (`UI/ChapterCard.cs`, `tools/AudioGen/Chapters.cs`, `assets/fonts/Anton-Regular.ttf`)
The owner: "implement the chapters as big title drops in the proper sections as noted in the file ... Big Bold letters on
the screen", with Alan Wake 2's title drops for reference, and then: "dont make the chapter font red make it like that 3rd
picture kind of off white topeish".
- **The look.** One line of huge, heavy, condensed capitals across the middle of the picture, in an old paper's off-white
  taupe, the chapter's number small and spaced under it. It drifts very slightly larger while it holds, over a soft dark
  band, and fades away. It waits for the screen to be clear of black and never stops the game.
- **The face.** Anton (Google Fonts, SIL Open Font License: free to bundle in a game for sale), in `assets/fonts` with its
  licence.
- **The sound.** A breath of air drawn in, then one deep blow under the floor and a low dark chord left ringing.
- **Where they fall** (once each in a playthrough, saved as `chapter_N`):
  1. *Back to Nature*: Act 1, as the opening hands over to the player.
  2. *The Stairs*: Act 2. The first time the forest goes dead silent round the stairs, the game's own name comes up
     first, **DEAD SILENT**, in the trailer's serif, over a long low bell, and then the second chapter.
  3. *Root of the Problem*: Act 8, into the bunker.
  4. *Dawn is Breaking*: Act 12, waking at the lake.
  5. *Ever Upward*: Act 19, into the library.
  6. *The Great Door*: Act 21, the dais risen to the long stair.
  7. *Enjoy Your Stay*: Act 23, snowed into the lodge.
  8. *Return*: Act 25, back out in the woods.

## The lodge

### The lobby (`SkiLodge.Interior.cs`, `SkiLodge.Furnish.cs`, `tools/textures/make_window_pane.py`)
The owner: "i also hate this grey drab bland stone color of the room lets change that to feel more modern, we need to make
it feel more open, we need large windows around the room that let more light in ... the windows should be snowed in a
little. Make the stairs have some character and have a bend around the wall element".
- **Warm, not grey.** Wide warm planks underfoot (Poly Haven's Wood Floor). Honey boards on the lower walls where the grey
  stone was. The chimney breast is warm stacked stone (Poly Haven's Rustic Stone Wall).
- **Tall windows.**
  - Either side of the front doors, full height, gridded, with a transom over the doors. The east one starts above the
    reception's pigeonholes.
  - On the back wall, a tall one by the stair and two high over its upper flight.
  - Every pane is drawn on its own. The bottom row has snow drifted up the glass outside, and frost creeps in at every
    corner. Outside there are small drifts against the low windows and snow on every glazing bar.
  - Cool daylight falls in from the low ones.
- **The stair.** It's a dog-leg round a stone pier now.
  - The lower flight climbs from the hall toward the back wall to a landing with a rug.
  - It turns, and the upper flight climbs along the wall to the gallery.
  - A knee wall of the same stone closes the space under the upper flight. A lamp burns on the pier's cap.
  - Honey treads with their nosings proud, the runner mirrored down both flights, great log newels at the foot.
- **Lamps in their places.** The owner: "there are lamps in weird place like on a window". Every sconce on the hall's
  walls now looks along its wall for plain stretch, clear of doors and windows, before it's hung. The wreaths moved in
  between the doors' casing and the new windows. A floor lamp stepped clear of the new stair.

### The corridor and the rooms (`SkiLodge.Overhaul.cs`, `tools/textures/make_wallpapers.py`)
- **The runner.** The owner: "the carpet in the hallway needs to properly aligned like how we have it on the stairs". It's
  laid as the stair's is now: dark boards wall to wall, the runner down the middle in two mirrored halves, its pattern
  carried on through the corner to the gallery.
- **Nothing over the doors.** The owner: "the pictures you put in are clipping over doors". Nothing is hung over any door
  in the lodge now. The corridor has two portraits in each stretch between its doors, both sides, with a sconce between.
  Each room has a painting on the wall facing you as you come in.
- **The wallpaper.** The owner: "that wallpaper of the hotel needs a big tweak". Four papers drawn for the game, so there's
  no licence to carry:
  - an olive and gold damask whose medallions look like ink blots (the corridor, the dining hall);
  - roses on dusty pink (202);
  - a powder-blue trellis (203);
  - sage stripes with a climbing vine (201, 204).

  All of them are aged: seams where the rolls meet, a faint water stain, a flocked relief.
- **The paintings.** The owner: "we are looking to publish this game on steam to sell it between 3-5$. We need to make
  sure all the paintings we put in are free for commercial use ... Look for creep ones, spooky ones, autumn ones, winter
  ones."
  - All 95 are from Artvee: public domain, commercial use allowed. Every artist died before 1955, so they're in the public
    domain everywhere the game sells.
  - They include dark portraits, Fuseli's and Wilson's Hamlet with the ghost, Dürer's *Knight, Death and the Devil*,
    Doré's *The Witch*, nocturnes and moonlit harbours, cemeteries, crows over a marsh at dusk, wolves in the snow, autumn
    woods, winter roads, and vanitas still lifes.
  - The artist, title and dates of each are in `assets/textures/paintings/credits.txt`.
- **The pantry.** Its white tiles are aged to cream; they glared under the lantern.

### The rugs (`tools/textures/make_rugs.py`)
The owner: "Make sure all the new rugs in the lodge have more design details on them and make them more christmas themed."
Four drawn at 1024 px, each with a braided rim, a woven grain, and the wear of years:
- **Holly:** bottle green, a red border, a wreath of holly with its berries, snowflakes, a poinsettia in the middle.
- **Snowflakes:** powder blue, navy border, a ring of eight great snowflakes and one in the middle, a dotted band.
- **Pines:** oxblood, gold and green borders, a ring of little pine trees pointing out, stars, an eight-pointed star in the
  middle.
- **Candy cane:** cream, twisted red, white and green bands, a ring of baubles on their caps, a star in the middle.

### The roof (`roof_snow.gdshader`)
The owner: "the roof of the lodge also looks bland give it some color like a rustic royal blue thats been faded".
- Faded royal-blue standing-seam sheet: Poly Haven's Box Profile Metal Sheet, its red paint taken to blue, with chalky
  streaks where the meltwater runs.
- The snow lies on it in drifts: heaviest at the eaves, in tongues between the seams, patchy toward the peak.

### The Christmas tree (`tools/textures/make_fir_needles.py`, `SkiLodge.Christmas.cs`)
The owner: "the christmas tree still has a lot of gray textures". Its needles were the winter woods' snow-laden bough
photo. It has its own drawn fir sprays now, green, with no snow. The freeze's ice glaze no longer greys it or its
garlands.

### The lodge burns (`SkiLodge.Fire.cs`, `SkiLodge.Finale.cs`, `HeldLantern.Thrust`)
The owner: "The player raises his lantern towards the wendigo in this cutscene and it smacks it away causing the ski lodge
to go up in flames. The wendigo flees through the door as planned then the player now has urgency to follow him because
of the raging fire."
- **The lantern in its face.** It comes off the balcony, lands, rises over them and leans into their face. They thrust
  the lantern up at it. It flinches from the flame with a hiss and a half step back, then strikes: the lantern is torn
  from the hand. (It no longer goes out in the dive.)
- **Into the tree.** The lantern flies across the hall, turning over, and bursts against the foot of the old Christmas
  tree. The tree, years dry, goes up in a column of fire.
- **The fire walks.** From the tree it spreads through the hall over twenty seconds: the presents, the banner over the
  doors, the gallery's garlanded rail, the buffet's cloth, the wall under the gallery, the walk above, the great rug, the
  sofas. A roar and a crackle swell with it.
- **It flees.** It shrieks at the fire and goes for the doors, two blows and through. They get up, look back at the hall
  going up, gasp, and go after it.
- **Behind them.** From Act 23's end on, the hall is burning, its light pouring out of the doorway into the snow tunnel.
  The heat turns them back if they try to go in.

### The crawlspace (`SkiLodge.Crawlspace.cs`, `LodgeTextures.CrawlAged`, `WendigoArm.cs`, `Lantern.Douse`)
The owner: "make this whole crawlspace section way freakier/scarier ... the further you get it gets moldier and darker and
grimier ... the lantern go out once you reach the christmas lights ... shrink the size of the hand by 15% ... its animation
needs to be smoother and more like it is reaching to try and find you instead of flailing. Make it feel like it has
purpose."
- **It rots as you go.** The first third keeps the lodge's boards. The middle is old grey planks gone soft (Poly Haven's
  Weathered Brown Planks). The last third is black with rot and furred with mould. It darkens cell by cell within each,
  so no line shows. Cobwebs thicken in the ceiling's corners deeper in, and the brick darkens.
- **The lantern dies** where the Christmas lights begin: a draught, a sputter, out. The lights are the only light from
  there. It's lit again once they're out of the wall.
- **The hand** is 15% smaller, and it hunts now:
  - Alone, it feels its way along the cavity, slow, patting the far boards with its fingers spread, each finger working
    on its own.
  - Sensing them, it creeps toward them, stops dead to listen with its fingers curling one by one, then reaches,
    deliberately, and snatches shut at the end. It draws back, and starts feeling again.
  - Every move is a damped spring: it slows into every stop and never jerks.
- **The floor at the wardrobe** no longer flickers: the exit's boards were 4 mm over the dining hall's parquet.

### The rest of the lodge
- The fire in the grate has the new 3D flames, and they go out properly when the lodge freezes.
- The test's crouch check beam (an autotest fixture the owner took for snow) is drawn in a flat debug red.

## Flames with depth (`flame_volume.gdshader`, `napalm_volume.gdshader`, `FireVfx.cs`, `NapalmStream.cs`)
The owner: "fix the flames for the flamethrower and burning things to look more 3d and less 2d."
- **Every fire** is made of tongues with a body now, not flat cards turned to the eye: the cabin, the burning creatures,
  the lodge, its hearth.
  - Each tongue is a sphere drawn up into a teardrop, its surface pushed about by noise rising through it.
  - It's thick and hot through its middle, thin and red at its rim, and torn away with height.
  - It has a shape from every side, from above as well.
- **The flamethrower's stream.** Each burning glob is a lumpy volume stretched along its flight, boiling as it goes, and
  drawn up into a licking tongue where it clings.
- **No flashing.** The fires' light breathes slowly now; it used to flicker fast.

## The wendigo reskinned (`wendigo_skin.gdshader`, `Wendigo.RestSpace`)
The owner: "we really need to reskin the wendigo model it is just so gray and bland right now ... it needs to be very high
res for up close encounters, it needs to be horrific and intimating."
- **Why it was bland.** Its baked maps were thousands of tiny islands on one sheet, with next to nothing in any of them.
- **How it's drawn now.** Its hide is drawn in the shader, as fine as the screen at any distance. The detail is fixed to
  the body's rest pose, stored in the mesh, so it rides the flesh as it moves instead of swimming.
- **The hide.**
  - A starved corpse's grey, sallow in patches, bruised in the hollows, darker in the creases.
  - Veins standing up under it; dry patches split like old leather.
  - Raw wounds, wet red at the middle and black at the edge.
  - Old blood run down from the grin in its throat over its chest and arms, dried, with wet streaks.
  - Black frostbitten hands and feet; hoarfrost on its shoulders.
  - All of it raised and sunk into the light, with a sick wet sheen.
- **The skull and the rack.** Old yellowed bone, stained and split, with blood dried round the jaw.
- **Its head follows you,** eased, on top of whatever it's doing. The maze's hunter only does it while it can see you.

## The Leviathan's five ways to strike (`Leviathan.cs`, `BossRoom.Fight.cs`)
The owner: "there should be different attack styles for the lovecraftian monster boss fight for the tentacles slamming so
it all doesn't look the same."
- **Overhead:** reared straight up and smashed down.
- **Sweep:** coiled low off to one side and lashed across the catwalk in an arc.
- **Corkscrew:** reared high, its tip coiling round and round faster and tighter, then down in a spiral.
- **Double:** a short rear, two blows hammered onto the spot. The second can't crush; it's after the circle's gone.
- **Whip:** drawn far back, then cracked down with the tip trailing the body so it snaps.

The plain ones come while it's fresh, all of them as it rots. Every one lands where the red circle said, when it said. On
recovery the limb drags itself back off the catwalk.

## The HUD (`UI/NeonKit.cs`, `EquippedItemHud.cs`, `SaveIndicator.cs`, `GameSettings.HudColor`)
The owner: "we need the saving animated icon to not be gray and be more realistic. we need the inventory ui in the bottom
right to have a full overhaul giving it some personality. Instead of grey lets make a glowing green ... You should have
settings in the menu on changing colors for the inventory: white, red, purple, blue, yellow, orange. Make the colors look
tasteful in neon."
- **The inventory is neon.**
  - The item's icon is drawn in glowing tubes, one for each thing that can be carried, inside a bracket. The compass's
    needle settles and the lantern's flame breathes.
  - The name is in condensed neon lettering, and the next item sits small and dim beneath with its own little icon.
- **Its animation.**
  - The glow breathes slowly.
  - Switching items strikes the new one like a tube: a quick rise, a soft dip, then full.
  - A pickup sends a ring of light out round the icon.
  - A glint runs round the bracket now and then.
  - Nothing strobes.
- **Its colour.** Settings now has an "Inventory glow" option: green (the default), white, red, purple, blue, yellow or
  orange. Each is a tube's core over the light it throws, never pure and never a glare.
- **Saving** shows a real 35mm canister now: brushed metal shaded round its curve, a film stock's yellow label, an amber
  leader with its sprocket holes winding in. It's outlined in the HUD's neon, which breathes while it saves.
- **The compass.** The owner: "the ui for the compass needs to match the colors of the other ui elements ... the animation
  for the compass needs to be smoothed out when the player is turning, it looks very janky".
  - It's the same neon now: a tube for its line fading out at the ends, ticks every 15 degrees, the cardinals in the same
    lettering, a notch at its centre, and the objective a breathing neon diamond.
  - The jank was two things. Every tick snapped to a whole pixel of the game's 640 px picture, so they stepped a pixel at
    a time as you turned. And it followed the camera's raw yaw, frame by frame.
  - Now it's drawn between pixels, and its heading follows the view on a critically damped spring: quick enough never to
    lag a turn, smooth enough that a flick doesn't stutter it across.
- **The stamina bar.** The owner: "the stamina bar also needs to match the other ui color, we should give that more flare
  and a better animation too, also allow it to drain 25% slower".
  - A neon tube in a thin bracket. The breath left is lit; what's just been used lingers as a fading ghost.
  - It eases down with a bright head where it ends, and a pulse runs back along it while you run. A ripple of light runs
    up it as it fills.
  - Spent, it sinks to a low, dim, slowly flickering red until you can run again.
  - It drains 25% slower (0.11 to 0.0825 a second).

- **Quieter.** The owner: "the inventory ui needs to be shrunk a little to be made more subtle for its size, we also should
  get rid of that little circle dot animation that plays, maybe we can replace it with a short little glowing animation
  when you originally change to a new item".
  - It's smaller and dimmer.
  - The dot that ran round the icon's bracket is gone. In its place, switching to an item blooms a short soft glow behind
    its icon, which fades.
- **White by default.** The owner: "we should also make the standard ui color the white". White is the default now; the
  other six colours are still in the settings ("HUD glow").

## Cinematic (`UI/HudPresence.cs`, `Player/FocusPull.cs`, `StoryManager.PreloadLevel`)
From the owner's production brief: "Minimize screen clutter to maintain filmic framing ... a contextual HUD that fades out
completely during exploration", "smart focus pulling based on player proximity to objects of interest", and "make sure
the transitions between each act have minimal loading screens and transition smoothly".
- **The HUD only when it's wanted.** On the move, the compass and the inventory fade away completely.
  - The compass comes back softly when you stand still a moment, when there's a new objective, and while it's going
    wrong near the stairs (that's the story).
  - The inventory comes back on a switch or a pickup, or standing still a while.
  - The stamina bar was already only there while running or getting your breath back.
  - "HUD only when needed" in the settings turns this off.
- **The lens pulls focus.** A ray down the middle of the view finds what you're looking at, and the focus eases onto it
  like a focus puller's hand: quick onto something near, slower out to the distance. Beyond it the picture softens a
  touch, more the closer you're looking. Lean in to a note and the room behind it goes soft; down a long corridor almost
  nothing does. Under a metre and a half, what's nearer softens too. It's always gentle, so the dark still reads.
  "Focus pull" in the settings turns it off.
- **The next level is read while you play.** The hollow is read from disk on a background thread from Act 1's start. The
  trailhead is read once the flamethrower's in hand. A level change only has its own building left to do.
- **Already in place** from earlier passes: a colour grade for each place, eased from one to the next; the vignette,
  halation and grain of the finish; the letterbox easing in and out (never popping); the volumetric fog lit only by
  lights. New in this pass: bounced light and bloom (below).

## The wendigo, fluid (`Wendigo.cs`)
The owner, watching it come off the balcony: "this whole animation sequence needs to be worked on a ton, it is so janky and
not fluid at all ... making the wendigo move fluidly to really start nailing down how intimidating it is to the player."
- **Why it was jerky.**
  - Its clips were blended in 0.03 to 0.15 s, so pose to pose was a pop.
  - Its moves were straight tweens and snaps to marks.
  - The crouch before a leap lasted 0.07 s: no gather at all.
- **The body follows on a spring now.** What you see follows where the creature is and which way it faces on a critically
  damped spring. Whatever moves it (a straight run, a turn, a snap to its mark), the body eases into the move, carries its
  momentum through a change of direction, and settles. It's tight while it leaps, so it stays on its arc, and looser on
  the ground.
- **Blends and the gather.** Every clip blends over at least 0.22 s (gaits 0.3 s). Before a leap it gathers for a quarter
  of a second, folding down, then springs.
- **Its head follows you,** eased.

## The held lantern (`ItemMeshes.Lantern`, `HeldItem.cs`)
The owner: "make the things the player is holding look way better, like the lantern."
- Twice as finely turned. It has six wire guards and three hoops, the font's rolled seam and filler cap, the burner and its
  wick knob, soot clouding the top of the glass, the cap's vent holes, and a smoother bail.
- Every held thing has a faint cool rim light on the held layer, so its edges and metal read against the dark instead of
  going to a black silhouette.

## Sound kept where it belongs (`ForestAmbienceManager.cs`, `AudioDirector.CurrentSpace`, `WinterWoods.cs`)
The owner: "all inside every building in the game and underground in the tunnels you can hear the forest noises of bugs
and stuff, we need to get rid of that."
- Inside any building's room, any tunnel, or the snow maze (every interior has its reverb space), and anywhere more than
  3 m under the ground, the forest's birds, insects and wind are shut out entirely. They used to be muffled by only 9 dB.
- Underground, the weather is shut out as well.
- The winter woods' hush no longer lets go when you step into the lodge (that was where the summer's insects came back in).

## The lighting (`ForestAtmosphere.cs`)
- **Light that bounces.** A lamp's warmth on the wall beside it and the floor under it, the fire's orange round a room, the
  flares' green down a tunnel (screen-space indirect light). It's on with Shadows at High only: at the default it cost the
  round room its 60 fps.
- **A soft bloom** off what truly shines (lamps, fire, flares, lit glass): wide, faint, and only above full white.
- **The windows** are dimmer from inside; they glared.

## The retexture through the rest of the game (`SurfaceSets.Scan`, `tools/textures/polyhaven_sets.py`)
All from Poly Haven (CC0: free for any use, the Steam release included), laid over materials that were drawn in code:
- the forester station: Red Brick 03, Marble 01, Rusty Metal 02;
- the sewer: Mossy Brick, Rusty Metal 03;
- the pit's steel: Metal Plate 02;
- the stairwell: Concrete Wall 004, Concrete Block Wall 02;
- the bunker: Concrete Wall 006 on its walls, Concrete Floor Worn 001 underfoot.

## Fixes
- **The lodge popped in.** The owner: "when the player was outside of the ski lodge it was completely disappeared then
  popped back into view". Coming down the road, the woods' measured bounds ran out before the lodge's began. In that gap
  no area held the player and only the station was drawn. The gap now belongs to the nearest area.
- **The pantry.** The owner: "you also always get stuck right here when you are testing". The test's walk to a pantry
  drawer stopped 1.9 m out, which was inside the shelves across the pantry, and pushed into them for ten seconds every
  run. It now stops short of whatever's across.

- **Act 24 from its save** began inside the lodge, now burning, in the doorway's heat. Its start is out on the landing.

## Tests
- **The full autotest:** 802 of 804, from the opening to the endings. The two misses, both fixed and re-run:
  - **The round room's frame rate** (57 fps against the test's 60). The bounced light is now on High shadows only, and
    the focus pull's blur at its cheapest. Act 20 re-run: 30 of 30, 63 fps.
  - **The whiskey on the lobby's low table.** The test's walker pinned itself on the sofa and chairs out of reach. It now
    goes round to the table's open side, by the fire. Act 23 re-run: 100 of 100.
- **Acts 23 to 25 on their own:**
  - the lantern in its face, struck into the tree, the fire walking through the hall;
  - Act 24 from its save on the landing, the herd, down the hole, followed in;
  - the endings.
- **Act 18:** 27 of 27, with the five strikes.
- **The continue test:** 1058 of 1058, 40 scenarios.
- **The sign audit:** 0 blocking.
- **The settings' round trip:** 19 of 19, the HUD's colour, the contextual HUD and the focus pull among them.
- **The clip audits:** 41 z-fighting pairs in the hollow, 44 in the winter. The one new pair, the lobby's window reveals on
  the wall's own cut faces, is fixed (they're a centimetre inside the opening now). The rest are as they were.
- **A bug caught on the way.** The background read of the next level first went into the middle of GameFlow's chain of
  test drivers, so no test driver was started at all. It's in its own place now. The level change also waits for a
  background read a frame at a time; it never blocks on it.
