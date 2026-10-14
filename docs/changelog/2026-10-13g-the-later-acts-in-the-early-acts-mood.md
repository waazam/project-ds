# 2026-10-13g — The later acts' surfaces in the early acts' mood

The owner: "lets update all textures to make sure they match the mood of the earlier acts textures."

## The reference
Acts 1-7 (the trail, the camp, the cabin, the shed, the clearing, the bunker's outside), photographed from each act's
spawn and inside the cabin (`look_shots.tscn`), set the mood:
- **Palette.** Earthy and desaturated: browns, dark olive, grey-green. The only strong colour is the lantern's warm light.
- **Values.** Dark. Most of the frame sits in the lower third. Nothing near white except a light source.
- **Surfaces.** Rough and matte. Every surface has a grain you can see in relief under the lantern (bark, the log
  walls, the forest floor). No gloss except wet ground and glass.
- **Wear.** Nothing is new: grime, damp, rot and stains everywhere.

Then every later act was photographed from its spawn. The bunker was shot with its preview harness, and the cabin and
the church from poses inside them. Each surface that didn't match is listed below.

## The audit
| Area | What was wrong | What changed |
|---|---|---|
| Bunker hall, CRT room, looping rooms, maze (Acts 8-10) | The concrete shader drew the scan's colour only, with no relief and one roughness. Under the lantern it was a flat print of concrete. The hall floor was the palest, cleanest surface down there. | `bunker_concrete.gdshader` takes the scan's normal map (world-triplanar, whiteout-blended) and its roughness map. The walls use `concrete_wall_006` at 1.4, the floors `concrete_floor_worn_001` at 1.1. The hall floor is darker (0.76) with more grime (0.6, was 0.4). The facade outside is left as it was: it's part of the reference. |
| The bunker's CRT cases, telephone, plastics (Acts 8-10) | Pale cream, clean, semi-gloss (0.84 albedo, roughness 0.55): new plastic. | The plastic texture is yellowed and darker, with grime in blotches and specks. Roughness 0.78, specular 0.3. |
| Forester station lobby (Act 13, kept stage) | A clean yellow-cream room. The paper's stripes were near white under the lamp and the rug was a flat, saturated red and gold. | The paper is greyer, darker and foxed, and has the plaster's relief and roughness (`SurfaceKit.Relief`). The rug is a faded wool with the carpet's weave in relief (no flat colour). The wainscot, floor and ceiling boards are a step darker and greyer. The lamp is unchanged, so the stages still read. |
| Station room 3 / the stairwell's corridor (Act 14) | Riveted steel plate, pale and clean, with a hot glint under the lantern (roughness 0.5, metallic 0.55). | Darker, duller plate (roughness 0.74, metallic 0.4). Grime heavier toward each plate's foot, rust running down from the rivet rows and seams, rust blooms. |
| The boss room's pipes (Act 18) | The lagged pipes were clean pale cream. The painted, galvanised and foil pipes were glossy (roughness 0.3-0.45). | The lagging is darker (tint x0.68), with water stains, soot and frayed bands. The painted pipes are at least 0.68 rough, with less metal. Galvanised is 0.72 rough and darker, foil 0.5. |
| The library (Act 19) | Flat untextured surfaces: pale plaster (0.85), a flat red, gold and purple rug, flat red armchairs. A glossy orange floor (roughness 0.4). The fireplace was pale clean concrete. | The plaster is `vault_plaster` with its relief and roughness, darker (0.5). The woodwork and floor are duller (0.75-0.8), with the hinoki grain in relief and a browner, darker floor. The rug is muted, with the carpet's weave. The chairs are leather with their relief. The fireplace is sooted stone (`fireplace_stone`). The passage gains the concrete's relief (its colour still matches the round room's). |
| The church (Acts 21-22) | Already dark and old. Only the marbles were polished (roughness 0.3), and the red aisle read as a bright red runner. | Both marbles are 0.52 rough. The red marble's tint is browner (0.5, 0.43, 0.41). |
| The lodge's mudroom (Act 23) | The skis were a bright glossy red. | An old, chalky red lacquer (0.33, 0.12, 0.09; roughness 0.72). |
| Act 25's snow edge at the trailhead | The winter's fresh snow, out in the autumn dusk, glared as clean white shapes under the lantern. | Its own copy of the snow: old snow going grey (top 0.53, sides 0.38-0.45) with less glisten. The lodge's and the maze's snow are unchanged. |

**Looked at and left as they were:**
- **The sewer (Act 17).** Rusty and grimy already, a match.
- **The narrow corridor and the red-light hall (Act 15).** Varied concrete, already with relief.
- **The winter woods (Act 22).** Snow stays snow.
- **The lake at dawn (Act 12).** The dock shares the cabin's deck wood.
- **The church interior.** Apart from the marbles.

**Not touched (another agent's work):**
- The lodge's glowing wall panel.
- The Act 16 closet's cans.
- The Act 24 snow tunnels.
- The Act 21 room. The round room (Act 20) shares that room's `_stone` material, so it is left too, though it has the
  same pale, flat concrete. That material wants the treatment the library's passage got: stained concrete with its
  relief.

## Shared code
- `SurfaceSets.Relief(m, set, normal)`: a baked set's normal and roughness maps only, for a material that draws the
  set's albedo itself (a triplanar projection, a tint). Once per material.
- `bunker_concrete.gdshader`: `nor_tex`, `rough_tex`, `normal_strength`, `rough_map` (0 = off, as before).

## Cost
No new texture files: every map was already in `assets/textures/surfaces`. The bunker's concrete samples two more
triplanar maps (6 taps). The library draws four of the small existing sets: plaster, carpet, leather and fireplace stone.
