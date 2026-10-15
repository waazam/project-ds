# 2026-10-14h — Acts 15-18 old and abandoned: the hall an old service corridor, the sewer's lamps brown out, the pit lit by work lamps

Dan wants the later rooms to read as a decades-old, neglected real 20th-century building: not ancient ruins, and not
sci-fi, alien or a nightclub. The look survey's "Futuristic elements" table (`test-output/survey/SURVEY.md`) listed
Act 15's neon runway, its green doorway glow and the shadow man's white-blue glow, Act 17's teal brown-out, and Act 18's
teal laser and red emergency light. This pass replaces them in Acts 15-18. It uses the interiors kit
(`docs/design/interior-kit.md`) where it fits.

## Red light, green light is unchanged
- `Act15Hallway.Rlgl.cs` and `Act15Hallway.Variants.cs` are byte for byte as they were.
- The hall's dice (`_rng`) take the same draws in the same order. The service doors replaced the black doorways, but each
  door still takes its two draws (`RandfRange(28, 46)`, then `Randf()` for the side). The closet's dummy shelf draws stay.
  Everything new uses its own `RandomNumberGenerator`.
- The build now prints the dice's state once it is done (`[rlgl] hall dice after build: state ...`). The state is
  `8407554524482128862` before this pass and after it. Every red and green timing is drawn after that point by unchanged
  code, so the timings are the same.
- The strips' and lamps' colour and level still come from `SetLights` and the centre row's dips. The strips' new shader
  reads them each frame from the same two carrier materials (`SyncStrips`). Green is a step less neon
  (0.3, 1, 0.42 → 0.4, 0.95, 0.42). Red is (1, 0.1, 0.05). They mean the same thing at the same moments.

## Act 15: the hall
- **Walls** (`assets/shaders/corridor_wall.gdshader`): painted concrete block (the `concrete_wall_004` scan, 21 cm
  courses) in two tones split at a dado line, with a dark line between them: an institutional green-grey below and an old
  cream above.
  - The paint is chipped back to grey block and primer, more of it low down and at rail height.
  - Along the foot there is a scuffed black kick band, with trolley scuffs at about 0.8 m.
  - A tide line of rising damp sits at its own height along the wall.
  - There are brown water stains with darker rings, and runs come down from the dark overhead.
  - Above the 3.2 m ledge the paint gives way to grimy bare concrete.
  - The shader is projected flat on each wall's own plane. This fixes the long diagonal streaks on the 22-34 m walls:
    the old `concrete_varied` shader's second set was swizzled end-on onto them.
- **Floor**: old vinyl composition tile, 30 cm, off-white and grey-green in a broken checker (`CorridorTextures.VinylTile`),
  weathered by `RoomSurface`. A few patches have lifted to the black adhesive, and paper is trodden into the floor along
  the walls.
- **Dressing** (`Act15Hallway.Dressing.cs`, none of it with collision):
  - A hardwood bump rail on brackets down both sides, bay by bay between the pilasters. About one length in 14 is gone
    (its brackets left), and some hang off one end.
  - A perished rubber base along the foot of the walls.
  - Steel service doors with wired-glass vision panels and S-numbered plates. These are part 1's old "doorways that go nowhere"
    plus about one bay in four in part 2.
  - Red fire cabinets: glass gone or cracked, some extinguishers missing, the glass on the floor.
  - Enamel signs, some on flag brackets so they read down the hall: NO ENTRY, BOILER ROOM, KEEP CLEAR, FIRE EXIT, NO
    ADMITTANCE and so on.
  - Tide, scuff, mould, peel, crack and streak decals by rule along both walls, with a floor stain now and then.
- **The floor strips are kept** (`assets/shaders/old_strip_light.gdshader`). Each segment has its own brightness and a
  yellowed, dirty diffuser, with grime lying over them along the floor.
  - Runs of 2-6 segments are burnt out, and odd ones are dead.
  - About 6 % sag and buzz on their own time. With Reduce Flashing they only sag, slowly.
  - About 7 % have a cracked piece of lens lying askew over them.
  - The housings are old rusty steel. The centre row's dead segments are the same ones as before (the same seed).
- **Lights**:
  - Part 1's green omnis floating in the air are now old caged bulbs on wall brackets (`FailingBulb`): warm, some
    steady, one flickering, one buzzing, some dead.
  - The taper's glow is a less saturated green.
  - Part 2's hanging lamps keep the signal, under chipped enamel shades.
  - One more warm bulb on the wall just past where the shadow man first stands lights the wall behind him.
- **Air**: the green fog is now a dirty dim haze with only a breath of green in it. The red still swings it to dark red.
- **The shadow man** (`ShadowMan.cs`): the cold rim overlay is gone, and the specks in his aura are grey ash, not points
  of light. His eyes are two dim pinpricks at rest and still burn when he takes someone. He is an unlit black shape
  against the lit wall.
