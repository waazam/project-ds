# 2026-10-11 — The owner's song through all of Act 24, its synths as the sound of progress; your own body, seen looking down, with a shadow that has arms; the tools seen doing their jobs; the climb heard through the load; a body that tires and fears; the wendigo's idle habits; a tune for each stretch of the story; things that change behind your back; the best pictures closing the credits; captions, rebinding and a shake slider; the Steam page's art, copy and achievements; a quiet pacing log

The owner: "do all 12 in the most efficient order you can". They added: "we need the timer to either be hidden or only
viewable in the pause menu", and for the first-person body, "it needs to look really really good, we should probably
hold off on doing this last or do a lot of modeling of the body before we implement it". On the music: "use the song for
the whole act 24 wendigo fight and use some of the synths in the song as sound ques when we are picking up
objects/progressing objectives".

## The song in Act 24 (`Audio/Act24Score.cs`, `assets/audio/music/petty_theft/`)
The owner's track, "Petty Theft" (the trailer's), cut by the owner into eight-bar loops at 85 bpm, plus one-shot impacts,
a fall and a tail. The score picks the loop that fits the moment and only changes on a bar line, so the beat never breaks:
- **Alone in the maze:** the intro, then the build, low.
- **The wendigo hunting** (it's heard or seen something): the main theme, A and A2 alternating each time round.
- **Chased:** section B, with an impact as the chase begins.
- **The trenches and the cavern:** section C.
- **The flamethrower in hand:** the fall, then section D with an impact on its downbeat.
- **Each burn** lands an impact. After the second burn comes the final A; after the third, the climax.
- **It dies:** the last impact and the song's tail, then silence.

The level sits under the storm and the creature's sounds, and rises a little as it closes in.

## The song's synths as cues (`Audio/SongCues.cs`)
Picking things up, notes, opened locks, solved puzzles and checkpoints each play the next synth stab in the owner's order
(`synth_1` → `synth_2` → `synth_3`). Underground (tunnels, caverns, the snow maze, the sewers) they use the echoing set
(`synth_4` → `synth_5` → `synth_6`). In Act 24 each one waits for the song's next beat, so it lands in time.

## Your own body (`Player/PlayerBody.cs`, `tools/Blender/player_body.py`, `assets/models/player/player_body.glb`)
Done last, as the owner asked, and modelled in Blender first:
- **Who.** A day hiker in late October:
  - a dark olive softshell (zip, chest pockets, collar, the hood rolled at the back, cuffs);
  - a small rust day pack (padded straps, a sternum strap and buckle);
  - indigo jeans, worn at the thighs and knees and stacked over the boots;
  - brown leather hiking boots (black sole, toe cap, welt, padded collar, tan laces criss-crossed through eyelets);
  - dark gloves and a charcoal beanie.
- **How it's made.** Each garment is lofted on the body and fused with a voxel remesh, so it drapes as one piece. A
  high-detail copy carries each material's own weave (softshell grain, denim twill, leather grain, the sole's lugs) and
  is baked to 2048 px albedo and normal maps. It is rigged to 27 bones.
- **Posed every frame, no canned clips:**
  - the stride is in step with the footsteps, so each foot comes down as its step sounds;
  - each foot is carried the way you're going (forward, back or sideways) and set on the ground under it (stairs,
    slopes), with the knees bent by IK;
  - the hips dip on each footfall and turn with the leading leg, with the chest turned back against them;
  - you lean into a run, and crouched the hips sink and the knees fold;
  - standing still, the body lags your view, and past 50° it turns after it with a small shuffle of the feet;
  - in the rowboat you're seated.
