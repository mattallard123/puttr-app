#!/usr/bin/env python3
"""make_icons.py: the Puttr app icons, flat (docs/design-guidelines.md): on black, no glow.

  python3 docs/appicon/make_icons.py

Square icons (phones, tablets, Mac): the official mark (marketing/assets/logo.svg), white on black.
Wide icons (Apple TV, Android TV, Top Shelf): the horizontal lockup (logo_header.svg) on black, its mark in brand
green rgb(45, 140, 60) and the PUTTR text white (Matt, 2026-11-24).

Writes over the pictures the icon slots already point at, so nothing in Player Settings changes:
  Assets/PuttrArcade/AppIcons/Icon-NxN.png           every square size
  Assets/PuttrArcade/AppIcons/ (wide ones)           Icon-*x720/1440, Ground-*, Logo-*, AndroidBanner
  Assets/PuttrApp/TvIcons/                           Front-* (lockup, see-through) over Back-* (black), Shelf-*, Banner
Never run Puttr > Make App Icons after this: it draws the old pictures again.
"""
import pathlib, re, subprocess, tempfile
from PIL import Image

here = pathlib.Path(__file__).resolve().parent
root = here.parent.parent
marketing = root.parent / 'marketing' / 'assets'
GROUND = (0, 0, 0)
CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'


def svg(name, mark='#ffffff'):
    s = (marketing / name).read_text()
    s = s[s.index('<svg'):]
    box = re.search(r'viewBox="([^"]+)"', s).group(1)
    s = re.sub(r'<svg[^>]*>', '<svg xmlns="http://www.w3.org/2000/svg" viewBox="%s">' % box, s, count=1)
    s = re.sub(r'<style.*?</style>', '', s, flags=re.S)
    s = re.sub(r'<g id="bg".*?</g>', '', s, flags=re.S)
    s = s.replace('class="st2"', 'fill="%s"' % mark).replace('class="st3"', 'fill="#ffffff"')   # st2 is the mark, st3 the PUTTR text
    w, h = [float(v) for v in box.split()[2:]]
    return s, w / h


MARK, _ = svg('logo.svg')
LOCKUP, LOCKUP_RATIO = svg('logo_header.svg', '#2d8c3c')


def render(w, h, logo, logo_w, ground):
    """A w x h picture: the logo logo_w pixels wide in the middle, on the black ground or on nothing."""
    k = 1
    while w * k < 1000: k *= 2   # headless Chrome crops narrow windows: draw big, then scale down
    page = ('<!doctype html><meta charset="utf-8"><style>html,body{margin:0;width:100vw;height:100vh;overflow:hidden;background:%s;display:grid;place-items:center}'
            'svg{width:%dpx;display:block}</style>%s') % ('rgb(%d,%d,%d)' % GROUND if ground else 'transparent', logo_w * k, logo)
    with tempfile.TemporaryDirectory() as tmp:
        html, png = pathlib.Path(tmp) / 'i.html', pathlib.Path(tmp) / 'i.png'
        html.write_text(page)
        subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000', '--force-device-scale-factor=1',
                        '--window-size=%d,%d' % (w * k, h * k), '--screenshot=%s' % png, 'file://%s' % html], capture_output=True, timeout=120)
        im = Image.open(png).convert('RGBA')
    if k > 1: im = im.resize((w, h), Image.LANCZOS)
    return im


def flat(im):
    out = Image.new('RGB', im.size, GROUND)
    out.paste(im, mask=im.split()[3])
    return out


app = root / 'Assets/PuttrArcade/AppIcons'
tv = root / 'Assets/PuttrApp/TvIcons'

# Square: one master, scaled down so every size is the same drawing.
master = flat(render(1024, 1024, MARK, int(1024 * 0.78), True))
for f in sorted(app.glob('Icon-*.png')):
    w, h = [int(v) for v in f.stem.split('-')[1].split('x')]
    if w == h: master.resize((w, h), Image.LANCZOS).save(f)
master.save(here / 'out' / 'Icon-1024x1024.png')

# Wide: the lockup. On the Apple TV icon it floats over the ground as its own layer, kept well inside the safe area.
for w, h in [(400, 240), (800, 480), (1280, 768)]:
    front = render(w, h, LOCKUP, int(w * 0.62), False)
    back = Image.new('RGB', (w, h), GROUND)
    size = '%dx%d' % (w, h)
    front.save(tv / ('Front-%s.png' % size)); back.save(tv / ('Back-%s.png' % size))
    front.save(app / ('Logo-%s.png' % size)); back.save(app / ('Ground-%s.png' % size))
for w, h in [(1920, 720), (3840, 1440), (2320, 720), (4640, 1440)]:
    shelf = flat(render(w, h, LOCKUP, int(h * 0.30 * LOCKUP_RATIO), True))
    shelf.save(tv / ('Shelf-%dx%d.png' % (w, h))); shelf.save(app / ('Icon-%dx%d.png' % (w, h)))
banner = flat(render(320, 180, LOCKUP, int(320 * 0.70), True))
banner.save(tv / 'Banner-320x180.png'); banner.save(app / 'AndroidBanner-320x180.png')
print('icons written to', app, 'and', tv)
