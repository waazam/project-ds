# Photo trip: Act 1 as a picture-taking walk

Design + implementation plan, 2026-09-21. Read-only analysis; nothing in the project was changed to write this.

The owner's ask: *"I want the beginning to be like a minigame where you're taking pictures of things on your way to the stairs, like we're doing with the birds."*

Ground rules kept throughout:
- The bird beats stay exactly as they are (three cardinal-like birds in red, blue, purple; a fourth black one with a red eye glow whose photo brings a distant scream and empties the flock; the camera is taken away on the first step onto the stairs).
- No story beat changes (`docs/REWORK_BRIEF.md` lines 5-8). Every story hook below is either pure flavour or explicitly marked as needing Dan's OK.
- Quiet, diegetic presentation. No busy HUD, no fanfare.

All line numbers are as of 2026-09-21.

---

## 1. What exists now

### 1.1 End to end, one photo

| Step | Where | Notes |
|---|---|---|
| Camera in the world | `scenes/levels/forest_world.tscn:264-274` (`CameraPickup`, `Kind = 6`, `TrailAnchor Distance = 10, Offset = -2.8`) | A film SLR lying in the leaves 10 m up the trail, 2.8 m left of it (roughly world (-2, 10)). Mesh: `scripts/world/ItemMeshes.cs:418-466` (leatherette body, chrome plates, rewind knob, lens, strap). |
| Pick up | `scripts/world/Pickup.cs:144-153` → `PlayerInventory.TryPickup` (`scripts/player/PlayerInventory.cs:69-85`) | Sets `HasCamera`; sets flag `pickup_taken_camera` (`StoryManager.Flag.PickupTakenCamera`, `scripts/systems/StoryManager.cs:57`). The HUD lists "Camera" (`PlayerInventory.OwnedItems`, line 113; `scripts/ui/EquippedItemHud.cs:42`). |
| Input actions | `scripts/systems/GameSettings.cs:131-134` and `149-152` | `focus` = right mouse / right trigger; `photo` = left mouse / X. Registered in code, not in `project.godot` (which has no `[input]` section). |
| Input edges | `scripts/player/PlayerInput.cs:22-31` (`Focus`, `PhotoHeld`, `PhotoPressed`), `43-44` (action table), `137-155` (`UpdateButtons`) | Mouse-bound actions only count while the mouse is captured (line 148), so the click that recaptures the mouse never takes a photo. The test bot drives `ScriptedFocus` / `ScriptedPhoto` (lines 37-39, 157-164). |
| Raise the camera | `scripts/player/CameraTool.cs:61-64` | `raising = HasCamera && input.Enabled && input.Focus`; `_raise` eases over `RaiseSeconds = 0.18` (line 25); `IsRaised` when `_raise > 0.85` (line 30). The rig zooms in with focus (`PlayerCameraRig.FocusFovScale = 0.72`, `scripts/player/PlayerCameraRig.cs:27, 132-133`). |
| Viewfinder overlay | `scripts/ui/CameraViewfinder.cs:39-81` | Own `CanvasLayer` at `Layer = 25` (line 25), created by `CameraTool._Ready` (line 48). Draws a 3:2 frame (lines 45-47), dims outside it (49-54), corner brackets (56-67), a centre focus square that goes bone → eye-yellow when `FocusLocked` (69-74), and the readouts `"F2.8 1/60 ISO 800"` and `"NN EXP"` (76-80). |
| Subject recognition | `CameraTool.FindSubject`, `scripts/player/CameraTool.cs:71-90` | Iterates group `"photo_birds"` (line 80); a `Bird` that is not `Photographed` (82); distance from the camera to the bird's origin (its feet, `scripts/world/Bird.cs:21`) must be ≤ `Range = 14` m (lines 19, 85); the bird must be within `FovDegrees = 22°` of the camera's forward axis (lines 20, 77, 86); the most central wins (87). **No line-of-sight test**: a bird behind a trunk still counts. `FocusLocked` is refreshed every frame the camera is raised (line 66). |
| Shoot | `CameraTool.Shoot`, lines 92-108, gated by line 67 (`IsRaised && PhotoPressed && _framesLeft > 0`) | Film goes down by one whether or not anything was in frame (97). Flash: full-screen `ColorRect` on `Layer = 30` (lines 43-47) set to alpha `0.85`, or `0.2` under `GameSettings.ReduceFlashing` (lines 99-100; `scripts/systems/GameSettings.cs:29`), fading at 3.5/s (55-60). Shutter sound `camera_shutter.wav` on the `Player` bus at -6 dB (101). `PhotoTaken` static event with the `Camera3D` (28, 102). Then `bird.Capture()`; if it was the omen, `TriggerScream()` (104-107). |
| Bird capture | `scripts/world/Bird.cs:354-365` | Flies up and back over 0.55 s, then the model is freed; the snag perch stays. `RemoveFromGroup("photo_birds")` (358). |
| The black bird | `CameraTool.TriggerScream`, lines 110-116 | Captures every remaining bird, plays `distant_scream.wav` on `Unnatural` (3D, unit size 8, max 500 m, at the player), logs `[story] Act 1: the fourth bird screams and the flock is gone`. |
| Bird calls | `Bird.Sing`, `scripts/world/Bird.cs:326-351` | Within 35 m, a quiet call from the bird's own position on the `Birds` bus (`bird_01..08`, ravens for the omen). Silent once `StairsClimbed` (line 333). |
| The stalker's sting | `scripts/entities/Stalker.cs:148` (subscribes `PhotoTaken`), `301-316` (`OnPhotoTaken`) | If it is out there, not yet stung this appearance, and any sample point is inside the frustum with a clear ray (`VisibleFraction`, lines 434-449), it plays `stalker_seen_01.wav` on `Unnatural` at -4 dB (76, 307-310), counts as seen and vanishes (311-315). Once per appearance. |
| Camera taken away | `scripts/world/FirstClimbEvent.cs:53` → `PlayerInventory.TakeAwayCamera` (`scripts/player/PlayerInventory.cs:94-99`) | Fires the moment the first-climb trigger fires. `HasCamera = false` means the camera never raises again (`CameraTool.cs:62`). Nothing re-grants it: the only source is the world pickup, and its taken-flag keeps it gone on Continue (`Pickup.cs:73-77, 155-160`). |
| Audio assets | `tools/AudioGen/Program.cs:40-41`; `tools/AudioGen/Sfx.cs:583-602` (`CameraShutter`: mirror-slap click + softer closing click 90 ms later), `608-640` (`DistantScream`) | Both generated, deterministic. |

