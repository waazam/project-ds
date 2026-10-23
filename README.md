# Project DS (Dead Silent)

A first-person horror game in Godot 4.7.2 .NET (C#). The story is [docs/STORY.md](docs/STORY.md); it is authoritative.

## Requirements
- Godot **4.7.2 .NET** (the mono build). On this machine it lives at `C:\Users\Dan\tools\Godot_v4.7.2-stable_mono_win64\`. The Steam copy of Godot is the non-.NET build and cannot run this project.
- .NET 8 SDK
- For the Windows export: Godot 4.7.2's .NET export templates.

## Run
Run from the project folder, with `dotnet build` first after any C# change. Arguments after `--` go to the game.
```sh
dotnet build
Godot_v4.7.2-stable_mono_win64.exe --path .                     # play
Godot_v4.7.2-stable_mono_win64_console.exe --path . res://scenes/levels/clip_audit.tscn -- --bake-texcache   # before a release: ship the drawn textures (assets/texcache_baked)
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --export-release "Windows Desktop" build/windows/DeadSilent.exe   # release build
```
Bake after any change to the texture-drawing code, then export: the game draws a texture itself only if the shipped copy hasn't it. F9 in game saves a bug report (screenshot, state, log) under `%APPDATA%\Godot\app_userdata\Project DS\bug_reports`.

## Tests
All of them write to `test-output/` and exit with code 1 on any failed check. Use the `_console.exe` so the log shows.
```sh
# the fast functionality test: every act, the endings and the systems (report test-output/playtest/report.md, a screenshot a beat)
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --playtest
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --playtest --playtest-only=13-15   # those acts; 0 = the systems beat; beat ids like 23c or 25-3 work too
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --playtest --playtest-fast   # chains passed beats without reloading (no per-act Continue)

# the slow full-story test, walking everything, Acts 1-25 across both levels (report and screenshots in test-output/story/)
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest --story-from=12 --story-to=15   # a range of acts
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest --story-from=14 --story-to=14 --measure-walks   # time each long stair and hall, walking and running
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest --hitches   # the lag meter: every frame over 33 ms and its cause, hitches.txt beside the report

# the inventory and the hands: switching checks, then each held thing shot from the eye (test-output/hands/)
Godot_v4.7.2-stable_mono_win64_console.exe --path . res://scenes/levels/hands_preview.tscn
Godot_v4.7.2-stable_mono_win64_console.exe --path . res://scenes/levels/hands_preview.tscn -- --hands-shots=Axe,Camera   # only those shots
Godot_v4.7.2-stable_mono_win64_console.exe --path . res://scenes/levels/hands_preview.tscn -- --hands-studio            # each model alone, turned

# the Continue round trip for every checkpoint (report in test-output/systems/)
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --continue-test
```
`--test-out=<name>` puts a story test's output in `test-output/<name>/`. The playtest uses its own save slots for each process; `--autotest` and `--continue-test` share `user://test_save_*`, so give concurrent runs their own `APPDATA`. None of them touches the player's saves.

## Controls
The keyboard keys for moving, running, crouching, leaning, use, the lantern, the blacklight, the album, throwing and holding breath can be rebound under Settings > KEYS (`KeyRebinds.cs`, kept in the settings file). The mouse buttons, the pad's buttons and the fixed keys (Esc, F11) stay as they are, and the hints on screen name the keys as bound.

Two hands: the lantern is always the left hand's; the right hand holds one thing (the camera, the axe, the hammer, the cap, the walkie-talkie, the knife, the lighter, the crowbar, the flamethrower) or nothing. Keys, keycards and the compass are pocketed. The flamethrower takes both hands (the lantern hangs at the belt). A 1-9 key the player has rebound to an action does that action instead.

| | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | WASD / arrows | Left stick |
| Look | Mouse | Right stick |
| Run | Shift | LT / L3 |
| Crouch | C or Ctrl (a toggle; hold to crouch in Settings) | R3 |
| Lean left / right | Q / R | D-pad left / right |
| Use / read / take | E | A |
| Lantern on / off | F | Y |
| Blacklight (the lantern's other light) | B | D-pad down |
| The right hand's: raise the camera (when it's in hand), else gather the lantern's beam | Right mouse (hold) | RT |
| Take a picture (the camera raised); fire the flamethrower (Act 24) | Left mouse | X |
| Camera zoom (while raised) | Mouse wheel | RB / LB |
| Right hand: next / previous thing (an empty hand among them) | Mouse wheel | RB / LB |
| Right hand: a thing by its place (again: put it away) | 1-9 | |
| Photo album (its pages: the wheel) | Tab | Back |
| Throw (a flare, a tin) | G | B |
| Hold breath | V | D-pad up |
| The bunker's keypad (after E on it): press a key / choose one / take back a digit / try the code / step back | 0-9 or numpad, or E on the highlighted one / arrows or WASD / Backspace / Enter or # / Esc | A / D-pad / X / # / B |
| Row (Act 12): row / row hard / steer | W / Shift / A, D (alternating A and D also rows) | Left stick / LT / left stick |
| Puzzle box (Act 19): pick up and drag a piece / turn it | Left mouse (hold, drag, let go) / right mouse or wheel; or A, D to pick, E to take, WASD to move, E to set in | Left stick or LB / RB to pick, left stick to move, A to take and set in, RT, LB or RB to turn |
| Fullscreen / window | F11 or Alt+Enter | |
| Pause / settings | Esc | Start |
| Dev builds only: debug readout | F3 | |
| Dev builds only: third-person camera | F5 | |
| Dev builds only: stalker behind you / far ahead | F6 / F7 | |

## Layout
See [docs/CONVENTIONS.md](docs/CONVENTIONS.md). Research and story proposals live in `docs/research` and `docs/design`; the development history is in [docs/changelog](docs/changelog/README.md).

## Audio
The game's sounds come from three places:
- **Generated in code.** `tools/AudioGen` (`dotnet run --project tools/AudioGen`) synthesizes most of the sound effects and beds into `assets/audio/`. The winter's, the lodge's and the crawlspace's sounds come from the Python generators in `tools/WinterAudio`, and the climb loop from `tools/audio/make_climb_loop.py`.
- **The owner's own recordings.** His songs and samples, and "Petty Theft" (Act 24's score and the cues for finds and progress, in `assets/audio/music/petty_theft/`).
- **Third-party material** is listed with its source and licence in [docs/CREDITS.md](docs/CREDITS.md).

`tools/AudioAudit` (`dotnet run --project tools/AudioAudit -- --csv out.csv`) scores every `.wav`/`.ogg` for how tonal it is and whether its pitch glides, to find synthesized Foley that sounds like a cartoon. Any file in `assets/audio/` can be replaced with a recording of the same name.
