#!/usr/bin/env python3
"""Keeps the style sheets on the design guidelines (docs/design-guidelines.md).

  python3 Tools/design/uss_tokens.py            report colours and corner radii that are off the guidelines
  python3 Tools/design/uss_tokens.py --fix      rewrite them to the nearest token (neutrals, greens, radii)

Only solid rgb() colours are touched; rgba() overlays in the games are left alone.
"""
import glob, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'PuttrApp', 'Resources', 'PuttrApp')

BLACK, BASE, SURFACE, SURFACE2, SURFACE3, LINE = (0, 0, 0), (10, 12, 13), (21, 24, 27), (34, 38, 42), (48, 53, 58), (38, 42, 46)
WHITE, SOFT, TEXT2 = (255, 255, 255), (196, 201, 205), (139, 147, 153)
BRAND, BRAND_HOVER, BRAND_PRESS, BRIGHT = (45, 140, 60), (52, 158, 68), (36, 112, 48), (122, 212, 70)
AMBER, RED, GOLD, BLUE, FIRE, RED_DARK = (255, 179, 0), (255, 82, 82), (255, 210, 74), (86, 135, 255), (255, 122, 58), (140, 34, 34)
PALETTE = {BLACK, BASE, SURFACE, SURFACE2, SURFACE3, LINE, WHITE, SOFT, TEXT2, BRAND, BRAND_HOVER, BRAND_PRESS, BRIGHT, AMBER, RED, GOLD, BLUE, FIRE, RED_DARK}

# Shapes that are meant to be round (status dots, switches, the tee and hole markers): their radius is left alone.
ROUND = {'dot', 'knob', 'thumb', 'ring', 'switch', 'toggle', 'slider', 'radio', 'hole', 'ball', 'bead', 'spinner', 'pip', 'coin', 'circle', 'disc', 'lamp', 'orb', 'pulse', 'blip', 'spot'}


def neutral(c): return max(c) - min(c) <= 10
def green(c): return c[1] > c[0] + 30 and c[1] > c[2] + 30


def map_colour(prop, c):
    """The token a colour should be, or None to leave it."""
    v = max(c)
    if prop.startswith('border'):
        if c == BASE: return c     # a border in the page colour cuts a badge out of what it sits on
        if neutral(c) and v <= 90: return LINE
        if green(c): return BRIGHT if c[1] >= 170 else BRAND if c[1] >= 100 else LINE
        if v <= 90: return LINE    # a dark tinted border
        return None
    if prop in ('color', '-unity-background-image-tint-color', '--unity-cursor-color'):
        if neutral(c):
            if v >= 225: return WHITE
            if v >= 160: return SOFT
            if v >= 60: return TEXT2
            return None            # dark text on a light fill: reviewed by hand
        if green(c): return BRIGHT
        return None
    if prop in ('background-color', '--fill'):
        if neutral(c):
            if v <= 7: return BLACK
            if v <= 17: return BASE
            if v <= 30: return SURFACE
            if v <= 44: return SURFACE2
            if v <= 75: return SURFACE3
            return None
        if green(c):
            if c in (BRAND_HOVER, BRAND_PRESS): return c
            if c[1] >= 100: return BRAND
            return SURFACE2 if c[1] >= 60 else SURFACE
        if v <= 80: return SURFACE2 if v > 45 else SURFACE    # a dark tinted panel: grey, the meaning goes in the text colour
        return None
    return None


def run(fix):
    changed = off = 0
    for path in sorted(glob.glob(os.path.join(ROOT, '*.uss'))):
        text = open(path).read()
        name = os.path.basename(path)

        def block(m):
            nonlocal changed, off
            selector, body = m.group(1), m.group(2)
            width = re.search(r'(?<![-\w])width:\s*(\d+)px', body)
            height = re.search(r'(?<![-\w])height:\s*(\d+)px', body)
            small = width and height and width.group(1) == height.group(1) and int(width.group(1)) <= 14
            keep_round = small or bool(ROUND & set(re.split(r'[^a-z]+', selector.lower())))

            def colour(d):
                nonlocal changed, off
                prop, c = d.group(1), tuple(int(x) for x in d.group(2, 3, 4))
                want = map_colour(prop, c)
                if want is None:
                    if c not in PALETTE:
                        off += 1
                        if not fix: print(f'{name}: {selector.strip()[:50]}: {prop} rgb{c} is not a token')
                    return d.group(0)
                if want == c: return d.group(0)
                if not fix:
                    off += 1
                    print(f'{name}: {selector.strip()[:50]}: {prop} rgb{c} -> rgb{want}')
                    return d.group(0)
                changed += 1
                return f'{prop}: rgb({want[0]}, {want[1]}, {want[2]})'

            body = re.sub(r'([-\w]+):\s*rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)', colour, body)

            def radius(d):
                nonlocal changed, off
                if keep_round: return d.group(0)
                def one(n):
                    nonlocal changed, off
                    if int(n.group(1)) <= 4: return n.group(0)
                    if not fix:
                        off += 1
                        print(f'{name}: {selector.strip()[:50]}: radius {n.group(1)}px -> 4px')
                        return n.group(0)
                    changed += 1
                    return '4px'
                return d.group(1) + re.sub(r'(\d+)px', one, d.group(2))

            body = re.sub(r'(border(?:-[a-z]+)*-radius:)([^;}]*)', radius, body)
            return selector + '{' + body + '}'

        new = re.sub(r'([^{}]+)\{([^{}]*)\}', block, text)
        if fix and new != text: open(path, 'w').write(new)
    print(f'{changed} values rewritten' if fix else f'{off} values off the guidelines')
    return 0 if fix or off == 0 else 1


if __name__ == '__main__':
    sys.exit(run('--fix' in sys.argv))
