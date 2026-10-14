# 2026-10-13 — Shorter stairs and halls, and running always gets you there

The owner: "Some of the hallways and staircases are way too long and some don't progress when you're sprinting down
them; look into that please."

## Every long walk, timed
A new measuring mode, `-- --autotest --measure-walks --story-from=N --story-to=N`, walks each long stair and hall of an
act twice with the same scripted keys a player's would give: once walking, once holding run the whole way (stamina as
in a release build: it runs out and comes back). It reports the game time, the slowest speed while pushing on, and any
jump in place (a loop-back), and fails if running is ever slower than walking. Acts with a walk to time: 11, 14, 15, 17
and 21 (24 re-checks the winter world's clearance, below).

| Place | Length | Walk | Run (holding it) |
|---|---|---|---|
| Act 14, the stairwell | 30 turns, 140 m down → **20 turns, 94 m** | 135 s → **91 s** | about 100 s → **68 s** |
| Act 15, the narrow corridor and taper | 224 m → **166 m** (corridor 178 → 120) | 83 s → **62 s** | 48 s → **35 s** |
| Act 15, red light, green light | 505 m → **312 m** (hall 523 → 330) | 250 s, 7 reds → **152 s, 4 reds** | 142 s → **89 s** |
| Act 21, the long stair | 52 flights, 300 m up → **16 flights, 92 m up** | 285 s → **87 s** | 171 s → **51 s** |
| Act 11, the last staircase | 220 steps (unchanged) | 32 s | 17 s |
| Act 17, the sewer pipe to the hole | 100 m pipe, wading (unchanged) | 79 s | 46 s |

Not timed with the driver (flat, straight, measured from their lengths): the bunker hall (Act 8, 90 m: about 35 s
walking, 18 s running, plus the 1.2 s scare), the bunker's exit hall (Act 10, 40 m: 15 s and 8 s). The first stairs
(Act 2) are a glide, not a walk. The clearing's flights (Act 6) are 48 steps each, four to six of them with hand-offs
part way up: about half a minute.

## Running
- **The stairwell's old sprint trick is gone from the code**, not just switched off: no setting, no loop-back, no held
  grime. Running down the shaft takes you down it. The only thing that ever takes you back up is the crawler, if you
  stand still and let it reach you.
- Measured, running beats walking everywhere above, with no stall and no jump back.
- Red light, green light is as it was: moving in the red is still being taken.

## Shorter
- **The stairwell** is 20 turns (was 30). Its events, signs and the crawler are placed by fraction of the way down,
  so they all stay in their order, closer together. The descent music (4 min 12 s) fades out at the edge as before; it
  was already cut short at 30 turns.
- **The long hallway**: the narrow corridor is 120 m (was 178), the red-light hall 330 m (was 523). The game itself is
  unchanged: the same greens, reds, turns and save past him.
- **The long stair** is 16 flights (was 52), still concrete into timber over its middle. The church and everything
  after it (the winter woods, the lodge, the maze) hang off its top, so they all come down 208 m and 402 m nearer. A
  new check measures the winter ground at least 10.6 m above the sewer, the closest thing under it, and the maze clear of
  everything.

## Tests
- Act 14: running checks the body never rises back up the shaft (the old loop-back counter is gone).
- Act 15: the narrow stretch's time is now 35-60 s; the game needs three reds (was four) and two of his turns (was
  three) in the shorter hall.
- Act 21: the long stair's sprint estimate is now 30-90 s (was 140-240).
- Results: Act 14 34/34, Act 15 21/21, Acts 21-24 219/220 (the one miss was a save written by another test run on the
  same machine: every copy of the project shares `user://test_save_`; run with its own `APPDATA` to keep them apart),
  Act 24 alone 59/59, `--continue-test` 1086/1086.
