# 2026-09-26f — The visual pass: texel density, filtering, supersampling, shadows, contact shadows

Commit: this pass's single commit on `main` (see `git log` for the hash).

The owner: raise the global texel density target, lift the texture resolution caps, adjust the
global LOD and mip bias, and make the game pop visually.

## Texel density (`TexelBoost`)
- Every texture the game generates was written at a small native size, 16 to 128 px. Each is now
  raised to a target of about 384 texels on its long side, at most 4x and never above 512 (the new
  cap). Every material factory goes through it: forest, buildings, props, stairs, items, station,
  stairwell, sewer, pit and bunker textures.
  - **Tile-safe bicubic upscale**: the edges wrap, so repeating textures stay seamless.
  - **A light unsharp mask**: each original texel keeps its edge instead of melting into the next.
  - **Real detail at the new density**: fine three-octave grain (pores, fibres, grit), stronger where
    the surface is already busy. A plank gets grain; a flat coat of paint stays calm.
  - **Cut-out alpha** (foliage, fringes) re-thresholded softly, so the bigger edges are crisp.
  - Colours keep their palette: the grain moves luminance by a few percent, never hue.
- Data textures read by position (masks, lookup maps, the UV ink) are left at their native size.

## Engine config (`project.godot`)
- **Anisotropic filtering 16x** (was off): ground, floors and walls at a glancing angle stay sharp
  instead of smearing.
- **Mipmap bias -0.5** (was 0): the finer mip is chosen sooner, so textures hold their detail further
  off.
- **3D rendered at 1.5x and downsampled** (bilinear supersampling): smoother edges, more stable thin
  things (branches, rails, wires) and cleaner texture sampling. It still resolves into the 640x360 PS2
  pixel grid, so the look and the UI are unchanged.
- **Shadows**: the directional map and the positional atlas are 4096 (was 2048 for the sun, 4096
  default for lamps), and soft shadows are on at medium quality (were off).
- **Mesh LOD threshold 0.5 px** (was 1): higher-detail meshes are kept further away.
- A first try at 2x supersampling with 8192 shadow maps and high soft shadows cost too much
  (34 fps average in the test walk). The settings above hold about 77 fps in the forest and about
  102 fps at the lake and station.

## Contact shadows (SSAO)
- Screen-space ambient occlusion in the environment: every corner, the roots, steps, planks and
  props sit in their own shadow instead of floating on the ground (radius 1.2 m, intensity 2.2,
  mostly on ambient light).

## Tests
- `--story-from=3 --story-to=4`: 39/39, 77 fps average walking.
- `--story-from=12 --story-to=13`: 99/99, 102 fps average walking.
- Results:
  - Full `--autotest` (the whole game, start to finish): 522/523, at 95 fps average walking over the
    whole game. The one failure was the Act 1 album's "Tab closes it" step, flaking under load the
    same way its "opens it" step did; it now waits and retries the same way.
  - `--continue-test`: 642/645. The same three failures predate this work.
