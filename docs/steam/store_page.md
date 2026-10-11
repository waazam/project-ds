# The Steam store page

The copy and art for Dead Silent's page. The art is generated with `python tools/steam/make_store_art.py` into
`build/steam/` (not committed). It uses the story autotest's frames (`test-output/story`), so run the full autotest first.

## Art

| File | Steam slot | From |
|---|---|---|
| `header_capsule_920x430.png` | Header capsule | the church in the snow (Act 22) |
| `small_capsule_462x174.png` | Small capsule | the church in the snow |
| `main_capsule_1232x706.png` | Main capsule | the church in the snow |
| `vertical_capsule_748x896.png` | Vertical capsule | the road to the lodge (Act 22) |
| `library_capsule_600x900.png` | Library capsule | the road to the lodge |
| `library_hero_3840x1240.png` | Library hero (no text) | the crawlspace's lights (Act 23) |
| `library_logo_1280x720.png` | Library logo (transparent) | the title in Anton |
| `screenshot_1..5_*.png` | Screenshots, 1920x1080 | the church, the crawlspace, the tunnels, the TV room, the last stairs |

These are good enough to set up the page. Before launch, replace the wide capsules with a framed beauty shot (the
trailer's camera, `--trailer`, with the HUD hidden). Steam's rules: the capsules must carry the game's name, and the
hero must carry none. No review quotes or awards are allowed in capsule art.

## Short description (300 characters max)

> A camera, a trail and a day in the woods. Then the stairs. A slow, quiet first-person horror game in the look of the
> PS2 era. Follow a missing hiker's trail from the Blackfern woods to a lodge in the snow, and photograph what you find.
> Some of it will still be there when you look back.

## About this game

**It starts as a day out.** You park at the Blackfern trailhead with a camera and an afternoon to fill. The register's
last two entries are the same hiker's. The second one just says *"the steps"*.

**Then you find the stairs.** A staircase standing alone in the forest, going up into nothing. You climb it, and you wake
somewhere else.

- **A long way from the car.** Woods, a burning cabin, a bunker, a giant, a lake, a forester's station, under the world,
  a church in the snow, and a ski lodge where something is waiting for the storm to end.
- **A camera, not a gun.** Sixty-nine things to photograph, from birds to things that shouldn't exist. Every picture is
  scored and kept in your album.
- **Five endings.** How you photographed, how often you died and how fast you went decide how it ends. Nobody tells you
  which ending you're on.
- **Quiet horror.** No jump-scare spam and no strobing. The fear comes from the dark, the sound and things that move when
  you aren't looking.
- **The PS2 look.** Low-poly, dithered and soft, with a slight CRT feel. It's built to be played with the lights off.
- **Accessible.** Rebindable keys, sound captions, a screen-shake slider, head-bob off, and colour choices for
  the HUD.

## Tags

Horror, Psychological Horror, Atmospheric, First-Person, Walking Simulator, Exploration, Story Rich, Singleplayer,
Retro, PS1/PS2-style, Photography, Multiple Endings, Dark, Short.

## Content descriptors

Some violence and blood, and frightening themes. No sexual content.

## System requirements (Windows)

| | Minimum | Recommended |
|---|---|---|
| OS | Windows 10 64-bit | Windows 10/11 64-bit |
| CPU | 4 cores, 2.5 GHz | 6 cores, 3.5 GHz |
| Memory | 8 GB | 16 GB |
| Graphics | Vulkan 1.1: GTX 960 / RX 470, 2 GB | GTX 1660 / RX 5600, 6 GB |
| Storage | 3 GB | 3 GB |

## The store trailer (about 60 s)

This is the teaser (`docs/trailer.md`) with one more beat. It uses the same tools and the same song, "Petty Theft", the
owner's own.

1. **0:00–0:08.** The logos: Flicker Archive, then Godot.
2. **0:08–0:30.** Part A as it is: the trail, the register, the camera, the bunker and the stairwell. Cuts every three
   beats.
3. **0:30–0:42.** New: the winter. The church in the snow, the lodge's lobby in the firelight, and the crawlspace's
   lights. Same pace, eight shots of three beats each. Record it with `--trailer` pointed at the Act 22–23 saves.
4. **0:42.** The drop. A chapter card in the game's own style (`ChapterCard`), *"Chapter Seven: Enjoy Your Stay"*,
   held one bar and wiped off on the downbeat.
5. **0:42–0:55.** Part B: the last stairs in the fog.
6. **0:55–1:02.** The title on the hit, and black. "Wishlist now on Steam" in the title's type, small.

Same rules as the teaser: no monsters shown, gentle cameras, no strobing, and a 2.2:1 frame.
