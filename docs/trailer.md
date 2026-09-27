# The teaser trailer (1:37)

`scripts/systems/TrailerDirector.cs` films it in-engine, and `tools/AudioGen/TrailerSounds.cs`
scores it (`assets/audio/music/trailer_score.wav`).

## Record it

```
Godot_v4.7.2-stable_mono_win64_console.exe --path . --write-movie build/trailer/trailer.avi --fixed-fps 30 -- --trailer
```

- `--trailer` boots straight into the Hollow from an early save. It uses the test save slots, never
  the player's.
- It hides the HUD, parks and hides the player, flies its own camera, sets the atmosphere for every
  shot, plays the score, and quits at 30 s.
- The console prints the frame the trailer starts on (`[trailer] ... starts at frame N`).

The last shot is recorded separately, in Act 1's trail level:

```
Godot_v4.7.2-stable_mono_win64_console.exe --path . --write-movie build/trailer/partB.avi --fixed-fps 30 -- --trailer-ending
```

It has a 4 s pre-roll (the camera already standing at its start, so the Act 1 fog has closed in) that
is trimmed off. (Record the Hollow part as `partA.avi` with `-- --trailer`.)

## Make the MP4

The recording is 640x360, the game's own resolution. Trim the load, upscale 3x with nearest-neighbour
(keeping the PS2 pixels sharp) and encode. Here N/30 = 0.734 s:

```
ffmpeg -ss 0.734 -t 84 -i partA.avi -ss 4.734 -t 13.5 -i partB.avi -filter_complex "[0:v][0:a][1:v][1:a]concat=n=2:v=1:a=1[v][a];[v]scale=1920:1080:flags=neighbor,eq=gamma=1.15:brightness=0.02[vo]" -map "[vo]" -map "[a]" -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart DeadSilent-Teaser.mp4
```

(`pip install imageio-ffmpeg` provides an ffmpeg binary.)

## The cut

The Evil Dead trailer's "force" camera (the owner's reference): low point-of-view glides through the
woods. After the owner's notes on the first cut:
- slow (about four metres a second), not fast-forwarded;
- smooth, with only a slight sway, because the owner gets headaches from shaky cameras;
- no strobing: only cuts and short dips to black;
- brighter: each shot opens up the exposure, most in the night woods.

The monsters are only glimpsed: a shape between trunks, and two eyes at the end of the crypt.

| s | Shot |
| --- | --- |
| 0–3 | Black; the drone begins |
| 3–13 | The force: a slow glide through the woods at dusk toward the cabin |
| 13–19 | The cabin, a light in its window |
| 19–22 | The glimpse: drifting along the treeline, something tall between two trunks |
| 22–30 | The lake at dawn, skimming the water |
| 30–37 | The bunker's hallway, lantern in hand |
| 37–44 | Straight down the stairwell, sinking, turning slowly |
| 44–51 | The long hallway |
| 51–58 | The crypt, and for its last second two red eyes at the far end |
| 58–66 | The force again, at night |
| 66–74 | The church: down the candlelit nave to the red circle |
| 74–78 | The long stair, looking up into the dark |
| 78–82 | The great door, then the slam |
| 82–84 | Black, silence |
| 84–92 | The last shot: the first staircase, just peeking out of the Act 1 fog, a slow walk toward it |
| 92–97 | DEAD SILENT |
