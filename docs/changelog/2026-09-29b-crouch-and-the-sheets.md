# 2026-09-29b — Crouching, and the dining hall's sheets smoothed

Commit: this entry's commit on `main`.

The owner:
- a toggle-able crouch (C or left Ctrl), to duck under things and, later, keep out of the wendigo's line of
  sight in a stealth section;
- the table sequence in the dining hall needs its animations smoothed, and the sheets' physics a little
  tweaking.

## Crouching (`PlayerController`, `PlayerInput`, `PlayerCameraRig`, `PlayerFootsteps`)
- **The keys:** C or Ctrl, or the right stick's click on a pad. A toggle: press to go down, press again to
  come up.
- **The body:** the collision capsule folds from 1.75 m to 1.1 m, from the top, so its foot stays on the floor.
  It goes under what a standing body can't.
- **The view:** the eye drops 0.6 m, eased over a quarter of a second (a smooth settle, never a lurch). In third
  person the body folds down with it.
- **The pace:** half walking speed, with shorter, quieter steps (6 dB down). No running while crouched.
- **Standing up needs headroom.** Under something low, a press to stand is refused with a soft knock
  overhead, and the body stays down until it's clear.
  - Pressing run while crouched stands up first, if there's room.
  - A move (a respawn, a scene's start) stands the body up where there's room.
- **For the stealth section:** the controller exposes
  - `Crouching` (the toggle's state);
  - `CrouchAmount` (0 standing to 1 down, eased);
  - `HeadWorld` (the top of the head now: a watcher's line of sight should go to the head, not the feet);
  - `SetCrouch(bool)` and `RoomToStand()` (for scripts).
- A cloth rustle as the body goes down or comes up.

## The dining hall's sheets and tables (`SkiLodge.Act23.cs`)
- **The pull:**
  - It used to be a quick lift and then an accelerating yank. It's now one curve, eased at both ends over
    1.25 s: the edge is gathered up off the table, lifted over what's under it, drawn off to the side toward
    the table's nearer end, and brought down beside the player.
  - Drawn straight at the player, it billowed over the camera and hid the reveal.
  - It's let go a few points at a time from the ends in, so it slips from the hand, with a soft rustle as it
    drops.
- **The cloth:**
  - softer and better damped (it had fluttered and shivered);
  - a touch of air drag, so it billows as it comes off and settles rather than drops;
  - a finer simulation.
- **Riding over what's under it:** the plates, the bones and the bowl are solid to the sheet alone (a collision
  layer only the cloth feels, not the player or the interaction's rays), so the linen lifts over them. They used
  to show through it as it slid.
  - They come on a moment into the pull, once the linen is lifting, so it isn't thrown off them at the start.
  - The skull, a head taller than the linen lay, appears as the sheet sweeps over it, at the top of its lift,
    rather than cutting up through the rising cloth.
- **Setting it down:** the sheet is set down as a still mesh only once it has come to rest. A fixed 3 s used to
  freeze it mid-slide, which showed as a snap.
- **The fifth table's collapse:**
  - It used to fold into a V and stop with its ends in the air, with two legs standing in for four.
  - Now the top cracks in the middle and sags, then comes down flat to the floor as all four legs kick out
    from under it, and settles with a small bounce.
  - A Continue after it shows the same rest (the fallen top and all four legs), not the old V.
- **The collapse's dust:** soft and faint. Bright and small, the puffs had read as snowballs.
- **The platter:** tarnished silver. The mirror finish reflected the dark room and showed as a black disc.
- **The bowl's snow** is the soft indoor snow; it looked like crumpled paper.

## Fixes
- **Rooms 202–204:** the bed moved 0.2 m west and the sofa closer to the wall. Between them was only a body's
  width, so reaching 203's windowsill (the dining room's key) was a squeeze a player could stick in.

## Tests
- Act 23's story test has a crouch check in the lobby, on a beam put up for it 1.25 m off the floor:
  - standing, the beam stops the body;
  - crouched, the body is 1.1 m, the eye 0.6 m lower;
  - it goes under at half pace (1.35 m/s);
  - under it, standing is refused;
  - clear of it, the body stands and the eye is back where it was.
- Two new screenshots catch the first sheet mid-pull and just let go, so the motion is checked, not only where
  it ends.
- The route from 203's sill to the bathroom goes down the (now wider) gap between the bed and the sofa.
- Act 23: 56/56. It landed together with 2026-09-29c; the full autotest and the continue test for both are in
  that entry.
