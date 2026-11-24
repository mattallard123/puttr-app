#!/usr/bin/env python3
"""text.py <W> <H> <out.png> key=value...   a transparent reel text card (text.html), W x H pixels.
keys: h k a size c sub (see text.html)."""
import sys, subprocess, urllib.parse, pathlib, tempfile
from PIL import Image
W, H, out = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3]
q = dict(a.split('=', 1) for a in sys.argv[4:])
here = pathlib.Path(__file__).resolve().parent
url = 'file://' + str(here / 'text.html') + '?' + urllib.parse.urlencode(q)
tmp = tempfile.mktemp(suffix='.png')
subprocess.run(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu', '--hide-scrollbars',
    '--allow-file-access-from-files', '--default-background-color=00000000', f'--window-size={W},{H}', '--force-device-scale-factor=1',
    f'--screenshot={tmp}', '--virtual-time-budget=3000', url], capture_output=True, timeout=120)
im = Image.open(tmp).convert('RGBA')
if im.size != (W, H): im = im.crop((0, 0, W, H))
pathlib.Path(out).parent.mkdir(parents=True, exist_ok=True)
im.save(out)
print(out, im.size)
