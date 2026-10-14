# 2026-10-14d — The flamethrower's fire rebuilt, and the later acts made over (22-25)

The owner: "go through each scene and make everything look better. for example cabin wall and door shouldn't be the
same texture. flamethrower scene needs some work it doesn't look great", and earlier: "make sure everything blends and
looks good and not too boxy or robotic or disjointed."

Every scene in Acts 22-25 was photographed (`LookShots` from each spawn, and from 17 poses through the lodge), and the
flamethrower was shot frame by frame with a new look-book harness (`FlameShots`): at rest; firing at a trench wall, down
at the duckboards and down a dark tunnel; after the trigger is let go; and burning the wendigo. Then the fixes were made
and the scenes shot again.

## The flamethrower (the priority)

The fire was modelled on footage of the M2-2 and of napalm: a narrow, bright, oily jet out of the nozzle. It thickens
into a rolling, torn orange body wrapped in soot. Where it lands it splashes, sticks and burns on, with thick black smoke
rolling off it.

| Scene | What looked wrong | What changed |
|---|---|---|
| The stream (`NapalmStream`, `napalm_fire.gdshader`) | Each glob was a solid, saturated yellow ball up to 2 m across, alpha-blended. Overlapping, they summed into one flat yellow blob. Within a few metres of a wall it filled the screen. There was no colour gradient, no soot, and the shape was the same in every frame. | Each glob is premultiplied: the fire adds its light and the soot darkens. It's a narrow jet at the nozzle (young globs drawn out to 55 cm, so it's one rope, not beads), swelling into billows at the end. The colour runs by temperature: yellow-white only in the thick young core, then orange, deep red, and sooty black at the torn rim. Rich, darker pockets roll through the body. It thins to nothing in the last metre before the eye, so it's never a sheet of fire over the screen. |
| The splash and the cling (`napalm_tongue.gdshader`) | A glob that hit something swelled in place into a ball. It burned for 1.4-2.6 s and was gone. Nothing spread. | It splashes (a bloom against the surface, droplets kicked off). The fuel sticks in **patches** that every later glob feeds, and they burn on 12-16 s as tongues of flame stood up off the surface: a low carpet plus licks of every height that bend, kink and tear, their tips going to soot. A well-fed patch throws burning gobs out round it, which catch where they land, so the fire spreads across the trench floor. |
| Smoke and steam (`napalm_smoke.gdshader`) | Flat round dark-grey puffs, no shape and no light. | Thick, oily, near-black smoke from the weather's smoke flipbook (billowing from a knot to a wisp), torn by noise and lit dull orange from below by the fire. It climbs and drifts with the wind. Grey steam bursts off the snow where the fuel first lands. |
| Embers (`napalm_spark.gdshader`) | None. | Burning droplets flung off the jet and the splashes fall in arcs. Sparks rise off the patches and wander. Each is a short streak that cools from white-yellow through orange to red. |
| Scorch | None. | Each patch leaves a char (a decal drawn in code): black soot at its heart, a brown scorch ring, flecks of ash. Embers glow in it while it burns and cool after, and it stays for two minutes. Neighbouring patches spread one char rather than stacking. |
| Light | One smooth omni light that eased between levels: no flicker, no shadows. | The stream's light breathes unevenly (smooth noise at a few hertz, never a strobe) and casts shadows: the trench posts' shadows swing on the walls as the stream sweeps. A second light on the burning patches wavers on its own. The mouth's light lights the wand, the hands and the snow underfoot while firing. All of them light the volumetric fog. |
| Heat shimmer | None on the stream. | A haze card over the burning patches. `heat_haze.gdshader` is now fog-disabled: in the cave's fog it drew as a pale rectangle. |
| The pilot flame | A FireVfx whose smallest tongue is 35 cm wide: a flat disc of fire over the nozzle. | Two small licks of flame at the nozzle's mouth, a few centimetres high. |
| The wand (`Flamethrower.WandRoles`) | It looked gold under the sun in the hands test. | Cause: the olive role was a lit olive (0.17, 0.19, 0.11) with the default specular; under the warm pilot light and the sun it read as brass. Now a flat, matte olive drab (0.095, 0.11, 0.085), roughness 0.94, specular 0.15. The steel is grimed and duller (metallic 0.5, roughness 0.68), and the brass fittings are tarnished (0.2, 0.16, 0.1; metallic 0.35). |
| The wendigo burning (`NapalmBurn`, new) | One FireVfx box at the body's middle: a smooth teardrop of fire beside the creature, left behind when it turned and ran. | The fire clings to the body: tongues of napalm on up to 16 of its bones (spine, chest, shoulders, arms, thighs, head), stood up off them in world space whatever the pose. Oily black smoke pours off it, and a breathing light sits in its chest. It fades as it did before. The same fire is on the burning wendigo in Act 25 (the pyre against the maze's wall, and ending 3's charge off the car). |
| Warm-up | The new shaders compiled at the first shot. | The fire's four materials are drawn tiny for a few frames when the stream is made, and are added to the lodge's shader warm-up. |

**Frame rate** in the flamethrower scene (the `FlameShots` harness, 1280x720 windowed, this machine; mean over each
burst after its first 0.4 s):

