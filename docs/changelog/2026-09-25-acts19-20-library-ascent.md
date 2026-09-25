# 2026-09-25 — Acts 19-20: the library, the puzzle box, and the ascent (the demo's end)

Commit: this pass's single commit on `main` (see `git log` for the hash).

Built from `Project_DS_The Real Act 19.docx` and `Project_DS_The Real Act 20.docx`, with the owner's
bamboo interlocking puzzle-box reference. These are the last two acts for now, so the game ends
after Act 20 and a Windows demo build was exported.

## Story Beats
### Act 19: the library
- **The save**: through the steel door out of the pit, walking into the library is Act 19's save
  (checkpoint 18, as before).
- **The room** is the calmest place in the game, a break from everything before it. It's a quaint
  library packed full:
  - bookcases floor to ceiling on every wall, holding about six thousand hardbacks of every size
    and cloth colour, a few leaning and a few gaps;
  - a parquet floor, a big layered rug and dark ceiling beams;
  - a stone fireplace with a real fire, and two leather armchairs either side of it turned in
    towards it;
  - a reading table with a green banker's lamp, a floor lamp, sconces, a globe, a grandfather
    clock (ticking), and a rolling ladder.
  - The sound is only the fire crackling and the clock. The pit's murk gives way to clear, warm,
    still air.
- **The sheet**: a white sheet is thrown over a side table by the west shelves, with something
  square under it. Near it, E ("Pull off the sheet") plays a short cutscene: the sheet is gripped,
  lifted and drawn off towards you, revealing a bamboo puzzle box with five loose pieces beside it.
- **The puzzle box** (after the reference): a shallow two-tone bamboo box, four cells by four
  inside, with inlaid chequered strips on its rim. The five blocky pieces are a square, an S, a
  straight three, a corner and a straight two.
  - They fill the box exactly, in only two essentially different ways, turning in quarter turns.
  - E ("Work the puzzle box") bends you over it in a top-down close-up. A / D (or the wheel) picks
    a piece, which lifts a little, and E takes it. W A S D moves it over the box, right-click or
    the wheel turns it, and E sets it in. Esc puts it back or straightens you up.
  - A piece that doesn't fit knocks against the rim and stays in your hand. Pieces already set in
    can be taken out again.
- **Solved**: the box glows once, softly (a slow warm bloom, never a flash, and dimmer still with
  Reduce Flashing on). Then it dissolves out of the world, burning away through a glowing edge,
  and leaves a red silk bookmark where it stood.
- **The bookcase**: on the back wall one hardback sticks out a finger's width ("One of the books
  sticks out."). With the bookmark, E ("Slide the bookmark in") slides it into the book, the book
  slides home, and a click and a clunk follow. The bookcase then groans open on its hinge onto a
  candle-lit stone passage.

### Act 20: the round room, and up
- **The save**: at the end of the passage, stepping into the round room is Act 20's save (new
  checkpoint 19).
- **The room**: a round stone room with a very high ceiling (32 m).
  - Eight long, tall arched windows go all the way round, each bricked up.
  - It is lit by candle sconces between the windows and more high up, with stone ribs rising into
    the vault.
  - In the middle is a round stone dais with iron posts round its rim, thick with old spider web
    slung between the posts and drawn up into a tent. The web can't be pushed through.
- **The webs**: with the lighter, E ("Burn the webs") flicks it. The web catches where the flame
  touches, the fire runs round the rim both ways and up the tent, and it shrivels away.
  - The lighter is never used up, so you have had it since the station. An old save without one
    finds a spare on the sill by the way in.
- **The ascent**: stepping into the middle of the dais holds you for twenty seconds while it
  rises: a grinding start, then a slow, smooth climb past the bricked windows and the candles, up
  a shaft in the ceiling, and a settling stop in a small round stone room above.
  - The camera stays gentle: no shake, only a slow look up the drum and then level again.
- **The end of the demo**: arriving is Act 20's end (new checkpoint 20). A dim stair climbs away
  out of the light through an arch (the way on). "Up. Always up, from here." Then the credits roll.

## Additions
- `Library` (with `Library.Story.cs`), `RoundRoom`, `libraryparts/PuzzleBox`, `PuzzleOverlay`, and
  `dissolve.gdshader` (the box going out of the world).
- A new item, the **Bookmark** (a red silk ribbon with a gold stitch and a tassel).
- New checkpoints:
  - `Act19Finished` = 19, which respawns at the round room's way in;
  - `Act20Finished` = 20, which respawns in the room at the top.
- A new flag, `round_room_web_burned`.
- The pit's door now opens onto the library, which replaces Act 18's placeholder "tidy room" and
  its early credits.
- `--story-from=19` and `--story-from=20`.

## Audio (AudioGen)
- `wood_take`, `wood_place` and `wood_bump`: bamboo pieces picked up, set in (a hollow knock) and
  knocked against the rim.
- `puzzle_glow`: a soft, warm chord swelling in and fading as the box dissolves (pure tones, no
  hiss).
- `bookcase_swing`: a long, low wooden hinge groan with books faintly rattling, and a thud.
- `lift_start`, `lift_loop` and `lift_stop`: the dais's stone knock and short grind, then a slow
  tonal mechanical labour (the drive's hum, a ratchet and counterweights), then a heavy seating.
  There is no noise bed.
- The library uses the existing fire-crackle and clock-tick loops.

## Tests
- `StoryTest` Act 19:
  - the save;
  - the jutting book with nothing to do yet;
  - the sheet cutscene revealing the box and five pieces;
  - the close-up opening;
  - the solver finding a solution;
  - a wrong placement knocked back;
  - a real key press moving the held piece;
  - the whole solution set in;
  - the glow and dissolve leaving the bookmark;
  - taking the bookmark;
  - "Slide the bookmark in", the bookcase opening, and the bookmark used up;
  - through the passage to Act 20's save.
- `StoryTest` Act 20:
  - the save;
  - the web keeping you off the dais;
  - "Burn the webs", and the webs gone;
  - stepping into the middle starting the ride with the player held;
  - the ascent taking 20 s;
  - Act 20's save at the top;
  - standing on the dais, then stepping off onto the room's floor;
  - the camera still in hand at the end.
- `ContinueRoundTripTest`: new scenarios for checkpoints 19 and 20, and the library's state on
  checkpoint 18.
- Test fixes:
  - The Act 18 bot now gets five tries at the boss instead of three. One run lost three in a row
    to bad luck, while every other run won in one or two.
  - Acts 19 and 20 stop early when Act 18 wasn't won, so a lost fight no longer loops them.
  - The Continue test waits for control before its "can move" check, because the boss room's
    short intro briefly holds the player.
- Results:
  - `--story-from=19 --story-to=20`: 28/28.
  - `--story-from=18 --story-to=20`: 46/46 (the boss was won on the first try).
  - Full `--autotest` (the whole game, start to finish, through Act 20): 460/460 in 29 minutes.
  - `--continue-test`: 642/645. The same three failures (the Act 6 loop's fire state, the Act 10
    mood, the clearing stairs) predate this work.

## Performance
- The library is hidden (not drawn, its lamps off) behind the pit's door until the door opens, so
  its six thousand books and shadowed lamps cost nothing during the boss fight.

## Demo build
- A Windows x86-64 release export of the whole game (`ProjectDS.exe` with its .NET data folder),
  zipped as `ProjectDS-Demo-win64.zip`. It starts at the main menu, New Game runs from Act 1, and
  the demo ends with the credits after Act 20.
