#!/usr/bin/env python3
"""Packs the tree cut-outs (source/cutouts, from bake_trees.py) into the atlas the game draws trees from.

Writes to Assets/PuttrApp/Resources/PuttrApp/Tracks:
  Trees.png        4 x 4 cells of 512 px: eleven trees and bushes from the side, five canopies from above (RGBA)
  TreesNormal.png  the same cells as dome-shaped normals (rgb = n * 0.5 + 0.5: x right, y up, z out of the card)
  Trees.txt        one line a tree: name lie sideCell topCell heightShare widthShare topShare minYards maxYards
                   (shares are of the side cell's square; topShare is the canopy card's square over that square)
A model that holds several plants was rendered a plant at a time (bake.sh): pine_tree_01_a, _b, ...
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
CUT = os.path.join(HERE, "source", "cutouts")
OUT = os.path.join(HERE, "..", "..", "Assets", "PuttrApp", "Resources", "PuttrApp", "Tracks")
CELL, GRID = 512, 4

# (name in the atlas, model, which plant in the row, lie, top view it shares, shortest and tallest in yards)
TREES = [
    ("pine_a", "pine_tree_01_a", "wood", "pine", 18, 25),
    ("pine_b", "pine_tree_01_c", "wood", "pine", 16, 22),
    ("fir_a", "fir_tree_01_a", "wood", "fir", 16, 23),
    ("fir_b", "fir_tree_01_b", "wood", "fir", 13, 19),
    ("oak", "jacaranda_tree", "wood", "oak", 12, 17),
    ("olive_a", "island_tree_01", "wood", "olive", 7, 11),
    ("olive_b", "island_tree_02", "wood", "olive", 6, 10),
    ("ash", "tree_small_02", "wood", "oak", 8, 12),
    ("bush_a", "searsia_lucida_a", "scrub", "bush", 1.3, 2.2),
    ("bush_b", "searsia_lucida_b", "scrub", "bush", 1.4, 2.4),
    ("bush_c", "searsia_lucida_c", "scrub", "bush", 1.6, 2.8),
]
TOPS = [("pine", "pine_tree_01_a"), ("fir", "fir_tree_01_a"), ("oak", "jacaranda_tree"), ("olive", "island_tree_01"), ("bush", "searsia_lucida_c")]


def load(model, view):
    return np.asarray(Image.open(os.path.join(CUT, "%s_%s.png" % (model, view))).convert("RGBA"), dtype=np.float32) / 255.0


def runs(mask, gap):
    """Stretches of True in a 1D mask, joined across breaks shorter than gap."""
    out, start, last = [], None, None
    for i, on in enumerate(mask):
        if not on: continue
        if start is None: start = i
        elif i - last > gap: out.append((start, last + 1)); start = i
        last = i
    if start is not None: out.append((start, last + 1))
    return out


def extent(model, view):
    """The x range of what is drawn in a view."""
    xs = np.where(load(model, view)[:, :, 3].max(axis=0) > 0.05)[0]
    return xs[0], xs[-1] + 1


def square(img, cx, bottom, size):
    """A square cut of an RGBA float image: size px a side, centred on cx, its bottom edge at `bottom`; clear outside."""
    s = int(round(size))
    out = np.zeros((s, s, 4), np.float32)
    x0, y0 = int(round(cx - s / 2)), int(round(bottom - s))
    sx0, sy0 = max(0, x0), max(0, y0)
    sx1, sy1 = min(img.shape[1], x0 + s), min(img.shape[0], y0 + s)
    out[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = img[sy0:sy1, sx0:sx1]
    return out


def blur(a, radius):
    h, w = a.shape[:2]
    fy, fx = np.fft.fftfreq(h)[:, None], np.fft.fftfreq(w)[None, :]
    k = np.exp(-2 * (np.pi * radius) ** 2 * (fx * fx + fy * fy))
    if a.ndim == 2: return np.real(np.fft.ifft2(np.fft.fft2(a) * k))
    return np.stack([np.real(np.fft.ifft2(np.fft.fft2(a[:, :, c]) * k)) for c in range(a.shape[2])], axis=2)


def cell(img):
    """To a 512 px cell: scaled down with colour weighted by cover, and the leaves' colour bled out into the clear
    pixels round them, so neither the smaller mips nor filtering pull a pale fringe in."""
    pm = img.copy(); pm[:, :, :3] *= pm[:, :, 3:4]
    small = np.stack([np.asarray(Image.fromarray(pm[:, :, c], "F").resize((CELL, CELL), Image.BOX if img.shape[0] >= CELL else Image.BICUBIC)) for c in range(4)], axis=2)
    a = np.clip(small[:, :, 3], 0, 1)
    rgb = small[:, :, :3] / np.maximum(a[:, :, None], 1e-4)
    known = (a > 0.02).astype(np.float32)
    fill = rgb * known[:, :, None]
    for r in (2, 6, 18, 60):
        wsum = blur(known, r); csum = blur(fill, r)
        spread = csum / np.maximum(wsum[:, :, None], 1e-4)
        ok = wsum > 0.004
        take = ok & (known < 0.5)
        fill[take] = spread[take]; known = np.where(take, 1.0, known).astype(np.float32)
    mean = rgb[a > 0.5].mean(axis=0) if (a > 0.5).any() else np.array([0.2, 0.3, 0.15])
    fill[known < 0.5] = mean
    return np.dstack([np.clip(fill, 0, 1), a])


def dome(rgba):
    """Normals for a cut-out: a dome over the whole crown (from its outline, blurred wide), finer domes over its
    clumps, and the leaves' own light and shade as the smallest relief."""
    a = rgba[:, :, 3]
    lum = rgba[:, :, :3].mean(axis=2) * a
    h = blur(a, CELL / 9) * 2.2 + blur(a, CELL / 28) * 0.7 + (blur(lum, 2) - blur(lum, 10)) * 2.0
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * CELL / 16
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * CELL / 16
    n = np.dstack([-dx, dy, np.ones_like(h)])   # image y runs down, the card's y runs up
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n * 0.5 + 0.5


