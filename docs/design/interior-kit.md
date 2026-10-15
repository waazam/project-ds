# The interiors kit (2026-10-14g)

Shared tools for making a procedural room read as an old, abandoned 20th-century building rather than a clean box
(the look survey, `test-output/survey/SURVEY.md`). The church vestry (`scripts/world/Church.Vestry.cs`) is the worked
example; read it first. Everything works in the room's own node space and goes into the room's `MeshKit`s.

| Piece | File | What it does |
|---|---|---|
| Tessellation | `MeshKit.Tess` | Big quads drawn as a ~0.6 m grid (plus a row 15 cm in from each edge), so baked colour has vertices to sit on |
| Bevels | `MeshKit.Bevel` / `BevelAxis`, `MeshKit.ChamferBox`, `MeshKit.BevelBox` | Eased edges, 2-4 cm |
| Trim | `TrimKit` | A 2D moulding profile swept along a path, mitred and capped |
| Baked dark and dirt | `RoomShade` | Seams, corners, height gradient, under furniture, soot, light pools, damp and wear masks, in vertex colour |
| Weathered surfaces | `RoomSurface` + `assets/shaders/room_surface.gdshader` | A StandardMaterial3D redrawn with a world-space grime overlay, one pass |
| Decals by rule | `DecalDresser.Room`, `DecalDresser.Place` | Stains, cracks, peeling paint, picture ghosts, scuffs, soot, ceiling stains |
| Clutter | `ClutterKit` | Small props placed by rule: walls and corners first, stacked, leant, toppled, seeded |

## Recipe for a room

```csharp
var k  = new MeshKit();   // what casts shadows: the outer shell (so light doesn't leak out), furniture
var lk = new MeshKit();   // the room's inner skin, trim, floor, ceiling: no shadows needed for lights inside it
var small = new MeshKit();// small clutter: no shadows (the bake's dark under it does that job)

// 1. surfaces: weather the room's own materials (cached; returns the original if it can't)
var wall  = RoomSurface.Of(MyTextures.PlasterMat, RoomSurface.Plaster, seed);
var floor = RoomSurface.Of(MyTextures.BoardsMat,  RoomSurface.Wood, seed);

// 2. the big planes, tessellated
lk.Tess = 0.6f;
lk.Mat(floor); lk.Quad(...floor...);
lk.Mat(wall);  StationKit.WallAlongX(lk, body, ...);   // or any wall builder that uses Quad / BuildKit.Box
lk.Tess = 0f;

// 3. trim
var corners = new[] { /* the walls' inner faces at floor level, in order round the room */ };
var doors = new[] { (doorPointOnWall, doorWidth + 2 * casingWidth) };
lk.Mat(paintedWood);
float face = 0f;
foreach (var (run, closed) in TrimKit.Perimeter(corners, doors)) face = TrimKit.Wainscot(lk, run, 1.0f, beadboard: true, closed, roomCentre);
lk.Mat(wood);
foreach (var (run, closed) in TrimKit.Perimeter(corners, doors)) TrimKit.Loop(lk, run, TrimKit.Skirting.Out(face), closed, null, roomCentre);
TrimKit.Loop(lk, cornersAtCeiling, TrimKit.Cornice, true, Vector3.Down, roomCentreAtCeiling);
TrimKit.Casing(lk, TrimKit.RectOutline(doorFoot, alongWall, w, h), wallNormalIntoRoom);
TrimKit.Lining(lk, TrimKit.RectOutline(...), wallNormal, wallThickness);   // the reveal through the wall
TrimKit.Sill(lk, sillLeft, sillRight, wallNormal, depthIntoWall);
TrimKit.Beam(lk, a, b, 0.2f, 0.25f);

// 4. the bake (describe the room, then apply to each kit before CommitTo)
var shade = new RoomShade(x0, x1, z0, z1, floorY, ceilY)
    .Under(wardrobeBounds).Soot(lampPos).Light(lampPos, 4f).Damp(underWindow).Path(door, desk);

// 5. clutter (registers footprints into the bake, colliders into the body)
var clutter = new ClutterKit(roomBox, seed, shade, body).Keep(doorSwing, 1.5f).Keep(furnitureBox);
clutter.InCorner(k, ClutterKit.Model("crate", roles, canStackOn: true));
clutter.Lean(small, ClutterKit.Frame(wood, clutterMat));
clutter.Scatter(small, items, 16, wallShare: 0.75f, onTop: smallItems);
clutter.Topple(k, ClutterKit.Model("dining_chair", roles));

shade.Apply(k); shade.Apply(lk); shade.Apply(small);
k.CommitTo(room, "Room", true);
lk.CommitTo(room, "RoomLining", false);
small.CommitTo(room, "RoomClutter", false);

// 6. decals by rule
DecalDresser.Room(room, new DecalDresser.RoomSpec { Box = roomBox, Seed = seed, Windows = {...}, Doors = {...}, Flames = {...}, Painted = true });
```

