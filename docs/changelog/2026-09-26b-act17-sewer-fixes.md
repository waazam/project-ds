# 2026-09-26b — Act 17 fixes: the grey-washed cistern, floating vaults, and the interact highlight

Commit: this pass's single commit on `main` (see `git log` for the hash).

From the owner's playtest of the demo build: in the sewer's cistern, "clipping and textures not
loading". Pillars, vaults and floor went a flat pale grey near the hole.

## Bug Fixes
- **The grey wash (the "textures not loading")**: aiming at something you can use lights it up with
  a pale highlight. When an interactable had no highlight root, the highlight went on every mesh
  under its parent. The hole's "Drop down into the hole?" belongs to the whole sewer, so aiming at
  the hole painted the highlight over the entire cistern. Every wall, pillar, vault and the water
  went flat pale grey, and even the hole looked grey. This is fixed at the source (`Interactable`):
  with no highlight root, only small meshes (6 m or less) right by the interactable light up, never
  the room round it. The same bug would have lit up the whole library (the sheet, the puzzle box)
  and the round room (the webs). The hole itself doesn't highlight at all now, so it stays pitch
  black while it asks.
- **Floating vaults**: the barrel vaults met over each row of pillars with nothing under them
  between the pillars, so their thin edges hung in the air. There are now brick arches along every
  row of pillars, pillar to pillar and to the end walls, each with a shallow curved underside
  rising from the capitals. The vaults rest on them.
- **The smoke out of the hole** is now lit by the light over the hole (it glowed on its own
  before). It is thinner, and fades out close to the eye, so standing in it at the hole's edge no
  longer fogs the view.

## Tests
- The Act 17 test now tours the cistern on camera: a pillar, a vault, the water, the east wall, the
  far pillars and vault, and the room seen from right at the hole's edge in its smoke.
- A new check: aiming at the hole leaves the cistern and platform without a highlight.
- Results:
  - `--story-from=17 --story-to=17`: 13/13.
  - Full `--autotest` (the whole game, start to finish): 465/465.
  - `--continue-test`: 642/645. The same three failures predate this work.
- The Windows demo build was re-exported with these fixes.
