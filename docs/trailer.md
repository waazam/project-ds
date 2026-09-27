# The teaser trailer (1:05)

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

```
python tools/Trailer/make_teaser.py 21 21
```

The two numbers are the start frames the recordings printed. The script needs these in
`build/trailer`:
- `partA.avi` and `partB.avi`;
- `song.wav`;
- `flicker_logo.mp4`, the owner's company logo.

It writes `DeadSilent-Teaser.mp4`, in this order:
1. **The logos:** Flicker Archive (the owner's company, 5 s), then the Godot logo (3.5 s). The Godot
   logo is `tools/Trailer/godot_logo.png`, the engine's own vector logo, rendered from the SVG built
   into the editor.
2. **The trailer:** part A, then part B. The music is edited to the song's grid, 85 bpm, measured
   from its onsets:
   - 1:40.24–1:57.18 under part A: 24 beats, eight shots of three beats each;
   - a jump to the big downbeat at 3:28.94, on the cut to the stairs;
   - the title on the hit at 3:56.12.
3. **The ending:** the music fades out gently over 0:50.5–0:53 of the finished teaser. The title
   lands in the forest's own sound, the game's ambience eased up (quietly) in step with the fade. At 0:57
   the song's ending drops back in on its hit (4:25.41) and rings out. The title comes in over the
   last shot, which fades to black behind it; the picture holds black after the title fades.

The trailer's footage is framed at 2.35:1 (1920x816, letterboxed) with a subtle CRT look: scanlines,
a slight colour fringe, softness, a vignette and fine grain, with no flicker. The grade is a little
dark (gamma 0.96). The Flicker Archive logo carries the candle's blow-out sound
(`assets/audio/sfx/candle_blow_out.wav`, from AudioGen).

The game's own sound sits low under the music, and comes up in the quiet before the drop. (`pip install imageio-ffmpeg`
provides an ffmpeg binary. `yt-dlp` can fetch the song.)

## The cut

The owner's notes shaped it:
- short (30–45 s) and smooth at 60 fps;
- no monsters, so the horror surprises people;
- the bunker and the stairwell at its heart, each broken in two;
- gentle cameras (the owner gets headaches from shaky ones);
- no strobing.

| s (after the 8.5 s of logos) | Shot |
| --- | --- |
| 0.0–2.1 | The cabin at dusk, a light in its window (fades up) |
| 2.1–4.3 | From the treetops, looking down on the trail as the fog rolls in round the trunks |
| 4.3–6.4 | The lake at dawn, skimming the water toward a dark-green far shore |
| 6.4–8.5 | The bunker's hallway, lantern in hand |
| 8.5–10.6 | The same hallway, gone red |
| 10.6–12.7 | Straight down the stairwell |
| 12.7–14.9 | Its decayed depths, and the drop |
| 14.9–17.0 | The church's nave: a high, slow, surreal swoop toward the altar |
| 16.9–44.1 | The song jumps to the 3:28.94 downbeat: a slow walk up the end of the Act 1 trail, and the first staircase peeking out of the fog |
| 42.0–44.5 | The music fades out as the forest comes up |
| 44.1–57.0 | DEAD SILENT over the stairs, in the forest's sound; the shot fades to black behind it; at 48.5 (0:57 in all) the song's ending drops back in and rings out |
