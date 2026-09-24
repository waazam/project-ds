# 2026-09-24c — Act 15: the long hallway

Commit: this pass's single commit on `main` (see `git log` for the hash).

Built from `Project_DS_The Real Act 15.docx` and the owner's references:
- a claustrophobic red-lit concrete corridor with strip lights in the floor ("make the walls and
  ceiling even taller");
- a faceless shadow figure in a hat, with two white points for eyes and dark shards hanging above him.

## Story Beats
- **The save** is Act 14's end (checkpoint 14): the chamber under the stairwell. Its passage now
  opens into the hallway (the placeholder credits there are gone).
- **The narrow hallway** is a body's width across (1.6 m) and 22 m tall, so its ceiling is lost in
  the dark:
  - raw stained concrete, pilasters and ledges marching away, dark doorways that go nowhere;
  - three thin strip lights in the floor down each side;
  - a green-black haze, with the green glow coming from far ahead.

  It takes 2 minutes at a walk (measured by the test), 1 at a run.
- **The taper**: the walls lean apart and the ceiling climbs, into a stretched version of the hall
  7 m wide and 34 m tall, with green dome lamps hanging on long cables out of the dark.
- **The shadow man** stands frozen in the middle of it, because the lights are green. He is a tall,
  flat-black figure in a long coat and a brimmed hat, with no face, only two points of white light
  under the brim, and dark shards turning slowly in the air above him. He can't be walked through.
- **Red light, green light** begins once you are past him:
  - Every 20-30 s the lamps flicker three times, then go red with a relay's clunk and a low siren.
    The fog and the floor strips go red with them.
  - He is right behind you, 1.3 m away, facing you. His breath is close.
  - Moving in the red (after a short grace; the first red is kinder) means you are taken: the view
    is turned round onto him, his eyes flare, he comes in, and it's "You moved." and back to Act 15's
    start.
  - Looking round at him is allowed.
  - Red lasts 4-6 s, then the green comes back and he stays where he was, frozen, as you walk on.

  The section is about 8 minutes to the end (the test measured 8.0 minutes with 15 reds).
- **The door at the end** (JANITOR) opens into a small dark janitor's closet: shelves of bottles and
  tins, a mop fallen across the floor, a bucket, and a light switch by the door. Walking in, the door
  swings shut behind you. That is Act 16's save (checkpoint 15). Act 16 isn't built yet, so the
  credits roll after a few seconds in the dark.

## Comfort
- The flicker is three slow blinks (about two a second at most) down to a glow, never a strobe. With
  Reduce Flashing it is three soft dips. The red comes on once and stays.

## Additions
- `Act15Hallway` (with `Act15Hallway.Rlgl.cs`) and `hallwayparts/ShadowMan`.
- `ForestAtmosphere.UndergroundFogColor`: the hallway's fog is green-black, going dark red in the red.
  The stairwell now only drives the underground look inside its own space.
- New checkpoint: `Act15Finished` = 15, with a Continue marker in the closet.
- `--story-from=15` starts in the chamber.

## Audio
- New synthesized sounds: the hallway's duct hum (loop), a relay clunk, the low siren, the shadow
  man's breath (two), and his strike.

## Tests
- `StoryTest` Act 15 covers:
  - the chamber save, the green underground fog, and the narrow stretch (timed);
  - the shadow man frozen in the green;
  - the game starting once you pass him.

  The first pass keeps walking into the red: he is behind you, you are taken, and it reloads. The
  second pass plays it properly, walking in the green and standing still in the red (looking round
  at him in one red), all the way to the door, into the closet, and the Act 16 save.
- `ContinueRoundTripTest` has a new scenario for Act 15's end (in the closet, door shut). Its
  ground check now covers the whole station, stairwell and hallway.
- Results: `--story-from=15`: 19/19. Full `--autotest`: 389/389. `--continue-test`: 483/486. The same three failures (the Act 6
  loop's fire state, the Act 10 mood, and the clearing stairs) predate this work.
