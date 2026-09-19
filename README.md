# Project DS

## Requirements
- Godot **4.7.2 .NET** (the mono build). On this machine it lives at `C:\Users\Dan\tools\Godot_v4.7.2-stable_mono_win64\`.
- .NET 8 SDK

## Run
```sh
dotnet build
Godot_v4.7.2-stable_mono_win64.exe --path .                     # play
Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest   # scripted walkthrough, report in test-output/
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --export-release "Windows Desktop" build/windows/ProjectDS.exe   # release build
```

## Controls
| | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | WASD / arrows | Left stick |
| Look | Mouse | Right stick |
| Run | Shift | LT / L3 |
| Focus (slight zoom) | Right mouse (hold) | RT |
| Fullscreen / window | F11 or Alt+Enter | |
| Pause / settings | Esc | Start |
| Debug readout | F3 | |
| Dev: third-person camera | F5 | |
| Dev: stalker behind you / far ahead | F6 / F7 | |

## Layout
See [docs/CONVENTIONS.md](docs/CONVENTIONS.md). Research and story proposals live in `docs/research` and `docs/design`.

## Placeholder audio
All current sounds are synthesized by `tools/AudioGen` (`dotnet run --project tools/AudioGen`). Replace any file in `assets/audio/` with a real recording of the same name.
