# Project DS

Working title. An original third-person psychological horror game. It looks like a lost PS2-era title but plays with modern third-person controls. The setting is a fictional American national park, where impossible staircases stand alone in the woods and the forest goes silent around them.

**Status:** first vertical slice. You walk from a trailhead into the woods and find a staircase in a clearing, and the forest falls silent as you approach.

## Requirements
- Godot **4.7.2 .NET** (the mono build). On this machine it lives at `C:\Users\Dan\tools\Godot_v4.7.2-stable_mono_win64\`.
- .NET 8 SDK

## Run
```sh
dotnet build
Godot_v4.7.2-stable_mono_win64.exe --path .                     # play
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest   # scripted walkthrough, report in test-output/
```

## Controls
| | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | WASD / arrows | Left stick |
| Look | Mouse | Right stick |
| Run | Shift | LT / L3 |
| Camera distance | Mouse wheel | Pause menu |
| Pause / settings | Esc | Start |
| Debug readout | F3 | |

## Layout
See [docs/CONVENTIONS.md](docs/CONVENTIONS.md). Research and story proposals live in `docs/research` and `docs/design`.

## Placeholder audio
All current sounds are synthesized by `tools/AudioGen` (`dotnet run --project tools/AudioGen`). Replace any file in `assets/audio/` with a real recording of the same name.
