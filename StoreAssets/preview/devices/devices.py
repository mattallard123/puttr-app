#!/usr/bin/env python3
"""devices.py: drawn device frames for the reel's "Play on" shots (no product photos, so nothing to license).

frame(kind, sw, sh) -> (RGBA image, ox, oy): a phone, tablet or tv body round a sw x sh screen, with a soft shadow
under it. The screen is a transparent hole with rounded corners, so the take goes UNDER the frame, its top left at
(x - ox, y - oy) when the frame's top left is at (x - ox, y - oy)... i.e. place the frame at (screen_x - ox, screen_y - oy).
studio(W, H) -> RGB image: the dark background they sit on, a soft lime glow behind the middle."""
from PIL import Image, ImageDraw, ImageFilter

S = 3  # drawn at 3x, then scaled down: smooth corners

SPEC = {  # bezel and outer corner radius as a share of the screen width
    'phone':  dict(bezel=.034, radius=.165, rim=(118, 124, 121), body=(14, 15, 14)),
    'tablet': dict(bezel=.042, radius=.062, rim=(118, 124, 121), body=(14, 15, 14)),
    'tv':     dict(bezel=.010, radius=.006, rim=(96, 101, 99), body=(12, 13, 12)),
}

def frame(kind, sw, sh):
    p = SPEC[kind]
    b = max(4, round(p['bezel'] * sw)); r = round(p['radius'] * sw)
    stand = round(.09 * sw) if kind == 'tv' else 0
    pad = round(.08 * sw)
    bw, bh = sw + 2 * b, sh + 2 * b
    W, H = bw + 2 * pad, bh + 2 * pad + stand
    ox, oy = pad + b, pad + b
    big = lambda v: v * S
    img = Image.new('RGBA', (big(W), big(H)), (0, 0, 0, 0))
    # shadow: a blurred dark body, a little low
    sh_layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh_layer).rounded_rectangle((big(pad), big(pad + round(.03 * sw)), big(pad + bw), big(pad + bh + stand // 2)), radius=big(r), fill=(0, 0, 0, 170))
    sh_layer = sh_layer.filter(ImageFilter.GaussianBlur(big(round(.035 * sw))))
    img.alpha_composite(sh_layer)
    d = ImageDraw.Draw(img)
    if kind == 'tv':  # neck and foot
        nw, fw = round(.07 * sw), round(.34 * sw)
        cx = pad + bw // 2
        d.rectangle((big(cx - nw // 2), big(pad + bh - 2), big(cx + nw // 2), big(pad + bh + stand - 8)), fill=p['rim'] + (255,))
        d.rounded_rectangle((big(cx - fw // 2), big(pad + bh + stand - 12), big(cx + fw // 2), big(pad + bh + stand)), radius=big(6), fill=p['rim'] + (255,))
    if kind in ('phone', 'tablet'):  # side buttons
        bt = max(3, round(.008 * sw)); y0 = pad + round(.16 * bh)
        for (yy, hh) in ((y0, .06), (y0 + round(.09 * bh), .09)) if kind == 'phone' else ((y0 - round(.1 * bh), .05),):
            d.rounded_rectangle((big(pad - bt), big(yy), big(pad + 2), big(yy + round(hh * bh))), radius=big(bt), fill=p['rim'] + (255,))
        if kind == 'phone':
            d.rounded_rectangle((big(pad + bw - 2), big(y0 + round(.05 * bh)), big(pad + bw + bt), big(y0 + round(.17 * bh))), radius=big(bt), fill=p['rim'] + (255,))
    # body: a light rim, then the black bezel
    d.rounded_rectangle((big(pad), big(pad), big(pad + bw), big(pad + bh)), radius=big(r), fill=p['rim'] + (255,))
    e = max(3, round(.006 * sw))
    d.rounded_rectangle((big(pad + e), big(pad + e), big(pad + bw - e), big(pad + bh - e)), radius=big(max(1, r - e)), fill=p['body'] + (255,))
    # the screen: a hole
    hole = Image.new('L', img.size, 0)
    ImageDraw.Draw(hole).rounded_rectangle((big(ox), big(oy), big(ox + sw), big(oy + sh)), radius=big(max(0, r - b)), fill=255)
    a = img.getchannel('A'); a.paste(0, mask=hole); img.putalpha(a)
    return img.resize((W, H), Image.LANCZOS), ox, oy

def screen_mask(kind, sw, sh):
    """The screen's own rounded shape (L, sw x sh), so a take's square corners do not show past the body."""
    p = SPEC[kind]; r = max(0, round(p['radius'] * sw) - max(4, round(p['bezel'] * sw)))
    m = Image.new('L', (sw * S, sh * S), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, sw * S - 1, sh * S - 1), radius=r * S, fill=255)
    return m.resize((sw, sh), Image.LANCZOS)

def studio(W, H):
    import math
    img = Image.new('RGB', (W, H))
    px = img.load()
    cx, cy = W * .5, H * .52
    for y in range(H):
        for x in range(0, W):
            t = min(1, math.hypot((x - cx) / (W * .75), (y - cy) / (H * .55)))
            g = (1 - t) ** 2
            px[x, y] = (int(8 + 26 * g), int(9 + 74 * g), int(8 + 24 * g))
    return img

if __name__ == '__main__':
    import sys
    kind, sw, sh, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
    im, ox, oy = frame(kind, sw, sh); im.save(out); print(out, im.size, ox, oy)