- **The doorway's glow** (`Stairwell.Bottom.cs` `FarGlow`): the green glow from nowhere is now one warm caged bulb on the
  passage ceiling. It flickers out now and then.

## Act 16: the closet
- The walls, floor and door wall are drawn finely (`Tess` 0.3) so the kit's bake shows: `RoomShade` darkens the corners
  and the foot, adds the dark behind and under the shelves and the bulb's pool, and puts damp in a back corner.
- The surfaces are weathered by `RoomSurface` (the same concrete scans), and `DecalDresser.Room` adds stains and cracks
  by rule, keeping clear of the shelves and the switch. The switch, the door and the light's red-green-white cycle are
  untouched.

## Act 17: the sewer
- **The lamps** are caged bulbs on wall brackets, or on a stalk from the pipe's crown (`FailingBulb`). One in the pipe
  flickers.
- **The second no** used to turn every lamp and the hole's light teal. Now the lamps brown out: over a couple of seconds
  they sag to a dim, stuttering orange-brown, each then swelling and sagging on its own time. Nearly half of them gutter
  and die one by one over about half a minute. The hole's light browns and dims but stays on. The murk that makes the
  room hard to see across is brown (`TealFog`, same density). The `Teal` property and the tests keep the old name.
- **The cistern past the pipe** was near-pure black. Now a street grate high in the far vault lets a little cold grey
  light through. A dim spot (no shadow pass) leaves a faint grey pool on the water far off, with a faint shaft in the
  fog. It gives the black a shape without lighting it. (A projector of the bars was tried: it blacked the light out.)

## Act 18: the pit
- **The telegraph is kept**, but physical: the four teal lasers are old carbon-arc searchlights. Each has a ribbed drum, a
  yoke and post, and a lens that glows as it strikes up. It throws a hard warm beam (same energy, same 5.5° cone, same
  shadowed spot), swings round onto the valve from where it last pointed (the same moment, the same target), and dust
  sifts down in the beam where it lands. It is still the only bright beam in the room.
- **Red light is gone**:
  - The eight red emergency omnis are amber caged work lamps on arms (`FailingBulb`): one dead, one guttering, one
    buzzing.
  - The cold stage lamps are warm industrial reflector lamps, two of them dead. The fill is warm grey.
  - The glow off the blood is a dark brown-red, not a saturated emergency red.
  - A caged bulb on a long flex swings slowly off the truss over the west catwalk.
  - The red is carried by paint: faded red stencils (DANGER, KEEP CLEAR, No 3 SUMP and so on), the red handwheels and the
    caps.
- **Pipes**: every painted material in the pit (`BossTextures.Painted`: pipes, valves, wheels, gauges, the yellow beam) is
  old paint chipped back to rust, with rust run down from the chips (`BossTextures.RustyPaint`, one 128 px texture a
  colour, the chip field drawn once), plus the rusty-metal scan's relief. The lagging is torn and hangs in flaps. Mineral
  drips have grown under the flanges, and water drips from some of them (`BossRoom.Wear.cs`, its own dice). The room's
  own dice, which lay the pipes and pick the valve order, are untouched.

## New shared piece
`scripts/world/FailingBulb.cs` is an old incandescent bulb going bad: a dirty glass bulb in a wire cage, a holder, its
omni, and a mode (Steady, Flicker, Buzz or Dead).
- `Level` lets a room brown it out from outside.
- It uses its own dice. With Reduce Flashing every drop is a soft dip.
- It only runs within its range of the camera.

## Verification
- Look shots before and after:
  - Before: `test-output/look/b_hall`, `b_closet`, `b_sewer` and `b_pit`, with sheets `test-output/b_*_sheet.png`.
  - After: the final `test-output/look/f_*`, with contact sheet `test-output/acts15-18_contact.png`.
- Playtest beats 15, 16, 17 and 18 (`--playtest-only=15,16,17,18`): 4/4 pass, 46/46 checks, 0 engine errors. The beat
  now also saves `15_15_red_light.png` and `17_17_brown_out.png`. Average fps: 222, 162, 123, 121. Beat 17's minimum of 1
  fps is the drop into the black, where the winter is built.
- The hall's dice state is `8407554524482128862` in every build, before this pass and after.
- The strips now sync just before each frame is drawn (`RenderingServer.FramePreDraw`), so the red shows on the strips
  the frame it comes on. The beat's red-light screenshot first showed them still green.
- The look shots' fps are within run-to-run noise of before (other agents share the GPU). The hall is 120-145 at the
  poses and the pit 59-79; the pit's primitives are dominated by the creature. The swinging lamp casts no shadow: its
  omni shadow pass added about 2.8M primitives over the creature.
