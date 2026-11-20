#!/usr/bin/env python3
"""Generate the logo SVGs and the installer artwork from the mux pixel wordmark.

The wordmark is pixel art on a 19 x 5 grid (traced from assets/logos/logo-green-512px-x-512px.png and
assets/icons/icon-macos.png, which agree cell for cell), so the SVGs are exact rather than traced curves.

Outputs (all committed, so the release workflow needs no image tools):
  assets/logo.svg                         wordmark, transparent
  assets/icon.svg                         app icon: navy rounded square with the green wordmark
  assets/icon-1024.png                    the app icon at 1024 x 1024, transparent corners
  assets/installer/wizard-<w>x<h>.bmp     Inno Setup WizardImageFile (100%, 150%, 200%)
  assets/installer/wizard-small-<w>x<h>.bmp  Inno Setup WizardSmallImageFile (100%, 150%, 200%)
  assets/installer/pkg-background.png     macOS .pkg background (light), bottom-left logo
  assets/installer/pkg-background-dark.png  macOS .pkg background (dark mode)
  assets/installer/hicolor/<n>x<n>/apps/ai.latticetech.mux.png  Linux icon theme sizes

Usage: python3 tools/installer-assets/generate.py   (needs Pillow: pip install pillow)
"""

import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

GRID = [
    "##...##.##.##.##.##",
    "###.###.##.##.##.##",
    "##.#.##.##.##..###.",
    "##...##.#####.##.##",
    "##...##..###..##.##",
]
COLS = len(GRID[0])
ROWS = len(GRID)

LOGO_GREEN = (32, 128, 32)      # assets/logo-green.png
BRIGHT_GREEN = (74, 222, 128)   # the wordmark on the dark icon
NAVY_TOP = (29, 39, 57)         # icon gradient, top
NAVY_BOTTOM = (8, 13, 26)       # icon gradient, bottom
ICON_ID = "ai.latticetech.mux"


def hexc(rgb):
    return "#%02x%02x%02x" % rgb


def cells():
    """Merge each row's runs of filled cells into (x, y, width) rectangles, in cell units."""
    out = []
    for y, row in enumerate(GRID):
        x = 0
        while x < COLS:
            if row[x] == "#":
                start = x
                while x < COLS and row[x] == "#":
                    x += 1
                out.append((start, y, x - start))
            else:
                x += 1
    return out


def wordmark_rects_svg(fill, scale, ox, oy):
    return "\n".join(
        '  <rect x="%g" y="%g" width="%g" height="%g" fill="%s"/>' % (ox + x * scale, oy + y * scale, w * scale, scale, fill)
        for (x, y, w) in cells()
    )


def write(path, text):
    full = os.path.join(ROOT, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", newline="\n") as handle:
        handle.write(text)
    print("wrote", path)


def logo_svg():
    body = wordmark_rects_svg(hexc(LOGO_GREEN), 1, 0, 0)
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" shape-rendering="crispEdges">\n'
        "  <title>mux</title>\n%s\n</svg>\n" % (COLS, ROWS, body)
    )


def icon_svg():
    # 1024 canvas, 820 rounded square inset by 102 (the macOS icon grid), wordmark 560 wide centered.
    scale = 560 / COLS
    ox = (1024 - 560) / 2
    oy = (1024 - ROWS * scale) / 2
    body = wordmark_rects_svg(hexc(BRIGHT_GREEN), round(scale, 4), round(ox, 4), round(oy, 4))
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">\n'
        "  <title>mux</title>\n"
        "  <defs>\n"
        '    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">\n'
        '      <stop offset="0" stop-color="%s"/>\n'
        '      <stop offset="1" stop-color="%s"/>\n'
        "    </linearGradient>\n"
        "  </defs>\n"
        '  <rect x="102" y="102" width="820" height="820" rx="185" fill="url(#bg)"/>\n'
        '  <g shape-rendering="crispEdges">\n%s\n  </g>\n'
        "</svg>\n" % (hexc(NAVY_TOP), hexc(NAVY_BOTTOM), body)
    )


