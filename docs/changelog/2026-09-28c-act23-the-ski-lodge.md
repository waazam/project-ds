# 2026-09-28c — Act 23: the ski lodge

Commit: this entry's commit on `main`.

The owner's "Real Act 23" document and reference pictures, and the brief:
- the lodge more eccentric and high-society than anything else in the game, a stark contrast;
- The Shining as the heavy inspiration, made our own and not the Overlook copied;
- a major set piece before the finale: pull out all the stops.

The act ends (for now) at room 201's door; what's inside 201 comes later.

## Story beats
1. **Snowed in.** Through the back door (Act 22's end), a rumble overhead; they turn, and the roof's snow
   comes down and buries the doorway behind them, a spray of it blowing in.
2. **The bar.** Room 202's keycard is taped under the counter's lip, on the customers' side.
   - In the lantern's flame there's nothing there. By the blacklight: a smeared handprint and the word
     UNDER on the counter's front, and the card's edge glowing.
   - Peeled off, the note taped to its back comes up to read: the closing staff's instructions ("stick
     together in pairs… do not listen to any strange noises or voices…").
3. **Room 202.** Blue with the snow banked up its windows.
   - Six places to search: both nightstands, the two vanity drawers, the wardrobe, the closet.
   - In one of them (a different one each game, kept in the save) is a key with a note wrapped round it:
     "Servant's Pantry key."
   - Leaving with it: the reader sparks, its light dies, and the door slams and jams (a save).
   - Stand at the door and listen: something inside, in a voice copied from someone, saying it's too cold,
     it's starving, "always… hungry".
4. **The servants' pantry**, off the lobby under the balcony. As the key opens it, the mop that was leaning
   inside comes down across the doorway: a false scare. In one of its eight drawers (random again) is 203's
   keycard.
5. **Room 203.**
   - The window is left open and the room is full of snow and ice: drifts over the carpet, ice on the
     floor, icicles off the curtain rail, snow blowing in.
   - Outside, low, a voice mumbles ("freezing… too cold… hungry… starved"), louder near the window.
   - The dining room's key is on the sill.
   - In the bathroom, the wall to 204 is broken through: a ragged hole, plaster and lath on the green tiles.
6. **Room 204.** Ordinary, but for the snow come in through the hole. In one of its drawers (random) is a
   smudged note: "If you can hear it, it already knows you are here."
   - 204's door opens from inside.
   - Out in the corridor, 203's door slams and jams too (a save). Behind it now there's only the snow
     coming in.
7. **The dining hall.** Frosty, a window left open at the far end. Six long tables under white sheets, and
   the sheets come off as cloth. The n-th sheet pulled is the n-th thing, whichever table:
   1. dirty dishes, left for weeks;
   2. a streak of blood dragged the length of the table and down its end; low laughing outside (about
      5 s);
   3. a frozen, headless skeleton, dirt and snow on it. The storm gets up: the windows go grey and dark,
      the wind rises to a howl. Faint laughing again;
   4. roaches pouring off the table in every direction, and on a silver platter the wendigo's own skull
      (the elk skull, the man's jaw, the snapped antler), still twitching, blood pouring from its mouth.
      The laughing, loud now, runs round the room in two voices going opposite ways;
   5. the table breaks: the top folds into a V, the legs kick out, a crash and dust (a false scare);
   6. always the last: 201's keycard, standing in a bowl of snow. Taken, the snow slumps and runs red:
      blood, to the brim (a save).
8. **Room 201.** The card: the reader goes green and the door opens on the dark. The fade, and the credits
   (the demo's end until 201 is built).

## The lodge inside (new: `SkiLodge.Interior.cs`, `SkiLodge.Furnish.cs`, `SkiLodge.Act23.cs`, `lodgeparts/`)
Everything fits inside the Act 22 exterior. The hall's six walls are rebuilt with openings for the bar's
arch, the pantry's door, the dining hall's double doors and the upstairs corridor.

- **The lobby:** the whole hexagonal hall, open to its great timbered roof: log rafters on the corners and
  a ring beam.
  - Stone below, split logs above.
  - A river-stone fireplace up the back-east wall to the roof, a low fire in it, an elk's head over the
    mantel.
  - Chesterfields and violet wing chairs round a big medallion rug; floor lamps.
  - An iron-and-antler chandelier with 28 candle bulbs.
  - A balcony round the west and back walls, with log posts and an iron railing, reached by a grand
    staircase of 24 treads with a carpet runner.
  - The front desk by the doors: panelled, gold-lipped, a bell, the ledger, a green-shaded lamp,
    pigeonholes, and the four rooms' key hooks (201's empty).
  - The front doors from inside: chained through their handles, the padlock on this side.
- **The Gilded Antler** (the bar, after the Gold Room, ours):
  - a long counter lit from within, amber through frosted panels, with a gold lip and brass stools;
  - a mirrored back bar of bottles;
  - red damask over dark wainscot;
  - small black tables with candle lamps;
  - amber lights down the ceiling.
- **The service corridor** from the mudroom, and **the pantry**: chequered linoleum, shelves of tins and
  jars, a run of drawer cabinets, a bare bulb.
- **Upstairs** (off the balcony at the hall's west corner): a corridor carpeted with rows of nested pointed
  arches in oxblood, orange and black (our answer to the Overlook's hexagons), striped cream-and-gold paper,
  brass numbers and keycard readers.
  - The rooms: peacock-scale carpet in bottle green and violet, violet velvet beds and sofas, and snowlight
    through the windows.
  - Bathrooms tiled mint-green, the tub in an arched alcove with a mustard trim and lace curtains (after the
    references, ours).
- **The dining hall:** the east wing's full height, herringbone parquet, a coffered ceiling of dark beams,
  three chandeliers, both rows of windows, six long tables and 48 violet chairs.
- **The light:** noon behind the snow, and every lamp lit though nobody's there.
  - Warm light everywhere, a faint haze (a new "lodge" lighting mode inside), cool daylight through the
    frosted glass.
  - Never a glare: the window glass is toned grey-blue.
- **New pieces:**
  - `LodgeKit`: walls with a finish on each face and holes cut for doors and windows (collision to match);
    furniture (sofas, armchairs, beds, tables, chairs, lamps, chandeliers, sconces).
  - `LodgeDoor`: hinged doors with keycard readers whose light goes red, green or dead, doors that open
    with a key, and doors that slam and jam.
  - `Searchable`: drawers that slide out, and cabinet doors that swing open, with a find put in when opened.
  - `LodgeTextures`: all procedural, PS2-sized.
- **Items:** keycards (cream plastic, a gilt crest, a band per room), the pantry key in its paper envelope,
  the dining room's ornate brass key.
- **Notes:** a new smudged style for the note reader. Each word's ink is a different strength and some are
  nearly rubbed away, with a soft doubled shadow under the strokes.

## Sound (new, `tools/WinterAudio/make_lodge_audio.py`)
- The wendigo's voice through a door (room 202), and mumbling outside a window (203): its copied voice,
  muffled or slurred.
- Three laughs:
  - a long low one;
  - a short faint one;
  - a big one, played moving round the dining hall.
- The keycard reader's beep, error and spark; the mop's clatter; the roaches; the table's collapse.
- The snow burying the door, and a storm-wind loop that rises when the storm gets up.

## The compass
It points at whatever the lodge wants next: the bar's counter, then room 202, then the pantry, room 203,
204, the dining hall, and room 201.

## Saves
Three new checkpoints, and the continue test covers each:
- `Act23Room202Done`;
- `Act23Room203Done`;
- `Act23Keycard201`.

The random hiding places are kept in the save, so a Continue finds them where they were.

## Tests
- A new story-test act, "Act 23: the ski lodge", walks the whole act.
  - It covers being snowed in, the blacklight card under the bar and its note, and 202's six searchables.
  - Then the jam and the voice, the pantry and its mop, 203's snow and mumbling, and the sill key.
  - Then through the hole into 204, the smudged note and 203's jam.
  - Then the dining doors, all six sheets in order (each event checked) and the melt, and 201's door.
  - It passes 51/51.
- `--story-from`/`--story-to` go up to 23.
- Act 22's test now ends snowed in rather than at the credits.
- The continue test has three new scenarios (`act23_room202`, `act23_room203`, `act23_card201`).
  - Each checks the lodge restored as saved: doors jammed or open, finds taken, sheets pulled, the random
    hiding places kept.
- Full autotest: 654/654. Continue test: 813/813 over 30 scenarios.
- Test fixes found on the way:
  - The vanity's two stacked drawers: the test retries if the aim caught the neighbour.
  - The dining hall's route goes through its doorway.
  - The continue test's new flag lists are declared before the scenario table that uses them (static
    initialisation order).
