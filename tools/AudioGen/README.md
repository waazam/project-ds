# AudioGen

Generates Project DS's placeholder forest audio procedurally (C#, no NuGet packages, no samples).
The output is deterministic because it uses a fixed seed, so regenerating gives byte-identical files.

```
dotnet run --project tools/AudioGen                 # regenerate everything, then verify
dotnet run --project tools/AudioGen -- --verify     # only verify the existing files
dotnet run --project tools/AudioGen -- --only bird,raven  # regenerate files whose name contains any listed fragment
```

It can run from any directory. It walks up to `project.godot` to find the repo root, then writes to:

- `assets/audio/ambient/*_loop.wav`: seamless 20-40 s beds (wind, leaves, insects, distant, stream, drone, ringing, breath, heartbeat)
- `assets/audio/sfx/`: `bird_01..08`, `step_dirt_01..06`, `step_wood_01..04`, `cloth_01..04`, `step_stone_*`, `stalker_*`, `twig_snap_01..04`, `branch_drop_01..02`, `trunk_creak_01..03`, `cricket_chirp_01..03`, `raven_01..02`, `rustle_01..03`

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

## Story sounds (2026-09-21 rework)
The owner rejected every synthetic continuous noise bed (wind read as an airplane, leaves as fire or surf, the distant bed as a helicopter), pitched knocks (drums, "bamboo"), repetitive breathing, and anything busy. So these sounds are built from events, not noise beds:

- `rain_loop` (180 s): a drizzle of individual drops at three distances (near ~0.8/s, mid ~7/s, far ~60/s), plus a very soft under-layer below 300 Hz at -26 dB. Density swells over 36-90 s.
- `thunder_01..03`: distant rolling rumble built from hundreds of low-passed pressure pulses grouped into rolls. No crack, no overdrive, natural decay. Variant 1 is far, variant 2 is nearer with a dull clap, variant 3 is very far.
- `fire_crackle_loop` (30 s): clustered pops (power-law sizes), fine crackle, snaps every ~5 s and a soft roar below 300 Hz. No hiss.
- `choir_chant_loop` (72 s): "come and see" chanted in unison by 4 men and 4 women, using the formant voice in `Voice.cs`, far off in an FDN hall (`Dsp.Hall`).
- `stairs_hum_loop` (40 s): a D1 harmonic hum that stays clean when turned up a lot.
- `giant_step_01..03`: low noise-only thuds for the giant's stride.

Event loops are written circularly, and their filters and reverb run through `Dsp.Circular`, so they loop exactly. `--verify` prints extra taste measurements for these files and fails them on:
- a sustained floor
- clipping or overdrive
- continuous hiss
- a choir that never stops
- a hum or thud that small speakers can't carry

`--voice-test` writes dry chant renders to `test-output/audio/` and prints band energy for each phone.