- **What you see:** looking down, your hips, legs and boots. The chest, arms, hands, head and pack appear only in your
  shadow (the hands in view are the held items' own). As you look down, the body moves back from your eye, so you see
  your boots rather than the inside of your collar.
- **Your shadow** has the whole figure: arms swinging, the pack, the left arm out in front while the lantern is raised.
  The lantern and a held flare don't cast it, because right beside them it made a black blade across the view. Every
  other light does.
- The body is hidden whenever the view isn't your own eye (cutscene cameras).
- **A setting:** *Body in view* (on by default). Off, the body appears only in your shadow.
- A preview harness: `res://scenes/levels/body_preview.tscn` takes shots from the eye of standing, walking, running,
  backing up, side-stepping, crouching, turning and stairs, into `test-output/body/`.

## The tools seen doing their jobs (`Player/HeldUse.cs`)
When a tool is used, it comes up in the right hand toward what it's used on, then goes away again:
- a keycard held up to the reader and swiped down through it;
- a key pushed in and turned a quarter;
- the crowbar worked under the crate's lid and levered down, once for each nail;
- the lighter raised and lit, held to the wick or the web;
- the knife drawn across;
- anything else (the chalice, the bookmark) reached out and let go.

This covers the lodge's doors, the church (candles, font, chalice), the round room's webs, the library's bookmark, the
station's doors and the iron door's three pieces.

## The climb heard through the load (`Systems/BlackBridge.cs`, `tools/audio/make_climb_loop.py`)
The black after Act 1's stairs is no longer silent. The climb carries on in the dark while the hollow is built: wooden
steps slowing, breathing with them, the stairs' hum, a heartbeat far down. It's a loop that plays on its own while
nothing else can run, and it fades once the new level has started. Other long loads get the winter wind.

## A faster Continue (`ForestScatter.cs`)
Continuing past the station no longer builds the hollow's forest scatter, which nobody would see again.

## A body that tires and fears (`Player/BodyState.cs`)
Health and fear show on the body, not as bars:
- **Spent:** low stamina or a long exertion. The edges of the view darken a little, a heartbeat comes up, and the hands
  shake.
- **Afraid:** each fright raises fear, which fades over a few seconds. The heartbeat quickens and the view sways gently.
  None of this applies when Head motion is off.

## The wendigo's idle habits (`WendigoHunter.Beats.cs`)
When it stops on a patrol or a search, it sometimes does something before moving on. Each is chosen once per stop:
- **Sniff:** it lifts its head and scents the air.
- **Prints:** it crouches to your footprints.
- **Wall:** it drags its claws down the nearest wall.
- **Listen:** it goes still and turns its head toward a sound.

## A tune for each stretch (`tools/AudioGen/Motifs.cs`)
One slow lullaby in A minor runs through three places:
- **A music box upstairs in the lodge,** winding down: slower and flatter, its last note never comes. It plays when
  snowed in, and as one of the lodge's haunts from the next room.
- **A church bell far across the snow,** three tolls every couple of minutes, while you're out in the winter woods.
- **On the radio:** a quarter of what the radio picks up is the lullaby, thin and swimming in static.

## Things that change behind your back (`SkiLodge.LookBack.cs`)
In Act 23's lobby, each happens once, only after you've seen the thing as it was, and never while you can see it:
- the wing chair by the west wall turns to face you;
- wet bare footprints cross the boards from the front doors to where you were just standing.

There's no sound when it happens; you'll only find it.

## The best pictures close the credits (`UI/PolaroidMontage.cs`)
The five highest-scored photographs move to the end of the polaroid montage, as its closing collage.

## Accessibility (`Systems/KeyRebinds.cs`, `UI/RebindButton.cs`, `UI/SoundCaption.cs`, `UiKit`, `GameSettings`)
- **Keys:** every action (14) can be rebound in Settings. Click, then press the new key; Esc cancels. A key taken from
  another action is freed from it.
- **Sound captions:** short captions for the sounds that matter ("[knocking, behind]", "[a howl, far off]"), with
  which side they came from.
- **Screen shake:** a slider from none to full.
- **Hold to crouch:** an option to hold the crouch key instead of toggling it. A cutscene's own crouch is left alone.
- **Colour:** the HUD is one colour of the player's choosing (white by default), and nothing in it is told apart by
  colour alone, so it reads the same to colour-blind players. The keycard reader's green light always comes with its
  beep and the door opening.
- The settings now scroll, in the main menu and the pause menu alike.

## The Steam page (`tools/steam/make_store_art.py`, `docs/steam/`, `Systems/Achievements.cs`)
- **Art:** generated from the game's own frames, with the picture cut out of its letterbox, lifted a little, and the
  title set in Anton:
  - the capsules: header, small, main, vertical and library;
  - the library hero (no text) and the library logo (transparent);
  - five 1080p screenshots.

  It's good enough to set the page up. Before launch, the wide capsules want a framed beauty shot.
- **Copy:** the short description, the about section, tags and system requirements, plus a 60 s store trailer cut
  from the teaser with the winter added (`docs/steam/store_page.md`).
- **Achievements:** 20, kept in `user://achievements.cfg` across every save slot, each with its Steamworks API name:
  - the story's turns;
  - the five endings, earned when the ending plays out to the credits;
  - the camera's four (first picture, ten subjects, every subject, a five-star picture).

  The Steam hookup is one call in `Achievements.Unlock` (`docs/steam/achievements.md`). A save made before
  achievements were added is credited with everything it has already passed.

## The play-through pass (`Systems/PacingLog.cs`, `UI/PauseMenu.cs`)
- **The timer** shows only in the pause menu, under PAUSED: the chapter and the time played.
- **The pacing log:** a quiet record in `user://pacing.txt` with nothing on screen. It records:
  - each checkpoint, with the minutes since the last;
  - "STALLED" after two minutes inside a 15 m circle with no progress;
  - "5 min without progress" wherever you wander.

  Read it after a play-test to find where a landmark, a light or a sound would help. The tests don't write to it.

## The lantern, the HUD and the title cards (the owner's notes during the batch)
- **The lantern held from the top** (`HeldLantern.BuildBailGrip`). The owner: "the player's hand is holding it from the
  top instead of this stick thing it has now". A gloved hand now grips the top of the bail: the back of the hand up,
  four fingers curled over the wire and under it, the thumb round it from behind, and a cuff. The jacket's sleeve
  reaches up to it from the view's lower left corner. The sleeves of every held thing are the body's olive jacket now.
- **The cage's shadows, thin** (`Lantern.BuildCage`). The owner: "too many shadows from its cage ... make the shadows
  thinner and less noticeable". The heavy grid came from the lantern in the hand: its wire guards, right round the
  flame, cast into the light, which only skipped lighting them. The lantern's light (and a flare's) no longer takes
  shadows from anything held. The shadow cage's wire rings are gone, and its four struts are a third as thick, so what's
  left is a few faint soft lines.
