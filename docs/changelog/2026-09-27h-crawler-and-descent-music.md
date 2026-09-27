# 2026-09-27h — Act 14: the descent's music, and the crawler

Commit: this entry's commit on `main`.

The owner:
- their own music for the stairwell in Act 14, from when the player starts down;
- a new monster, to be met again later in the church's crypt, that "will slowly be following us
  down the stairs making us not want to look back and just advance as far as we can running down.
  It speeds up and slows down so the player can't get too far away … a humanoid … crawling very
  unnaturally behind you on all 4 appendages" (the owner's reference: a pale, starved figure in a
  backbend crouch, limbs splayed);
- its voice from a breath sample (free, from Freesound via Pixabay), modulated to be the game's own;
- its four footfalls sounding on the metal stairs.

## The music (`assets/audio/music/stairwell_descent.mp3`)
- The owner's track (4:12) fades in as the player starts down from the top landing.
- It plays under the shaft's drone and hum, and fades out at the edge of the fallen flight.
- Once only: not on a Continue from Act 14's end.

## The crawler (`Crawler`, `Stairwell.Crawler.cs`)
- **The body:** procedural, like the rest of the game's creatures.
  - A narrow, starved torso: ribs showing, the spine a ridge of knuckles.
  - The pelvis low, the neck craning down, the long skull hanging below the shoulders, its jaw open
    and its eye sockets dark.
  - Pale grey, mottled rotting skin (`rot_skin`).
  - Four long, thin limbs: two-bone IK from the shoulders and hips, the elbows and knees thrown up and
    out above the back (the reference's bridge).
  - Long hands and feet with splayed fingers.
- **The gait:** each hand and foot is planted on the stairs themselves (a ray down) and stays until the
  body has moved past it. Then it snatches forward in one quick, arcing step: one limb at a time, in an
  uneven order (left hand, right foot, right hand, left foot). The head snaps to new angles and holds
  them, never turning smoothly; the body heaves with its breath. Hurrying makes its steps quicker and
  its breath faster.
- **Its voice (`crawler_breath_01..03`):** the owner's breath sample, made its own:
  - pitched down to 0.8 and warbled with vibrato, with a metallic slap-back echo (the shaft);
  - reversed and pitched to 0.72 with a tremolo, as a wet, backward intake;
  - sped up to 1.18, narrowed and warbled, as a panting.
- **Its footfalls (`crawler_step_01..08`, AudioGen):** a wet slap of palm on steel and nail-tips on
  the tread, the tread ringing (a stamped plate's inharmonic modes, quickly damped), and a small rattle
  of the tread in its frame. Its feet land harder than its hands, and each limb's tread rings at its
  own pitch.
- **The hunt:**
  - A turn after the lantern's flame dies (a quarter of the way down), it lets itself down onto the
    stairs two turns above them.
  - It hangs back about three and a half flights, in sight across the well if they look up, drifting
    nearer and further. Every 7–14 s it surges to just over a flight behind, then falls back.
  - However fast they go, it's never more than about six flights behind: it closes at up to 5.5
    flights a second.
  - If they stand still for more than 2.5 s, it comes on.
- **Running is the escape now:** the shaft's old trick (a sprint quietly taken back up to the top) is
  off while it hunts. The trick still teaches "don't run" in the first quarter, before it comes.
- **If it reaches them:**
  - the view turns to it, it comes up the last of the way, a gentle shake, black;
  - they come to three turns further up, with it somewhere above again;
  - not a death: the descent is long, and the stairwell's punishment has always been more stairs.
- **At the fallen flight** it stops on the stairs above and watches. It is gone when they jump.
- **Later:** it is built to be reused in the church's crypt (next act).

## Tests
- Acts 13–14: 90/90, with the new checks:
  - the descent has its music;
  - after the flame died, the crawler came down the stairs after them;
  - it kept to its distance: 1.1 to 8.1 flights behind at a walk, never caught, 868 steps planted;
  - at the edge it has stopped on the stairs above, watching.
- Its voice plays one breath at a time. A first version started a new breath every few seconds over
  clips 9–14 s long, and stacked up to seven.
- In the tests it never takes the player: they pause for screenshots, and it would.
- The creature preview (`creature_preview.tscn`) now stands it up in the light and photographs it
  from three sides (`test-output/creatures/crawler_*`).
