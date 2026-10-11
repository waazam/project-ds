# 2026-10-10b — All the buildings' wood re-textured with real scanned planks; the way down into the snow; the wendigo that learns; the catwalk that remembers; rooms that sound like rooms; the radio; the lodge's wardrobe, sheet and door; R.H.'s pages

The owner: "do all 12 in the most efficient order, after they are implemented test, debug, push to main". And, part way
through: "at the start of act 24 when the player leaves the lodge to go into the tunnels both the exit for the lodge and the
entrance to the tunnel needs a lot of work and look bad right now, make that whole section have better entrances/doorways
and tighten up the loading screens, could potentially make the player be forced to go into the tunnels by the wendigo
instead of them just following it inside."

## The start of Act 24 (`Act24Maze.cs`, `Act24Maze.Herd.cs`, `SkiLodge.Hide.cs`)
- **The lodge's doorway.**
  - The porch's great doors are gone, but the frame still stands. The torn leaves hang off the hinges on both sides in
    jagged columns of splintered boards.
  - An iron strap is bent out and down, and the chain and its padlock lie where they dropped on the landing.
  - Boards are flung across the landing and out onto the snow.
  - The storm is already drifting in: snow banked against the jambs, a tongue of it over the threshold, more along the
    landing's edge.
- **The way down: a snow tunnel from the door.** It took several goes, with the owner looking at each one ("it just
  looks like a closed igloo now lol", "the icecicles in front of it are still just floating", "remove that igloo rock
  ring look") and finally drawing it: "step outside the front door of the ski lodge and you are already incased in a snow
  tunnel and you have to traverse it to the entrance of the real maze".
  - The storm has buried the front of the lodge. Out of the splintered doorway it's a vault of packed snow, 7.6 m wide
    and 4.8 m high, the porch's stone piers and roof swallowed in its walls.
  - It runs straight out 24 m, narrowing a little, with ribs of packed snow every 4 m (the arches in the drawing). Its
    floor is the ground, segment by segment, the porch's landing first.
  - The night's light comes faint and cold through its roof.
  - At its end is a wall of snow, and low in it, black, the way down. It's 2.3 m across, cut in and down, the maze's own
    scalloped ice inside, its rim scooped unevenly, with icicles hung from the wall's real face over it.
  - It's all one solid (CSG): nothing to see round, nothing to climb over. The separate drift out in the open is gone.
- **Driven down.** There are no tracks to follow in any more.
  - Seven metres into the tunnel, the wendigo is in the lodge's doorway behind you: it came through the lodge after you.
    It rears and screams ("Go. Go."), then comes on down the tunnel.
  - While you keep going it walks a few metres back, cutting off the way round. Turn away or dawdle and it comes faster.
    If it catches you, the act starts again at the doorway.
  - As you go down it comes at the mouth behind you, and you hear it at the top as the dark closes.
  - In the maze it follows you down 14 s later, along the tunnel you came in by. Loading a save in the maze starts it off
    in the far tunnels as before.
- **The lantern, knocked out** (`Lantern.Snuff`, `SkiLodge.Finale.cs`). The owner: "our lantern should go out when the
  wendigo jumps at us at the end of act 23, we should be using the flares in the maze for our light source ... this
  change will also make the green lighting from the flares really pop".
  - As you throw yourself aside from its leap off the balcony, the lantern is knocked from your hand: a crack of glass, a
    sputter, and it's out.
  - It stays out, flame and blacklight both, all through the snow tunnel and the maze. F only clicks ("The glass is
    cracked. It won't light."), and the held lantern goes down out of view.
  - The green flares are the only light down there, apart from the flamethrower's pilot later.
  - It takes again once the wendigo is dead (Act 25).
- **The black at the hole.** It's shorter: a 0.35 s slide into the dark, 0.1 s held, and a 0.6 s fade back (it was
  0.6 + 0.3 + 1.0 s). The maze is built well before you reach it.

## All the buildings' wood: Hinoki Planks (`SurfaceSets.Hinoki`, `BuildingTextures`, `LodgeTextures`, `ChurchTextures`, `PropTextures`)
The owner, linking [Poly Haven's Hinoki Planks](https://polyhaven.com/a/hinoki_planks) (CC0, by Charlotte Baglioni):
"replace all the wood for the lodge and the church and the cabins, all building wood assets and retexture them with what i
just linked it all needs to pop".
- **The source.** Its 2k colour, normal (OpenGL) and roughness maps are in `assets/textures/surfaces/hinoki_*.jpg`.
- **Plank surfaces.** These take the planks projected in the object's own space, so every plank is the same 14 cm however
  the mesh was UV'd, running level along walls and floors:
  - the cabin's boards and floor, fresh boards;
  - the lodge's woodwork, log walls, parquet, the crawlspace's boards, studs and ceiling;
  - the church's oak and old planks;
  - posts and decking.
- **Log walls** (`hinoki_log`): one plank band of the scan, stretched round into a log with shading toward its top and
  bottom and a band of chinking.
- **Round beams, rails, posts and firewood** (`hinoki_round`): the same band turned so the grain runs along them.
- **Each place its own tone of the one wood:**
  - the cabin honey-pale;
  - the lodge's woodwork stained dark and rich, its walls the bare wood;
  - the church darker, its oak deepest.

  The lodge's timber, trim and doors were tinted near-black before; now they're tones you can see the grain in.

## The twelve

1. **Flare light on the walls** (`Flare.cs`). A planted or thrown flare within 22 m of you casts shadows, so everything near
   it, the wendigo included, throws a long shadow down the tunnel ahead of it. Further off, it doesn't, so a tunnel full of
   flares doesn't cost a shadow each.
2. **It learns** (`WendigoHunter.Lure.cs`).
   - If it sees you throw (you're in its sight as the flare comes down), it goes for you, not the flare.
   - After three flares it falls for only half of them.
   - Mid-chase, a flare landing within 10 m of it is light in its face. It recoils, hissing, blind for three seconds, and
     then goes to the flare and not to you. That's the way out of a chase.
3. **It's in the picture** (`SaveLamps.cs`, `SecondLook.cs`). Once the safe light's visitor has stood behind you, a photo
   taken toward that spot (within 16 m) shows it standing just where it was, though there was nothing there to see. It
   happens once.
4. **The catwalk remembers** (`BossRoom.Damage.cs`). Crushed and back on the catwalk, it's as battered as it was when you
   died: the same dents, the same bent rails. It's set right only once the fight is won.
5. **Footprints in the maze** (`Act24Maze.cs`, `WendigoHunter.cs`).
   - Your boots press prints into the maze's soft floor (shallower crouched), and its claws leave prints wherever it walks.
   - They stay, the newest few hundred, so a tunnel you've already walked shows it, and so does one it has been down.
   - Its claws are on the snow outside too, where it herds you.
6. **Breath you can see** (`AirParticles.cs`). In the cold, the breath comes quicker the more winded you are, down to
   every 1.1 s. Holding your breath, there's none to see. When you let go, a breath straight away.
7. **Rooms that sound like rooms** (`AudioDirector.cs`, `SkiLodge.Loose.cs`, `Act24Maze.cs`). Most interiors already had
   reverb; now so do these:
   - The lodge's hexagonal hall rings. The upstairs corridor and rooms are small and wooden. The dining hall is long.
   - A new space, snow: packed snow all round, which swallows sound in a short, soft, dead ring. All of the maze's tunnels
     are snow, and the cavern rings.
   - Zones have priorities now, so a cavern inside the snow tunnels wins.
8. **The standing sheet** (`SkiLodge.Hide.cs`).
   - In the dining hall, among the dust sheets over the furniture, one is standing, a person's height.
   - Every time your light goes out in that hall (the lantern put out, a flare guttering), it's up to 0.9 m nearer when
     you see again. It never moves while it's lit, and never closer than 2.4 m.
   - It goes round the tables under their sheets, not through them.
9. **The radio** (`RadioVoices.cs`).
   - Carrying the walkie after the bunker, sometimes it clicks on by itself when something is near and unseen.
   - A burst of static, and through it a voice: in the winter, one of the wendigo's stolen voices; before that, a whisper
     too broken to make out. Then static, and it's off.
   - It's rare: two to four minutes apart, a 30% chance each time, and never the same voice twice in a row.
   - It isn't your own voice: the game has no recordings of it. `--radio-voices` forces it whenever it's allowed.
10. **Hiding in a wardrobe** (`SkiLodge.Hide.cs`, `LodgeKit.Solid`).
    - Room 202's and room 204's wardrobes, once open: step in (E), and the door is pulled to with a crack left to look
      out through. E again to step out.
    - The first time in Act 23, something comes up the corridor while you're in there: its weight on the boards step by
      step, a stop outside the room's door, a long breath, then on, and gone.
11. **A way out of a chase.** That's the flare in its face (2), and the hollows from last night: chased into one and
    unseen, it searches, comes sniffing at the mouth, and you hold your breath.
12. **R.H.'s pages** (`LorePages.cs`, `PhotoLogPage.cs`).
    - Six leaves torn from his notebook lie on the floor a step from six saves: the lake, the station's way in, Room 3,
      the sewer, the church's crypt, the lodge's corridor.
    - Each is in his hand, and they tell the way he went before you, ending at the lodge door: "It wore my voice at the
      lodge door and I nearly let myself in. If you hear me after this, it isn't me."
    - Read, they're kept on the album's last page, in order, with a gap where each unread one goes.

**And the third second look:** on one run in four, the lodge's room 204 door, locked and shut, is found standing ajar,
dark inside, once you've been out of 202 and looked away from it. Come near and it eases shut, and the latch clicks.
`--second-look` forces it.

## Next (the owner)
"https://polyhaven.com/textures/wood ... explore this site later ... we really need to make all of our textures pop ... go
through and see what we can spice up with making all our buildings look way better, we also probably need a huge lighting
overhaul". That's the next pass: the rest of the buildings' surfaces from Poly Haven's scans (stone, plaster, metal,
roofing), and the lighting and environment.

## Tests
- **The full autotest:** run start to Act 24 with the earlier drift. The test walker couldn't climb the drift's raised
  mouth and was caught there each try; that's what led to the tunnel from the door. Everything else passed.
  - The one other failure was the test miscounting the catwalk's restored dents, which it does right now.
- **The rebuilt sections, re-run:**
  - **Act 18:** 21 of 21. The catwalk as it was left after a death: both dents and their bent rails back.
  - **Acts 23 to 25:** 149 of 149. This covers:
    - the lantern knocked out;
    - the wardrobe and the standing sheet;
    - walking out of the door into the snow tunnel, the wendigo appearing in the doorway behind;
    - in through the way down, and it follows;
    - the flares and the hollows;
    - the endings.
  - **Act 17:** 15 of 15.
- **The continue test:** 1058 of 1058, 40 scenarios.
- **The sign audit:** 0 blocking.
- **The settings' round trip:** 16 of 16.
