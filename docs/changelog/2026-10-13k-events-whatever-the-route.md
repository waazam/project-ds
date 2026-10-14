# 2026-10-13k: Events fire whatever the route

Dan: "make sure events are still triggered even if character doesnt cross the bridge. for example the gunshot in the
beginning". Every place-triggered event in the game was audited: trigger volumes, radius and distance checks, progress
along the trail, checkpoints reached by walking somewhere, and scares. For each one the question was whether a player
could miss it by going another way: wading, going round, hugging a wall, sprinting through a thin volume, coming in from
another side, or Continuing past it.

Sprinting through is never the problem. Running is 5.4 m/s at 60 Hz, about 9 cm a tick, and the thinnest volume is
0.3 m behind a 3 m lip. The player can't jump either. The real gaps were routes round small volumes in open ground or
along walls, and beats that never came because the one trigger they depended on was missed.

## Fixed

| Event | Where | How it could be skipped | Fix |
|---|---|---|---|
| Act 1 gunshot "while crossing the bridge" | `SoundEvent` (`trail_slice.tscn`) | A 2.5 m cylinder on the deck. Wading the creek beside the bridge, or going round the stream's end, never fired it. | When its target is a `Footbridge`, it also fires the moment the player is over the creek by any route (`Footbridge.IsAcross`, checked 10 times a second). On the deck it still fires half way over, as before. |
| Act 6 checkpoint 5 (`Act6BridgeCrossed`), and everything waiting on it: the compass to the clearing, the clearing's turn and fog, the voice from the creek on, the blood rain's full presence, the fire, the bunker | `BridgeCrossEvent` (`Footbridge`) | A box on the deck. A player who waded across with the cap got no checkpoint, so the clearing never turned and Act 6 stalled. | While the window is open (cap taken, checkpoint 4), getting over the creek by any route fires it. The deck box stays. |
| The creek itself | `Footbridge.IsAcross` (new) | | Which side of the stream's middle line a point is on, compared with the side the trail carries on from. Past either end of the stream it falls back to the line through the bridge across the trail. In the Hollow the stream spans the whole level, so it can't be got round. |
| Act 7 giant: it paces "until the player has watched the fire (or gone on past it)" | `GiantStalkerEvent` | Checkpoint 6 usually lands by sight about 60 m short of the lookout. The compass then leads straight across the woods to the bunker and never comes within the 30 m pass radius, so the giant paced (and rattled, audible 600 m off) through Acts 8 to 11. | It is also released within 60 m of the bunker, and by any later checkpoint. |
| Act 7 sight beat's fallback timer | `CabinFireEvent` | Walking out of the fire's 90 m heat zone without having sighted the cabin put the event to sleep, which stopped the 40 s fallback clock. The bunker still caught it. | It no longer sleeps while the sight beat is waiting (the same guard the per-frame check already had). |
| Act 7 numbers 3 and 4 | `Bunker` (dial) | The notes stand by the trail's loop, but a compass-follower cuts straight across to the bunker, 50 to 100 m from them, and reaches a dial they can't open. | At the door with either still unread, one line under the picture (at most once a minute): "His last numbers are on the trees by the path, back toward the lookout. B: the blacklight." |
| "Something on a tree back there" nudge | `TreeNote` | It needed the player within 9 m, so anyone a few steps off the path walked past unnoticed. | 14 m in, 21 m out. |
| Act 13 Room 2 door slams and the knocking | `StationRoom2` | The 2.4 m entry box let a player slide along the door wall round it. | The box spans the room's width. |
| Act 18 → 19 checkpoint (`Act18Finished`), chapter 5 | `BossRoom` (`TidyRoomTrigger`) | 4 m in an 11 m library: step in and slide along the front shelves. With no save, a Continue put them back in the pit fight. | The box spans the library's width. |
| Act 19 → 20 checkpoint (`Act19Finished`), the spare lighter | `RoundRoom` (`Act20Trigger`) | 3 m in the 15 m drum: hug the curved wall. | The box spans the drum. |
| Act 23 201's door slams for good (`Act23Letter201`) | `SkiLodge.Room201` | It slammed 0.9 s after the letter was put down, wherever the player was. Walk out while reading and you were jammed out in the corridor, with the way on (the bathroom's hole) behind the door. | It waits until they are in 201. |
| Act 23 202 and 203 slam and save (checkpoints 24, 25) | `SkiLodge.Act23` | "Been in the room" isn't saved. A Continue after taking the room's key never needed a visit back, so no slam and no save. | Passing the open door (within 4 m) with the key taken does it. |
| Act 22 the wendigo's first appearance on the road (every later glimpse waits on it) | `WinterWoods.Stalking` | Put off 2 s every time its spot was in view. Looking straight down a straight could keep putting it off to the road's end. | After three tries it takes a spot further off in the murk (38, 52 or 66 m) that isn't in view. |

