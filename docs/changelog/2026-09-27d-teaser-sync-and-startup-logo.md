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
- **The ending's sound (the owner's direction, after trying a hard cut, a crossfade and a later return):**
  - the music fades out gently, over 0:50.5–0:53 (it was 0:51–0:52, which the owner found too
    abrupt);
  - the title lands in the forest's own sound. The game's ambience comes up in step with the
    music's fade: eased, in decibels, and quiet (about -38 dB; at first it came in loud and
    intrusive);
  - at 0:57 the song's ending drops back in on its hit (4:25.41) and rings out.
- **The title lands over the game.** On the 3:56 hit, DEAD SILENT appears over the last shot of the
  stairs. The camera drifts on and settles; the shot fades to black behind the title over 3 s. Then
  the title holds on black and fades out as the song rings out.
- The title has a soft shadow so it reads over the pale fog.

## The look: 2.2:1, a CRT, and a darker grade (the owner)
- **2.2:1.** The trailer's footage is letterboxed at 1920x872 in the 1080p frame; the logos stay
  full-frame. It was 2.35:1 first; the owner asked for a little less bar.
- **A CRT's look,** subtle:
  - a scanline every three pixel rows (the game's own 360 lines);
  - a slight red/blue fringe;
  - a touch of softness;
  - a vignette;
  - fine moving grain.
  - There is no flicker or roll: nothing flashes.
- **Darker:** gamma 0.96 with a little more contrast and a little less colour (it was lifted, gamma
  1.1). The tunnels and the stairwell feel more ominous.

## The candle, blown out (`flicker_logo_candle`)
- The Flicker Archive logo's candle now has a sound: the owner's pick, a royalty-free clip of a candle
  (by kai_audio). It's cut so the candle crackles while it burns and the blow lands as the flame dies
  (2.84 s into the logo), followed by the smoke's wisp.
- A first generated version (a breath and a smoke draught) sounded like a hiss, not a wisp, and was
  replaced.
- It plays both in the teaser and in the game's startup logo, starting with the video.

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
