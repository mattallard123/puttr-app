#!/usr/bin/env python3
"""sheet.py <take.mov> <out.jpg> [every seconds=1]: frames labelled with their time, 8 across."""
import sys, subprocess, tempfile, pathlib
from PIL import Image, ImageDraw, ImageFont
src, out = sys.argv[1], sys.argv[2]
every = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
d = pathlib.Path(tempfile.mkdtemp())
subprocess.run(['ffmpeg', '-v', 'error', '-i', src, '-vf', f'fps={1/every},scale=320:-2', str(d / 'f%04d.jpg')], check=True)
frames = sorted(d.glob('f*.jpg'))
font = ImageFont.truetype('/Users/matthewallard/puttr/puttr-app-2/StoreAssets/screenshots/Montserrat-Bold.ttf', 22)
w, h = Image.open(frames[0]).size
cols = 8; rows = (len(frames) + cols - 1) // cols
sheet = Image.new('RGB', (w * cols, h * rows))
for i, f in enumerate(frames):
    im = Image.open(f); dr = ImageDraw.Draw(im)
    t = f'{i * every:.0f}s'
    dr.rectangle((0, 0, 70, 30), fill=(0, 0, 0)); dr.text((6, 3), t, font=font, fill=(255, 220, 0))
    sheet.paste(im, ((i % cols) * w, (i // cols) * h))
sheet.save(out, quality=82)
print(out, len(frames))
