# 2026-10-14g — Interiors lit like the woods (phase 1: global indoor lighting)

The owner loves the forest's look (a dark frame, one lantern pool, fog, a muted palette) and wants the interiors in the same
mood: PS2-era horror (Silent Hill 2/3, Fatal Frame), old and abandoned, never sci-fi. The interior survey
(`test-output/survey/SURVEY.md`) found the same few global causes behind almost every room. This pass fixes those causes
only. No room's geometry, props or textures were restyled; the per-area material passes come next.

Every interior from the survey, and the forest acts, was photographed with `LookShots` from the same poses before and
after (`test-output/look/before/`, `test-output/look/after/`; the contact sheets are in `test-output/interior-pass/`).

## What looked wrong, and what changed

| Cause | What looked wrong | What changed |
|---|---|---|
| The lantern indoors | The woods' lantern (21 m, energy 4.3, attenuation 0.8) lit a 4x5 m cabin or a 10 m lobby edge to edge: one even orange wash, with no falloff and no dark corners. | Indoors (`ForestAtmosphere.Indoors`: a room or tunnel, the church, the lodge, underground) the glow draws in to a pool: 7.5 m, energy 2.8, attenuation 1.3. The focused beam goes to 15 m, energy 2.8. It eases over about half a second at a doorway. Outdoors the woods' values are exact (`Lantern.UpdateIndoor`). |
| No air indoors | `EnclosedClearsFog` cut the depth fog to x0.2, the volumetric to 0.0035 and pushed the depth fog to 16 m. The far wall was as crisp and bright as the near one. | A room keeps a thin, dark haze of its own: density 0.009, coloured toward a near-black dust grey. That is about 9 % across a 10 m room and 40 % down the nave. Volumetric dust is 0.011 (the church keeps 0.014). The depth fog begins at 5 m. The lodge's own haze is 0.012 (was 0.004). |
| Flat ambient | The lodge's warm ambient was 0.42: daylight everywhere at dusk in a blizzard. Other rooms kept 0.55 of the outdoor ambient. | The lodge is 0.15 (frozen: 0.2, was 0.36). Other rooms keep 0.38. The rooms' lamps, the fire and the lantern's pool light them. |
| No contact shadows | SSAO only darkened the ambient (`SsaoLightAffect` 0.15). Under the lantern no corner or furniture foot got any darkening. The SSIL bounce spread the lantern up every wall. | Indoors, the AO darkens direct light too (light affect 0.5, intensity 2.8). The SSIL bounce is halved (0.45). Outdoors these stay 0.15, 2.2 and 0.9. |
| Orange frames | The whole frame was orange indoors: the lantern, warm wood and paper, all at one mid value. | An indoor layer over each place's grade (`AreaGrade`): saturation x0.74, the warm highlights pulled 60 % toward neutral, the shadows a touch cooler. `mid_gamma` 1.12 in `ps2_post` lowers the mids while black and white stay put. It eases over about a second. It is off while the lantern is snuffed (Act 24's maze has its own look). |
| Glowing windows | The lodge's panes were white sheets at night: `DayGlass` was driven to 0.9 emission. Room 202's blue window was 0.8. The church's clear lancets were near-clear glass in front of the snow glare. | The lodge glass is darker and grimier (albedo 0.2-0.26, roughness 0.45). It is lit dimly from outside at dusk: emission 0.2, down to 0.08 in the storm, cold blue-grey. The window lights are a third of what they were. The frozen whiteout is a dim grey (0.22). The 202 and 203 windows are 0.28 and 0.2. The church's quarries are frosted and grimed (alpha 0.5-0.97, glow 0.14). |
| Acts 14-20 in violet | The flame is dead there, so the lantern switched itself to the blacklight. Six acts were washed violet and magenta, a nightclub. | See below. |

## Acts 14-20: the ember (story)

STORY.md says that a quarter of the way down the stairwell the flame gutters out and won't take again. The owner said "do
what you think best". The story stays true: the flame is dead and F only clicks. What is left is a **smouldering wick**:

- a weak, slowly guttering amber glow, 8 m, energy 0.85, attenuation 1.0;
- a dark red coal in the glass of the held lantern.

The lantern no longer switches itself to the blacklight. B still does, for what is written in the ink. Hints:

- When the flame dies: "The flame won't take. The wick only smoulders. B: the blacklight still works."
- Once in each place, when they stand by something written in the ink with only the ember showing: "Something's there the
  ember won't show. B: the blacklight." This covers the stairwell's writing and the library's book and journal.

The blacklight itself is a colder, greyer violet and a little dimmer:

| | Before | After |
|---|---|---|
| Beam colour | (0.38, 0.12, 1) | (0.36, 0.2, 0.82) |
| Beam energy | 4.2 | 3.4 |
| Glow colour | (0.62, 0.5, 1) | (0.52, 0.5, 0.72) |
| Glow energy | 1.1 | 0.9 |

The ink's reveal is geometric (the `uv_light_*` globals and `Lantern.UvOn`), so no puzzle depends on the light's
brightness. STORY.md (Act 14, "The flame dies") is updated.

## Files
- `scripts/player/Lantern.cs`: indoor pool, ember mode, ember hints, the blacklight's colour and energy, no auto-blacklight.
- `scripts/player/HeldLantern.cs`: the ember's glass, coal and hand light.
- `scripts/world/ForestAtmosphere.cs`: `Indoors`; the indoor haze, ambient, volumetric, SSAO and SSIL; the lodge's ambient and haze.
- `scripts/ui/AreaGrade.cs`, `scripts/ui/PostGuard.cs`, `assets/shaders/ps2_post.gdshader` (`mid_gamma`): the indoor grade.
- `scripts/world/SkiLodge.Act23.cs`, `SkiLodge.Frozen.cs`, `lodgeparts/LodgeTextures.cs`: the lodge's glass and window light.
- `scripts/world/churchparts/ChurchTextures.cs`: the frosted quarries.
- `scripts/systems/Playtest.Under.cs`, `StoryTest.cs`: "F won't light it" now checks for the ember.

## Tests
- `dotnet build`: 0 errors.
- `--playtest-only=8,13a,14,15,19,20,23a`:
  - 13a, 14, 15, 19, 20 and 23a pass.
  - In that run, 8 failed at "E on the keypad leans in to it". It was the first beat after start-up, at 17 fps average and
    7 fps minimum while shaders compiled.
  - Beat 8 alone then passed, 37/37.
  - The beats cover the stairwell (the flame dies, the ember), red light green light (unchanged), the library's journal by
    blacklight and the lodge bar's blacklight card.
- The forest shots are unchanged within the grain: mean luminance 0.110/0.110 (Act 1), 0.055/0.055 (Act 7) and 0.024/0.024
  (Act 3's storm).

## Still off (for the per-area passes)
- The sewer's great room is still near black past the lantern, as it was before.
- The cabin's warm albedo still makes it the warmest room.
- The pit's red and teal lights, the hall's green strips and the round room's red ring are the area passes' to replace.
- The cabin's lit-window pane glows from inside too.
