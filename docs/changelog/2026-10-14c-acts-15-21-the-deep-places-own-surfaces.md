# 2026-10-14c — Acts 15-21: the deep places' own surfaces

The owner: "go through each scene and make everything look better. for example cabin wall and door shouldn't be the
same texture", and earlier "make sure everything blends and looks good and not too boxy or robotic or disjointed."
This pass covers Acts 15-21: the long hall and the closet, the sewer, the pit, the library (not the puzzle box), the
round room, the long stair, the church and the crypt. Every space was photographed with `LookShots` before and after
from the same poses. The red light, green light rule, its timings and the hall's random draws are unchanged.

## What looked wrong, and what changed

| Scene | What looked wrong | What changed |
|---|---|---|
| Act 15/16, the closet door | One flat box of the stairwell's structural steel. That 32 px texture's rust chips were stretched to coin-sized red polka dots. It stood in a raw hole in a wall of the same grey. | `ServiceDoor` (new): the leaf in old green-grey paint over Poly Haven's Rusty Metal 02, its rust coming through. Rolled edges, a kick plate, three hinges, a lever on a rose and an escutcheon on both faces. A hollow-metal frame in a darker scuffed brown-grey: face trim both sides, a soffit and a stop on the hall side (it swings into the closet). The JANITOR stencil is on a plate. |
| Act 16, the closet | Walls, floor and ceiling were all one pale board-marked concrete, its pits blown up to the size of coins. The door's wall inside was the hall's raw concrete. The bulb hung on nothing. | Painted block walls (Concrete Wall 004 under an old cream gone grey), a worn concrete floor of its own, a darker ceiling and a black rubber base where wall meets floor. The door's wall has the same painted block. The bulb sits in a porcelain batten holder. |
| Act 15, the corridor | The "doorways that go nowhere" were black cards on the wall's face. The floor strips lay on the bare concrete like glowing sticks. | A reveal round each doorway (jambs and a sill). Each strip segment sits in a low steel channel. No new random draws. |
| Act 17, the sewer's door | The same polka-dot steel box, on a bulkhead in the pipes' rust (the wall read as a rusted plate). | The closet's own door as seen from inside (it is the same door), framed. The bulkhead and the landing are stained wet concrete. |
| Act 17, the cistern's platform | Pale board-marked concrete, and the hole a cut with nothing round it. | The deep places' stained concrete. A rusted iron angle round the hole's lip, its leg down the hole's sides. |
| Act 18, the pit | The walls were a flat drawn stain at a big scale, with no relief. The way out was a flat polka-dot box in a raw hole. | `DeepConcrete` walls with relief. The door is a heavy plate (Metal Plate 02) with edge angles, three stiffeners and a lifting lug, in a steel frame. |
| Act 19, the library | The door from the pit showed the pit wall's raw cut: pale concrete, a bright frame between dark bookcases. The armchairs were five boxes each. The grandfather clock was one box with a white disc. The globe was a glossy plain ball on a rod. The fire stood on the bare hearth. The book cloths read as new candy colours. | A dark wood lining, casing and plinth blocks round that door. Club chairs: a domed cushion with a welt, rolled arms with scrolled fronts, a leaning rolled back, eased edges, on turned feet. The clock has a plinth, a trunk with its door, a hood, a crown, an ivory dial in a brass bezel and hands. The globe is varnished, with a brass meridian and a foot. The fire has iron fire dogs, split and charred logs, and glowing embers. The mantel has a moulded shelf and plinths. The book colours are faded toward brown. |
| Act 20, the round room | The `_stone` the drum shares with Act 21's top room was pale, flat concrete (0.8, no relief): one bright tone under the bulbs. The same material covered the floor and the dais. The window sills were knife-edged boxes. | `DeepConcrete` (new): Poly Haven's Concrete Wall 006 with its stains, pours, tie holes, normal and roughness, world-projected round the drum and on the ceiling. The floor has its own worn concrete. The dais is a dressed limestone. The window jambs and sills have eased edges (`BevelBox`). |
| Act 21, the top room and the long stair | The top room's stone (13c) was still the pale pitted concrete, darkened. Past the arch, the stair's concrete walls were pale and bright under the bulbs and the lantern, the treads were flat slabs, and the rail was polka-dot steel. Snow fell inside the top room and the stair (the glade's snow boxes reached 120 m round the church, underground too). | The top room and both of the stair's wall pours are `DeepConcrete`, a shade darker than the drum. The treads and landings are trodden concrete with a worn steel nosing on each concrete step. The rail is plain black. The glade's snow now asks for snow only when the camera is above the crypt's floor (`WinterGlade`). |
| Act 21, the church | The rose window was a dartboard: flat primaries in clean rings. | Old glass: each petal cut into small irregular pieces in lead, each with its own shade. The colours are sunk and smoky and grimed toward the rim, with a roundel in each petal's head and dark stone tracery. |
| Act 21, the crypt | The floor was a clean black-and-white game board (0.45 rough). | The white has yellowed to grey and the black to charcoal. Grime is packed in the joints and spreads from them, with damp blotches and a cracked square here and there. Rougher (0.62). |

Looked at and left: the hall's walls (13g's varied concrete reads well), the Shadowman (flat black by design), the pit's
pipes and catwalk (13g), the church's nave stone and pews, the round room's webs and switches.