### 1.2 What is stored today

- Only two things about the camera persist: the flag `pickup_taken_camera` and `"camera"` in the inventory string (`PlayerInventory.Serialize`, `scripts/player/PlayerInventory.cs:40-49`). Saves are `ConfigFile`s with checkpoint, position, `flags` (comma list) and `inventory` (`scripts/systems/SaveSystem.cs:42-56`).
- `StoryManager.SetFlag` saves immediately, but only once `Current > Checkpoint.None` (`scripts/systems/StoryManager.cs:203-209, 212-214`). `Act1Start` is reached by `GameFlow.Begin` right after the opening fade (`scripts/systems/GameFlow.cs:76-84`), and `ReachCheckpoint` writes every flag set so far (194-197), so Act 1 flags are safe from the first second of play.
- **Not stored:** which birds were photographed (`Bird.Photographed`, `Bird.cs:34`, is plain runtime state; `Bird._Ready` never reads the story), the film count (`CameraTool._framesLeft`, lines 37, 50), whether the scream already happened. On Continue at `Act1Start` the player is put back at the spawn marker (`scripts/systems/RespawnPoints.cs:34-35`) with all four birds back and a fresh roll of 24. The black-bird scream can therefore replay. This is a restore-contract gap (`REWORK_BRIEF.md` line 13) independent of this feature; the plan fixes it.

### 1.3 Tests that touch the camera

- The autotest walk never picks the camera up and never presses photo (no `Photo`/`ScriptedPhoto` anywhere in `scripts/systems/AutoTest.cs`; the walk at lines 190-217 only counts `BirdCalls`).
- `ContinueRoundTripTest` restores an `act1_start` save carrying `"camera;tool=None"` and checks the inventory round-trips (`scripts/systems/ContinueRoundTripTest.cs:40, 209-211`).
- `UiPreviewDriver` screenshots the HUD but never raises the camera (`scripts/ui/UiPreviewDriver.cs:65-124`).

### 1.4 Limitations to design around

