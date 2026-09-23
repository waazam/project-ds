# 2026-09-22d — Sound pass: footsteps, rain, omen bird

Commit: `0ad8ffb`

A focused audio-feel pass from playtesting feedback: footsteps felt like
jogging, rain sounded like popcorn, and the fourth (omen) bird needed to
land harder as a scare.

## Audio Fixes
- **Footsteps**: cadence was tuned for a jogger (roughly 3.75-4.7
  steps/second at normal walk/run speed). Widened the per-step stride
  distance (`PlayerFootsteps.WalkStride` 0.72→1.3 m, `RunStride` 1.15→1.9 m)
  so the same movement speed now produces a natural ~2.1-2.8 steps/second,
  without touching actual player speed.
- **Rain**: the procedural generator's (`tools/AudioGen`) drop layers sat
  in a bright, resonant 1.2-4.5 kHz band that read as popcorn popping
  rather than water landing on dirt. Moved all three distance layers down
  to a duller, wider 450-1600 Hz band, softened their attacks, and blended
  in more of the soft low "body" (thump) component underneath each drop.
  `rain_loop.wav` regenerated; its own verifier now measures a spectral
  centroid of 960 Hz with ~98% of the energy sitting between 100 Hz-3 kHz.
- **Omen bird's call**: the raven-style call generator (`Forest.Raven`) had
  its fundamental dropped an octave (240-320 Hz → 120-165 Hz) and its
  "gurgle" modulation slowed from a buzzy 22-34 Hz rattle to a 4-7 Hz throb,
  with the vocal formants and lowpass shifted down to match. `raven_01.wav`
  / `raven_02.wav` regenerated; centroid dropped from ~700-1400 Hz to
  ~370 Hz. Its playback volume was also raised 12 dB (-8 dB → +4 dB) so it
  stands out from the three ordinary birds.

## Additions
- The omen bird now lunges past the player's head in a fast 0.2 s dive when
  its photo is taken, instead of calmly retreating upward like the other
  three birds — a jumpscare payoff for photographing it.

No story, level design, or systems changes this pass — audio and one small
gameplay reaction (the bird's flight path) only.
