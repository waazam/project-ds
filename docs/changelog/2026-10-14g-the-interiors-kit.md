# 2026-10-14g — The interiors kit: trim swept from profiles, the dark baked into the vertices, weathered walls in one pass, decals and clutter by rule; the vestry rebuilt with it

The look survey (`test-output/survey/SURVEY.md`) found the interiors were clean boxes. Each big plane was one tiled
material from edge to edge. There was no trim, no dark in the corners or at the floor, no grime, and the floors were
empty. This is the shared toolkit for fixing that room by room, and one room, the church's vestry, built with it as
the proof. The API is in `docs/design/interior-kit.md`.

## The kit
- **`TrimKit`** (new): a 2D moulding profile swept along a path, its corners mitred and its ends capped.
  - **Profiles:** skirting (plain and tall), door and window casing, chair rail, picture rail, cornice, and boards.
    `Scaled` and `Out` derive new ones.
  - **Runs:** `Loop` goes round a room, and `Perimeter` cuts the room's outline at its doorways.
  - **Openings:** `Casing` goes round any opening's outline, a square head (`RectOutline`) or a pointed arch
    (`ArchOutline`). `Lining` covers the reveal through a wall's thickness. `Sill` is a window board with its apron.
  - **Wainscot:** V-grooved beadboard, or stiles and rails round fielded panels, capped by a chair rail.
  - **`Beam`:** a chamfered timber.
- **`MeshKit`**:
  - **`Tess`:** big quads are drawn as a grid of about 0.6 m, with an extra row 15 cm in from each edge, so baked
    colour has vertices to sit on.
  - **`ChamferBox`:** only the four edges along one axis are chamfered (a wall's or a post's corners).
  - **`Bevel` / `BevelAxis`:** `BuildKit.Box` eases its edges (StationKit's walls and the like get bevels with one line).
  - **`Shade`:** a vertex visitor. Second UVs are kept whole when geometry is added after it.
- **`RoomShade`** (new): a room's dark and dirt baked into its vertex colours as it's built, the PS2's way.
  - **Seams and corners:** each of the room's planes darkens what meets it, so corners darken twice or three times.
  - **Height:** a gradient (the wall's foot, under the ceiling, the ceiling itself).
  - **Furniture and flames:** dark under and behind furniture, soot over flames.
  - **Light pools:** dimness lifted near the room's own lights.
  - **Masks for the shader:** wet on walls (under a window, a leak) and worn paths on floors, in the alpha. The second
    UV holds the height above the floor and the depth under the ceiling.
  - **`ApplyTo`:** retrofits a mesh that's already built.
- **`RoomSurface`** + **`assets/shaders/room_surface.gdshader`** (new): a room's StandardMaterial3D redrawn by a shader
  that does what it did (UV or triplanar, object or world space, two-channel normal maps without tangents, roughness,
  the detail grain, vertex colour) plus a world-space breakup:
  - broad mottling;
  - water streaks running down from the ceiling and from damp patches;
  - a ragged tide line of rising damp;
  - ring-edged stains on ceilings;
  - dirt and pale worn paths on floors.

  It is one material and one pass, from a few samples of one 256 px noise. Presets: Plaster, Stone, Concrete, Wood, Paper.
- **`DecalDresser.Room`** and **`DecalDresser.Place`**: decals placed by rule, at once, deterministic, with no
  physics. Under a window go streaks and a seep. Cracks run off the heads of openings and down from the ceiling. There
  are tide stains at the walls' feet, mould in the upper corners, and stains on the ceiling. Painted walls get peeling
  paint and the ghosts of pictures. Scuffs go by the doors and soot over flames.
- **Six new 128 px decals** (`tools/Textures/make_decals.py`): crack, peel, scuff, ghost, ceiling and soot.
- **`ClutterKit`** (new): small props placed by rule, about 70% against the walls and in corners.
  - **Placement:** stacked on each other, leant against walls, knocked over (`Topple`), and turned 2-8 degrees off
    square. The rest are set down beside other pieces, never alone mid-floor. Doorways and kept spots stay clear.
  - **Bake and colliders:** footprints go into the room's bake, and colliders into its body.
  - **Pieces:** any FurnitureKit model (`Model`; `FurnitureKit.Bounds` is new), or the simple ones built here: carton,
    frame, board, broom, bucket and bundle.
- **`LookShots`**: `--look-perf=S` samples the frame time, draw calls and primitives at each pose.
  `--look-perfhide=A,B` repeats the sample with those nodes hidden, giving what a room costs.

## The vestry (`Church.Vestry.cs`, new)
It had been bare ashlar edge to edge, with a plank slab for a wardrobe, an empty floor, and even light. It is now a
sexton's room of about 1900, left forty years.
- **Walls:** limewashed plaster over the stone (the plain plaster scan; the church's own plaster pattern drowned the
  stains), streaked and damp-stained.
- **Joinery:** painted beadboard to a chair rail, with a tall skirting in front. Above are a picture rail and a cornice.
- **Openings:** casings round the pointed door and the lancet window, and the window's sill and apron.
- **Ceiling and furniture:** the beams are chamfered. The wardrobe is panelled (plinth, carcass, cornice, two panelled
  doors). The table's edges are eased.
- **The bake:** the dark of the corners and seams; under the wardrobe, the table and the hook board; soot over the
  candle; damp under the window and down the leaking corner; boards worn from the door to the table and to the hooks.
- **Dressing:** 29 pieces of clutter and 20 decals.
- **Shell (`Church.Shell.cs`):** the vestry's three stone base courses are gone, replaced by the skirting and wainscot.
  The alb is a step darker.
- **Performance:** the lining, trim, floor and ceiling are in a mesh that casts no shadow. For a light inside the room
  they never stand between the light and anything. The small clutter shares one material told apart by vertex colour,
  and casts no shadow either. Only the stone shell, the furniture, the crates and the chairs cast.

Before and after: `test-output/look/v_before/`, `test-output/look/v_after7/`, side by side in
`test-output/look/vestry_before_after.png`.

**Cost**, measured in place, with and without the room's nodes, at four poses and 8 s each: about 165-170 draw calls
and about 230k primitives a frame, including the lantern's and the candle's shadow passes. The frame-time difference
was between noise and 1 ms (12-14 ms frames either way). A first cut cost about 315 draws and 480k primitives, before
the shadow split and the shared clutter material.

## Not done here
- **Lighting:** the lantern's range, the ambient, fog indoors and the window glass belong to the lighting pass
  running alongside. The window still glows white.
- **Other rooms:** they are the area passes' job. `docs/design/interior-kit.md` has the recipe.
