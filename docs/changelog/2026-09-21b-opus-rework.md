# 2026-09-21b — Opus rework: foundations, saves, rebuilt models, Act 1 flow, title screen

Commit: `33f6214`

A systems and art consolidation pass. Story beats stayed the same except
where explicitly called for (Act 5's tool limit — see `docs/STORY.md`); the
work here is mostly foundations that later story passes would build on.

## Systems & Foundations
- A cutscene system; `Interactable`/`PlayerInteraction` with a
  crosshair-only dim highlight; held/pressed input edges added to
  `PlayerInput`.
- Save/Continue now restores the *whole* world: the storm, the door state,
  the fire, the Act 6 clearing, Act 11, story-beat guards, inventory, and a
  safe respawn point. A new `ContinueRoundTripTest` checks this.
- `StoryBeat`/`StoryTrigger`, `IndoorZone`, `RequestSilence`, `StairsHum`,
  dedicated Radio/VoiceHarsh audio buses, and a "Reduce Flashing"
  accessibility setting.

## Subtractions
- `NavMeshBaker` removed.

## Level Design / Art
- Cabin, shed, and bunker (split into `bunkerparts`) rebuilt; friend,
  items, birds, fire/rain VFX, and the veiny ground model rebuilt.
- Bunker mound: denser, darker growth, with litter on its steep faces.
- Cabin windows now only board up once the friend has actually boarded
  them (a continuity fix — they used to appear boarded before the story
  beat that boards them).

## Story Beats (Act 1 specific)
- The main trail now ends at a fallen fir (root plate on one side, crown on
  the other); the way to the stairs beyond it is trackless, marked only by
  rare waymarker stones.
- Unused faint paths removed; the overlook path demoted.
- Birds spread across the whole trail and call from wherever they're
  perched; landmarks moved into the back half of the trail.
- Faster walk/run, longer sprint; unlimited stamina in debug builds.

## Additions
- One unified inventory: every item usable at any time, no selecting
  between them, any number of tools carried at once.
- A camera viewfinder overlay; the stalker's sting now fires specifically
  when it is the thing photographed.
- Title screen: an old TV in the dark playing a worn VHS tape of the
  stairs (`TitleTv`, `title_crt` shader).

## Audio Fixes
- The "turn around" cue was disabled after discovering its sound file was
  actually a duplicate of the "come up and see" take (a mislabeled asset);
  the real Act 6 clearing voice line now plays instead.
