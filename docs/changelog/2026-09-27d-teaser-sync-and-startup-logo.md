# 2026-09-27d — The teaser's sync, ring-out and logos; the company logo at startup

Commit: this entry's commit on `main`.

The owner's requests:
- "the song needs to ring out at the end of the trailer";
- "you are so close to having the downbeat of the song sync up with the transition";
- the Godot engine logo and their company's animated logo (Flicker Archive) before the trailer;
- their company logo at the game's startup, after the Godot screen and before the main menu.

## The teaser (`tools/Trailer/make_teaser.py`, `docs/trailer.md`)
- **The sync.** The song's onsets were measured.
  - Its tempo is 85 bpm; 84.7 was the earlier estimate.
  - The big downbeat is at 3:28.94, not 3:28.24, so it used to land 0.7 s after the cut to the
    stairs. Now it lands on the cut (within 10 ms).
  - The title lands on the hit at 3:56.12.
  - The eight opening shots are re-timed to 85 bpm. Each is three beats, with every cut on the beat.
- **The ring-out.** The song no longer stops dead at the title.
  - Half a bar after the title hit, it jumps, on its own grid, to its real ending (4:22.59).
  - That ending rings out to silence under the title, about 11 s.
  - The title holds 9 s and fades out slowly; the picture stays black until the music is gone.
- **The logos:** Flicker Archive (the owner's 5 s animation), then the Godot logo, then the trailer.
  - The Godot logo is the engine's own vector logo, taken from the editor's built-in SVG and rendered
    crisp at full size (`tools/Trailer/godot_logo.png`).
- The teaser is 1:05 in all: 8.5 s of logos, the 48 s trailer, and the ring-out.
- The encode is now one script, `make_teaser.py`, in place of a long ffmpeg command. The logos get
  real silence under them, not a timestamp gap, which some players mishandle.

## The ending, polished (the owner: "so close to being a perfect transition")
- **The jump into the song's ending is now a crossfade.** It runs over the half bar from the title
  hit, on the song's grid: the groove fades out as the ending (from its pickup hit at 4:21.18) fades
  in, both equal-power. Before, it was a hard cut 11 s from the end.
- **The title lands over the game.** On the 3:56 hit, DEAD SILENT appears over the last shot of the
  stairs. The camera drifts on and settles; the shot fades to black behind the title over 3 s. Then
  the title holds on black and fades out as the song rings out.
- The title has a soft shadow so it reads over the pale fog.
- The game's own sound fades out over those 3 s with the picture.

## The company logo at startup (`StartupLogo`, `scenes/ui/startup_logo.tscn`)
- The game's first scene is now the Flicker Archive logo (`assets/video/flicker_archive_logo.ogv`,
  converted to Ogg Theora for Godot). It plays after the engine's boot splash, then goes to the
  main menu.
- It plays at the window's full resolution; the game itself renders at 640x360, which would blur
  the logo's thin lettering. The menu gets its usual scaling back.
- Any key, click or controller button skips it.
- A run with a command line (the tests, the trailer) goes straight to the menu, as before.

## Tests
- Startup recorded with the movie writer: the logo plays full-screen, then the main menu appears at its
  normal scaling.
- `--autotest --story-from=1 --story-to=2`: 56/56.
