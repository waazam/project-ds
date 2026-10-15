# 2026-10-15e — Paint runs, portraits and reeds

The scan's group (c) (`test-output/scan/SCAN.md`, 2026-10-15): the Hollow's, the lake's, the bunker's, the station's and
the stairwell's own props. The fixes are in the same commit as this file.

## Station, Room 1 (`StationRoom1.cs`; SCAN top #3, and the lettering)
- **The runs from the writing.**
  - **The problem:** after it's solved, dozens of perfectly straight, equal-width, saturated red lines ran from the
    letters to the floor and read as neon. Each was one stretched decal (3 + 1.4 a metre of line, so 9-10 to a line).
  - **Now:** two to four runs to a line (one to three on the middle one), about 20 a room. Each is a strip mesh of its
    own: a blob where it leaves the letter, a 2-4 cm run that wanders and kinks a little, swells and thins, and ends in
    a heavier teardrop bead. Its shape is set in metres down the wall, so growing it lengthens it without stretching it.
  - Dried: dark brown-red, matte, its edges soft.
  - The melt lets each run creep on and **slow to a stop** (it was a race to the floor). Most stop part way. The upper
    lines' runs stop on the dado rail or short of it (they used to pass behind it). Up to two a wall reach the
    skirting, from the bottom line, standing just off the panel mouldings.
- **The lettering** is laid letter by letter, each with its own lean, size and height. The brush's second pass is off
  each letter by its own amount. The red is browned (0.44, 0.035, 0.025; was a bright 0.62 red).
- **The cigar box's tapes** have their tops 4 mm over the lid, not in its plane (the clip audit's z-fight).

## Station, Room 3 (`StairwellTextures.cs`, `PortraitKit.cs`, `StationRoom3.cs`; SCAN top #5 and the portraits)
- **The plate.**
  - **The problem:** one 128 px rust-flake tile, upscaled, printed on every plate. Its hard threshold made
    stair-stepped pixel edges.
  - **Now:** 1024 px over a 4 x 4 m tile (the walls map it at uv 0.25). The tile holds eight 1 x 2 m plates. Each has
    its own paint shade, its own patch of the flake field and its own amount of flaking (some nearly sound, some half
    gone), under a slow wall-scale drift.
  - The flakes' edges are smoothstepped, the rivets are round, and the seams are soft-edged with a shadowed lip.
