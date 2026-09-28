# 2026-09-27j — Polish pass: a clipping, collision and texture audit

Commit: this entry's commit on `main`.

The owner: "crunch down on debugging, run a thorough polish pass, and conduct a strict collision and
UV/texture audit to eliminate any asset clipping or artifacting before the next build".

## The audit tools
- **`clip_audit.tscn` (`ClipAudit`, new).** Loads the trailhead and the Hollow (with the station and
  everything under it: bunker, stairwell, boss room, library, church) standalone, the save slots
  backed up and put back, and audits every static mesh (about 2,850 meshes, 2.85 million triangles):
  - **Z-fighting:** triangles of different surfaces in the same plane (within 0.1 mm), facing the same
    way and overlapping, measured by true polygon clipping.
    - 0.1 mm because Godot's reverse-Z depth buffer separates anything further apart. A first run at
      3 mm tolerance flagged surfaces that don't really fight.
  - **Walk-through props:** a solid-looking prop at body height with no collider in it (the player
    would pass through it).
  - **UV faults:** collapsed UVs (a smeared texture), NaN UVs, and surfaces whose texel density
    varies more than 12x (stretching).
  - Report: `test-output/clipping/report.txt`, worst first, with an example triangle pair for each
    fight.
- **`ground_audit.tscn` (existing), rerun:** 2,187 objects, none floating. The 10 "hanging" are logs
  and a fallen tree bridging slopes, which is natural.
- **`tools/Textures/scan_shots.py` (new):** scans all the story test's screenshots (626) for
  artifacting: magenta (missing textures), blown-out frames, all-black frames, and flat-row render
  glitches.
- The full test run's engine log: no errors or warnings (no missing resources, no shader errors).

## Clipping (z-fighting): 148 pairs → 0 that can be seen
- **Inside one mesh** (a paint band on a post, a dial on its face, a sign's letters on its board; the
  car, shed, cabin, table, chair, info board, trail markers, backpack, fire ring):
  - `MeshKit` now separates them as it builds any mesh. Where a later-added surface lies in the plane
    of an earlier one, facing the same way and overlapping it, the overlay is lifted 1.5 mm along its
    normal.
  - It uses a plane-bucketed sweep with Godot's polygon intersection, so it's cheap.
  - This fixed every intra-prop fight at once.
- **Between meshes:**
  - the bunker's exit hall against its rooms (shared walls and floors);
  - the stairwell's bottom chamber against the shaft and the Act 15 hallway (the chamber's passage
    duplicated the hallway's first metres);
  - the trench against the shaft and Room 3's floor edge;
  - Room 3's plate and Room 2's window frame, flush on their walls;
  - the bunker's door frames and entry, and the vine door's frame, with thresholds and jambs flush on
    floors and in openings;
  - the CRT on the console.
  - Fixed with three small tools on `MeshKit`:
    - `Nudge`: a hair along one direction, for an overlay proud of its wall;
    - `NudgeAll`: a hair on every axis, for a whole shell;
    - `Shrink`: 0.2% about the piece's centre, for a lining whose jambs and sill must sit just inside
      the opening.
- **Left, and why** (38 pairs, all where no one can see them):
  - faces at floor level inside wall bases and bollard bases;
  - undersides (a table-top's, the sewer water's, the cigar box lid's);
  - pipe end caps inside their flanges;
  - two eye sockets inside the leviathan.

## Collision (walk-through props): 15 → 0
- The lobby's potted plant (and its dead stage), coat stand, bench and upturned chair now have box
  colliders from their meshes (`MeshKit.Solidify`). They come and go with the lobby's decor stages,
  so a hidden stage never leaves an invisible wall.
- Also given colliders:
  - the chair on the stairwell's landing;
  - the wreck in the stairwell's chamber;
  - the lake rescue station's upturned boat and flagpole;
  - the sewer's shut door;
  - Room 3's bollards;
  - the forgotten tent at the trailhead.
- Checked and meant to be so: the Act 15 closet (walked into), the sewer's hole, a ladder flat on a
  wall, the boss room's overhead fixtures, and things mounted on walls (a clock, a notice board, a
  wheel, a painted-shut door).

## Textures and UVs
- **Stretching:** the round stream and waterfall stones had sphere-wrapped UVs, pinched at the poles
  and stretched where the stones are flattened (up to 73x from one part of a stone to another). They
  now use the rock texture projected from the world's axes (triplanar), so there's no stretching.
- **No collapsed or NaN UVs anywhere; no magenta** (missing texture) in any screenshot.
- **Slivers:** `MeshKit` drops triangles under 0.1 mm² as it builds (cone tips, cap centres). The
  32,000 "degenerate" triangles the audit still counts turned out to be the finely tessellated tiny
  spheres of eyes and bulbs: harmless.

## Visual fidelity: the second half of Act 14 was pitch black
- The screenshot scan found every frame below the stairwell's halfway point at a mean brightness of
  0 (out of 255), the blacklight included.
  - Its deep violet lit the UV ink but hardly the walls: blue carries little perceived brightness.
  - Now the crawler hunts there, and the steps have to be seen.
- The blacklight's spill is up modestly: 0.32 → 0.55 round the lantern, and 2.6 → 3.2 in its cone.
  The stairs and rails show dimly a few metres out; no glare. The forest's UV secrets still only
  show where the cone points.

## The dead eye (the basement drain and the iron door's LOOK hollow)
Something cut across the eyeball in both places. Three causes, all fixed (one model serves both):
- **A drooping lid:** a sheet of skin over the top whose front edge dropped through the ball and read as
  a dark band. Removed; the wet rim round the eye stays.
- **The milky film** (a see-through sphere at 1.03x the ball) sat inside the iris and pupil, which stand
  out to 1.07x, so they poked through its facets. The film is now at 1.12x, rounder, and thinner
  (alpha 0.45 → 0.28), so the red iris shows through clearly.
- **The drain grate:** the eye rested 8 cm above the floor with a 14 cm radius, so it was sunk into the
  floor and the grate's bars crossed it. It now sits on top of the bars (17 cm), and the same on the
  other floor spots it hops to in the hunt.
- In the iron door the eye no longer carries its torn stump, which came out of the back of the door.

## Tests
- **Full autotest:** 569/570, average 109 fps while walking.
  - The one failure is Act 6's "cannot back down on flight 1" (the one-way wall hadn't armed yet). It is
    a timing flake: Act 6 alone passes 58/58, and the run before passed 578/578.
- **Caught on the way:** a box collider round Room 3's bollards and chain sealed the steps down, so
  Act 14 couldn't start its descent. Reverted: the posts already have colliders of their own. The audit
  now lists the bollards as checked.
- **Continue test:** 698/701. The three failures predate this work: Act 6's cabin fire, and Act 10's
  lighting mood and clearing stairs.
- **Clip audit:** 38 z-fights left, all hidden; 0 walk-through props. **Ground audit:** nothing floating.
- **Screenshot scan:** no magenta and nothing blown out. The black frames left are ones meant to be
  black (the closet, the sewer pipe, the jump, fades).
- **Engine log:** 0 errors.