- **The HUD's glow toned down** (`NeonKit`). The owner: "almost blurry ... like I have some kind of astigmatism". Each
  tube and letter is crisp now, with one faint close halo instead of three wide ones.
- **No brackets.** The little [ ] corners round the sprint bar and the held item are gone.
- **The chapter titles** (`ChapterCard`). The owner: "way way less shadow and be more subtle like the previous
  pictures from the alan wake 2 references ... warmed up a little". There's no dark rim round the letters any more,
  just a faint soft lift. The colour is a warmer old-paper cream, and the band behind is lighter.
- **The save icon in green** (`SaveIndicator`). Following the owner's reference photo, the canister's label is all one
  film-stock green with its print in white, the yellow gone, and black caps.

## Models bridged, not built from pieces (the owner: "the separate pieces look chunked ... we need to bridge the meshes")
- **The arms in view, real hands now** (`tools/Blender/fp_arms.py`, `HeldItem.BuildArm`). The owner: "it looks like a
  cartoon character right now ... getting the arms the best we can will really make the game stand out", then "better
  grip around the handle ... make the gloves a darker texture". Each arm is modelled in Blender the way a hand is
  built, and fused into one skin:
  - a palm lofted from the wrist to the knuckles, the back arched, the thumb's fleshy base and the heel of the hand
    swelling under it;
  - four fingers of three bones at their true lengths, a little flat in section, slimmer between the joints, knuckles
    proud at every joint;
  - the thumb aimed at where it rests: along the first finger when carrying the lantern, across the curled fingers in
    the fist;
  - a rounded wrist joint, so the bend into the forearm is one surface, with no cut end showing;
  - the forearm swelling toward the elbow.

  The hands wear dark leather gloves, worn paler over the knuckles and fingertips, with a soft sheen and a cuff at the
  wrist. Over the forearm, separate, the jacket's sleeve is loose, its folds gathered toward the wrist, its cuff open
  (drawn both sides).

  There are two grips. The left hand carries the lantern by its bail from above, the fingers closed round the wire with
  the wire cradled in the middle of the hook. The right hand is a fist round a handle (a flare, a crowbar, a tool).
  They're coloured by vertex: a bake from a dense copy scattered streaks over the maps, and the vertex colours are
  smooth.
