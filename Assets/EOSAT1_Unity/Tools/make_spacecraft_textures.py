#!/usr/bin/env python3
"""
make_spacecraft_textures.py - textures for the EOS SAT-1 model, drawn to match the
reference render ("wings closed" picture) and the exploded view.

    python3 make_spacecraft_textures.py --out ../Assets/EOSAT1/Textures
    python3 make_spacecraft_textures.py --only foil     # just the gold foil

Writes
  panel_a.png, panel_a_mask.png   the six-row panel (3 strings of 2 rows of cut-corner cells,
                                  white bypass diodes along each string's centre line)
  panel_b.png, panel_b_mask.png   the three-band panel (3 strings of 4 x 16 cells; the bronze
                                  substrate shows as small diamonds between cell corners)
  mli_gold.png, mli_gold_normal.png, mli_gold_mask.png
                                  crinkled gold multi-layer insulation, 2048 px, tiles seamlessly:
                                  the foil's metal colour (no shading painted in), its wrinkles,
                                  and how glossy it is (R = metallic, A = smoothness)

Panel textures cover one whole panel: u runs along the panel's long edge (across the
face of the body), v along the spacecraft's camera axis (top of the image = camera end).
Masks are linear: R = metallic, A = smoothness (Unity's Metallic/Smoothness layout).
"""
import argparse
import math
import os

import numpy as np
from PIL import Image, ImageDraw

RS = Image.Resampling

# panel size in mm (keep in step with eosat1_model.py)
PANEL_W, PANEL_H = 830.0, 790.0
BORDER, BAR = 22.0, 18.0
TEX = 1024
SS = 3                      # supersampling for clean edges

BRONZE = (172, 141, 88)
BRONZE_DARK = (120, 96, 58)
CELL = (12, 15, 24)
DIODE = (226, 222, 210)
HOLE = (40, 32, 22)