## Notes

- **Profiles** are `(d, h)` in metres: d out of the wall, h along the run's side (up for skirtings and rails, down for a
  cornice, away from the opening for a casing). Ready-made: `Skirting`, `SkirtingTall`, `CasingProfile`, `ChairRail`,
  `PictureRail`, `Cornice`, `Board(t, w)`. `Scaled(d, h)` and `Out(d)` (stand it in front of wainscot) derive new ones.
- **`Loop` needs to know which side the room is on**: pass the room's centre as `inside`, or it uses the run's own
  average, which is wrong for a single straight run.
- **The bake needs vertices.** A wall drawn as one quad gets one colour per corner. Draw rooms with `Tess` set, and keep
  it off for small things. The church's `Wall()` strips and `BuildKit.Box` both go through `Quad`, so they tessellate.
- **The bake needs materials that multiply vertex colour.** `RoomSurface` materials always do. Most of the game's
  texture factories set `VertexColorUseAsAlbedo`; check yours. Transparent materials are skipped (their alpha is kept).
- **Retrofitting an existing builder:** `shade.ApplyTo(meshInstance, roomNode)` bakes into a mesh already committed (it
  is not tessellated, so it shows best where the builder drew finely), and `RoomSurface.ApplyTo(meshInstance, preset)`
  swaps its materials as overrides.
- **Auto exposure eats global darkening.** Only relative contrast shows, so the bake's seams and height gradient carry
  it, not `Dim`. Lighting itself (lantern range, ambient, fog) is not this kit's job.
- **Weathering presets:** `Plaster`, `Stone`, `Concrete`, `Wood`, `Paper`; tune one with
  `RoomSurface.Plaster with { Streaks = 0.8f }`. Use a different `seed` per room so stains don't repeat.
- **The shader's inputs from the bake:** vertex colour rgb is occlusion. Alpha is 1 minus wet on walls and 1 minus wear
  on floors. UV2 is (height above the floor, depth below the ceiling); the tide line and the streaks' start need it.
- **Decals** (`assets/textures/decals`, `tools/Textures/make_decals.py`, 128 px): tide, streak, mould, floor, wine,
  blood, puddle, seep, crack, peel, scuff, ghost, ceiling, soot. `Place` puts one by hand. Keep the depth shallow, or
  it stains the props in front of the wall.
- **Clutter material:** give the small procedural props one shared material and tell them apart by tint
  (`Carton(..., tint)`, `Bundle(..., tint)`). Every distinct material is another draw in every pass.
- **Cost of the vestry demo** (8 x 10 m, 29 clutter pieces, 20 decals): about 165-170 draw calls and about 230k
  primitives a frame, including the lantern's and the candle's shadow passes. The frame-time difference was within
  noise to 1 ms on the dev machine. Most of the primitives are the modelled furniture (a dining chair is 5k
  triangles) drawn again in each shadow pass.