- **The portraits.**
  - **The problem:** 42 near-identical bright gilt frames, each with the same round black hole and white collar plate.
  - **The canvases** are 128 x 160 now, every edge anti-aliased. There are four kinds of sitter: a gentleman in a stock,
    a lady with her hair up, an officer with epaulettes and buttons, and a cleric in bands. Each has its own ground,
    clothes, place and size, and some sit before a curtain. The burn is its own size and lean, climbs as flame does,
    and its soot is scorched up the paint above it (the flat soot quad is only on some, up on the frame's top rail).
  - **The frames** come in six styles: tarnished gilt, black lacquer with a gold slip, carved walnut, tarnished silver,
    plain worn oak and a better-kept gilt. The woods are no longer metal. Width and depth come from the picture, and
    some bosses or cartouches have been knocked off.
  - **How they hang:**
    - More are askew.
    - Here and there one is gone, leaving its nail and a paler patch where it hung.
    - A few hang empty, their canvas cut out (no eyes can open in those: `SecondLook` looks for a canvas).
    - Up to four have come down: leaning at the wall's foot, or one face down on the floor.
  - The frames hang 15 mm off the plate. They stood 5 cm out in the air.
- **The corridor's walls** go 2 cm up into its ceiling slab (they were coplanar with its underside).

## Station lobby and basement
- **The web over the iron door** (`IronDoor.cs`).
  - **The problem:** behind the photo cards hung a flat grey 2.5 x 2.6 m rectangle that read as a sheet of cloth. Its
    white outline had also found the door's leaf behind it, a lit rectangle through the silk.
  - **Now:** a drape of silk of its own. It has a ragged lobed outline, a felt of threads, and is grey with dust in its
    belly. It bellies out toward the room and is slack at the foot.
  - Guy lines run to the ceiling, the back wall and the desk, each with a little sag.
  - The outline is drawn round a lobed proxy of the web's shape.
- **The stage-3 rust plates** (`LobbyDecor.cs`) stand 3 mm off the wall's face. They had a 1 cm air gap.
- **The filing cabinets** are a few centimetres apart and both turned a touch the same way. Their front corners had
  all but touched.
- **The basement** (`StationBasement.cs`).
  - **The clock's debris** was flung 1.4 m either way from a corner clock, into both walls, and lay at any angle half
    through the floor. Now it goes out into the room, lies flat on the floor, and never lands on the key.
  - **The drained debris** (papers, planks, bottles) settles by its own underside, level. The papers had lain 3 cm up
    in the air.

## The lake (`LakeDressing.cs`, `LakeDressing.Shore.cs`)
- **The reeds.**
  - **The problem:** they were the grass tuft stretched four times upward, black cut-outs with a jagged, cactus-like
    edge, one clump everywhere.
  - **The texture:** drawn reed blades, 128 x 512. There are thirty-odd thin tapered blades, each with its own height,
    lean and curve. They are olive going to straw at the tips, some dead and pale all through, with a midrib and dark
    feet. The short reeds carry bulrush heads and the tall beds carry drooping seed plumes.
  - **The clumps:** three shapes each of the short and the tall. Each is five to eight cards at uneven angles, each its
    own width, height, lean and slice of the texture. They are lit from above and glow a little against the sky
    instead of going black.
- **The wrack** is turned more, some end for end, and sized and heaped each its own way. Along the near shore's
  straight run it all lay one way and one size.

## The bunker (`CrtRoom.cs`, `bunker_crt.gdshader`)
- **The CRTs.** Every set had shown the same bright picture. Each set now has its own state, in the screen's vertex
  blue:
  - most working;
  - some weak and dim;
  - some dead (black glass);
  - a few with the glass cracked in a star.
  - It has its own dice, so the sets stand where they did. The story's switch-off and the stairs picture are
    unchanged.

## The Hollow
- **The cabin** (`Cabin.cs`, `Cabin.Interior.cs`): the corner webs are no longer one in every corner, alike. Some
  corners are clean and the rest are each their own size. The shelf's jars and tins are no longer jar, tin, jar, tin
  at 14 cm: uneven gaps, a gap where one was taken, some pushed back, turned any way.
- **The shed** (`Shed.cs`): the same for its shelf.
- **The footbridge** (`Footbridge.cs`): the abutment rocks sit down to the lowest ground under them. One hung 22 cm
  over the low side.

## Not done
- The station's coplanar faces at y 0.092 (`Walls` against Room 1's and Room 2's) are the undersides of skirtings
  inside the shared wall's cavity. They cannot be seen.
- The fallen tree's crown bridging a dip, the waterfall's rocks (the water agent's file) and the camp's hang (not
  identifiable from the report; 12 cm over 2 % of it) are left.
- The Act 1 spawn in the car's hatch: the car is being rebuilt elsewhere.
- The road sign's "->" is `WinterWoods.Props.cs` (group e).

## Verified
- **Before and after shots** with `look_shots`, at the scan's poses (p14_auto, the act 14 spawn, p13_station,
  p12_lake, p06_cabin, p06_shed, p08_bunker), plus close-ups:
  - Room 1's runs, before and after the solve;
  - Room 3's plate, portraits and the two frames that have come down;
  - the web over the iron door;
  - the basement's debris;
  - the reeds;
  - the footbridge.
  - The before shots are the scan's own.
- **Playtest**, beats 1, 3, 5a, 6, 7, 8, 11, 12, 13a, 13b and 14: 11/11 beats, 261/261 checks. Beats 13a, 13b and 14
  were run again on the final build: 3/3, 67/67. No engine errors in any run.
