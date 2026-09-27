# 2026-09-27c — The teaser, re-cut to the owner's song

Commit: this entry's commit on `main`.

The owner's notes on the first cut (1:37) were:
- the resolution was low;
- the first two shots repeated;
- the monster in the woods revealed too much;
- the lake's far trees looked white;
- the bunker and stairwell were the best shots;
- the church shots came too deep into the game, and the camera clipped through trees;
- they wanted a bird's-eye view of the trail with fog rolling in, and a higher, surreal swoop down
  the nave.

The owner then supplied the music, their song "Petty Theft": from 1:40, jumping at 3:28 to the
stairs, and stopping at 3:56 on the title. After the next cut, they added: "way too slow and the fps
looks super low … 30-45 seconds would be great."

## The new cut (`TrailerDirector`, `docs/trailer.md`)
- **48 s, 1920x1080 at 60 fps.**
  - The movie writer records at the project's viewport size, so the trailer is recorded with a
    temporary `override.cfg` (in `docs/trailer.md`; never committed) that renders the 3D at a full
    1080p.
  - It was 640x360, upscaled, at 30 fps.
- **Cut to the song.** It runs at 84.7 bpm.
  - 1:40.2–1:57.2 carries eight shots of three beats each, with every cut on a beat.
  - The song then jumps to 3:28.24 for the stairs, and DEAD SILENT lands as it stops at 3:56.
  - The game's own sound sits low under it.
- **The shots:**
  - the cabin (the duplicate first shot is gone);
  - from the treetops, looking down on the trail as the fog rolls in round the trunks (it replaces
    the tree-clipping glide);
  - the lake at dawn;
  - the bunker's hallway, then the same hallway gone red;
  - the stairwell from the top, then its decayed depths and the drop;
  - the nave as a high, slow swoop toward the altar;
  - the first staircase peeking out of the Act 1 fog, then the title.
- **Removed:**
  - the monster glimpses (the woods and the crypt's eyes);
  - the church door;
  - the long-stair climb;
  - the night glide.
- **The score:** the in-engine score (`trailer_score.wav`, `TrailerSounds`) is gone, and the song
  replaces it. The song isn't in the repo.
- **Per-shot environment:** shots can now set the environment each frame (`Shot.Tick`), after the
  atmosphere drivers:
  - the treetops shot thins the air and ramps the height fog up round the trunks;
  - the lake shot thins its haze so the far shore reads green.
- `project.godot`: the movie writer's MJPEG quality is 0.95 (Godot's default is 0.75).

## The lake's white trees (in the game too)
- The dawn fog is thinner (0.003, was 0.0105) and a little darker. The far shore's treeline reads as a
  dark-green line through the haze, not a white wall.
- The lake's big trees are drawn out to 340 m (was 260 m), so the far shore's treeline doesn't
  thin out.

## Tests
- A build, and the two trailer recordings, run clean. The dawn fog and the lake's tree distance are
  small number changes; the full suites weren't re-run for this entry.
