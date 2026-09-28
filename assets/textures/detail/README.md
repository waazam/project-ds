# Detail maps (micro-surface)

Small tiling maps laid over the game's surfaces by `DetailKit` (see `scripts/world/DetailKit.cs`):
- an albedo multiplier: only the fine grain kept, near white, with the game's own grime baked in at
  load;
- a normal map for a faint relief.

They are kept subtle (256 px, soft, shallow) for the PS2 look.

Made by `tools/Textures/make_detail.py` from these Poly Haven textures (https://polyhaven.com), all
**CC0** (public domain; no attribution required, credited here with thanks):

| kind | source |
| --- | --- |
| wood | Fine Grained Wood (`fine_grained_wood`) |
| plaster | Grey Plaster (`grey_plaster`) |
| concrete | Concrete (`concrete`) |
| metal | Rusty Metal 02 (`rusty_metal_02`) |
| fabric | Denim Fabric (`denim_fabric`) |
| leather | Brown Leather (`brown_leather`) |
| stone | Dark Rock (`dark_rock`) |
| marble | Marble 01 (`marble_01`) |
| bark | Bark Brown 02 (`bark_brown_02`) |
| foliage | Leafy Grass (`leafy_grass`) |
| ground | Forest Leaves 02 (`forest_leaves_02`) |
| grime | Dirty Concrete (`dirty_concrete`) |

To remake them, put each source's 1k colour (`<id>_col.jpg`) and OpenGL normal (`<id>_nor.jpg`) JPGs
in `build/polyhaven/`, then run `python tools/Textures/make_detail.py`.
