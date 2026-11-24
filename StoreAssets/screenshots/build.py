#!/usr/bin/env python3
"""build.py <shots.json> <takes or stills dir> <device> <out dir>

device: appletv (3840x2160, frames from simulator takes: "take" + "t" seconds),
        iphone  (1290x2796, stills: "img" png in the stills dir, the app screen in a panel),
        ipad    (2064x2752, as iphone).
Every screenshot is RGB with no alpha, at the exact App Store size."""
import sys, json, subprocess, pathlib, tempfile
from PIL import Image
shots, src, dev, out = sys.argv[1], pathlib.Path(sys.argv[2]), sys.argv[3], pathlib.Path(sys.argv[4])
here = pathlib.Path(__file__).resolve().parent
size = {'appletv': (1920, 1080, 2), 'iphone': (860, 1864, 1.5), 'ipad': (1032, 1376, 2)}[dev]
W, H, scale = size
tmp = pathlib.Path(tempfile.mkdtemp())
for s in json.load(open(shots)):
    if 'take' in s:
        img = tmp / f"{s['name']}.png"
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-ss', str(s['t']), '-i', str(src / f"{s['take']}.mov"), '-frames:v', '1', str(img)], check=True)
    else:
        img = (src / s['img']).resolve()
    cls = s.get('c', {'appletv': 'tv', 'iphone': 'phone', 'ipad': 'tablet'}[dev])
    args = [f'img={img}', f"h={s['h']}", f"k={s.get('k', '')}", f"s={s.get('s', '')}", f"a={s.get('a', '#7ad446')}", f'c={cls}']
    for key in ('pos', 'hs', 'ar', 'pt'):
        if s.get(key): args.append(f'{key}={s[key]}')
    if dev != 'appletv' and 'ar' not in s:
        w0, h0 = Image.open(img).size
        args.append(f'ar={w0}/{h0}')
    dst = out / f"{s['name']}.png"
    subprocess.run(['python3', str(here / 'render.py'), str(W), str(H), str(scale), str(dst)] + args, check=True, capture_output=True)
    im = Image.open(dst).convert('RGB')
    want = (W * scale, H * scale)
    if im.size != want: im = im.resize(want, Image.LANCZOS)
    im.save(dst)
    print(dst.name, dev, im.size)
