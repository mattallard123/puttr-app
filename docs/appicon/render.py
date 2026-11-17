#!/usr/bin/env python3
"""render.py <W> <H> <out.png> <rgb|rgba> key=value...   renders art.html (keys: c imgs tag)"""
import sys, subprocess, urllib.parse, pathlib, tempfile
from PIL import Image
W, H, out, mode = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3], sys.argv[4]
q = dict(a.split('=', 1) for a in sys.argv[5:])
here = pathlib.Path(__file__).resolve().parent
# Chrome headless crops windows narrower than ~500px: render big, then scale down.
k = 1
while W * k < 1000: k *= 2
tmp = tempfile.mktemp(suffix='.png')
subprocess.run(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu', '--hide-scrollbars',
    '--default-background-color=00000000', '--allow-file-access-from-files', f'--window-size={W*k},{H*k}', '--force-device-scale-factor=1',
    f'--screenshot={tmp}', '--virtual-time-budget=3000', 'file://' + str(here / q.pop('page', 'art.html')) + '?' + urllib.parse.urlencode(q)], capture_output=True, timeout=120)
im = Image.open(tmp)
if k > 1: im = im.resize((W, H), Image.LANCZOS)
if mode == 'rgb':
    bg = Image.new('RGB', im.size, (0, 0, 0)); bg.paste(im, mask=im.convert('RGBA').split()[3]); im = bg
else: im = im.convert('RGBA')
pathlib.Path(out).parent.mkdir(parents=True, exist_ok=True); im.save(out); print(out, im.size, im.mode)