## The flaky Act 18 beat (a test-bot bug, not a game bug)
- **"Never crushed" failed.** Four reruns reproduced it in 2 of 4. Each time it was crushed at valve 8 or 9,
  running at full speed with stamina left, only 0.7-2.9 m from the centre of a 3 m circle.
- **The cause, from a frame trace** (now printed as `[act18trace]` when it happens):
  - A slam came down behind the bot as it walked to a valve. The bot chose the right way out, back the way it came.
  - But `TestBot.Steer` turns the view only 5 degrees a frame, while the bot pushes forward. So it ran on toward the
    slam for a third of a second while it came round.
  - It then stalled against the valve and was caught.
  - A player flicks the mouse round. The fight itself was fair.
- **The fix:** the bot now turns the view to the escape at once (`Playtest.Under.cs`). Afterwards, 9 runs out of 9
  passed: 5 alone, and 4 with a second instance running alongside.
- **The "3 fps minimum".** It came from the fps meter, not from the fight. The meter waited 1.5 s after a beat's start
  but not after its reload, so it timed the first frames after the crushed-and-reloaded level's build. It now waits
  after each phase (`Playtest.cs`, `PhaseStartMs`). Act 18's minimum is now 68-99 fps.
- **Seen and left:** `--hitches` also shows a 30-60 ms frame every couple of seconds in the fight, with a gen-1 GC each
  time (something in the pit allocates every frame). It is too small to matter for the fight.

## Shared code
- New: `DeepConcrete.Stained` (`scripts/world/DeepConcrete.cs`) and `HallwayParts.ServiceDoor` (`Leaf`, `Frame`,
  `LeafMat`, `FrameMat`, `HardwareMat`).
- `ChurchTextures.Rose`, `ChurchTextures.Chequer` and `ChequerMat` (used only by the church).
- `WinterGlade._Process`: the height check.
- `Playtest.cs`: `PhaseStartMs`.
- `Playtest.Under.cs`: the Act 18 bot's evade and its trace.
- The shared `StairwellTextures` mats are untouched; the scenes use their own materials now.
- All the new normal-mapped surfaces are triplanar (MeshKit writes no tangents).

## Tests
- `dotnet build`: 0 errors.
- `--playtest-only=15-21`: 8/8 beats, 123/123 checks, 0 engine errors.
- Act 18 alone: 9/9 since the fix (2/4 and 1/4 failed before).
- **Shots:**
  - Before: `test-output/look/c14/before15`-`before22`.
  - After: `after1_16`, `after1_17`, `after1_20`, `after1_21`, `after2_15`, `after2_19`, `after2_19b`, `after2_20`,
    `after2_21`, `after3_17`, `after3_18`, `after3_21`.
  - Playtest: `test-output/c14pt/base` and `final1`.
- The Act 17 beat's 1 fps minimum is the black frame of the drop into the pit's level build. It was there before and
  is left.
