# AudioGen

Generates Project DS's placeholder forest audio procedurally (C#, no NuGet packages, no samples).
The output is deterministic because it uses a fixed seed, so regenerating gives byte-identical files.

```
dotnet run --project tools/AudioGen                 # regenerate everything, then verify
dotnet run --project tools/AudioGen -- --verify     # only verify the existing files
dotnet run --project tools/AudioGen -- --only bird  # regenerate the files whose name contains "bird"
```

It can run from any directory. It walks up to `project.godot` to find the repo root, then writes to:

- `assets/audio/ambient/*_loop.wav`: seamless 20-40 s beds (wind, leaves, insects, distant, stream, drone, ringing, breath)
- `assets/audio/sfx/`: `bird_01..08`, `step_dirt_01..06`, `step_wood_01..04`, `cloth_01..04`

Format: 16-bit PCM mono at 22050 Hz. `insects_loop` and `ringing_loop` use 44100 Hz so that their 4-8 kHz content stays clean.

## How the loops stay seamless
- Noise beds (wind, leaves, insects, distant, stream) render past the loop end, then crossfade the tail into the head with an equal-power fade.
- Tonal and event beds are exactly periodic. In drone and ringing, every frequency and modulator is a whole number of cycles per loop. Breath writes each breath into a circular buffer.

## Verification
After it writes the files, the tool reads every WAV back and prints one row per file. Each row shows:
- format and duration
- peak and RMS in dBFS
- the loop-seam jump compared with the file's own 99.9th-percentile sample step (`d` must be ≤ 1)
- for one-shots, whether both ends sit at zero
- the energy share in the bands <100 / 100-1k / 1-3k / 3-8k / >8k Hz
- the spectral centroid
- the level-modulation range

It also checks content: insects must sit mostly above 3 kHz, drone below 100 Hz, and so on. The process exits non-zero if any check fails.

## Levels
One-shots peak at -3 dBFS. Bed RMS targets:
- wind -21
- leaves -27
- insects -29
- distant -30
- stream -24
- drone -22
- breath -27
- ringing -26 peak

These are only sensible starting levels. The real mix belongs to the buses and `ForestAmbienceManager`.
