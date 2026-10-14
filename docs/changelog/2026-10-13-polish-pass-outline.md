# 2026-10-13 — Polish pass: the outline

One page for the whole day's work (the owner's playtest list after pulling the collaborator's 70 commits). No new story:
everything here improves what was already in the game. The detailed entries for each part are listed at the bottom.

## Sound
- **The "bouncy / circus" sound fixed.** The main cause was the rebuilt outdoor footsteps: two low thuds, heel then toe,
  on a slightly different note every step (a bouncing ball). Rebuilt as one heel strike and a toe scuff, with less pitch
  wobble. Also rebuilt: the lantern's gutter (it was a falling "sad trombone" buzz), about 87 knocks, drips and steps that
  rang like woodblocks, and the crawler's steps.
- **Muffling flicker gone.** Sounds behind a tree trunk muffled and unmuffled in a tenth of a second ("wah"). Now three
  rays must be cut and the muffle eases in and out.
- **The song's synth cues** were 8 dB louder than intended; set back. They no longer play on checkpoints before Act 24.
  Chapter and title hits 8 dB quieter.
- **Less pitch wobble in code:** heartbeat, oars, crawler voice, interior haunts.
- **New tool:** `tools/AudioAudit` scores every sound for tonality, pitch glides and woodblock ring.

## Weather
- **The rain over the cabin** now stops for good when the cap is taken, on every path (including Continue), and the
  roof and bough drips stop about 12 s later instead of a minute.
- **Blood rain and the clearing** now spawn when the cabin door is opened to leave with the cap (not at the bridge);
  the blood rain is absent at the cabin and ramps in to full strength over the bridge.

## Look
- **Trees no longer look like paper.** Trunks are shaded round in the shade with visible bark; crowns have depth and
  light through the needles; light shafts, snags and moss toned down.
- **Every fir rebuilt** (Act 1, the Hollow, the lake): limbs carrying sprays of needles instead of a few giant frond
  cards; the lake's bald cypresses use the same crown. No longer palm-like.
- **No fog inside buildings.** Fog banks, volumetric haze and distance fog are cut indoors (cabin, shed, station,
  library, round room, church, lodge); lamps and windows still glow.
- **Blacklight marks are real decals** on real surfaces: no floating, no pale "paper" rectangle, spread out instead of
  clumped. Handprints rebuilt from forensic references (palm pads, creases, jointed fingers); human prints for people,
  clawed prints for the creature.
- **Creature hands clawed:** long hooked black talons, knobbed knuckles, tendons (stalker, giant, wendigo, its
  crawlspace arm, crawler). **Shoulder spikes removed** from the stalker and the giant.
- **Act 1:** the red backpack ("suitcase") in the woods removed.
- **Chapter cards** show the title alone, no "CHAPTER N".
- Station, lodge and Act 25 snow toned down a little.

## The player
- **The lantern hand** is a gloved fist on the handle (was a flat paddle); the handle is a normal length; the lantern
  sits higher in the view.
- **Looking down** shows the jacket, belt, jeans and boots, not a cut-off stump.
- **The regular lantern is brighter** (about 23 %); the blacklight is unchanged.
- **No more lines on tunnel walls** from the lantern's cage; the light never goes inside a wall.
- **The wake after the first stairs** is one fade up out of the black (no wake, black, wake).

## Story beats and puzzles (same story, played better)
- **Act 1:** the falling trees wait until you look at them; the fir's crack stops you and draws your eye to the falls,
  then lets you go. Wandering off the way gets a tree falling across your path in view. "DEAD SILENT" appears only
  within about 18 m with the stairs in view (was 87 m).
- **Bunker code:** the on-screen code tracker is gone; the dial starts at 0000 and the code is dialled from memory.
  Two short hints point to the blacklight ("His numbers are on the trees. B: the blacklight shows them." and, after
  walking past one, "Something on a tree back there. B: the blacklight.").
- **Act 6:** the fall off the last flight goes off the edge (stumble, topple, drop, black) instead of through the stone.
- **Act 12, the lake:** hold W to row, Shift to row hard (breath meter), A/D to steer; about 77 s instead of about 2 min.
  Fog rolls in, something bumps the hull, a limb watches from the fog, two strikes slam the water beside the boat,
  and the hunt surges to a final dash. It only takes the boat after a clear warning.
- **Shorter walks, and sprinting always gets you there:** Act 14 stairwell 30 → 20 turns; Act 15 corridor 178 → 120 m
  and red-light hall 523 → 330 m (the red/green rule unchanged); Act 21 long stair 52 → 16 flights. The stairwell's
  old sprint loop-back code is deleted.

## Performance (the lag before events)
- Things that were built at the moment they appeared (Act 7 giant 3.2 s, lake breach 1.6 s, Act 14 crawler 1.8 s,
  library/church 4–5 s) are now built and their shaders warmed during load blacks or ahead of time.
- Late saves stay black until the level is built instead of showing the picture and freezing.
- Per-frame garbage cut about tenfold (garbage-collection hitches were the most common stutter).
- The frame logger now measures real frame times (the old one capped at 150 ms) and names the cause (`--hitches`).

## Bugs fixed
- Act 24: the flamethrower wasn't in hand after a Continue or a death.
- Ending 3: the squashed car logged a physics error.
- The pause menu could open during the ending's fade (and Quit to Menu there threw an error).
- Engine errors from async waits after a level change.

## Tests
- **New `-- --playtest`:** every act, all five endings and the core systems, jumping straight to each beat; each act
  starts from its own save (so it doubles as a Continue test); `--playtest-only=<acts or beat ids>`,
  `--playtest-fast`; report and screenshots in `test-output/playtest/`. Playtest saves and settings are kept apart
  from the player's.
- New `--measure-walks` (walk vs run times for the long walks) and `--hitches`.
- StoryTest and ContinueRoundTripTest updated for every change above.

## Docs
- `docs/STORY.md` Revision 4: the whole game as it plays now, Acts 1–25.
- README: full controls table, every test command, where the sounds come from. CONVENTIONS testing section.
  Pacing map extended to Act 25.

## Detailed entries
- [Shorter stairs and halls](2026-10-13-shorter-stairs-and-halls.md)
- [The lag pass](2026-10-13-the-lag-pass.md)
- [The lake crossing reworked](2026-10-13-the-lake-crossing-reworked.md)
- [Solid trees, no fog indoors, and a look audit](2026-10-13-solid-trees-no-fog-indoors-and-a-look-audit.md)
- [The hand, the body, the wake and the code](2026-10-13-the-hand-the-body-the-wake-and-the-code.md)
- [Blacklight decals and claws](2026-10-13-blacklight-decals-and-claws.md)
- [The falls are seen](2026-10-13-the-falls-are-seen.md)
- [The fall and the door](2026-10-13-the-fall-and-the-door.md)