- **The Leviathan's tentacles** (`Leviathan.BuildTube`, `BuildSuckers`). There are four rings to a segment now, not two,
  so a tight bend curves instead of hinging. The suckers are skinned to the tube's own bones and bend with it. They used
  to be rigid blocks on 18 segments that broke apart at every bend.
- **The Leviathan's body** (`Leviathan.BuildBody`, `HideRadius`). It's one continuous surface: a sphere pushed out to the
  soft union of the same lobes (its lumps, its folds, the collars where the limbs leave it), so each lump flows into the
  next. It used to be overlapping balls with a crease wherever two met. Its eyes sit in the new surface.

## The platter's skull looks at you (`SkiLodge.PlatterTick`)
The owner: "the skull just looking away from the player, we should track it to the players head so it looks at us
... and needs to be larger". It turns round on its platter after you, slowly and with weight, easing into the turn and
settling out of it, a little behind. It tips back to look up at your face as you come close, with a dry scrape of bone
on silver as it starts to move. It's half again as big (three-quarters of full size).

## The crawlspace, stuffed (`SkiLodge.CrawlClutter.cs`)
The owner: "make it look like people just shoved some shit in their overtime and forgot about it". Years of Christmases
are stuffed in there and left:
- wreaths on nails along the walls, some slipped off and lying against the boards;
- holly strands stapled along the tops of the walls, sagging, with berries;
- on the floor, kept to the edges so the way stays clear:
  - crumpled paper and loose sheets;
  - torn wrapping paper in three old patterns (candy stripe, holly green with gold dots, navy with stars);
  - bows come off presents, runs of tinsel, small gifts never given;
- in the dead ends, cardboard boxes shoved in and stacked, one open with its tinsel spilling over;
- more cobwebs: a few in the first third now, most of the corners by the end, sometimes two to a cell.

It's all a few merged meshes, one to a material.

## Act 23 into Act 24 (`SkiLodge.Finale`)
The owner: going out into the tunnel "still isnt showing up in a clean transition". The snow tunnel was only built once
Act 23 had ended, so the porch stood bare in the doorway while you looked out left and right, and the vault of snow came
in afterwards. It's built as the finale starts now: it's what the front doors are pushed against, and what the wendigo
bounds off down, low under its roof. A test checks it's there before the act ends.

## The tests' walk through the dining room
The owner: "you always get stuck here in the dining room while testing". The test's walker was pushing at the chairs
for 14 s at a time between the tables, then going on from wherever it had ended up. A walk pinned for 5 s is now logged
and hops the rest of the way. The tables are reached down the middle of the hall and then across, and the waypoint by
the door that sat among the chairs has been moved.

