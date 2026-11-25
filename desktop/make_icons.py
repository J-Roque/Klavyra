"""Draws Klavyra's mark (the same shapes as web/src/app/layout/logo.ts) into its icon files.

Writes:
  src/Klavyra.Desktop/Klavyra.ico      window, taskbar, Klavyra.exe and the installer
  src/Klavyra.Desktop/splash-logo.png  the mark on the startup screen (transparent)
  web/public/favicon.ico               the browser tab while developing
Run from the repository root:  worker\\.venv\\Scripts\\python.exe -I desktop\\make_icons.py
"""
from pathlib import Path
import sys

from PIL import Image, ImageDraw

ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path.cwd()
PAPER = (247, 244, 236)
BRASS = (224, 179, 84)
INK = (18, 33, 27)
SCALE = 32  # the logo's viewBox is 32 x 32
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def mark(px: int, bars=PAPER) -> Image.Image:
    """The mark alone on a transparent square, drawn large and scaled down so small sizes stay smooth.
    The bars are light on the dark icon and dark on the light startup screen."""
    big = px * 4
    k = big / SCALE
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for x, y, h, alpha in ((2, 13, 8, 0.55), (8, 8, 18, 0.75), (14, 11, 11, 0.55)):
        draw.rounded_rectangle([x * k, y * k, (x + 3) * k, (y + h) * k], radius=1.5 * k, fill=bars + (round(255 * alpha),))
    draw.rounded_rectangle([23.5 * k, 3 * k, 26.5 * k, 25 * k], radius=1.5 * k, fill=BRASS + (255,))
    # The note head: an ellipse rotated -20 degrees around (21.7, 25).
    head = Image.new("RGBA", image.size, (0, 0, 0, 0))
    cx, cy, rx, ry = 21.7 * k, 25 * k, 4.8 * k, 3.6 * k
    ImageDraw.Draw(head).ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=BRASS + (255,))
    image.alpha_composite(head.rotate(20, center=(cx, cy), resample=Image.BICUBIC))
    return image.resize((px, px), Image.LANCZOS)


def tile(px: int) -> Image.Image:
    """The mark on a dark rounded square, so it reads on light and dark taskbars."""
    big = px * 4
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(image).rounded_rectangle([0, 0, big - 1, big - 1], radius=big * 0.22, fill=INK + (255,))
    inner = round(big * 0.80)
    offset = (big - inner) // 2
    image.alpha_composite(mark(inner), (offset, offset + round(big * 0.01)))
    return image.resize((px, px), Image.LANCZOS)


def main() -> None:
    icons = [tile(size) for size in SIZES]
    for target in (ROOT / "src/Klavyra.Desktop/Klavyra.ico", ROOT / "web/public/favicon.ico"):
        icons[-1].save(target, format="ICO", sizes=[(s, s) for s in SIZES], append_images=icons[:-1])
        print("escrito", target)
    splash = ROOT / "src/Klavyra.Desktop/splash-logo.png"
    mark(192, bars=INK).save(splash)
    print("escrito", splash)


if __name__ == "__main__":
    main()
