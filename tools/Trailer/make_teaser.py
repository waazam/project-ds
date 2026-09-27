"""Builds the Dead Silent teaser (docs/trailer.md) from the two movie-writer recordings and the song.

    python tools/Trailer/make_teaser.py [partA start frame] [partB start frame]

Inputs in build/trailer: partA.avi, partB.avi (1080p60), song.wav ("Petty Theft"), flicker_logo.mp4 (the
owner's company logo). The Godot logo (tools/Trailer/godot_logo.png) is the engine's own, rendered from the
SVG built into the editor. Output: build/trailer/DeadSilent-Teaser.mp4.
"""
import subprocess, sys, os
import imageio_ffmpeg

FF = imageio_ffmpeg.get_ffmpeg_exe()
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "build", "trailer")

fa = int(sys.argv[1]) if len(sys.argv) > 1 else 21
fb = int(sys.argv[2]) if len(sys.argv) > 2 else 21

BEAT = 60 / 85                      # the song's tempo, measured from its onsets
A_LEN = 24 * BEAT                   # eight shots of three beats
S1 = 100.242                        # 1:40, on the beat
S2 = 208.93                         # the big downbeat at 3:28.94 (a hair early, to keep its attack)
TITLE_SONG = 236.123                # the hit the title lands on
STAIRS = TITLE_SONG - 208.94        # the ending shot, to the title (TrailerDirector.EndingStairs)
TITLE_HOLD = 11.0                   # TrailerDirector.TitleHold
JUMP_FROM, JUMP_TO = 237.534, 262.589   # half a bar after the title hit, into the song's own ending, both on the hit
XF = 2 * BEAT                       # the crossfade into the ending: the half bar from the title hit, on the grid
SONG_END = 273.9
FLICKER, GODOT = 5.0, 3.5           # the logos before it

B_LEN = STAIRS + TITLE_HOLD
music = A_LEN + (JUMP_FROM - S2) + (SONG_END - (JUMP_TO - XF)) - XF
video = A_LEN + B_LEN
pad = max(0.0, music - video)
intro = FLICKER + GODOT
total = intro + video + pad
title_at = A_LEN + STAIRS

fc = (
    # the logos: the owner's company, then the engine
    f"[0:v]scale=1920:1080:flags=lanczos,fps=60,format=yuv420p,setsar=1,trim=0:{FLICKER},setpts=PTS-STARTPTS[lf];"
    f"color=c=black:s=1920x1080:r=60:d={GODOT}[bg];"
    f"[1:v]scale=860:-1:flags=lanczos,format=rgba[gl];"
    f"[bg][gl]overlay=(W-w)/2:(H-h)/2:shortest=1,format=yuv420p,setsar=1,fade=t=in:st=0.3:d=0.6,fade=t=out:st={GODOT - 0.8}:d=0.6[lg];"
    # the trailer
    f"[2:v]trim=0:{A_LEN},setpts=PTS-STARTPTS,setsar=1[va];"
    f"[3:v]trim=0:{B_LEN},setpts=PTS-STARTPTS,setsar=1[vb];"
    f"[va][vb]concat=n=2:v=1:a=0,eq=gamma=1.1:brightness=0.015,scale=out_range=tv,format=yuv420p,"
    f"tpad=stop_mode=add:stop_duration={pad + 0.1}:color=black[vt];"
    f"[lf][lg][vt]concat=n=3:v=1:a=0[v];"
    # the game's own sound, low, gone by the title
    f"[2:a]atrim=0:{A_LEN},asetpts=PTS-STARTPTS[ga];[3:a]atrim=0:{B_LEN},asetpts=PTS-STARTPTS[gb];"
    f"[ga][gb]concat=n=2:v=0:a=1,volume=0.13,afade=t=out:st={title_at}:d=3[game];"
    # the song: 1:40, then 3:28.94 to the title and half a bar on, then its own ending ringing out
    f"[4:a]asplit=3[s1][s2][s3];"
    f"[s1]atrim={S1}:{S1 + A_LEN},asetpts=PTS-STARTPTS,afade=t=out:st={A_LEN - 0.04}:d=0.04[m1];"
    f"[s2]atrim={S2}:{JUMP_FROM},asetpts=PTS-STARTPTS[m2];"
    f"[s3]atrim={JUMP_TO - XF}:{SONG_END},asetpts=PTS-STARTPTS,afade=t=out:st={SONG_END - JUMP_TO + XF - 1.5}:d=1.5[m3];"
    f"[m2][m3]acrossfade=d={XF:.4f}:c1=hsin:c2=hsin[m23];"
    f"[m1][m23]concat=n=2:v=0:a=1[music];"
    f"[music][game]amix=inputs=2:duration=longest:normalize=0,aresample=44100,aformat=sample_fmts=fltp:channel_layouts=stereo[mix];"
    # silence under the logos (real samples: adelay only leaves a timestamp gap, which players treat differently)
    f"aevalsrc=0|0:s=44100:d={intro},aformat=sample_fmts=fltp:channel_layouts=stereo[sil];"
    f"[sil][mix]concat=n=2:v=0:a=1,asetpts=N/SR/TB,apad[a]"
)
args = [FF, "-y", "-hide_banner", "-loglevel", "error",
        "-i", os.path.join(OUT, "flicker_logo.mp4"),
        "-loop", "1", "-t", f"{GODOT}", "-i", os.path.join(HERE, "godot_logo.png"),
        "-ss", f"{fa / 60:.4f}", "-i", os.path.join(OUT, "partA.avi"),
        "-ss", f"{fb / 60 + 4.0 + 0.01:.4f}", "-i", os.path.join(OUT, "partB.avi"),   # past the 4 s pre-roll
        "-i", os.path.join(OUT, "song.wav"),
        "-filter_complex", fc, "-map", "[v]", "-map", "[a]",
        "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p", "-r", "60",
        "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", "-t", f"{total:.3f}",
        os.path.join(OUT, "DeadSilent-Teaser.mp4")]
subprocess.run(args, check=True)
print(f"teaser: {total:.2f} s (logos {intro:.1f} s, stairs cut at {intro + A_LEN:.2f} s, title at {intro + title_at:.2f} s)")