def gradient(size, top, bottom):
    w, h = size
    img = Image.new("RGB", size, top)
    draw = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        color = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        draw.line([(0, y), (w, y)], fill=color)
    return img


def draw_wordmark(img, color, x0, y0, cell):
    draw = ImageDraw.Draw(img)
    for (x, y, w) in cells():
        draw.rectangle([x0 + x * cell, y0 + y * cell, x0 + (x + w) * cell - 1, y0 + (y + 1) * cell - 1], fill=color)


def icon_png(size, inset_ratio):
    """The app icon: rounded navy square (inset by inset_ratio of the canvas) with the wordmark."""
    scale = 4
    big = size * scale
    inset = int(big * inset_ratio)
    side = big - 2 * inset
    bg = gradient((big, big), NAVY_TOP, NAVY_BOTTOM).convert("RGBA")
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).rounded_rectangle([inset, inset, inset + side - 1, inset + side - 1], radius=int(side * 0.225), fill=255)
    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    canvas.paste(bg, (0, 0), mask)
    cell = max(1, int(side * 0.68 / COLS))
    width = cell * COLS
    draw_wordmark(canvas, BRIGHT_GREEN + (255,), (big - width) // 2, (big - cell * ROWS) // 2, cell)
    return canvas.resize((size, size), Image.LANCZOS)


def wizard_image(size):
    """Inno Setup's tall welcome-page image: navy gradient with the wordmark in the upper third."""
    w, h = size
    img = gradient(size, NAVY_TOP, NAVY_BOTTOM)
    cell = max(1, int(w * 0.72 / COLS))
    width = cell * COLS
    draw_wordmark(img, BRIGHT_GREEN, (w - width) // 2, int(h * 0.30), cell)
    return img


def wizard_small(size):
    w, h = size
    icon = icon_png(max(w, h) * 4, 0.02).resize((min(w, h), min(w, h)), Image.LANCZOS)
    img = Image.new("RGB", size, (255, 255, 255))
    img.paste(icon, ((w - icon.width) // 2, (h - icon.height) // 2), icon)
    return img


def pkg_background(dark):
    # Installer.app draws the background at its natural size, anchored bottom-left (scaling="none").
    w, h = 620, 418
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    cell = 8
    draw_wordmark(img, (BRIGHT_GREEN if dark else LOGO_GREEN) + (255,), 24, h - 24 - cell * ROWS, cell)
    return img


def main():
    write("assets/logo.svg", logo_svg())
    write("assets/icon.svg", icon_svg())

    icon_png(1024, 0.1).save(os.path.join(ROOT, "assets/icon-1024.png"))
    print("wrote assets/icon-1024.png")

    for size in [(164, 314), (246, 459), (328, 604)]:
        path = "assets/installer/wizard-%dx%d.bmp" % size
        os.makedirs(os.path.join(ROOT, "assets/installer"), exist_ok=True)
        wizard_image(size).save(os.path.join(ROOT, path), "BMP")
        print("wrote", path)
    for size in [(55, 58), (83, 80), (110, 106)]:
        path = "assets/installer/wizard-small-%dx%d.bmp" % size
        wizard_small(size).save(os.path.join(ROOT, path), "BMP")
        print("wrote", path)

    pkg_background(False).save(os.path.join(ROOT, "assets/installer/pkg-background.png"))
    pkg_background(True).save(os.path.join(ROOT, "assets/installer/pkg-background-dark.png"))
    print("wrote assets/installer/pkg-background(-dark).png")

    for n in [48, 64, 128, 256, 512]:
        path = "assets/installer/hicolor/%dx%d/apps/%s.png" % (n, n, ICON_ID)
        os.makedirs(os.path.dirname(os.path.join(ROOT, path)), exist_ok=True)
        icon_png(n, 0.04).save(os.path.join(ROOT, path))
        print("wrote", path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
