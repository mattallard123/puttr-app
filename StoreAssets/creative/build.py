#!/usr/bin/env python3
"""build.py <out dir> -> header-3840x1646.png and search-3840x2560.png (iOS 27 creative assets) from stills/ (Apple TV takes)."""
import sys, subprocess, urllib.parse, pathlib, tempfile
from PIL import Image
out = pathlib.Path(sys.argv[1]); here = pathlib.Path(__file__).resolve().parent
s = lambda n: str(here / 'stills' / f'{n}.png')
H = lambda n: str(here / 'stills/h' / f'{n}.png')
HEADER = [H('spotkick'), s('x-session'), H('lanes'), H('majorleague'), H('onfire'), s('x-pqs'), H('tracks')]
SEARCH = [(s('tracks'), 'Tracks'), (s('x-coach'), 'AI Coach'), (s('x-session'), 'Daily Sessions'), (s('majorleague'), 'Major League'), (s('x-stats'), 'Stats')]
def render(c, W, H, imgs, names, dst):
    url = 'file://' + str(here / 'creative.html') + '?' + urllib.parse.urlencode({'c': c, 'imgs': '|'.join('file://' + i for i in imgs), 'names': '|'.join(names)})
    tmp = tempfile.mktemp(suffix='.png')
    subprocess.run(['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '--headless=new', '--disable-gpu', '--hide-scrollbars',
        '--allow-file-access-from-files', f'--window-size={W},{H}', '--force-device-scale-factor=2', f'--screenshot={tmp}',
        '--virtual-time-budget=4000', url], capture_output=True, timeout=180)
    im = Image.open(tmp).convert('RGB')
    assert im.size == (W * 2, H * 2), im.size
    out.mkdir(parents=True, exist_ok=True); im.save(dst); print(dst, im.size)
render('header', 1920, 823, HEADER, [], out / 'header-3840x1646.png')
render('search', 1920, 1280, [i for i, _ in SEARCH], [n for _, n in SEARCH], out / 'search-3840x2560.png')