class Canvas:
    """Draw in panel millimetres; image row 0 is the camera end (v = 1)."""

    def __init__(self, w_mm, h_mm, px):
        self.w_mm, self.h_mm = w_mm, h_mm
        self.W = self.H = px * SS
        self.sx, self.sy = self.W / w_mm, self.H / h_mm
        self.rgb = Image.new("RGB", (self.W, self.H), BRONZE)
        self.mask = Image.new("RGBA", (self.W, self.H), (220, 0, 0, 140))   # bronze: metal 0.86, smooth 0.55
        self.d = ImageDraw.Draw(self.rgb)
        self.m = ImageDraw.Draw(self.mask)

    def pt(self, x, y):
        return (x * self.sx, y * self.sy)

    def poly(self, pts, fill, mask):
        p = [self.pt(x, y) for x, y in pts]
        self.d.polygon(p, fill=fill)
        self.m.polygon(p, fill=mask)

    def circle(self, x, y, r, fill, mask=None):
        self.d.ellipse([self.pt(x - r, y - r), self.pt(x + r, y + r)], fill=fill)
        if mask is not None:
            self.m.ellipse([self.pt(x - r, y - r), self.pt(x + r, y + r)], fill=mask)

    def finish(self, px):
        rgb = self.rgb.resize((px, px), RS.LANCZOS)
        mask = self.mask.resize((px, px), RS.LANCZOS)
        a = np.asarray(rgb).astype(np.float32)
        # faint brushed grain on the metal, a soft sheen across the cells
        rng = np.random.default_rng(7)
        grain = rng.normal(0, 1, (px, px)).astype(np.float32)
        grain = np.asarray(Image.fromarray(grain, "F").resize((px, px // 16), RS.BILINEAR).resize((px, px), RS.BICUBIC))
        metal = np.asarray(mask)[..., 0:1].astype(np.float32) / 255.0
        a += grain[..., None] * 5.0 * metal
        yy, xx = np.mgrid[0:px, 0:px] / px
        a += (1 - metal) * (6.0 * np.exp(-((xx - 0.35) ** 2 + (yy - 0.3) ** 2) / 0.18))[..., None] * np.array([0.6, 0.8, 1.0])
        return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB"), mask


CELL_MASK = (0, 0, 0, 225)      # cells: not metal, glassy cover
DIODE_MASK = (0, 0, 0, 90)
HOLE_MASK = (0, 0, 0, 40)


def frame_and_bars(c):
    """Bronze frame, two bars between the three strings, screw holes; returns the band rectangles."""
    W, H = c.w_mm, c.h_mm
    usable = H - 2 * BORDER - 2 * BAR
    band_h = usable / 3.0
    bands = []
    y = BORDER
    for i in range(3):
        bands.append((BORDER, y, W - BORDER, y + band_h))
        y += band_h + BAR
    # screw holes along the border and the bars
    step = 46.0
    n = int((W - 2 * 12) // step)
    for k in range(n + 1):
        x = 12 + k * (W - 24) / n
        for yy in (11, H - 11):
            c.circle(x, yy, 2.4, HOLE, HOLE_MASK)
    n = int((H - 2 * 12) // step)
    for k in range(n + 1):
        yy = 12 + k * (H - 24) / n
        for x in (11, W - 11):
            c.circle(x, yy, 2.4, HOLE, HOLE_MASK)
    for i in range(2):
        yb = bands[i][3] + BAR / 2
        for k in range(0, 9):
            x = BORDER + 30 + k * (W - 2 * BORDER - 60) / 8
            c.circle(x, yb, 3.0, HOLE, HOLE_MASK)
    return bands


def panel_a():
    """Six rows: each string has two rows of 17 cells, cut corners and diodes on the centre line."""
    c = Canvas(PANEL_W, PANEL_H, TEX)
    bands = frame_and_bars(c)
    cols = 18
    for (x0, y0, x1, y1) in bands:
        mid = (y0 + y1) / 2
        gap_mid, gap_cell, margin = 7.0, 2.6, 5.0
        pitch = (x1 - x0 - 2 * margin) / cols
        cw = pitch - gap_cell
        for row in (0, 1):
            ry0 = y0 + margin if row == 0 else mid + gap_mid / 2
            ry1 = mid - gap_mid / 2 if row == 0 else y1 - margin
            for k in range(cols):
                cx0 = x0 + margin + k * pitch + gap_cell / 2
                cx1 = cx0 + cw
                cut = min(cw, ry1 - ry0) * 0.28
                if row == 0:     # cut the lower-left corner, towards the string's centre line
                    pts = [(cx0, ry0), (cx1, ry0), (cx1, ry1), (cx0 + cut, ry1), (cx0, ry1 - cut)]
                    tri = [(cx0 + 0.6, ry1 - cut + 1.2), (cx0 + cut - 1.2, ry1 - 0.4), (cx0 + 0.6, ry1 - 0.4)]
                else:            # cut the upper-left corner
                    pts = [(cx0, ry0 + cut), (cx0 + cut, ry0), (cx1, ry0), (cx1, ry1), (cx0, ry1)]
                    tri = [(cx0 + 0.6, ry0 + 0.4), (cx0 + cut - 1.2, ry0 + 0.4), (cx0 + 0.6, ry0 + cut - 1.2)]
                c.poly(pts, CELL, CELL_MASK)
                c.poly(tri, DIODE, DIODE_MASK)
                # thin silver grid fingers across each cell
                for f in range(1, 6):
                    yy = ry0 + (ry1 - ry0) * f / 6
                    c.d.line([c.pt(cx0 + 1.5, yy), c.pt(cx1 - 1.5, yy)], fill=(19, 22, 31), width=max(1, SS // 3))
    return c.finish(TEX)


def panel_b():
    """Three strings of 4 x 16 cells; all four corners cut so the bronze shows as diamonds."""
    c = Canvas(PANEL_W, PANEL_H, TEX)
    bands = frame_and_bars(c)
    cols, rows = 14, 3
    for (x0, y0, x1, y1) in bands:
        margin, gap = 4.0, 2.4
        px = (x1 - x0 - 2 * margin) / cols
        py = (y1 - y0 - 2 * margin) / rows
        for r in range(rows):
            for k in range(cols):
                a0 = x0 + margin + k * px + gap / 2
                a1 = a0 + px - gap
                b0 = y0 + margin + r * py + gap / 2
                b1 = b0 + py - gap
                cut = min(px, py) * 0.17
                pts = [(a0 + cut, b0), (a1 - cut, b0), (a1, b0 + cut), (a1, b1 - cut), (a1 - cut, b1), (a0 + cut, b1),
                       (a0, b1 - cut), (a0, b0 + cut)]
                c.poly(pts, CELL, CELL_MASK)
                for f in range(1, 5):
                    yy = b0 + (b1 - b0) * f / 5
                    c.d.line([c.pt(a0 + 2, yy), c.pt(a1 - 2, yy)], fill=(18, 21, 30), width=max(1, SS // 3))
    return c.finish(TEX)


# ------------------------------------------------------------------ gold foil
# The body's multi-layer insulation, drawn as a metal: the colour map holds only the foil's
# own colour (no shading painted in), the normal map holds every wrinkle, and the mask says
# how glossy each spot is. Unity's lighting and the sky's reflection (SpaceReflections.cs)
# then make it look like metal. One tile covers FOIL_TILE_MM of the body, so a face of
# EOS SAT-1 shows about one tile and no repeats; at 2048 pixels that is 0.44 mm a pixel.
FOIL_TEX = 2048
FOIL_TILE_MM = 900.0


def _freqs(n):
    """Spatial frequency of every FFT bin, in cycles per tile, as (fx, fy, |f|)."""
    f = np.fft.fftfreq(n) * n
    fx, fy = np.meshgrid(f, f)
    return fx, fy, np.hypot(fx, fy)


def _shaped_noise(n, weight, seed):
    """Seamlessly tiling noise whose spectrum is 'weight' (an n x n array), scaled to unit RMS."""
    rng = np.random.default_rng(seed)
    spec = np.fft.fft2(rng.normal(size=(n, n))) * weight
    out = np.real(np.fft.ifft2(spec))
    out -= out.mean()
    return out / (out.std() + 1e-12)


def _smooth01(x, lo, hi):
    t = np.clip((x - lo) / (hi - lo), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _slope_scaled(h, px_mm, rms_slope):
    """Scale a height field (in pixels) so that its RMS slope is rms_slope; returns height in mm."""
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / (2 * px_mm)
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) / (2 * px_mm)
    s = np.sqrt(np.mean(gx ** 2 + gy ** 2)) + 1e-12
    return h * (rms_slope / s)


def mli_gold(n=FOIL_TEX, tile_mm=FOIL_TILE_MM):
    """Crinkled gold foil: colour (sRGB), normal map and metal/smoothness mask, all tiling."""
    px = tile_mm / n                                    # mm per pixel
    fx, fy, f = _freqs(n)
    f_mm = f / tile_mm                                  # cycles per mm
    f_mm[0, 0] = 1e-9

    def band(lo_mm, hi_mm, power=1.0):
        """Smooth band-pass over wavelengths lo..hi mm, with a 1/f^power tilt."""
        lam = 1.0 / f_mm
        w = np.exp(-0.5 * (np.log(lam / np.sqrt(lo_mm * hi_mm)) / (0.5 * np.log(hi_mm / lo_mm) + 1e-9)) ** 2)
        return w / f_mm ** power

    def oriented(lam_mm, along_bw, across_bw, angle, seed, k_dirs=8):
        """Ripples about lam_mm apart whose crests follow the direction field 'angle'."""
        acc = np.zeros((n, n))
        total = np.zeros((n, n))
        for i in range(k_dirs):
            th = np.pi * i / k_dirs
            c, s_ = np.cos(th), np.sin(th)
            k_along = (fx * c + fy * s_) / tile_mm      # across the crests
            k_across = (-fx * s_ + fy * c) / tile_mm    # along the crests
            w = (np.exp(-0.5 * ((np.abs(k_along) - 1 / lam_mm) / (along_bw / lam_mm)) ** 2) *
                 np.exp(-0.5 * (k_across / (across_bw / lam_mm)) ** 2))
            field = _shaped_noise(n, w, seed=seed + i)
            d = np.angle(np.exp(2j * (angle - th))) / 2  # angular distance, period pi
            wt = np.exp(-0.5 * (d / (np.pi / k_dirs * 0.6)) ** 2)
            acc += wt * field
            total += wt
        acc /= total + 1e-9
        return acc / (acc.std() + 1e-9)

    # the way the foil is pulled: wrinkles line up along a direction that drifts over the sheet
    angle = np.pi * (0.5 + 0.5 * np.tanh(1.2 * _shaped_noise(n, band(250, 900, 1.0), seed=21)))
    angle = (angle + 0.35 * _shaped_noise(n, band(80, 300, 1.0), seed=22)) % np.pi

    # 1. the sheet: broad waves, tilted enough to catch both the bright Earth and black space,
    #    which is what makes a foil read as metal
    undulate = _slope_scaled(_shaped_noise(n, band(140, 900, 1.0), seed=11), px, 0.20)

    # 2. soft wrinkles all over: elongated, a few centimetres long
    soft = oriented(38.0, 0.55, 0.30, angle, seed=200)
    soft = _slope_scaled(soft, px, 0.07)

    # 3. puckered patches: short tight ripples gathered in places (as on the reference picture)
    rip = oriented(9.0, 0.45, 0.14, angle, seed=100)
    rip = np.tanh(0.9 * rip)                                         # a little rounder than a sine
    patch_noise = _shaped_noise(n, band(120, 600, 1.0), seed=31)
    patches = _smooth01(patch_noise, 0.7, 1.6)                      # about a fifth of the sheet
    patches *= 0.6 + 0.4 * _smooth01(_shaped_noise(n, band(40, 160, 1.0), seed=32), -0.8, 0.8)
    ripple_h = _slope_scaled(rip, px, 0.16) * patches                # RMS slope 0.16 inside a patch

    # 4. a few long folds: soft creases along the zero lines of mid-sized noise, only in places
    z = _shaped_noise(n, band(120, 500, 1.0), seed=41)
    crease_line = np.exp(-(z / 0.10) ** 2)
    crease_gate = _smooth01(_shaped_noise(n, band(200, 900, 1.0), seed=42), 0.5, 1.4)
    crease_h = _slope_scaled(-crease_line * crease_gate, px, 0.06)  # valleys

    # 5. fine grain, so the foil stays crisp when you put your face up to it
    grain = _slope_scaled(_shaped_noise(n, band(1.2, 4.0, 0.0), seed=51), px, 0.025)

    h = undulate + soft + ripple_h + crease_h + grain                # mm

    # normal map (tangent space, +Y = up the picture = +v, as Unity expects)
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / (2 * px)
    gy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) / (2 * px)          # rows run down the picture
    nrm = np.dstack([-gx, -gy, np.ones_like(h)])
    nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
    normal = np.clip(np.round((nrm * 0.5 + 0.5) * 255), 0, 255).astype(np.uint8)

    # how deep in a fold each spot is (to darken the colour a little in the troughs)
    hs = soft + ripple_h + crease_h
    lap = (np.roll(hs, 1, 0) + np.roll(hs, -1, 0) + np.roll(hs, 1, 1) + np.roll(hs, -1, 1) - 4 * hs) / px ** 2
    cavity = np.clip(lap / (np.percentile(np.abs(lap), 99) + 1e-9), -1, 1)   # + in troughs

    # colour: the foil's own reflectance (its metal colour), warm gold, slightly uneven
    tint = np.tanh(0.9 * _shaped_noise(n, band(200, 900, 1.0), seed=61))
    blotch = _shaped_noise(n, band(30, 120, 1.0), seed=62)
    gold = np.array([0.78, 0.52, 0.15])                              # linear
    amber = np.array([0.72, 0.43, 0.10])
    pale = np.array([0.84, 0.62, 0.24])
    w_amber = np.clip(tint, 0, 1)[..., None] * 0.6
    w_pale = np.clip(-tint, 0, 1)[..., None] * 0.6
    col = gold * (1 - w_amber - w_pale) + amber * w_amber + pale * w_pale
    col *= (1.0 + 0.035 * blotch)[..., None]
    col *= (1.0 - 0.12 * np.clip(cavity, 0, 1) - 0.08 * crease_line * crease_gate)[..., None]
    srgb = np.where(col <= 0.0031308, col * 12.92, 1.055 * np.power(np.clip(col, 0, None), 1 / 2.4) - 0.055)
    colour = np.clip(np.round(srgb * 255), 0, 255).astype(np.uint8)

    # mask (linear): R = metallic, A = smoothness. Mostly metal; a little diffuse keeps the
    # shape readable where the foil mirrors black space. Glossy where the sheet is smooth,
    # duller where it is puckered, dullest in the folds.
    smooth = 0.74 - 0.05 * patches - 0.12 * crease_line * crease_gate + 0.025 * blotch
    smooth -= 0.04 * np.clip(np.abs(cavity), 0, 1)
    smooth = np.clip(smooth, 0.45, 0.85)
    metal = 0.9 - 0.04 * patches
    mask = np.zeros((n, n, 4), np.uint8)
    mask[..., 0] = np.round(metal * 255).astype(np.uint8)
    mask[..., 3] = np.round(smooth * 255).astype(np.uint8)

    return (Image.fromarray(colour, "RGB"), Image.fromarray(normal, "RGB"), Image.fromarray(mask, "RGBA"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "EOSAT1", "Textures"))
    ap.add_argument("--only", choices=("all", "panels", "foil"), default="all",
                    help="write only the solar panel textures or only the gold foil")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    if a.only in ("all", "panels"):
        rgb, mask = panel_a()
        rgb.save(os.path.join(a.out, "panel_a.png"), optimize=True)
        mask.save(os.path.join(a.out, "panel_a_mask.png"), optimize=True)
        rgb, mask = panel_b()
        rgb.save(os.path.join(a.out, "panel_b.png"), optimize=True)
        mask.save(os.path.join(a.out, "panel_b_mask.png"), optimize=True)
    if a.only in ("all", "foil"):
        col, nrm, mask = mli_gold()
        col.save(os.path.join(a.out, "mli_gold.png"), optimize=True)
        nrm.save(os.path.join(a.out, "mli_gold_normal.png"), optimize=True)
        mask.save(os.path.join(a.out, "mli_gold_mask.png"), optimize=True)
    print("spacecraft textures written to", os.path.abspath(a.out))


if __name__ == "__main__":
    main()
