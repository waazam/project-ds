# The teaser trailer (0:48)

`scripts/systems/TrailerDirector.cs` films it in-engine. The music is the owner's own song,
"Petty Theft" (soundcloud.com/alf452/petty-theft). It isn't in the repo; download it as
`build/trailer/song.wav`.

## Record it at 1080p60

Godot's movie writer records at the project's viewport size (640x360). For the trailer, put this
`override.cfg` in the project root while recording, and **delete it afterwards** (never commit it):

```
[display]
window/size/viewport_width=1920
window/size/viewport_height=1080
window/size/window_width_override=1920
window/size/window_height_override=1080
window/stretch/mode="canvas_items"
```

The 3D renders at full 1080p. The trailer's title card is laid out for 640x360 and scaled up.

Record the two parts:

```
Godot_v4.7.2-stable_mono_win64_console.exe --path . --windowed --resolution 1920x1080 --write-movie build/trailer/partA.avi --fixed-fps 60 -- --trailer
Godot_v4.7.2-stable_mono_win64_console.exe --path . --windowed --resolution 1920x1080 --write-movie build/trailer/partB.avi --fixed-fps 60 -- --trailer-ending
```

- `--trailer` boots straight into the Hollow from an early save (the test save slots, never the
  player's). It hides the HUD, parks and hides the player, and flies its own camera. It sets the
  atmosphere for every shot and quits when done.
- `--trailer-ending` does the same in Act 1's trail level for the last shot. That shot has a 4 s
  pre-roll so the Act 1 fog has closed in; the pre-roll is trimmed off.
- Each recording prints the frame it starts on (`[trailer] ... starts at frame N`). Trim N/60 s from
  the front.

## Make the MP4

The music is edited in two parts:
- song 1:40.2–1:57.2 under part A: 24 beats at 84.7 bpm, eight shots of three beats, with a cut on
  every third beat;
- a jump to 3:28.24–3:56.0 under part B.

The title lands as the song stops at 3:56. The game's own sound sits low under the music.
With part A starting at frame 21 and part B at frame 22:

```
ffmpeg -ss 0.35 -t 17.0 -i partA.avi -ss 4.367 -t 31.26 -i partB.avi -i song.wav -filter_complex "[0:v][1:v]concat=n=2:v=1:a=0,eq=gamma=1.1:brightness=0.015,scale=out_range=tv,format=yuv420p[v];[0:a][1:a]concat=n=2:v=0:a=1,volume=0.13,afade=t=out:st=44.2:d=0.5[game];[2:a]asplit=2[s1][s2];[s1]atrim=100.2:117.2,asetpts=PTS-STARTPTS,afade=t=out:st=16.8:d=0.2[m1];[s2]atrim=208.24:236.0,asetpts=PTS-STARTPTS,afade=t=in:st=0:d=0.12[m2];[m1][m2]concat=n=2:v=0:a=1,apad=pad_dur=3.6[music];[music][game]amix=inputs=2:duration=longest:normalize=0[a]" -map "[v]" -map "[a]" -c:v libx264 -preset slow -crf 17 -pix_fmt yuv420p -r 60 -c:a aac -b:a 256k -movflags +faststart -t 48.3 DeadSilent-Teaser.mp4
```

(`pip install imageio-ffmpeg` provides an ffmpeg binary. `yt-dlp` can fetch the song.)

## The cut

The owner's notes shaped it:
- short (30–45 s) and smooth at 60 fps;
- no monsters, so the horror surprises people;
- the bunker and the stairwell at its heart, each broken in two;
- gentle cameras (the owner gets headaches from shaky ones);
- no strobing.

| s | Shot |
| --- | --- |
| 0.0–2.1 | The cabin at dusk, a light in its window (fades up) |
| 2.1–4.3 | From the treetops, looking down on the trail as the fog rolls in round the trunks |
| 4.3–6.4 | The lake at dawn, skimming the water toward a dark-green far shore |
| 6.4–8.5 | The bunker's hallway, lantern in hand |
| 8.5–10.6 | The same hallway, gone red |
| 10.6–12.7 | Straight down the stairwell |
| 12.7–14.9 | Its decayed depths, and the drop |
| 14.9–17.0 | The church's nave: a high, slow, surreal swoop toward the altar |
| 17.0–44.8 | The song jumps to 3:28: a slow walk up the end of the Act 1 trail, and the first staircase peeking out of the fog |
| 44.8–48.3 | DEAD SILENT, as the song stops |
