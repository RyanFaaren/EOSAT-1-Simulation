#!/usr/bin/env python3
"""Builds the 8K Earth maps (8192 x 4096) from satellite imagery.

  earth_day.png    the day side (land, sea and ice; no clouds),
                   alpha = open water (the planet shader uses it for sun glint)
  earth_night.png  city lights only, on black

Inputs: Solar System Scope's Earth textures (CC BY 4.0, based on NASA imagery),
https://www.solarsystemscope.com/textures/ - equirectangular, longitude -180 at the left,
north at the top:
  8k_earth_daymap.jpg, 8k_earth_specular_map.jpg, 8k_earth_nightmap.jpg

  python3 make_earth_maps.py <folder with the three inputs> [output folder]

Needs Pillow and numpy. make_textures.py still writes the old drawn maps; run this after
it if you regenerate everything.
"""
import os
import sys

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
SIZE = (8192, 4096)


def load(folder, name, mode):
    im = Image.open(os.path.join(folder, name)).convert(mode)
    return im if im.size == SIZE else im.resize(SIZE, Image.LANCZOS)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = sys.argv[1]
    here = os.path.dirname(os.path.abspath(__file__))
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "..", "Assets", "EOSAT1", "Textures")

    day = load(src, "8k_earth_daymap.jpg", "RGB")
    day.putalpha(load(src, "8k_earth_specular_map.jpg", "L"))      # white = water
    day.save(os.path.join(out, "earth_day.png"), optimize=True)

    # The night picture has a faint dark blue Earth under the lights; take that floor off
    # so only the lights are left (the shader adds this map on the night side).
    night = np.asarray(load(src, "8k_earth_nightmap.jpg", "RGB")).astype(np.float32)
    floor = np.array([18.0, 20.0, 34.0], dtype=np.float32)
    lights = np.clip((night - floor) / (255.0 - floor), 0.0, 1.0) * 255.0
    Image.fromarray(lights.round().astype(np.uint8), "RGB").save(os.path.join(out, "earth_night.png"), optimize=True)
    print("wrote earth_day.png and earth_night.png to", os.path.normpath(out))


if __name__ == "__main__":
    main()
