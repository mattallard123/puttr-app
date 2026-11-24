#!/usr/bin/env python3
"""Game card art for the rule games (the arcade games use their own 3D renders, -puttrShots).

python3 docs/cards/make_cards.py <arcade card folder>  ->  Assets/PuttrApp/Resources/PuttrApp/Cards/<GAMETYPE>.png
480 x 288, the arcade's card size. Dark board, a glow in the game's colour, a motif on the right; the app draws
the title over the bottom left.
"""
import math, os, shutil, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets/PuttrApp/Resources/PuttrApp/Cards")
ICONS = os.path.join(ROOT, "Assets/PuttrArcade/Resources/PuttrArcade/Icons")
FONT = os.path.join(ROOT, "Assets/PuttrArcade/Resources/PuttrArcade/Fonts/Montserrat-Bold.ttf")
W, H = 480, 288
os.makedirs(OUT, exist_ok=True)

def hexc(h): return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))

def base(accent):
    img = Image.new("RGB", (W, H), (12, 14, 16))
    glow = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(glow)
    for r in range(260, 0, -8):
        k = (1 - r / 260) ** 1.6
        d.ellipse([330 - r, 120 - r, 330 + r, 120 + r], fill=tuple(int(c * k * 0.55) for c in accent))
    img = Image.blend(img, glow, 0.9)
    d = ImageDraw.Draw(img)
    # the ramp, in perspective: lines toward a point beyond the top
    for i in range(-8, 9):
        d.line([(240 + i * 70, H), (300 + i * 14, -40)], fill=tuple(int(c * 0.22) + 14 for c in accent), width=1)
    for j in range(1, 9):
        y = H - j * j * 4.2
        d.line([(0, y), (W, y)], fill=tuple(int(c * 0.14) + 14 for c in accent), width=1)
    return img

def glow_layer(draw_fn, accent):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(layer))
    blur = layer.filter(ImageFilter.GaussianBlur(9))
    return blur, layer

def compose(img, draw_fn, accent):
    blur, sharp = glow_layer(draw_fn, accent)
    img = img.convert("RGBA")
    img.alpha_composite(blur); img.alpha_composite(blur); img.alpha_composite(sharp)
    return img.convert("RGB")

