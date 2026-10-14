# 2026-10-13l — The bunker's keypad: a real one on the wall beside the door, used where it hangs; no overlay, no cursor

The owner: "make the bunker keypad an actual keypad that cant be clicked with the mouse".

## What was wrong
- **The code lock was a screen overlay.** E on the vault door brought up `CodeLockOverlay`, a 2D plate of four
  numbered wheels over a darkened picture. It sat over the world instead of being part of it, and it read as a menu to
  click.
- **The door's "dial"** was a small four-drum lock on the door's face that did nothing itself.

## The keypad (`BunkerParts/BunkerKeypad.cs`)
- **What it is.** A weathered 1990s door-entry pad, built in code like the rest of the bunker, after the stainless
  outdoor pads of the period (Sargent, Bosch, Alarm Lock: a stainless faceplate on a cast back box, square engraved
  keys, status lamps).
  - **Where:** on the concrete face right of the vault door's frame, at hand height, under the station card.
  - **The housing:** a cast grey-green back box with flanges top and bottom and four masonry bolts, and a sloped rain
    hood with cheeks.
  - **The face:** a brushed stainless faceplate, duller toward the bottom, with four slotted tamper screws.
  - **The readout:** a red seven-segment display of four digits behind dark glass, slanted as old LED readouts were,
    with every unlit segment faintly there.
  - **The lamps:** three, labelled PWR (amber), LOCK (red) and OPEN (green).
  - **The keys:** twelve square stainless keys on rubber seats: 1-9, `*` (CLEAR), 0 and `#` (ENTER). The numerals are
    engraved and paint-filled. Each key is grimed its own way, the lower rows dirtier.
  - **The wiring:** a conduit out of the bottom through a gland nut, held by two straps, down to a junction box on
    the wall.
  - **The weathering:** rust running from the bottom flange, and the hood's drip line down the face.
- **Dead until the fire.** Before the cabin's been seen burning (Act 7) the pad is dark: "A keypad. Dead." When Act 7
  comes, the amber and red lamps light. The vault door itself only ever says it's locked, and from Act 7 on, "Locked.
  The keypad's to the right."
- **Using it, in the world.**
  - **Leaning in.** E on it (A on a pad) eases the view in over about half a second, close and square on to the pad,
    with a camera of its own. Back out takes 0.4 s. Nothing pauses.
  - **The highlighted key.** The view's centre (the crosshair) sits on the highlighted key, and the key carries the
    dim focus rim. The arrows, WASD or the D-pad move it, and the view slides gently after it. E, Space or A presses it.
  - **The keyboard.** The number keys and the numpad press their key straight away. Backspace (X on a pad) takes the
    last digit back, `*` clears, Enter or `#` tries the code, and Esc or B steps back.
  - **The mouse.** The cursor never comes up and nothing can be clicked. Mouse motion and buttons are swallowed while
    the pad is up, and the sticks are ignored.
  - **The hint.** The first time in a game, one line under the picture gives the keys, or the pad's buttons if a pad
    was used last.
- **The hands.** While the pad is up the hands are held (`PlayerHands.Hold`), and the player's input is modal. So 1-9
  don't switch the right hand, D-pad down doesn't toggle the blacklight and the wheel does nothing. All are released
  on the way out.
- **Each press** sinks the key 2.5 mm and lets it spring back. It clicks, from eight takes, never one of the last three
  again. The digit comes up on the readout, and a dash blinks where the next one goes (four at most).
- **A wrong code** gives a rasping three-pulse buzz (three takes, never the same twice running). The red lamp flashes
  three times with the digits, then the readout clears.
- **The right code (4729)** gives two rising tones, and the lock's solenoid clacks. The green lamp lights and the
  readout says `OPEn`, then the view eases back.
  - **The bunker's unlock is unchanged:** the `BunkerUnlocked` flag is saved and the door grinds open with its groan,
    called at the moment the code is right, as the dial's was.
  - **Entering the bunker:** checkpoint 7, the admission and its retry are untouched.
  - **On a Continue** with the door open, the pad shows green and isn't usable.
- **From memory, as before.** The readout is empty every time. There's no tracker, and the blacklight nudges at the door
  and on the trees are unchanged. The station card still says "four numbers, marked on the way in (UV). In order."

## Removed
- `UI/CodeLockOverlay.cs` (and its node in `hud.tscn`), with no other users.
- The drum lock on the door's face.
- `Bunker.OnDialUsed`.

## Audio (`tools/AudioGen/Keypad.cs`)
- **`keypad_press_01`-`08`:** a worn key's dome snapping, the cast box's knock, grit under the cap and the pad's piezo
  chirp (one pitch, as on a real pad; the click under it differs every take).
- **`keypad_accept_01`:** two rising piezo tones and the solenoid's clack.
- **`keypad_deny_01`-`03`:** a low, rasping square buzz in three pulses.
- **Captions:** "a beep, and the lock clacks" and "a buzz".

## Tests
- **Playtest beat 8.** It now:
  - walks to the keypad and checks it's awake;
  - leans in and checks the close view, the mouse mode untouched and the input modal, with the readout empty;
  - presses up and E for "2", then Backspace;
  - keys a wrong code on the numpad: the buzz state, still shut, then cleared;
  - keys 4729 on the top-row number keys and checks the item in hand doesn't change;
  - opens with numpad Enter: saved, green, and the view and control come back.
  - Screenshots: `keypad`, `keypad_close`, `keypad_blacklight`, `keypad_code`.
- **StoryTest (Act 8).** It now keys a wrong code with E and the arrows (5555 then `#`) and then the remembered code
  key by key, as on a pad.
- **Results:**
  - Playtest beats 7, 8 and 8c pass. The last runs were 8 alone, 37/37.
  - `--story-from=8 --story-to=8`: 50/50.
  - `dotnet build`: 0 errors.
