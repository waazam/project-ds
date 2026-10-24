# Art baseline, 2026-10-24

Every act from its start, four headings, no HUD: `test-output/look/baseline/aNN/`, tiled in
`test-output/look/baseline/sheet_*.png` (`python tools/art/sheets.py grid ...`). Read all 25. What they show, worst first:

## Priorities

1. **Acts 2–7 and 11 read as near-black.** The Hollow at night is mostly an unlit silhouette: trunks, ground and sky in
   the same few values. Dark is the point, but nothing has shape. Wanted: a cool moonlit rim on trunks and the path, the
   ground a step lighter than the trees, the fog lit faintly from above so trunks stand out against it. Start in
   `ForestAtmosphere` (per-act moon and fog), not in materials.
2. **Acts 8–10: the whole frame is one red-orange tint.** The fire's grade washes over everything. Keep the red at the
   fire and let distance go back to night blue-black.
3. **Acts 15–17 (lake shore, sewer approach) are black frames** with a single lamp. These are the same problem as 1, in
   a different place.
4. **Act 14 (stairwell): flat rust-mottle walls** read as camouflage, not as rusted steel. The pattern is too large
   and too even. Wanted: rust running down from seams, bolts and edges; paint holding on flat areas.
5. **Act 23 (lodge rooms): plain boxes.** Walls, a door, a picture. No trim, skirting or ceiling detail. The
   interior kit (`TrimKit`, `RoomShade`) isn't applied here yet.
6. **Act 24–25 snow tunnels:** the snow is a single orange-lit noise texture that reads as foam. It needs layered,
   wind-packed snow with blue shadow.
7. **Act 1 holds up best** (golden light, cabin, signs). The pilot starts there.

## What already works (keep)

Act 12's lakeside at dusk (light, fog, silhouettes), Act 13's station rooms (wallpaper, wainscot, clutter), Act 19's
library (warm practicals, books), Act 20's webbed room, Act 18's tentacle sewer (red practicals against black).

## Pilot: the trailhead cabin (Act 1)

`test-output/look/cabin_pair.png` (before over after, the six poses in `docs/art/poses.md`):
- The foundation and chimney are now Poly Haven's laid fieldstone scan, in place of the drawn stone. Its even
  hexagonal pattern had read as tile.
- Each log has its own girth, taper and slight bow. They were identical straight cylinders.
- The logs are weathered by height: darker near the ground, sun-bleached higher up. The outside wood is warmer and
  less washed out, matching the porch and gables.
- The chinking is a patched old lime, darker and varied, where it had been one bright line per course.

Honest read: the stone is a clear win. The logs are better, but the change is subtle at distance under the post.
Next on the cabin: moss and lichen on the lower logs and the stone, rust streaks under the door hardware, a gutterless
drip line in the dirt under the eaves, and leaf litter piled against the foundation.