def icon(name, accent, size=150, at=(330, 120)):
    def f(d):
        pass
    path = os.path.join(ICONS, "Icon" + name + ".png")
    def draw(d):
        if not os.path.exists(path): return
        ic = Image.open(path).convert("RGBA").resize((size, size))
        tint = Image.new("RGBA", ic.size, accent + (255,))
        tint.putalpha(ic.split()[3])
        d._image.alpha_composite(tint, (at[0] - size // 2, at[1] - size // 2))
    return draw

def text(words, accent, size=86, at=(330, 120)):
    font = ImageFont.truetype(FONT, size)
    def draw(d):
        d.text(at, words, font=font, fill=accent + (255,), anchor="mm")
    return draw

def shapes(kind, accent):
    a = accent + (255,)
    def draw(d):
        cx, cy = 330, 118
        if kind == "cups":
            r = 20
            for row, n in enumerate([4, 3, 2, 1]):
                for i in range(n):
                    x = cx + (i - (n - 1) / 2) * 46; y = cy - 66 + row * 42
                    d.ellipse([x - r, y - r, x + r, y + r], fill=(196, 40, 48, 255), outline=(240, 240, 240, 255), width=4)
        elif kind == "doors":
            for i in range(3):
                x = cx - 110 + i * 80
                d.rectangle([x, cy - 70, x + 60, cy + 70], outline=a if i == 1 else (120, 120, 120, 255), width=6)
                d.ellipse([x + 44, cy - 4, x + 52, cy + 4], fill=a)
        elif kind == "zigzag":
            pts = [(cx - 130 + i * 52, cy + (-50 if i % 2 else 50)) for i in range(6)]
            d.line(pts, fill=a, width=10, joint="curve")
        elif kind == "wave":
            pts = [(cx - 140 + x, cy + math.sin(x / 22) * 40 * (1 - x / 320)) for x in range(0, 281, 4)]
            d.line(pts, fill=a, width=9)
        elif kind == "high":
            d.arc([cx - 120, cy - 40, cx + 120, cy + 200], 200, 340, fill=a, width=10)
            d.ellipse([cx + 70, cy + 10, cx + 104, cy + 44], outline=(255, 255, 255, 255), width=5)
        elif kind == "frog":
            for i, (x, y) in enumerate([(cx - 120, cy + 40), (cx - 40, cy - 30), (cx + 40, cy + 40), (cx + 120, cy - 30)]):
                d.ellipse([x - 22, y - 22, x + 22, y + 22], fill=a if i % 2 == 0 else (255, 255, 255, 255))
            d.arc([cx - 120, cy - 70, cx + 120, cy + 90], 200, 340, fill=(255, 255, 255, 160), width=3)
        elif kind == "striker":
            d.rectangle([cx - 14, cy - 90, cx + 14, cy + 90], outline=(200, 200, 200, 255), width=4)
            for k in range(9): d.line([(cx - 30, cy - 80 + k * 20), (cx - 16, cy - 80 + k * 20)], fill=(200, 200, 200, 255), width=3)
            d.ellipse([cx - 14, cy - 40, cx + 14, cy - 12], fill=a)
            d.ellipse([cx - 30, cy - 120, cx + 30, cy - 80], outline=a, width=6)
    return draw

ARCADE = {"ARCADESTREAK": "OnFire", "TARGET": "Archery", "CRICKET": "Cricket", "DERBY": "Derby", "MAJORLEAGUE": "MajorLeague",
          "BOWLING": "Lanes", "BALLOONPOP": "BalloonPop", "ROCK": "PuttStar", "FREEPUTT": "FreePutt", "BEERPONG": "BeerPong",
          "DEADWEIGHT": "DeadWeight", "SENDIT": "SendIt", "SLINGSHOT": "Slingshot", "SPOTKICK": "SpotKick",
          "TUGOFWAR": "TugofWar", "TURFWAR": "TurfWar", "UNDERTHECUP": "UndertheCup", "WINDMILL": "Windmill",
          "PUTTBALL": "Putt-ball"}   # every arcade game: one left out keeps its old picture (Putt-ball did, 2026-11-23)

COLOURS = {
    "TOURJUICE": "#ffb300", "SPEEDDEMON": "#ff5a3c", "METAL": "#c9ced6", "LAB": "#5687ff", "FULLWORKOUT": "#7ad446",
    "FITTING": "#56c2ff", "DEADCENTER": "#ff4fb0", "ASSESSMENT": "#9ed87a", "HORSE": "#ffb300", "OHONE": "#ff5a3c",
    "REDUCER": "#b36bff", "MATCH": "#7ad446", "HIGHSTRIKER": "#ffd54a", "BEERPONG": "#ff5a3c", "LEAPFROG": "#7ad446",
    "DOORS": "#56c2ff", "HIGHSIDE": "#ffb300", "SWITCHBACK": "#b36bff", "GEARS": "#c9ced6", "GROOVE": "#ff4fb0",
    "CLOCK": "#56c2ff", "CENTERCUT": "#7ad446", "STANDARD": "#7ad446", "TRACKS": "#7ad446",
}

def main():
    for game, colour in COLOURS.items():
        accent = hexc(colour)
        compose(base(accent), rebind(game, accent), accent).save(os.path.join(OUT, game + ".png"))
    if len(sys.argv) > 1:
        for game, card in ARCADE.items():
            src = os.path.join(sys.argv[1], "card_" + card + ".png")
            if os.path.exists(src): shutil.copy(src, os.path.join(OUT, game + ".png"))
    print("cards:", len([f for f in os.listdir(OUT) if f.endswith(".png")]))

SPEC = {
    "TOURJUICE": ("icon", "Lightning"), "SPEEDDEMON": ("icon", "Fire"), "METAL": ("text", "+11", 110), "LAB": ("icon", "Flask"),
    "FULLWORKOUT": ("icon", "Barbell"), "FITTING": ("icon", "Ruler"), "DEADCENTER": ("icon", "Target"), "ASSESSMENT": ("icon", "ListChecks"),
    "HORSE": ("text", "H O R S E", 54), "OHONE": ("text", "301", 120), "REDUCER": ("icon", "RadioButton"), "MATCH": ("text", "2 UP", 100),
    "HIGHSTRIKER": ("shape", "striker"), "BEERPONG": ("shape", "cups"), "LEAPFROG": ("shape", "frog"), "DOORS": ("shape", "doors"),
    "HIGHSIDE": ("shape", "high"), "SWITCHBACK": ("shape", "zigzag"), "GEARS": ("icon", "GearSix"), "GROOVE": ("shape", "wave"),
    "CLOCK": ("icon", "Clock"), "CENTERCUT": ("icon", "Target"), "STANDARD": ("icon", "Target"), "TRACKS": ("icon", "MapPin"),
}

def rebind(game, accent):
    s = SPEC[game]
    if s[0] == "icon": return icon(s[1], accent)
    if s[0] == "text": return text(s[1], accent, s[2])
    return shapes(s[1], accent)

if __name__ == "__main__":
    main()
