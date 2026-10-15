# 2026-10-15 — Props turned, not stacked: the banker's lamp, the library, the church's candles; the hall not a hospital

Dan: "do another pass on all props like lamps on the table etc and make sure they're proportioned and not too blocky
looking. For example the green lamp in the room." And then, for the Act 15 hall: it isn't meant to be a hospital.

## New kits
- **`TurnedKit`** (`scripts/world/TurnedKit.cs`): a lathe that takes any profile (flat steps, overhangs, lips) and keeps
  its sharp turns crisp (over 38-50 degrees) while gentle ones stay smooth, so a knop reads round and a step reads as a
  step at 640x360; `Rod` (a bent rod along a curve, its rings kept square to the curve's plane so they never twist);
  `Curve`; `Leg` (a turned table leg under a square block for the apron). MeshKit is untouched.
- **`LampKit`** (`scripts/world/LampKit.cs`): `Banker`, `Floor`, `Sconce` and `Candlestick`, built to real measure.

## The green banker's lamp (Act 19, the reading table)
| | before | real | now |
|---|---|---|---|
| base | round brass disc 22 cm, a cone | oval stepped brass, 20-22 x 13-15 cm | oval 21 x 14 cm: plinth, bevel, step, low dome, collar |
| stem | plain 2.4 cm rod | turned, a knop | turned 1.5 cm, a knop and collars |
| shade | a full green cylinder 32 cm long, 26 cm across, squashed: a barrel | half-round trough 25-28 cm long, 12-14 deep, 9-10 high | half-round trough 27 x 14.4 x 8.8 cm, flared rims, rounded ends, tipped 11 degrees toward the reader |
| glass | one flat emissive green | cased: emerald outside, opal white inside | glossy emerald outside (faint glow), glowing opal inside, a pale rim line |
| harp / socket | none | socket under the shade, arms | brass socket under the shade's middle, two harp arms with ball finials |
| pull chain | a 4-sided stick | bead chain, a brass drop | a fine chain curving out past the front rim, a turned drop (casts no shadow) |
| height | 0.4 m to the shade's top | 36-40 cm | 0.37 m |
| light | a 7 m omni with shadows at the shade | a pool on the desk | a 58-degree warm spot under the shade (shadows) and a faint 4.5 m spill |

## The rest of the library
- **Reading table** and **the sheeted side table**: a top with a moulded edge, an apron set in, four turned legs (a
  square block, collar, taper, ring, bun foot). They were slabs on square posts. Same sizes, same colliders.
- **Floor lamp**: a weighted domed foot, a pole with turned collars and knop, a flared drum shade with brass bindings,
  lined, lit through; 1.6 m. **Sconces**: a turned wall rosette, a curved arm, a cup, a flared parchment shade.
- **Globe**: a turned wooden column on three splayed legs, a horizon ring on a cradle, the brass meridian tipped with
  the axis; 54 cm globe, its middle at 0.9 m. It was a 60 cm ball on a 1.2 m brass rod (its top at 1.45 m), and it had
  no collider: it has one now.
- **Mantel candlesticks**: turned brass, 22 cm, with burnt-down candles and a wax run. They were brass cones, and they
  stood 5 cm inside the chimney breast, so nobody ever saw them: now they're out on the shelf in front of it.

## Act 21: the church
- **Every candle** (votive racks, the crown, the door sconces, the chapels, the altars): turned, 8 sides, burnt into a
  hollow at the top, a lip of wax at the foot (six-sided sticks before).
- **The four great candle stands** (interactable): curved legs with paw feet, a turned stem with knops and collars, a
  dished drip pan, the candle burnt down with a wax run. Same mesh name and place, so their outline and colliders hold.
- **The crown**: round iron hoops with a band of uprights and a cup under each candle (the hoops were square bars).
- **Door sconces**: a wall plate, a scrolled arm, a drip pan with a pricket.
- **Transept altars**: the candles now stand in iron candlesticks (they stood on the cloth on nothing); the cross has
  flared terminals and a stepped foot.
- **The font's chain**: round links, each turned across the last, and a rounded padlock (six flat bars and a box).
- **The crypt**: tombs on plinths with worn edges and a raised lid slab; organ pipes on conical feet with mouths.

## Acts 17, 18, 20
- **Round room**: the sconces turned (a rosette, a curved arm, a cup and pan); the floor candles turned, burnt into
  ragged cups.
- **Sewer**: floating bottles turned (body, shoulder, neck, lip); the sacks on the heaps lumpy with tied necks (cones).
- **The pit**: the valves' bodies a cast globe with a bonnet and hex packing nut (a lumpy ball); the risers now come off
  their run on a stub and a swept bend with a flange (they stood off it with a ball at each end).

## Shared: `ClutterKit` (every area that uses them: the vestry, the cabin, the shed, the lodge, station room 1)
- **Bucket**: a pressed pail with a foot ring, two swaged hoops, a rolled rim, its inside to the bottom, ears and the
  wire bail lying down (a capped ten-sided cone). Same footprint, so nothing placed by the kit moves.
- **Broom**: a round handle with a rounded end, a stock with eased edges, splayed bristles (a hexagonal stick on boxes).

## Act 15: the hall is not a hospital
- It reads as an old institutional service corridor, no particular kind of building. The look stays: two-tone painted
  block, vinyl tile, the strips, the bump rail, the fire cabinets.
- Signs: NO ENTRY, NO SMOKING, BOILER ROOM >, KEEP CLEAR, < FIRE EXIT, ELECTRICAL, STAFF ONLY, STORES >, NO ADMITTANCE,
  SERVICE C (were WARD C, RADIOLOGY, QUIET PLEASE, WARD D, ISOLATION, PATHOLOGY, WARD E). Door plates S01, S03... (were
  C-101...). The same draws from the dressing's own dice, in the same order.
- Renamed: `hospital_wall.gdshader` to `corridor_wall.gdshader`, `HospitalTextures` to `CorridorTextures` (with their
  .uid files), the door meshes `ServiceDoor*`; comments and the 2026-10-14h changelog reworded.
- The extinguishers are turned (a domed shoulder, a valve head and handle, a looped hose) instead of a can and a cone.
- Red light, green light is untouched: `Act15Hallway.Rlgl.cs` and `Variants.cs` unchanged, the hall's `_rng` not touched.

## Checked
- Look shots before and after (`look_shots.tscn`, the same poses) in the library, the church and its vestry and crypt,
  the round room, the sewer, the pit, the hall, and the cabin (Act 6, for the shared bucket and broom).
- Playtest beats 5a, 13a, 15, 16, 17, 18, 19, 20, 21, 21c, 23a, 24a, 24c: all pass, 0 engine errors.
- The hall's dice after its build: `8407554524482128862`, as before.
- Frame rate in the look shots the same or a little better: the banker's lamp no longer casts a 7 m omni shadow (the
  library's primitives went from 11.6 M to 7.1 M a frame).
