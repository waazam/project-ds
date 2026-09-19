# Project DS

*Working title.*

A first-person horror game about a walk in the woods that goes quiet.

You follow a park trail in the last light of an October evening. Birds, insects, wind and water fill the forest, and the forest sounds right. Then, somewhere off the path, it stops. The quiet is the first sign that something is near: a flight of stairs standing on its own among the trees, with no house around it. Something you only ever glimpse keeps pace behind you through the trees.

It is built to look and feel like a game you half remember from a PS2 demo disc: low-poly, fog, dithered edges, 640x360. It controls like a game made now.

**Where it is now:** one playable stretch. You start at a trailhead and walk into the woods until the trail thins out and disappears, then search the forest for the stairs. The forest falls silent as you get close, and on the way something follows you.

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
