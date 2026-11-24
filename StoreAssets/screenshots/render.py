#!/usr/bin/env python3
"""render.py <W> <H> <scale> <out.png> key=value...   (keys: img k h s a c pos hs)"""
import sys, subprocess, urllib.parse, pathlib, tempfile
from PIL import Image
W, H, scale, out = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3], sys.argv[4]
q = dict(a.split('=', 1) for a in sys.argv[5:])
here = pathlib.Path(__file__).resolve().parent
url = 'file://' + str(here / 'shot.html') + '?' + urllib.parse.urlencode(q)
tmp = tempfile.mktemp(suffix='.png')
subprocess.run(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu', '--hide-scrollbars',
    '--allow-file-access-from-files', f'--window-size={W},{H}', f'--force-device-scale-factor={scale}', f'--screenshot={tmp}',
    '--virtual-time-budget=3000', url], capture_output=True, timeout=120)
im = Image.open(tmp).convert('RGB')
pathlib.Path(out).parent.mkdir(parents=True, exist_ok=True)
im.save(out); print(out, im.size)