## The song you can hear (`Act24Score`, `SongCues`)
The owner: "im also not hearing the song we placed in this section earlier, it should be looping on the soft drum
parts, and when the wendigo is near it should ramp up to the intense synth sections". The loops are quiet in
themselves (-15 to -22 dB), and the score played them at another -15 dB under the storm, so they couldn't be heard.
Now:
- the score follows how near the wendigo is, as well as what it's doing;
- far off, the soft drums loop (the intro and the build, round and round) at -4 dB;
- within about 25 m, the main synth sections (A and A2);
- within 10 m, or on a chase, section B;
- the level rises toward +4 dB as it closes in;
- the impacts, the tail and the synth cues are raised to match.

## The maze's sounds (`Act24Sounds.Cave`, `ForestDirector.Hush`)
The owner: "the environmental sounds are way too loud the growls can stay the same but there is some metalic sound that
is really bad and this ear ringing sound that both need to go away".
- **The metallic sound** was the cave loop's ice ticking: clicks at 2–3 kHz under a long reverb, which rang like metal.
  They're gone from the loop; only the occasional drip is left, lower and softer.
- **The ear-ringing** was the forest's silence layer (the ringing and the pressure that swell when everything goes
  still), still running underground. It's hushed while you're in the maze.
- **The cave air** is down from -6 to -13 dB, under the song rather than over it.
- The growls, howls and steps are unchanged.

## Act 24, balanced and hunted (`Act24Score.Duck`, `AudioDirector.Space.IceCavern`, `WendigoHunter.FleeToHide`)
The owner: "there needs to be a ton of sound balancing in the caves for act 24, the music should be very audible compared
to everything else, also the wendigo sounds like a weed wacker when he is being hit, after being hit by the flames, he
should be running away to hide for a chance to surprise the player and they have to track them down", and "a little too
much reverb in the cavern".
- **The song on top.** While it plays, the world's beds duck under it: wind, weather, far sounds and nature by 9 dB,
  water by 6, the steps and the events by 3. The creature and the voices are left as they are. It all comes back when
  the hunt ends.
- **The cavern's reverb.** It's an ice cave of its own now (a shorter, damped ring, a touch of it), no longer the
  cathedral-sized reverb it shared with the church nave and the pit.
- **No more weed wacker.** The flamethrower's burn throbbed ten times a second over a dense rattle of high crackle. It's
  now a gas burner's deep whoosh, swelling slowly, the crackle sparse and soft (the roar, the ignition, the shut-off).
- **Burned, it hides.** After each burn it runs to a dead end of the maze (one way in), far from you, and waits there,
  crouched and still. You have to track it down:
  - its ragged breathing and low growls carry down the tunnels;
  - its burns smoulder and glow;
  - its claws mark the snow.
  Come within six and a half metres and it comes out at you. Left alone for a long while, it gives up waiting and creeps
  back toward you. A test checks it hides far off and comes out when found.

## The arm, carried naturally
The owner: "the arm is looking better but still looks too stiff and unnaturally bent, the grip is still too loose
around the handle". The forearm now drops away down and back to the view's lower left, with the hand carried up a
little from it at the wrist, the way a lantern's carried, instead of a straight arm bent hard at the wrist. The bail sits
in the crease inside the middle knuckles, the fingers closed tight round it. The wrist gives a little with the lantern's
swing. The fist grips tighter.

## Fixes
- **Scene changes logged a needless error.** The deferred scene change returned its own task as the deferred call's
  value. It's a plain call now.
- **The lighter was hidden inside the fist** during its hand-off. It's raised clear now, with its flame showing.

## Tests
- **Acts 21–25:** 232 of 232. They include:
  - the song in the maze, and section D with the flamethrower;
  - the synth cues;
  - the hand-offs: the lighter at the candle, the keycard swiped, the crowbar on the crate;
  - the achievements at the end.
- **The body preview:** shots from the eye of standing, walking, running, backing up, side-stepping, crouching,
  turning and stairs. The body hides from any camera that isn't the eye.
- **The settings' round trip:** 23 of 23, including the shake slider, captions, hold to crouch and body in view.