| Set-up | Before | After |
|---|---|---|
| At the trench wall (stream at full length) | 61-71 fps | 61-65 fps |
| Down at the duckboards (fire spreading over the floor) | 71-73 | 70-76 |
| Down a dark tunnel | 67-69 | 66-74 |

Lows during the bursts were 45-60 fps both before and after. The playtest's Act 24 beat (four burns) averaged 167 fps,
minimum 53. The stream's light now casts shadows: it's the one cost added that you can see, and the rest roughly evens
out (300 globs, was 340; patches instead of 340 swelling balls).

## The lodge and the rest

| Scene | What looked wrong | What changed |
|---|---|---|
| The mudroom (Act 23's first room) | One orange box: the floor, the walls, the ceiling and the door were all the cabin's pine boards. | Flagstone floor; board wainscot to a dark rail; the corridor's painted plaster above it; a dark boarded ceiling on two beams; skirting boards; the back door cased (all `MeshKit.BevelBox`, eased edges). |
| The mudroom's and the pantry's doors | Drawn in the planked dark wood of the walls: the door read as more wall. | Old boards under a chipped oxblood paint (`BuildingTextures.PaintedBoards`, from the Acts 1-7 pass). `LodgeDoor.LeafMat` lets a door have its own surface. |
| The guest rooms' doors, and the lodge's furniture | Same planked dark wood as the wainscot beside them. | `LodgeTextures.DoorWoodMat`: the same wood as one smooth, varnished leaf, a step darker. It's used by the guest rooms' doors and by every lodge chair, bed, table and the Christmas furniture (the `wood` role). |
| The coats in the mudroom | Three coats in the same dark brown read as one flat dark shape. | Each its own heavy wool: a faded red, a navy and a loden green. |
| The snow through the back door | The woods' soft snow glared as a white wedge under the bulb. | The tunnel's packed snow, a greyer indoor copy (`SnowMazeLook.PackedSnowIndoor`). |
| The bar's damask | Half-metre medallions ringed in bright gold read as rows of staring eyes. | A hand-span figure, and the gold taken down to a dull bronze. |
| The lodge's windows from inside (`DayGlass`) | Flat white sheets in the walls. | A storm's grey light (emission 0.13, was 0.24; a greyer pane). |
| Room clutter on the furniture | Placed by a ray against the collision boxes, which stand up to 2 cm above the modelled tops. The things floated (the joints pass's leftover). | The placement method is fixed: `SurfaceProbe` finds the drawn surface itself (the highest upward-facing triangle under the point, from a grid of the lodge's meshes built once at build time). 20 of 20 placements on furniture are now set on its drawn top. A spot whose box has nothing drawn on it is skipped. The lodge's gap audit lists no room clutter (it listed a wine glass 31 mm and 7 mm over its table). |
| Act 24: snow on the trench posts | One cap height for every post, 1-18 cm above the shorter ones, on nothing (`TrenchSnow`). | Each cap sits on its own post's top, a little down over it. |
| Act 24: the hole's icicles | Hung from a circle, but the cut isn't round: 5-15 cm under the edge, on nothing. | Each found by a ray up from inside the opening to the rim's underside, its root 3.5 cm up in the snow. |
| Act 25: the snow at the trail's end | Flat blobs half sunk into the slope, with hard cut edges where the ground came up through them. | Soft-edged decals laid on the ground (old crusted snow with broken, melted-through edges and its own relief, drawn in code), following every bump. Two low drifts are heaped against the bank. |
| Act 25: the hole in the snowbank | A flat pale-blue faceted disc on the bank's face. | Smooth and near black: the dark of the tunnel behind it. |

**Looked at and left:** the winter road and woods (Act 22: already read well in the snow); the crawlspace (already
rotting boards and brick under its lights); the dining hall; the maze's ice. The car in Act 25 is the Acts 1-7 pass's
reshaped car (`ParkProp`), merged in and seen in the endings' beats.

## Tools
- **`flame_shots.tscn`** (`FlameShots.cs`): `-- --autotest [--fs-out=dir] [--fs-only=idle,wall,floor,tunnel,wendigo]
  [--fs-nohaze]`. It seeds the flamethrower save and photographs each burst frame by frame to `test-output/flame/<dir>/`,
  and logs the fps while firing.
- **`SurfaceProbe`**: where a thing set down really rests (the drawn surface, not the collision box). Use it at build
  time.

## Shots
- Flamethrower, before: `test-output/flame/before/` (from the old code). After: `test-output/flame/a6/`.
  - Compare `05_wall_fire_1.20`, `14_floor_after_0.5` (`13_` before), `16_floor_after_6.0`, `21_tunnel_fire_1.60` and
    `27-29_wendigo_*`.
- Wand under the sun: `test-output/hands/flamethrower_1_ahead.png`.
- The lodge: before `test-output/look/l23/`, after `test-output/look/a23/`.
- Act 25: before `test-output/look/b25/`, after `test-output/look/a25/`.

## Tests
- `dotnet build`: 0 errors.
- `--playtest --playtest-only=22,23a,23b,23c,23d,23e,24a,24b,24d,25-1,25-3` (with 23x, 23y and 24c): 14 of 14 beats,
  189 of 189 checks, 0 engine errors.
- The lodge's gap audit (`gap_audit.tscn -- --lodge`): no room clutter left floating.
- No full-game test run (the owner's instruction).
