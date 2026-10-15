# 2026-10-14g — The shed's steps: you can walk up into the shed for the hammer

The owner couldn't get up into the shed in Act 5 to fetch the hammer. The fix is in the same commit as this file.

## Bug fixes
- **What was wrong:** the shed stands on a steep hillside: the ground rises about 0.4 m a metre to the east. It faced
  west, straight down the slope.
  - Its floor grounds itself just above the highest ground under it (the uphill back corner).
  - So the sill stood 1.05 m over the ground at the door, 1.4 m a metre out, and 2 m at three metres.
  - The only way up was a single fieldstone step, and its collider left a ledge the capsule can't climb (the player
    has no step-up).
  - A ramp collider in its place wasn't enough either. To reach falling ground it had to be about 35° and 3 m long.
    In the playtest the bot stalled on it, sidestepped off, and ended 1.2 m under the floor beside the door.
- **Now:**
  - **The shed faces south**, across the slope and toward the cabin (`hollow_world.tscn`, same spot). The sill now
    stands about 0.75 m over the ground, and the ground in front barely falls.
  - **A flight of weathered plank steps** (`Shed.BuildSteps`) replaces the stone:
    - Two board stringers. Each has a plumb cut at the sill and a level cut where it meets its own ground, so it sits
      right on a cross-slope.
    - Each tread is two planks on cleats, with nail heads and a little jitter. A flat stone sits under each
      stringer's foot.
  - **It adapts to the ground.** However proud the floor stands, the flight takes as many risers of at most 20 cm as
    it needs: four here, up to 14 elsewhere.
  - **The collision is one smooth ramp along the nosings.** It runs from the floor's edge, past the foot and into the
    ground, so there is no lip at either end.

## Tests
- Playtest `5a`: "the shed walked into from the yard" passes. The bot starts 3 m in front of the door and walks in on
  foot.
- `--gap-audit`: 0 near misses.
- Ground audit: the shed shows one 12 cm "lip" cell under the downhill stringer, where it crosses a dip. It is not
  floating or hanging.
