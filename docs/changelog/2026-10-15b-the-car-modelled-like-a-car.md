# 2026-10-15b — The car modelled like a car: a 90s estate built in Blender, in its arches, faded and muddy

The owner asked: "make the car look less blocky and more real but keep it PS2 style." The first pass
(2026-10-15a) still read as stacked slabs from the three-quarter view. Its wheels looked odd: the rim discs showed
strangely, the tyres didn't read as round rubber in arches, and they seemed to poke below and outside the body. A light
grey wedge sat under the tailgate-end wheel, and the material ignored the mud and fade vertex colours, so the car was a
flat near-black. The fix is in the same commit as this file.

## Changes
- **The car is a model now** (`tools/Blender/car.py`, new; `assets/models/car/car.glb` and its textures). It is a plain
  mid-90s Japanese estate after the second-generation Subaru Legacy wagon (1994-98: 4.68 x 1.70 x 1.49 m, a 2.63 m
  wheelbase, 195/65 R15 tyres, about 15 cm under the sills), kept to the game's 4.6 m so everything that lines up on the
  car still does. It is about 7.7 k triangles: car_body, car_tailgate (lifted) and car_wheels.
  - **One skin lofted through real sections**: the sills tucked under, the flank widest at the doors, a shoulder crease,
    the upper body leaning in to the belt, a glasshouse leaning in another 17 degrees to the roof. The bonnet slopes
    and crowns down to a nose rounded in plan. The windscreen is raked about 60 degrees between its pillars, the roof is
    crowned with rails on feet, and the tail leans in under the raked tailgate glass. Hard edges only where a real car
    has them: the shoulder, the belt, the windscreen's foot (a black scuttle with parked wipers) and its head. Door shut
    lines are cut into the skin, the rear one meeting the arch.
  - **Pillars and glass**: black B and C pillars and window frames, body-coloured A and D pillars, quarter glass, and a
    black frit along the windscreen's edges. The glass is dark-tinted, glossy and see-through. The cabin's side of the
    pillars, frames and door tops is lined in grey trim (from the driver's seat the paint's back faces glared red).
  - **Wheels in their arches**: 65 cm tyres lathed with rounded sidewalls, sitting inside 39 cm arches with a 6 cm gap,
    their outer faces 1.5 cm inside the body. Each arch has a black liner and tub. The tyres have a tread and lettering
    texture, and the steel wheels carry plain slotted plastic trims. The underside is 17 cm off the ground (15 cm on the
    lot after the snap).
  - **Body-coloured wrap-round bumpers** with black rubbing strips, side mouldings, flush headlamps wrapped round the
    corners with the indicator at their outer ends, a grille with a plain badge, and tall tail lamps (red, amber and
    reverse) in the corners beside the opening. Also: US plates (2HW 417), mirrors with glass, door pulls, a side
    repeater, an aerial, an exhaust, gas struts to the tailgate, a tailgate handle and a rear wiper.
  - **The cabin**: a moulded dash with the binnacle's hood over lit-looking gauges, the stack (vents, radio, heater),
    a three-spoke wheel on its column (low enough that the seat's eye sees the road over it), the gear lever, two
    buckets with headrests, the rear bench, door cards with armrests, the headliner, visors, the mirror, and a duffel
    in the back.
  - **Paint, fade and mud baked into the vertex colours**: a faded burgundy, chalked pink on the roof and bonnet, with
    dried and wet mud up the sills, round the arches and thrown back behind each wheel, and road dust over the tail.
    Ambient occlusion is ray-cast against the car and the ground, with the glass letting light into the cabin. Small
    textures sit under the colours: a paint grain with chips and scratches, cloth, the tyre, the trim, and one atlas
    for the lamps, grille, plate, gauges and stack.
- **The game's materials use those colours** (`ParkProp.Car.cs`, rewritten): each of the model's role materials
  (paint, trim, gap, glass, atlas, tyre, hubcap, inner, seat, carpet, headliner, mirror) is replaced with one that
  multiplies the vertex colours by its texture. These are drawn from inside too, for the fifth ending's seat. A soft
  shadow decal grounds the car on the gravel.
- **The grey wedge** was `Bumper2`, the concrete wheel stop of the car's bay. It sat 1.75 m behind the car's middle,
  and the old rear tyre (its axle at 1.38 m) ran 5 cm into it. With the new 2.63 m wheelbase the tyre stops about 5 cm
  short of it, and it reads as the stop the car was backed up to.
- **Kept**: the two collision boxes, the load floor at 0.6 out to the lip at z 2.3 (the trunk camera lies on it), the
  tailgate's hinge at the roof's back edge (lifted to 28 degrees above level, over a standing head), the driver's eye
  at (-0.38, 1.15, 0.25) with EyeHeight 1.0, the roof under the wendigo's landing at 0.9 m once crushed, and the
  opening at the car. The crush still squashes every mesh under the car (the model's three).
- **LookShots** (`scripts/world/LookShots.cs`): `--look-nohud` hides the hands, held things, chapter card and
  subtitles. `--look-endings` photographs the fifth ending's seat view and the third's crushed car, posed as
  `EndingDirector` poses them.

## Verification
- Before and after from the trailhead (front 3/4, side, rear with the tailgate open, far, the front wheel, the cabin
  through the glass, the opening's look into the trunk, rear 3/4), the endings' views, and Blender close-ups:
  `test-output/contact_car_before_after.png`. The before column's last row is a mis-aimed shot.
- Playtest beats 1, 1b, 1c, 25-1 to 25-5 and 25c: 9/9, 113/113 checks, 0 engine errors.

## Files
- `tools/Blender/car.py` (new): the car, its textures and its vertex colours.
- `assets/models/car/` (new): `car.glb`, `car_atlas.png`, `car_grain.png`, `car_fabric.png`, `car_tyre.png`,
  `car_hubcap.png`, `car_shadow.png`.
- `scripts/world/ParkProp.Car.cs` (the model, its materials, the shadow, the collision), `ParkProp.cs` (the old car
  material helper and print gone).
- `scripts/world/LookShots.cs` (`--look-nohud`, `--look-endings`).