def main():
    atlas = np.zeros((CELL * GRID, CELL * GRID, 4), np.float32)
    normals = np.full((CELL * GRID, CELL * GRID, 3), 0.5, np.float32); normals[:, :, 2] = 1.0
    lines = []

    def put(index, rgba):
        r, c = divmod(index, GRID)
        atlas[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = rgba
        normals[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = dome(rgba)

    top_cells, top_metres = {}, {}
    for i, (key, model) in enumerate(TOPS):
        img = load(model, "top")
        metres = [float(v) for v in open(os.path.join(CUT, model + ".txt")).read().split()]
        tx0, tx1 = extent(model, "top")
        ys = np.where(img[:, :, 3].max(axis=1) > 0.05)[0]
        y0, y1 = ys[0], ys[-1] + 1
        size = max(tx1 - tx0, y1 - y0) * 1.04
        put(len(TREES) + i, cell(square(img, (tx0 + tx1) / 2, (y0 + y1) / 2 + size / 2, size)))
        top_cells[key] = len(TREES) + i
        top_metres[key] = size * metres[4] / 1024.0

    for i, (name, model, lie, top, lo, hi) in enumerate(TREES):
        img = load(model, "side")
        metres = [float(v) for v in open(os.path.join(CUT, model + ".txt")).read().split()]
        x0, x1 = extent(model, "side")
        part = img[:, x0:x1, 3]
        ys = np.where(part.max(axis=1) > 0.05)[0]
        y0, y1 = ys[0], ys[-1] + 1
        # Stood on the trunk's foot: the middle of what touches the ground.
        footrows = part[max(y0, y1 - max(4, (y1 - y0) // 40)):y1]
        fx = np.where(footrows.max(axis=0) > 0.05)[0]
        foot = x0 + (fx[0] + fx[-1]) / 2.0
        half = max(foot - x0, x1 - foot)
        size = max(y1 - y0, half * 2) * 1.04
        put(i, cell(square(img, foot, y1 + size * 0.01, size)))
        height_m = (y1 - y0) * metres[3] / 1024.0
        top_share = top_metres[top] / (size * metres[3] / 1024.0)
        # A shared canopy is sized by this tree's own spread, not the model it was taken from.
        if (top, model) not in TOPS: top_share = min(1.0, (x1 - x0) / size * 1.1)
        lines.append("%s %s %d %d %.4f %.4f %.4f %s %s" % (name, lie, i, top_cells[top], (y1 - y0) / size, (x1 - x0) / size, top_share, lo, hi))
        print(lines[-1], "(%.1f m)" % height_m)

    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((np.clip(atlas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, "Trees.png"))
    Image.fromarray((np.clip(normals, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, "TreesNormal.png"))
    open(os.path.join(OUT, "Trees.txt"), "w").write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
