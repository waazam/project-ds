"""Act 22's photo subjects, modelled in Blender (the owner, 2026-09-30: the things to photograph on the road were far too
low in detail). Run headless; each model (props_<name>.py, on the helpers in kit.py) is exported to
assets/models/winter/<name>.glb, and previews rendered to build/blender/:

    blender -b --python tools/Blender/winter_props.py [-- name ...]
"""
import importlib, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
NAMES = ["snowman", "snowplow", "antler_tree", "ski_gear", "buried_arm", "frozen_deer", "lodge_sign"]

if __name__ == "__main__":
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else NAMES
    for name in want:
        importlib.import_module("props_" + name).build()
