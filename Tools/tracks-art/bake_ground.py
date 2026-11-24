#!/usr/bin/env python3
"""Bakes the Tracks ground and sky textures from Tools/tracks-art/source into the project.

Ground: each scanned albedo is flattened (its slow light-and-dark patches divided out, so it tiles without blotches)
and scaled so its mean is MEAN in linear light on every channel. The shader multiplies the lie's own colour by
detail / MEAN, so the scan gives the blades and grain and the course's palette stays the game's.
Sky: the top half of the panorama (the half under the horizon is a mirror), 4096 x 1024.
"""
import glob, os, sys
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "source")
OUT = os.path.join(HERE, "..", "..", "Assets", "PuttrApp", "Resources", "PuttrApp", "Tracks")
MEAN = 0.4
SIZE = 1024

# name -> (source folder, how much of the scan's own hue variation to keep)
GROUND = {"Mown": "Grass008", "Rough": "Grass004", "Scrub": "Grass003", "Sand": "sand_01"}


def lin(s): return np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4)
def srgb(l): return np.where(l <= 0.0031308, l * 12.92, 1.055 * np.clip(l, 0, None) ** (1 / 2.4) - 0.055)


def find(folder, *names):
    for n in names:
        hit = glob.glob(os.path.join(SRC, "ground", folder, n))
        if hit: return hit[0]
    raise SystemExit("missing %s in %s" % (names, folder))


def wrap_blur(a, radius):
    # Gaussian blur that wraps at the edges (the texture tiles), done in the frequency domain.
    h, w = a.shape[:2]
    fy, fx = np.fft.fftfreq(h)[:, None], np.fft.fftfreq(w)[None, :]
    k = np.exp(-2 * (np.pi * radius) ** 2 * (fx * fx + fy * fy))
    return np.stack([np.real(np.fft.ifft2(np.fft.fft2(a[:, :, c]) * k)) for c in range(a.shape[2])], axis=2)


def ground():
    for name, folder in GROUND.items():
        col = Image.open(find(folder, "*_Color.jpg", "Diffuse.jpg")).convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
        a = lin(np.asarray(col, dtype=np.float32) / 255.0)
        a = a / np.maximum(wrap_blur(a, SIZE / 10), 1e-4)       # flatten the blotches
        a = a / a.reshape(-1, 3).mean(axis=0) * MEAN             # mean MEAN on every channel
        Image.fromarray((np.clip(srgb(a), 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "Ground%s.png" % name))
        nor = Image.open(find(folder, "*_NormalGL.jpg", "nor_gl.jpg")).convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
        nor.save(os.path.join(OUT, "Ground%sNormal.png" % name))
        print("ground", name, "<-", folder)


def sky():
    path = glob.glob(os.path.join(SRC, "sky", "*.jpg"))[0]
    im = Image.open(path).convert("RGB")
    w, h = im.size
    top = im.crop((0, 0, w, h // 2)).resize((4096, 1024), Image.LANCZOS)
    top.save(os.path.join(OUT, "Sky.jpg"), quality=93)
    a = np.asarray(top, dtype=np.float32) / 255.0
    lum = a.mean(axis=2)
    # The sun: the centre of the brightest patch (blurred, so a white cloud edge does not win).
    b = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(24)), dtype=np.float32)
    y, x = np.unravel_index(np.argmax(b), b.shape)
    print("sky sun u=%.4f elevation=%.1f deg" % (x / 4096.0, 90.0 * (1 - y / 1024.0)))
    def band(y0, y1): return tuple(round(float(v), 3) for v in lin(a[y0:y1]).reshape(-1, 3).mean(axis=0))
    print("sky linear colours: horizon", band(1000, 1024), "low", band(820, 900), "mid", band(400, 600), "zenith", band(0, 120), "all", band(0, 1024))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    want = sys.argv[1:] or ["ground", "sky"]
    if "ground" in want: ground()
    if "sky" in want: sky()