1. Only `Bird` nodes can be subjects; the group name and type are hard-coded (`CameraTool.cs:80-82, 112-113`).
2. Recognition is a single point (the bird's feet) inside a 22° cone at ≤ 14 m. Fine for a 0.5 m bird; wrong for a 9 m bridge or a valley view.
3. No line-of-sight check.
4. Nothing tells the player what a shot got. The only pre-shot hint is the focus square; the only post-shot feedback is the flash and the `EXP` counter going down.
5. No record of shots, no way to review them, no goal list. "Minigame" today = find four birds by ear.
6. Not restored on Continue (birds, film, scream).
7. `CameraTool` builds its own two `CanvasLayer`s (lines 43-49) instead of living in `scenes/ui/hud.tscn`; new UI should go in the HUD scene like everything else.

---

## 2. The trail today (what could be photographed)

Positions are from `scenes/levels/forest_world.tscn` and the trail curve (`Curve_trail`, lines 53-58: 22 points, about 502 m from the lot at Z = +20 to the fallen fir at Z = -463; the friend's line adds ~130 m to the stairs). `s` is trail distance in metres as used by `TrailAnchor` (`scripts/world/TrailAnchor.cs:13-31`; `Offset` + is right of the walking direction). World coordinates are approximate.

| Landmark | Node (`forest_world.tscn` line) | Placement | World ≈ (x, z) | Built by |
|---|---|---|---|---|
| Player spawn | `PlayerSpawn` :203-212 | (0, -0.28, 19) | (0, 19) | in the parking lot |
| Trailhead sign "CULLEN CREEK NATIONAL PARK" | `Trailhead/TrailheadSign` :322-331 | fixed, yawed 25° | (-2.6, 15.5) | `ParkProp.TrailheadSign` `scripts/world/ParkProp.cs:105-159` (also spawns the directional post at `s = 9`, lines 146-158) |
| Info board "BLACKFERN TRAIL" | `Trailhead/InfoBoard` :333-342 | fixed | (3.4, 19.8) | `ParkProp.InfoBoard` 161-216 |
| Trash can, vault toilet, 4 bumpers, boulder | :344-415 | fixed | the lot | `ParkProp` 219-261, 360-370 |
| Camera pickup | `CameraPickup` :264-274 | `s = 10`, off -2.8 | (-2, 10) | `Pickup` + `ItemMeshes.Camera` |
| Cabin | `Cabin` :214-225 | `s = 45`, off +7, yaw 90 | (2, -26) | `scripts/world/Cabin.cs` (front faces local +Z, line 17; `DoorCenter`, line 65) |
| Shed | `Shed` :227-238 | `s = 45`, off +26, yaw 90 | (21, -32) | `scripts/world/Shed.cs` |
| Trail markers 1,2,3,4,5,**4**,6,7,8 | `TrailMarkers` :422-425 | `s = 18, 58, 98, 138, 205, 268, 330, 395, 455` | along the trail | `scripts/world/TrailMarkerSet.cs:18-31` (the repeated "4" at `s = 268` is the deliberate wrong one, `PropsPreview.cs:62`) |
| Red bird | `Bird1` :276-285 | `s = 55`, off +3.2, h +0.7 (perched on a snag) | (-5, -34) | `Bird.cs` |
| Dropped backpack | `Props/DroppedBackpack` :432-443 | `s = 76`, off -3.1 | (-17, -54) | `ParkProp.Backpack` 285-300 |
| Torn map | `Props/TornMap` :458-469 | `s = 131`, off +1.6 | (3, -103) | `ParkProp.TornMap` 373-387 |
| Axe (an Act 5 tool, but visible from Act 1: no `RequiredCheckpoint`) | `AxePickup` :252-262 | `s = 158`, off -16 | (-9, -129) | `Pickup` |
| Blue bird | `Bird2` :287-296 | `s = 170`, off -4.5, h +0.5 | (3, -141) | `Bird.cs` |
| Stream (Cullen Creek) | `Terrain/Stream/S00..S18` :141-196 | markers along z -145..-172 from x -126 to +126; water at y ≈ 0 east of the trail, climbing to +27 up the west valley | crossing at the bridge | `ForestTerrain` (`Stream`, `WaterLevel`, `TryGetStreamCrossing`: `scripts/world/ForestTerrain.cs:105, 233, 240`) |
| Footbridge + "Blackfern Trail > / Cullen Creek" sign + rocks | `Footbridge` :427-428 (group `bridge_marker`) | `AutoPlace` onto the crossing, `s ≈ 189` | (6, -160) | `scripts/world/Footbridge.cs:31-51` (placement), `263-278` (sign), `177-260` (rocks); `BridgeGunshot` at the bridge, `scenes/levels/trail_slice.tscn:35-41` |
| Lost boot | `Props/LostBoot` :445-456 | `s = 212`, off +2.2 | (4, -182) | `ParkProp.Boot` |
| Overlook fork | `Terrain/Branches/Overlook` :133-134 (group `trail_branch`) | leaves the trail at (-2, -191), 9 points, ends (75, -270), ~115 m | | `ForestTerrain` branch |
| Fork signs | `ForkSign_A` :568-575 (`s = 219`), `ForkSign_B` :577-584 (`s = 529`, past the trail's end) | empty `Node3D`s with only a `TrailAnchor`, no script | | leftovers, nothing is built |
| Cut log (12 m, sawn gap on the trail) | `Props/CutLog` :497-508 | `s = 232` | (-6, -200) | `ParkProp.CutLog` 333-358 |
| Snapped walking stick | `Props/SnappedStick` :484-495 | `s = 247` | (-11, -215) | `ParkProp.WalkingStick` |
| Overlook landing | `Overlook/Landing` :526-531 + `ViewClear0..5` :532-566 | (75, -270) on the hill `Hills = (77, -267, r 22, h 30)` :122; the cleared view cone runs NW | (75, -270) | `ClearZone`s only; `ForestPreview.cs:115-127` logs the "overlook view profile" along (-0.6, -0.8) |
| Purple bird | `Bird3` :298-307 | `s = 290`, off +5, h +0.6 | (-3, -257) | `Bird.cs` |
| Forgotten tent | `Props/ForgottenTent` :471-482 | `s = 340`, off -5.5, yaw 15 | (-3, -305) | `ParkProp.Tent` 396-492 (door end is local -X) |
| Moss boulder | `Props/MossBoulder` :510-522 | `s = 372`, off +3.4 | (-1, -336) | `ParkProp.Boulder` |
| Key (Act 5) | `KeyPickup` :240-250 | `s = 402`, off +1.8 | (-4, -365) | `Pickup` |
| Black bird | `Bird4Omen` :309-318 | `s = 415`, off -7.5, h +0.5 | (-10, -378) | `Bird.cs` (red eye glow, lines 191-210) |
| Bunker mound + locked hatch | `Bunker` :622-639 | `s = 460`, off -14, yaw 90 | (-11, -422) | `scripts/world/Bunker.cs` (hatch prompt "Locked", 184-190) |
| Fallen fir (blocks the trail end) | `FallenTree` :586-590 | root (-10, -464), tip (8, -470.5) | `s ≈ 502` | `scripts/world/FallenTree.cs` |
| Friend's line to the stairs | `FriendTrail` :592-596 | waypoints (12, -470) → (41, -591); `ItemAt` empty, so no belongings, only sunken pavers | | `scripts/world/FriendTrail.cs` |
| The stairs | `Clearing/Stairs` :598-620 | clearing (40, 9.5, -605), stairs at +5.6 z; silence zone inner 9.5 / outer 120 (`scenes/environment/staircase_interior.tscn:40-44`); first-climb trigger at the foot (:30-38) | (40, -599) | `scripts/world/StaircaseBuilder.cs` |
| The stalker | `scenes/levels/trail_slice.tscn:30` | dormant for the first 30 m walked (`Stalker.cs:38`), then behind trunks | anywhere | `scripts/entities/Stalker.cs` |

Not present today: any mushroom or fungus (`scripts/world/ForestScatter.cs` scatters firs, broadleaves, snags, stumps, rocks, boulders, logs, branches, ferns and grass; nothing else, see its `BuildMeshes` at lines 72-91).

---

## 3. The photo trip

### 3.1 Fiction

A small spiral-notebook page tucked into the camera strap: a **shot list** written before the trip. The player reads it, walks the trail, and ticks it off with the camera. It is exactly what a person would do on a day out before anything goes wrong, which is the owner's own framing of the bird minigame (`docs/STORY.md` line 14). Once the stairs take the camera, the list goes with it and is never seen again.

Whose list? Two options, both story-safe:
- **A. The player's own list.** Plain, no strings attached. Header: *"Blackfern Trail — shots"*.
- **B. The friend's list**, in his hand. Adds one line of texture to the friend before Act 5 without adding a beat: he is the one who wanted the pictures. Header: *"shots for the album — get these!!"*. Nothing later references it, so the story is untouched. Recommended, but it is a taste call for Dan.

### 3.2 The subjects (12: 9 listed, 3 unlisted)

Listed entries are on the page from the start with an empty pencil box. Unlisted ones are not on the page at all; when photographed, a new line is written under a pencil rule at the bottom, in a heavier hand. "Recognition" is how `CameraTool` decides the subject is in the shot (rules in 3.3).

| # | Id | Where (trail order) | Recognition | Log line (pencil) | Notes / hook |
|---|---|---|---|---|---|
| 1 | `trailhead_sign` | `Trailhead/TrailheadSign`, (-2.6, 15.5), 5 m behind the camera pickup | `PhotoSubject` child of the sign: one look point at the plank centre (local (0, 1.75, 0.13)); 2-14 m; ≤ 14°; line of sight | `the park sign` | The first thing the player can shoot, and it is behind them when they pick the camera up: turning round to get it teaches the loop (RMB to raise, LMB to shoot) with zero text. |
| 2 | `cabin` | `Cabin`, (2, -26), 7 m right of the trail at `s = 45` | `PhotoSubject` in `scenes/environment/cabin.tscn`: look points door centre (0, 1.0, 2.6) and ridge (0, 3.6, 0); 4-22 m; ≤ 16°; LOS to at least one point | `the cabin` | The player will see this building burn in Act 7. Nothing is added; the photo just exists. |
| 3 | `bird_red` | `Bird1`, `s = 55` | **unchanged bird rule** (`CameraTool.FindSubject`) | `the red bird` | |
| 4 | `bird_blue` | `Bird2`, `s = 170` | unchanged bird rule | `the blue one` | |
| 5 | `creek` | Cullen Creek from the footbridge, `s ≈ 189` | `PhotoSubject` built in code by `Footbridge` (it auto-places): node at the bridge centre, look points 6 m up- and downstream at water level (local (±6, -0.9, 0)) and 12 m out (local (±12, -1.0, 0)); 3-28 m; ≤ 22°; LOS | `the creek from the bridge` | The bridge sign already says "Cullen Creek". Photographable from the deck or either bank. |
| 6 | `overlook` | `Overlook/Landing`, (75, -270), end of the branch | **vista rule**: player within 6 m (flat) of the landing, camera yaw within ±30° of NW (yaw ≈ +37°, i.e. forward (-0.6, 0, -0.8)), pitch ≥ -15°; no look point, no LOS | `the view from the overlook` | The only reason to take the branch today; this gives it one. Uses the existing cleared view cone (`ViewClear0..5`). |
| 7 | `bird_purple` | `Bird3`, `s = 290` | unchanged bird rule | `the purple one (he swears it's real)` under option B; `the purple one` under A | |
| 8 | `tent` | `Props/ForgottenTent`, `s = 340` | `PhotoSubject` child: look points ridge front (-1.0, 0.9, 0) and ridge rear (0.9, 0.7, 0); 3-16 m; ≤ 16°; LOS | `that tent` | The list makes the player stop at the one prop that is already slightly wrong. No change to the prop. |
| 9 | `mushrooms` | **new prop**, at the foot of `Props/MossBoulder`, `s = 372` (see 3.7) | `PhotoSubject` child: one look point at the cluster centre (0, 0.12, 0); 0.8-5 m; ≤ 12°; LOS | `mushrooms (don't touch)` | Close-up subject: the only one that makes the player point the camera down and get near. |
| 10 | `bird_black` | `Bird4Omen`, `s = 415` | unchanged bird rule; the scream and flock-vanish stay exactly as they are (`CameraTool.cs:105-116`) | new line: `a black bird` | Unlisted. Its thumbnail is nearly black with two red pixels where the eyes were (see 3.5, hook H2). |
| 11 | `stalker` | wherever it is | **unchanged stalker rule** (`Stalker.OnPhotoTaken`, `Stalker.cs:301-316`): when the sting fires, the log records it | new line: `(nothing there)` | Unlisted and deniable, as Act 1 demands (`STORY.md` line 10). The thumbnail is whatever the frame held: fog and a trunk. |
| 12 | `stairs` | `Clearing/Stairs`, from inside the clearing before stepping on | `PhotoSubject` in `scenes/environment/staircase_interior.tscn`: look points first riser (0, 0.4, 0), mid flight (0, 3.0, -5.5), top landing (0, 6.1, -10.4); 3-30 m; ≤ 18°; LOS to any point | new line: `stairs?` | Unlisted. The camera goes the moment they step on the first step (`FirstClimbEvent.cs:53`), so this can only happen from the clearing. Hook H2 makes the thumbnail come out wrong. |

Why these and not others: the backpack, boot, torn map and snapped stick are the "small wrong things" and should stay unlisted noise the player walks past, not checklist items. The cut log and bunker mound are visually flat at 640x360. The fallen fir is a good subject but sits where the atmosphere should already be pulling the player forward; if Dan wants it, swap it for the cabin (`fallen_fir`: look points root plate and mid trunk, 4-20 m, ≤ 18°).

### 3.3 Recognition rules

Existing gate, unchanged: camera raised (`IsRaised`), `PhotoPressed`, film left (`CameraTool.cs:67`).

Two passes, birds first so the bird beats cannot change:

1. **Birds (unchanged):** `FindSubject` as today (`CameraTool.cs:71-90`). If it finds a bird, that bird is the shot, `Capture()`/`TriggerScream()` run exactly as now, and the log records `bird_<color>`.
2. **Everything else:** only if no bird qualified. Iterate group `"photo_subjects"`; skip subjects already recorded (mirrors `Photographed` birds being skipped at line 82). For a **point subject**: for each of its look points, camera-to-point distance in `[MinDistance, MaxDistance]`, angle between the camera forward axis and the point ≤ `ConeDegrees`, and (if `RequireLineOfSight`) a ray from the camera to the point on layer 1, excluding the player's body and the subject's own colliders (the trick `scripts/world/VanishingProp.cs:47-60` uses: a hit on its own descendants counts as clear). The subject's score is its smallest angle; the best-scoring subject wins. For a **vista subject**: the player's flat distance to the node ≤ `StandRadius`, `|AngleDifference(rig.Yaw, Bearing)| ≤ YawTolerance`, `rig.Pitch ≥ MinPitch`.

`FocusLocked` (`CameraViewfinder.cs:15`, set at `CameraTool.cs:66`) becomes true for either pass, so the eye-yellow square keeps being the one pre-shot tell. No new indicator.

A shot with nothing recognised still spends film, flashes and clicks (as today) and records nothing. Photographing an already-recorded subject again is a blank shot. This keeps the rule simple and the roll finite.

Film: `FilmFrames = 24` (`CameraTool.cs:23`) was sized for four birds. With twelve subjects and a few misses, raise the export to **36** (a real roll size; the readout at `CameraViewfinder.cs:80` already prints whatever is left). A tunable, not a beat.

### 3.4 Feedback per shot (quiet, diegetic)

At t = 0, unchanged: shutter click (`Player` bus, -6 dB), white flash (`0.85`, or `0.2` under Reduce Flashing), `PhotoTaken` event, film -1.

At t = 0, new: before the flash rect is set, `CameraTool` grabs the frame (`GetViewport().GetTexture().GetImage()`, one 640x360 readback per shot, the same call the previews use), crops it to the viewfinder's 3:2 frame rectangle (the maths at `CameraViewfinder.cs:45-47`, exposed as a `FrameRect` property) and downsamples it to 96x64 with nearest filtering. That image is the shot. It is kept in memory only.

At t = 0.35 s, only if a subject was recognised: a small **print** slides in from the bottom-left edge, sits for 2.0 s, slides back out over 0.3 s (sine ease). At 640x360 it is a 74x58 px card: 4 px white border, 10 px bottom border like a print, the 66x44 image inside, and under it the pencil caption from the table in 9 px serif, graphite grey (`UiKit.Serif`, colour (0.22, 0.22, 0.24, 0.9)). No sound of its own; the shutter already happened. No motion beyond the slide; nothing pulses.

Honesty note on "polaroid": the camera the player holds is a 35 mm film SLR (24 EXP counter, rewind knob at `ItemMeshes.cs:434`; readouts at `CameraViewfinder.cs:79-80`). A print sliding out is a UI metaphor, not something that camera does. It still reads instantly and the owner asked for it, so the plan keeps it, sized small and quick. If Dan would rather stay strictly diegetic, the fallback is: no print at all; the only feedback is the `EXP` counter and, next time the page is opened, the tick. The two variants are one `[Export] bool ShowPrints` apart.

Blank shots get no print; the counter dropping is the feedback, as today.

### 3.5 Optional story hooks (none required; the base design works with none)

- **H1 (flavour, story-safe): the list is the friend's** (option B in 3.1). One header string. Recommended.
- **H2 (flavour, story-safe): two photos come out wrong.** The black bird's thumbnail is stored darkened to near black with the two eye pixels left red; the stairs' thumbnail is stored with the frame darkened 70% and the page line reads `stairs?` with the film readout under it wrong (`"F--  1/--"`). Deniable, cheap (two branches in `PhotoLog.Record`), and it lands the "the pictures are wrong" note without a single new beat. Recommended, but it is exactly the kind of thing Dan should see and say yes to.
- **H3 (a new beat, NOT proposed):** finding the friend's own prints of the same subjects later (e.g. inside the cabin in Act 5) would be new Act 5 content. `REWORK_BRIEF.md` forbids that without the lead. Listed only so nobody assumes it is in scope.

### 3.6 The log page

**Opening it.** A new action `photo_log` on **Tab** (keyboard) and **Back/Select** (gamepad), registered next to the others in `GameSettings.RegisterInputActions` (`GameSettings.cs:131-136, 149-152`). Tab is unused today (keys in use: WASD/arrows, Shift, Esc, E, F, F3 debug overlay, F5 camera mode, F6/F7 stalker dev keys, F11 fullscreen). The wheel actions `item_next`/`item_prev` are registered but read by nothing; they are not a good fit for a notebook and stay as they are. Press toggles the page; Esc still opens the pause menu (which sits at `Layer = 30`, `scripts/ui/PauseMenu.cs:19`, above the page). The toggle is ignored while the camera is raised (you cannot read with the camera at your eye) and while input is disabled; the page closes itself when input is disabled (cutscenes) and whenever `HasCamera` goes false. It does not pause the game and does not block movement: a hiker can walk with a notebook open.

**What it looks like (640x360).** A torn notebook page, about 150x200 px, anchored on the left with a 16 px margin, drawn in code like the compass and the item list (`scripts/ui/Compass.cs:96-163`, `scripts/ui/EquippedItemHud.cs:53-65`): the procedural paper texture that already exists (`ProcTextures.Paper`, `scripts/world/ProcTextures.cs:404`) tinted warm bone at 0.92 alpha, a 1 px shadow, faint ruled lines every 14 px, the spiral holes as five dark dots down the left edge. Header in `UiKit.Serif` 11 px. Nine lines, each a 7x7 pencil box and the caption in 10 px serif, graphite grey, with a ±1 px baseline jitter per line (seeded by the line index) so it reads as handwriting without a handwriting font. A ticked line gets a two-stroke pencil check drawn over the box. Under a pencil rule at the bottom, the unlisted lines appear as they are earned, in a heavier stroke (same font, drawn twice with a 1 px offset). Footer, small mono (`UiKit.MonoLabel`, size 9): `"NN exp. left"`. No percentage, no "7/9", no bar: the ticks are the progress. Layer 14 (between the item list at 13 and the interact prompt at 15; under the pause menu at 30 and the viewfinder at 25).

Thumbnails on the page are deliberately **not** shown (the page stays legible and quiet). The print slide-in is the only place a photo is seen. Open question for Dan: a "hold Tab to see the roll" contact sheet is easy later if he wants it.

**Going away.** Visibility is `inv.HasCamera` (subscribe to `PlayerInventory.ToolChanged`, `PlayerInventory.cs:16`). `TakeAwayCamera` (`PlayerInventory.cs:94-99`) fires from `FirstClimbEvent.Fire` (`FirstClimbEvent.cs:53`), so the page and the Tab action go dead at the exact moment the story says the camera goes, and since nothing ever sets `HasCamera` again, they never return. The flags stay in the save, harmlessly.

### 3.7 One new prop (art scope: tiny)

`ParkProp.PropKind.Mushrooms` (`scripts/world/ParkProp.cs:17-21` enum, one new builder next to `Boulder`): five to seven small caps in a clump, each a 5-sided `Cylinder` stem (`MeshKit.Cylinder`, `scripts/world/MeshKit.cs:124`) and a squashed `Blob` cap (`MeshKit.Blob`, line 182), tan and rust vertex colours over `ProcTextures.Flat(...)` materials, one darker "bruised" cap, no collision, ~80 triangles, `ClearRadius = 0`, `ClearFoliage = true` so ferns do not swallow it. Placed as `Props/Mushrooms` with a `TrailAnchor { Distance = 372, Offset = 4.6 }` (just beyond the moss boulder, on its trail side) plus a `GroundSnap`. Judged at 640x360 from 1.5 m with the camera raised (the 0.72x focus FOV helps).

No other new art. The overlook, the creek, the sign, the cabin, the tent and the stairs are recognised as they stand.

### 3.8 Coexistence with the bird beats

- The black bird's photo still captures every remaining bird and plays the scream (`CameraTool.cs:110-116`, untouched). The log records only `bird_black`; the listed bird lines the player never got stay unticked for good. That is the correct cost of the beat and needs no text.
- Everything else stays photographable after the scream, until the stairs.
- The stalker sting is untouched; it just also records a line.
- Bird calls stopping after `StairsClimbed` (`Bird.cs:333`) are irrelevant to the log (the camera is already gone).

### 3.9 Completeness reward

Recommendation: **nothing that leaves the page.** Ticking the ninth listed box writes one more pencil line at the bottom in the same hand: `that's the lot` (option B) or `done` (option A). No caption, no sound, no flag beyond the nine already set, no effect on any act. Anything louder would be a beat, and the honest read of `STORY.md` line 14 ("entirely optional content, there to add realism") is that the list itself is the reward. If Dan prefers absolutely nothing, delete one string.

### 3.10 Photographs as data

- One flag per subject: `"photo_" + id` (constants `StoryManager.Flag.PhotoPrefix = "photo_"` and `Photo(string id)`; a new `Flag` is the sanctioned way, `REWORK_BRIEF.md` line 14). Twelve flags, all set via `StoryManager.SetFlag`, each an immediate save (`StoryManager.cs:203-209`). `FlagSet` also re-checks story triggers (`scripts/systems/StoryTrigger.cs:43-49, 82-90`); harmless.
- Film left on Continue = `FilmFrames - (number of photo_* flags)`. Misses are forgiven. Exact film would need a value, and the save format is flags plus one inventory string; appending `;film=N` to `PlayerInventory.Serialize` is possible (old readers ignore a third segment, `PlayerInventory.cs:62`) but not worth the surface area.
- Thumbnails are never saved. After Continue a ticked line has no print to show, and since prints are only shown at shot time nothing is missing on screen.
- Birds restore themselves: in `Bird._Ready`, if `photo_bird_<color>` or `photo_bird_black` is set, the bird marks itself `Photographed`, leaves `"photo_birds"` and builds only its perch (no model, no calls). `Bird.cs` currently has no story read at all (lines 51-66). This also stops the scream replaying on Continue.
- `PhotoLog` reads the flags in `_Ready` (deferred, per the restore contract) and the page ticks itself. `ContinueRoundTripTest`'s "flags restored" check (`ContinueRoundTripTest.cs:134-135`) covers the raw round trip for free once a scenario includes the flags.

---

## 4. Implementation plan

Namespaces mirror folders (`docs/CONVENTIONS.md` line 20). Tabs. Exports for anything tunable. Every step leaves the game buildable.

### 4.1 New files

| File | Class | Role |
|---|---|---|
| `scripts/systems/PhotoLog.cs` | `ProjectDS.Systems.PhotoLog : Node` | The catalogue and the record. Static `Instance`. `static readonly Entry[] Entries` (`Id`, `Caption`, `Listed`, `Order`). `bool Has(id)`, `int Recorded`, `int ListedRecorded`, `event Action<Entry, ImageTexture> Recorded`. `BeginShot(ImageTexture frame)` / `EndShot()` so anything recording during the shot (birds via `CameraTool`, the stalker) gets the frame. `Record(string id)`: if unknown or already set, no-op; else `StoryManager.SetFlag(Flag.Photo(id))`, apply the H2 darkening for `bird_black`/`stairs` if enabled, raise `Recorded`. `_Ready` (deferred) reads existing flags into the in-memory set. |
| `scripts/world/PhotoSubject.cs` | `ProjectDS.World.PhotoSubject : Node3D`, `[GlobalClass]` | Exports: `Id`, `Vista` (bool), `LookPoints` (`Vector3[]`, local), `MinDistance`, `MaxDistance`, `ConeDegrees`, `RequireLineOfSight`, `StandRadius`, `BearingDegrees`, `YawToleranceDegrees`, `MinPitchDegrees`. `_Ready`: `AddToGroup("photo_subjects")`. `bool TryScore(Camera3D cam, PlayerController player, out float score)` implementing 3.3. `bool Captured => PhotoLog.Instance?.Has(Id) ?? false`. |
| `scripts/ui/PhotoThumb.cs` | `ProjectDS.UI.PhotoThumb : CanvasLayer` | The print slide-in (3.4). `Layer = 26`. Subscribes to `PhotoLog.Recorded`. Pausable (`ProcessMode` inherit, so it freezes under the pause menu like the viewfinder). |
| `scripts/ui/PhotoLogPage.cs` | `ProjectDS.UI.PhotoLogPage : CanvasLayer` | The notebook page (3.6). `Layer = 14`. Reads `PlayerInput.PhotoLogPressed`; watches `PlayerInventory.ToolChanged` for `HasCamera`; redraws on `PhotoLog.Recorded`. `bool IsOpen` for tests. |
| `scenes/levels/photo_preview.tscn` + `scripts/ui/PhotoPreview.cs` + `scripts/ui/PhotoPreviewDriver.cs` | `ProjectDS.UI.PhotoPreview`, `PhotoPreviewDriver` | Dev harness (4.5), modelled on `ui_preview.tscn` / `UiPreviewDriver` (`scripts/ui/UiPreview.cs:18-22` parks the driver on the root so it survives a scene reload). |

### 4.2 Edits to existing files

| File | Function / place | Change |
|---|---|---|
| `scripts/systems/StoryManager.cs` | `Flag` class (lines 31-61) | Add `public const string PhotoPrefix = "photo_";` and `public static string Photo(string id) => PhotoPrefix + id;`. Never rename once saves exist (line 30). |
| `scripts/systems/GameSettings.cs` | `RegisterInputActions` (123-153) | `AddKeys("photo_log", Key.Tab);` after line 136; `AddButton("photo_log", JoyButton.Back);` after line 152. |
| `scripts/player/PlayerInput.cs` | lines 25-31, 39, 43-47, 51-52, 157-164 | Add `PhotoLog = 5` to the index consts, `"photo_log"` to `Actions`, grow the five `bool[5]` arrays to 6, add `public bool PhotoLogPressed => Live && Edge(PhotoLog);`, `public bool ScriptedPhotoLog;`, and the `ScriptedButton` case. Keyboard action, so the mouse-captured guard at line 148 does not apply. |
| `scripts/player/CameraTool.cs` | `_Process` line 66; `FindSubject` 71-90; `Shoot` 92-108; `_Ready` 50 | (a) Add `private PhotoSubject FindOtherSubject()` (pass 2) and set `FocusLocked = FindSubject() != null \|\| FindOtherSubject() != null`. (b) In `Shoot`, before the flash is set: grab and crop the frame (`_viewfinder.FrameRect`), `PhotoLog.Instance?.BeginShot(tex)`; after `PhotoTaken?.Invoke(cam)` and the bird branch: if `best != null` → `PhotoLog.Instance?.Record("bird_" + colour name)`; else if `FindOtherSubject()` is `PhotoSubject s` → `Record(s.Id)`; finally `EndShot()`. Lines 104-107 and 110-116 (capture, scream) stay byte-for-byte. (c) `_Ready`: `_framesLeft = FilmFrames - (PhotoLog.Instance?.Recorded ?? 0)`, and `FilmFrames` default to 36. |
| `scripts/ui/CameraViewfinder.cs` | after line 47 | Expose `public Rect2 FrameRect { get; private set; }` computed with the same maths in a small helper called by `OnDraw` and by a public getter (valid even when the overlay is not drawing). |
| `scripts/world/Bird.cs` | `_Ready` (51-66) | After `AddToGroup` (53): if `StoryManager.Instance` has `Flag.Photo("bird_" + colour)` or `Flag.Photo("bird_black")`: `Photographed = true; RemoveFromGroup("photo_birds");` build the perch if needed and return before `Build()`. Guard with `!Engine.IsEditorHint()`. `Capture()` (354-365) unchanged. |
| `scripts/entities/Stalker.cs` | `OnPhotoTaken` (301-316) | One line after the sting plays (310): `Systems.PhotoLog.Instance?.Record("stalker");`. Everything else untouched. |
| `scripts/world/Footbridge.cs` | `Build`, next to `BuildSign` (163-165) | Add `BuildPhotoSubject(gen)`: a `PhotoSubject { Id = "creek", LookPoints = { (6, -0.9, 0), (-6, -0.9, 0), (12, -1.0, 0), (-12, -1.0, 0) }, MinDistance = 3, MaxDistance = 28, ConeDegrees = 22 }` under `Generated`. Built in code because the bridge auto-places onto the crossing (31-47). |
| `scripts/world/ParkProp.cs` | enum (17-21), switch (48-63), new `Mushrooms()` after `Boulder()` (360-370) | The one new prop (3.7). |
| `scenes/environment/cabin.tscn` | new child `PhotoSubject` of `Cabin` | `Id = "cabin"`, look points and band per table row 2. |
| `scenes/environment/staircase_interior.tscn` | new child `PhotoSubject` of `StaircaseStone` | `Id = "stairs"`, three look points per row 12, `MaxDistance = 30`. Only this scene (the Act 6 minis are code-built by `Act6ClearingEvent` and post-camera anyway). |
| `scenes/levels/forest_world.tscn` | `Trailhead/TrailheadSign` (+child); `Overlook/Landing` (+child with `Vista = true`, `StandRadius = 6`, `BearingDegrees = 37`, `YawToleranceDegrees = 30`, `MinPitchDegrees = -15`); `Props/ForgottenTent` (+child); new `Props/Mushrooms` (`ParkProp Kind = Mushrooms` + `TrailAnchor 372 / 4.6` + `GroundSnap`) with its own `PhotoSubject` child | Subjects live with the things they describe and are found by group (`CONVENTIONS.md` line 22). |
| `scenes/levels/trail_slice.tscn` | new `PhotoLog` node (script `PhotoLog.cs`) under `TrailSlice`, before `Player` | A systems node in the level, like `GameFlow`. |
| `scenes/ui/hud.tscn` | two new `CanvasLayer` nodes `PhotoThumb` and `PhotoLogPage` after `EquippedItemHud` | Keeps new UI in the HUD scene rather than in `CameraTool`. |
| `scripts/ui/UiPreviewDriver.cs` (optional) | after line 67 | `inv.TryPickup(ToolKind.Camera)` so the existing HUD shots also show the page once. |

Not changed: `PlayerInventory.cs` (gating by `HasCamera` already does the work), `FirstClimbEvent.cs`, `SaveSystem.cs`, `AutoTest.cs`.

### 4.3 Scene / group summary

- New group: `photo_subjects` (every `PhotoSubject`). Existing `photo_birds` untouched.
- New action: `photo_log`.
- New nodes: `TrailSlice/PhotoLog`; `Hud/PhotoThumb`, `Hud/PhotoLogPage`; `PhotoSubject` children on the trailhead sign, cabin, overlook landing, tent, mushrooms, stairs; a code-built one on the footbridge; `Props/Mushrooms`.
- Canvas layers, for the record: post 10, compass 12, items/stamina 13, **page 14**, prompt 15, subtitle 16, fader 20, viewfinder 25, **print 26**, flash/pause 30, debug 40.

### 4.4 Audio

None required. The shutter and the scream exist and are unchanged. The page open/close can use `cloth_02.wav` at about -16 dB on the `Player` bus (a paper-ish rustle stand-in); if that reads wrong in the room, add one job to `tools/AudioGen/Program.cs` (`("page_turn_01", sfx, ...)` → a new `Sfx.PageTurn`: a 120 ms band-passed noise flick with a soft second flick, peak -3 dBFS like the other one-shots) and regenerate with `dotnet run --project tools/AudioGen -- --only page_turn`. The print slide-in gets no sound.

### 4.5 Test plan (no `--autotest`)

1. `dotnet build` after every step in 4.6.
2. **Preview scene** `scenes/levels/photo_preview.tscn`: `forest_world.tscn` + `player.tscn` + `hud.tscn` + a `PhotoLog` node + `PhotoPreview` (which parks `PhotoPreviewDriver` on the root, as `UiPreview` does). Run with `C:\Users\Dan\tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --path . res://scenes/levels/photo_preview.tscn`. The driver, all through `PlayerInput.Scripted*` (never `Input`):
   - `inv.TryPickup(ToolKind.Camera)`; for each subject in trail order: `player.Teleport` to a stand point 5-8 m in front of its first look point (2 m for the mushrooms, the landing for the overlook), snap yaw/pitch at the look point (the `SnapLookAt` approach in `AutoTest.cs`: `rig.SnapBehind(yaw); rig.SetPitch(pitch)`), `pin.Scripted = true; pin.ScriptedFocus = true;` wait 0.4 s, check `viewfinder.FocusLocked`, screenshot `viewfinder_<id>`, `pin.ScriptedPhoto = true` for one frame, wait 0.6 s, check `PhotoLog.Instance.Has(id)`, screenshot `print_<id>` (the print is up), lower the camera.
   - Negative checks: the sign from 30 m does not lock; the tent from behind a trunk (stand behind the nearest scattered fir, found by a ray) does not lock; standing on the landing facing south does not record `overlook`; a bird and the cabin both in frame records the bird only.
   - Bird beats: photograph `Bird4Omen` and check every `Bird` has `Photographed == true` and the log has `bird_black` but not the unshot listed birds.
   - Page: `pin.ScriptedPhotoLog = true` one frame → `page.IsOpen`; screenshot `page_open` (nine lines, ticks, unlisted lines); again → closed; with the camera raised the toggle is ignored.
   - Take-away: `inv.TakeAwayCamera()` → page hidden, focus never raises, Tab does nothing; screenshot `after_stairs`.
   - Continue: `SaveSystem.Save(new SaveData { Checkpoint = Act1Start, Flags = { "pickup_taken_camera", "photo_trailhead_sign", "photo_bird_red", "photo_bird_black" }, Inventory = "camera;tools=" })`. The test slots are only used under `--autotest` (`SaveSystem.cs:36`), so either back the real slots up to `user://save_backup_*.cfg` first and restore them at the end, or add a `--photo-preview` user arg that `GameSettings` maps onto the test prefix. Then `StoryManager.Instance.ContinueGame()`; the driver survives the reload; check: `Bird1` and `Bird4Omen` have no `Model` child and are out of `photo_birds`, `Bird2`/`Bird3` are present, `PhotoLog.Recorded == 3`, `CameraTool` film = 33, the page shows two ticks and one unlisted line.
   - Shots go to `test-output/photo/` at 640x360 plus the nearest-scaled 1600x900 copy (as `UiPreviewDriver.Shot`, lines 37-49); the driver quits non-zero on any failed check.
3. **Playtest checklist for Dan** (the real test of the tone): walk from spawn to the stairs with the list open now and then; the print should never hold the eye for more than two seconds; the page should be readable at 1600x900 without leaning in; the black bird moment should feel exactly as before.
4. **Later, for the lead only:** add an `act1_photos` scenario to `ContinueRoundTripTest` (flags as in step 2) so the round trip is covered by the existing harness when the lead runs the final check.

### 4.6 Order of work (buildable at every step)

1. `StoryManager.Flag.PhotoPrefix/Photo` + `PhotoLog.cs` (catalogue, flags, `Record`, `BeginShot/EndShot`) + the `PhotoLog` node in `trail_slice.tscn`. No visible change.
2. `PhotoSubject.cs`; `CameraTool` pass 2 + `Record` calls + film restore; `CameraViewfinder.FrameRect`; `Bird` restore. Visible change: birds are recorded and stay gone on Continue.
3. Subjects in scenes: sign, cabin, overlook, tent, stairs; the code-built creek in `Footbridge`. Everything but the mushrooms is now photographable and logged (invisibly).
4. `ParkProp.Mushrooms` + the `Props/Mushrooms` node + its subject. Screenshot it from 1.5 m with the camera raised before moving on.
5. `PhotoThumb` in `hud.tscn` (+ H2 darkening behind an export). First visible feedback.
6. `photo_log` action (`GameSettings`, `PlayerInput`) + `PhotoLogPage` in `hud.tscn`.
7. The one-line `Stalker` record.
8. `photo_preview.tscn` + driver; run it; put the shots in `test-output/photo/` for Dan.
9. (Lead) `ContinueRoundTripTest` scenario.

### 4.7 Open decisions for Dan

1. Whose list is it (3.1 A or B)?
2. Print slide-in on, or strictly diegetic (page ticks only)?
3. H2 (the two wrong photos): yes or no?
4. The reward line (`that's the lot`) or nothing at all?
5. Film: 36, or keep 24 and let the roll run out?
6. Swap the cabin for the fallen fir?
