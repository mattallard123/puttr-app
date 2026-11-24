#!/usr/bin/env python3
"""tv.py: the TV icons, from the app icon's art (art.html, class icon solo).

  python3 docs/appicon/tv.py

Writes Assets/PuttrApp/TvIcons/:
  Apple TV app icon (layered): Front-WxH.png (the glowing mark, see-through) over Back-WxH.png (the green ground),
      400x240 and 800x480 (home screen), 1280x768 (App Store)
  Apple TV Top Shelf: Shelf-1920x720, Shelf-3840x1440, Shelf-2320x720, Shelf-4640x1440 (ground + PUTTR wordmark)
  Android TV banner: Banner-320x180.png (ground + wordmark): Unity writes it as @drawable/app_banner
TvBuilds.cs (Assets/PuttrApp/Editor) puts them in the tvOS icon slots and the Android banner slot.
"""
import pathlib, subprocess, sys
from PIL import Image

here = pathlib.Path(__file__).resolve().parent
root = here.parent.parent
out = root / 'Assets/PuttrApp/TvIcons'
out.mkdir(parents=True, exist_ok=True)
render = [sys.executable, str(here / 'render.py')]
wordmark = Image.open(root / 'Assets/PuttrArcade/Resources/PuttrArcade/LogoHeader.png').convert('RGBA')


def art(w, h, path, mode, c):
    subprocess.run(render + [str(w), str(h), str(path), mode, 'c=' + c], check=True)


def wide(w, h, path, share):
    """The ground with the mark-and-PUTTR wordmark across the middle, `share` of the height tall."""
    ground = out / ('_ground-%dx%d.png' % (w, h))
    art(w, h, ground, 'rgb', 'icon nofront')
    im = Image.open(ground).convert('RGBA')
    lh = int(h * share)
    lw = int(lh * wordmark.width / wordmark.height)
    im.alpha_composite(wordmark.resize((lw, lh), Image.LANCZOS), ((w - lw) // 2, (h - lh) // 2))
    im.convert('RGB').save(path)
    ground.unlink()


for w, h in [(400, 240), (800, 480), (1280, 768)]:
    art(w, h, out / ('Front-%dx%d.png' % (w, h)), 'rgba', 'icon solo noback')
    art(w, h, out / ('Back-%dx%d.png' % (w, h)), 'rgb', 'icon nofront')
for w, h in [(1920, 720), (3840, 1440), (2320, 720), (4640, 1440)]:
    wide(w, h, out / ('Shelf-%dx%d.png' % (w, h)), 0.26)
wide(320, 180, out / 'Banner-320x180.png', 0.34)
print('TV icons in', out)
