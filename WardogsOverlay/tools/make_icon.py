"""Рисует иконку приложения: тёмная плашка, оранжевый прицел, зелёная/красная стрелки статов.
Для 16–32 px — упрощённый вариант без стрелок, чтобы прицел читался.
Запуск: python tools/make_icon.py assets/app.ico"""
import sys
from PIL import Image, ImageDraw

S = 1024
BG = (24, 25, 27, 255)
EDGE = (52, 54, 57, 255)
ORANGE = (240, 150, 30, 255)
GREEN = (108, 197, 108, 255)
RED = (226, 92, 80, 255)
GRAY = (140, 143, 147, 255)


def draw(simple: bool) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((40, 40, S - 40, S - 40), radius=200, fill=BG, outline=EDGE, width=16 if not simple else 0)
    if simple:
        cx, cy, r, w = 512, 512, 290, 90
    else:
        d.rounded_rectangle((110, 190, 160, S - 190), radius=25, fill=ORANGE)  # полоса, как у плашки
        cx, cy, r, w = 560, 450, 240, 56
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=ORANGE, width=w)
    gap, over = (90, 70) if simple else (70, 60)
    for x0, y0, x1, y1 in [(cx - r - over, cy, cx - gap, cy), (cx + gap, cy, cx + r + over, cy),
                           (cx, cy - r - over, cx, cy - gap), (cx, cy + gap, cx, cy + r + over)]:
        d.line((x0, y0, x1, y1), fill=ORANGE, width=w - 8 if not simple else w)
    dot = 30 if not simple else 50
    d.ellipse((cx - dot, cy - dot, cx + dot, cy + dot), fill=ORANGE)
    if not simple:
        d.polygon([(330, 905), (440, 905), (385, 810)], fill=GREEN)
        d.polygon([(680, 810), (790, 810), (735, 905)], fill=RED)
        d.rectangle((475, 845, 645, 870), fill=GRAY)
    return img


full, simple = draw(False), draw(True)
frames = [(simple if s <= 32 else full).resize((s, s), Image.LANCZOS) for s in [16, 20, 24, 32, 40, 48, 64, 128, 256]]
out = sys.argv[1]
frames[-1].save(out, format="ICO", sizes=[f.size for f in frames], append_images=frames[:-1])
full.resize((256, 256), Image.LANCZOS).save(out.replace(".ico", ".png"))
