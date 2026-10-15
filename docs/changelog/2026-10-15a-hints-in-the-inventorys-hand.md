# 2026-10-15a — Hints in the inventory's hand: one readable look for every prompt, hint and thought, out of the way

The owner asked: "make hints match the inventory font and make it more readable and easy to see without being in the
way". The fix is in the same commit as this file.

## Changes
- **One look for every hint** (`HintStyle`, `HintText`, new in `scripts/ui/HintText.cs`).
  - **The face:** the inventory readout's: Anton with a hair of letter-spacing, lit in the HUD neon's core colour over a
    faint halo of its glow (it follows the "HUD glow" colour). Sentence case, not capitals: these are sentences.
  - **Contrast:** a dark outline round each letter, and under the block a soft dark pool (a blurred oval with no edge),
    so a line reads over snow, sky and black without a box round it.
  - **Keys:** a small key cap, a dark cap edged with the neon tube, its letter lit. "B: the blacklight." shows as the B
    cap and "the blacklight."; "A / D  choose" as two caps; a held prompt reads "HOLD" then the cap.
  - **Sharp:** drawn on CanvasLayers above the PS2 finish (layer 10). With the CRT filter on (the default) the 2D is drawn
    at the window's resolution, so the letters are sharp at 1080p and 1440p.
  - **Size:** 10 px of the 640x360 canvas (30 px at 1080p, 40 at 1440p). The inventory's own name is 9.
- **Where they sit** (they don't cover the middle of the view, they don't cover each other, and they stay clear of the
  inventory readout):
  - **The interaction prompt** (`InteractPrompt`): just under the crosshair (22 px below the centre, it was 64), over
    the thing looked at, clear of the lantern held low at the left. A long examine line ("An empty niche, the shape of a
    chalice...") wraps to two short lines instead of one line across the view.
  - **Context hints** (`Subtitle` lines that don't open with a quote: the lantern's lessons, the blacklight nudges, the
    keypad's controls, the curios' lines): on the black of the bottom cinema bar, centred. They wrap short of the
    inventory readout at the bar's right. A key's segment moves to the next line whole rather than splitting. If the
    block doesn't fit in the bar (bars off, or the Large size) it sits just above the bottom edge.
  - **Thoughts in play** (`ScreenFader` captions with no title, said while the world shows: "Pitch black in here. There
    was a switch by the door."): low too, in the bar, or stepped up just over a hint or a spoken line showing there.
    They were across the middle of the view, right under the crosshair, and collided with the prompt.
  - **Spoken lines** (subtitles that open with a quote) keep their serif look and place, just above the bar.
  - **Sound captions** step up over whatever shows in the low band instead of overlapping it.
  - Title cards, the black-screen narration, death lines, the credits and the end card keep the caption look in the
    middle: a caption with a title, or one said over black, is not a thought.
- **The modal overlays' control lines** (`ChoicePrompt`: the broken stair and the sewer hole; `CryptexOverlay`): in the
  same look and place, key caps and all. Their choice words and the notes and pages reading view are as they were.
- **The rowing prompt** (`RowingPrompt`): its key caps and labels in the same face.
- **Fades:** the prompt eases in over about 0.12 s and out over 0.2 s. A hint replacing a spoken line (or the other way
  round), and a thought replacing a title card's look, fades the old one out in 0.2 s instead of popping.

## Additions
- **Pad glyphs:** once a pad is used (`HintStyle.UsingPad`, from any pad button or stick push; a key or mouse click
  goes back), the hints name its buttons. The use key is A, the lantern Y, the blacklight "D-pad down", gather the beam
  RT, the camera X, the wheel "LB / RB", the album View. `KeyHint.Of` returns them. A new `KeyHint.ForDevice` rewrites a
  how-to line's lead keys (F, B, Right mouse, Left mouse, Wheel, Tab) to the player's rebinding or the pad's buttons;
  the lantern's and the opening's how-to lines go through it. The choice prompt's and the cryptex's lines have pad
  versions.
- **"Hint text" setting** (Settings, under Sound captions): Small, Normal, Large (hints 9, 10, 12 px; spoken lines 11, 12,
  14). Saved as `accessibility/hint_size`, and in the settings round-trip test.
- `CinemaFrame.Bar`: the bottom bar's current height, for the hints that sit on it.
- `ui_preview.tscn -- --hints [--hints-tag=name]`: every hint kind at 1920x1080 and 2560x1440, by day and in the dark,
  with pad, Large and Small variants, to `test-output/hints/<name>/`.

## Unchanged
- Every prompt, hint and caption string is the same text. Tests read `PromptText`, `CaptionAlpha` and
  `CurrentlyShowing`, and all three still work (`CaptionAlpha` counts the thought too).

## Known
- With the CRT filter off, the whole frame (UI included) is drawn at 640x360 and scaled up, as before. The hints stay
  readable but are pixel-scaled there.
- The pad's names are Xbox-style (A/B/X/Y, LB/RB, View). There are no PlayStation glyphs.
