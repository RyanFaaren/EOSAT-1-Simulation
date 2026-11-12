#!/usr/bin/env python3
"""Builds the Earth maps from NASA imagery instead of the hand-drawn coastlines.

  earth_day.png    4096 x 2048  NASA Blue Marble (land, sea floor, ice; no clouds),
                                alpha = open water (the planet shader uses it for sun glint)
  earth_night.png  4096 x 2048  city lights only, on black, from NASA's Earth at Night

Inputs (equirectangular, longitude -180 at the left, north at the top), as found in
github.com/vasturiano/three-globe under example/img (NASA Visible Earth imagery):
  earth-blue-marble.jpg, earth-water.png, earth-night.jpg

  python3 make_earth_from_blue_marble.py <folder with the three inputs> [output folder]

Needs Pillow and numpy. make_textures.py still writes the old drawn maps; run this after
it if you regenerate everything.
"""
import os
import sys

import numpy as np
from PIL import Image

SIZE = (4096, 2048)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = sys.argv[1]
    here = os.path.dirname(os.path.abspath(__file__))
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "..", "Assets", "EOSAT1", "Textures")

    day = Image.open(os.path.join(src, "earth-blue-marble.jpg")).convert("RGB").resize(SIZE, Image.LANCZOS)
    water = Image.open(os.path.join(src, "earth-water.png")).convert("L").resize(SIZE, Image.LANCZOS)
    day.putalpha(water)                               # white = water
    day.save(os.path.join(out, "earth_day.png"), optimize=True)

    # The night picture has a dark blue Earth under the lights: almost no red anywhere
    # (deserts and ice included) except in the lights, so the red channel picks them out.
    night = np.asarray(Image.open(os.path.join(src, "earth-night.jpg")).convert("RGB").resize(SIZE, Image.LANCZOS)).astype(np.float32)
    glow = np.clip((night[..., 0] - 14.0) / 90.0, 0.0, 1.0) ** 0.75
    lights = glow[..., None] * np.array([255.0, 214.0, 150.0], dtype=np.float32)
    Image.fromarray(lights.round().astype(np.uint8), "RGB").save(os.path.join(out, "earth_night.png"), optimize=True)
    print("wrote earth_day.png and earth_night.png to", os.path.normpath(out))


if __name__ == "__main__":
    main()
