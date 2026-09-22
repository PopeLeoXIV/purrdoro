"""
Generates Purrdoro's app icon and MSIX logo assets from the same simple
geometry as the in-app CatMascot control (round head, triangular ears, dot
eyes, tiny mouth). Everything is drawn from scratch; no external artwork.

Requires Pillow:   pip install pillow
Run from the repo root:   python tools/generate_assets.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src" / "Purrdoro" / "Assets"

TERRACOTTA = (200, 111, 74, 255)
CREAM = (255, 250, 243, 255)
INK = (59, 48, 42, 255)
BLUSH = (236, 166, 135, 170)

SS = 4  # supersampling factor


def draw_cat(draw: ImageDraw.ImageDraw, cx: float, cy: float, scale: float, stroke: float,
             face=CREAM, ink=INK, blush=BLUSH) -> None:
    """Draws the cat head centred at (cx, cy). Design units: a 100 x 80 box."""

    def p(x, y):
        return (cx + (x - 50) * scale, cy + (y - 45) * scale)

    w = max(1, round(stroke))
    # Ears (drawn first so the head covers their bases).
    for ear in ([(21, 40), (25, 11), (45, 25)], [(79, 40), (75, 11), (55, 25)]):
        pts = [p(*pt) for pt in ear]
        draw.polygon(pts, fill=face)
        draw.line(pts + [pts[0]], fill=ink, width=w, joint="curve")

    # Head.
    x0, y0 = p(16, 22)
    x1, y1 = p(84, 78)
    draw.ellipse([x0, y0, x1, y1], fill=face, outline=ink, width=w)

    # Cheeks.
    for bx in (30, 70):
        a0, b0 = p(bx - 5, 56)
        a1, b1 = p(bx + 5, 62)
        draw.ellipse([a0, b0, a1, b1], fill=blush)

    # Eyes.
    for ex in (38, 62):
        a0, b0 = p(ex - 3.2, 44)
        a1, b1 = p(ex + 3.2, 51)
        draw.ellipse([a0, b0, a1, b1], fill=ink)

    # Mouth: a tiny "w".
    mouth = [p(44.5, 55.5), p(47.2, 58.5), p(50, 56), p(52.8, 58.5), p(55.5, 55.5)]
    draw.line(mouth, fill=ink, width=max(1, round(w * 0.8)), joint="curve")


def rounded_square(size: int, radius_ratio=0.24, color=TERRACOTTA) -> Image.Image:
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, big - 1, big - 1], radius=big * radius_ratio, fill=color)
    return img


def icon(size: int, plated=True) -> Image.Image:
    big = size * SS
    img = rounded_square(size) if plated else Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # Small icons get relatively thicker lines so the cat stays legible.
    stroke_ratio = 0.075 if size <= 24 else 0.055 if size <= 48 else 0.04
    scale = big / 100 * (0.78 if plated else 0.98)
    draw_cat(d, big / 2, big / 2 + big * 0.03, scale, big * stroke_ratio)
    return img.resize((size, size), Image.LANCZOS)


def banner(width: int, height: int) -> Image.Image:
    big_w, big_h = width * SS, height * SS
    img = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    scale = big_h / 100 * 0.62
    draw_cat(d, big_w / 2, big_h / 2, scale, big_h * 0.022)
    return img.resize((width, height), Image.LANCZOS)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    icon(256).save(OUT / "AppIcon.png")
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [icon(s) for s in ico_sizes]
    frames[-1].save(OUT / "AppIcon.ico", sizes=[(s, s) for s in ico_sizes], append_images=frames[:-1])

    icon(300).save(OUT / "Square150x150Logo.scale-200.png")
    icon(88).save(OUT / "Square44x44Logo.scale-200.png")
    icon(24, plated=False).save(OUT / "Square44x44Logo.targetsize-24_altform-unplated.png")
    icon(50).save(OUT / "StoreLogo.png")
    icon(48, plated=False).save(OUT / "LockScreenLogo.scale-200.png")
    banner(620, 300).save(OUT / "Wide310x150Logo.scale-200.png")
    banner(1240, 600).save(OUT / "SplashScreen.scale-200.png")

    print("Assets written to", OUT)


if __name__ == "__main__":
    main()