## Follow-up: the two left over

| Event | Where | How it could go wrong | Fix |
|---|---|---|---|
| The ending, Act 24's last save | `Act24Maze.Defeated`, `Act25Ending` | Checkpoint 34 was saved before the `ending_N` flag. A quit in that gap had Continue fall back to ending 1, whatever the record said. | `Act25Ending.Decide` picks the ending and saves its flag before the checkpoint. `Play` reuses the decided ending, so the clock crossing the 2-hour mark in between can't change it. |
| Into the bunker (checkpoint 7) | `Bunker.OnEntered` | Stepping into the doorway while an admission was still running did nothing until the player stepped out and back in. | It waits for that admission to finish, then enters if the player is still in the doorway. |

Tested with beats 8, 8c, 24a, 25-1 and 25-4, all passing. In 24a the log shows `flag set: ending_1` before `checkpoint reached: Act24Finished`.

## Checked and left as they are

- **Act 1**
  - **The stairs and the title.** The title is a sight check every 0.1 s, the silence zone is a proximity curve, and the first step is a box over the whole flight.
  - **The fir and the walls (`Act1TreeFalls`, `FallenTree`).** Driven by progress along the way's line, plus a 25 s fall if the player never looks. Another agent is working there, so it is untouched. Not skippable in a way that stalls anything. By design, the walls only fall while the player is near the line.
- **Acts 3–6**
  - **The camp (checkpoint 3).** An 8.5 m cylinder round the stump that holds the gear the player must take.
  - **The safe zone.** Leaving it with the gear starts the storm, and so does picking up the gear while already outside it.
  - **The cabin.** Its door is the only way in. The reveal re-checks every frame while the player is inside, and the exit line is on the doorway.
  - **The stalker's intro.** A continuous distance to the cabin.
  - **The clearing.** Its voice zone is backed up: stepping onto any flight speaks the voice and starts the loop. The loop's tops are on one-way flights.
- **Acts 8–11**
  - **The bunker hall's red and its lunge.** Progress in the enclosed hall.
  - **The vine door, the CRT room, the three rooms and the chase.** Enclosed, or reached by interaction.
  - **Checkpoint 8 and the climb's radio lines.** Driven by the checkpoint, and by progress up a one-way flight.
  - **Act 11's call "under the stairs".** It fires at the 70 m fog edge. This file is with the "Who are you!" agent, so it is untouched.
  - **The pursuer's shove.** It only lands if the player lets it close. Optional by design.
- **Acts 12–21**
  - **Robust.** The station's door, the iron door, Room 3, the basement, the stairwell (progress-driven with catch-up), the gap's edge, the shadow man, the closet and its threshold, the sewer hole, the fight's start, the dais and the long stair.
  - **The lake's arrival box.** It fills the door. The rowboat agent is working there, so it is untouched.
- **Acts 22–25**
  - **Robust.** The road's halfway save (projected onto the road), the crawlspace save, its lights and arms (all on the maze's one main line), the hand-overs (plane crossings), the frozen lodge, the hole and the herd (enclosed tunnels), the trenches (the cavern's ellipse), and Act 25's lot (30 m round the car).
  - **The lodge's ambient haunts.** They share a cooldown, so some go unheard. They are optional by design.

## Tests

- **New playtest beats.**
  - **1b.** Wades the creek 14 m beside the trail's footbridge and checks that the gunshot fires "across", not on the deck.
  - **6b.** Wades the Hollow's creek beside the footbridge with the cap and checks checkpoint 5, the compass on the clearing and the voice calling.
- **Results.**
  - Playtest beats 1b, 6, 6b, 7, 13b, 18, 19, 20, 22, 23a, 23b, 23c, 23d and 23x all pass.
  - `dotnet build`: 0 errors.
