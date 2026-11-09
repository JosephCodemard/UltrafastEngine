#!/usr/bin/env python3
"""Generate the UltrafastEngine logo from code, at any resolution. No image generators, no external assets.

The mark is a 3 x 3 grid of the Zernike modes 2-10 (Noll order: tip, tilt, defocus, two astigmatisms, two comas, two
trefoils) with one coefficient each. Large sizes draw each mode as a wavefront map on a disc; small sizes (below
SMALL_BELOW px) draw each cell as one flat colour, the colour of its coefficient, with wide gaps, because nine maps
become mush at 16-32 px. The large logo puts "Ultrafast Engine" to the right of the mark, left-aligned, in dark text
(for light pages) or light text (for dark pages).

    python assets/logo/make_logo.py                        # what the project needs: the three SVGs and assets/ultrafast.ico
    python assets/logo/make_logo.py --png-sizes 32 256     # optional PNGs of the mark (and large logos, 128 px high), into --out
    python assets/logo/make_logo.py --out /tmp/logo --no-ico

Default outputs: ultrafast-logo-large.svg (dark text, light pages), ultrafast-logo-large-dark.svg (light text, dark pages),
ultrafast-logo-small.svg (the mark alone, flat cells), and ../ultrafast.ico, the multi-size icon the CLI and the Zemax
binding embed in their .exe (Windows cannot embed an SVG). Needs numpy, Pillow and matplotlib (for its bundled DejaVu Sans,
so the text is the same on every machine).
"""
from __future__ import annotations

import argparse
import base64
import io
import struct
from pathlib import Path
from typing import List, Sequence, Tuple

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SMALL_BELOW = 96          # marks smaller than this (px) are drawn as flat coefficient cells
SS = 4                    # supersampling factor for anti-aliasing
HERE = Path(__file__).resolve().parent

# One coefficient per mode (Noll 2..10), in [-1, 1]. Chosen for contrast between neighbours, not for physics.
COEFFS: Tuple[float, ...] = (0.80, -0.60, 1.00, -0.85, 0.65, -1.00, 0.55, 0.90, -0.70)
MODE_NAMES: Tuple[str, ...] = ("tip", "tilt", "defocus", "oblique astigmatism", "vertical astigmatism",
                               "vertical coma", "horizontal coma", "vertical trefoil", "oblique trefoil")

# Diverging palette: deep blue - pale - orange-red. Colour-blind safe (blue/orange) and readable on light and dark.
_STOPS = ((0.00, (22, 54, 120)), (0.25, (58, 124, 190)), (0.50, (238, 236, 228)), (0.75, (240, 150, 70)), (1.00, (176, 40, 24)))

TEXT_LIGHT_BG = (24, 32, 44)       # text colour on light pages
TEXT_DARK_BG = (240, 243, 247)     # text colour on dark pages
ACCENT = (200, 90, 40)


def palette(v: np.ndarray) -> np.ndarray:
    """Map values in [-1, 1] to RGB (0-255) through the diverging palette."""
    t = np.clip(0.5 + 0.5 * np.asarray(v, float), 0.0, 1.0)
    xs = np.array([s[0] for s in _STOPS])
    out = np.empty(t.shape + (3,))
    for c in range(3):
        out[..., c] = np.interp(t, xs, [s[1][c] for s in _STOPS])
    return out


def zernike(j: int, rho: np.ndarray, th: np.ndarray) -> np.ndarray:
    """Zernike mode j = 2..10 in Noll order, rms-normalised (Noll 1976)."""
    s3, s6, s8 = np.sqrt(3), np.sqrt(6), np.sqrt(8)
    table = {
        2: lambda: 2 * rho * np.cos(th),
        3: lambda: 2 * rho * np.sin(th),
        4: lambda: s3 * (2 * rho ** 2 - 1),
        5: lambda: s6 * rho ** 2 * np.sin(2 * th),
        6: lambda: s6 * rho ** 2 * np.cos(2 * th),
        7: lambda: s8 * (3 * rho ** 3 - 2 * rho) * np.sin(th),
        8: lambda: s8 * (3 * rho ** 3 - 2 * rho) * np.cos(th),
        9: lambda: s8 * rho ** 3 * np.sin(3 * th),
        10: lambda: s8 * rho ** 3 * np.cos(3 * th),
    }
    return table[j]()


def _layout(size: int) -> Tuple[int, int, int]:
    """(pad, gap, cell) in px for a square mark of `size` px; the grid is centred, so `pad` only bounds the cell size."""
    small = size < SMALL_BELOW
    pad = max(1, round(size * (0.04 if small else 0.02)))
    gap = max(1, round(size * (0.10 if small else 0.045)))
    cell = (size - 2 * pad - 2 * gap) // 3
    return pad, gap, max(1, cell)


def _cell_image(k: int, cell: int, flat: bool) -> Image.Image:
    """One cell as RGBA at `cell` px: a flat rounded square (small) or the mode's map on a disc (large)."""
    n = cell * SS
    y, x = (np.mgrid[0:n, 0:n] + 0.5) / n * 2 - 1
    c = COEFFS[k]
    if flat:
        r = 0.30                                            # corner radius, fraction of half-width
        qx, qy = np.abs(x) - (1 - r), np.abs(y) - (1 - r)
        d = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r
        inside = d <= 0
        rgb = np.broadcast_to(palette(np.array(c))[None, None, :], (n, n, 3))
    else:
        rho, th = np.hypot(x, y), np.arctan2(y, x)
        inside = rho <= 1.0
        z = zernike(k + 2, np.minimum(rho, 1.0), th)
        zmax = np.abs(zernike(k + 2, *np.meshgrid(np.linspace(0, 1, 200), np.linspace(-np.pi, np.pi, 400)))).max()
        rgb = palette(c * z / zmax)
    img = np.zeros((n, n, 4), np.uint8)
    img[..., :3] = np.where(inside[..., None], rgb, 0).astype(np.uint8)
    img[..., 3] = np.where(inside, 255, 0)
    return Image.fromarray(img, "RGBA").resize((cell, cell), Image.BOX)


def mark(size: int) -> Image.Image:
    """The square mark, RGBA, `size` px."""
    flat = size < SMALL_BELOW
    _, gap, cell = _layout(size)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    span = 3 * cell + 2 * gap
    off = (size - span) // 2
    for k in range(9):
        r, c = divmod(k, 3)
        canvas.alpha_composite(_cell_image(k, cell, flat), (off + c * (cell + gap), off + r * (cell + gap)))
    return canvas


def _font(bold: bool, px: int) -> ImageFont.FreeTypeFont:
    import matplotlib
    name = "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"
    return ImageFont.truetype(str(Path(matplotlib.get_data_path()) / "fonts" / "ttf" / name), px)


def _text_layout(height: int) -> Tuple[ImageFont.FreeTypeFont, ImageFont.FreeTypeFont]:
    """Fonts for 'Ultrafast' (bold) and 'Engine' (regular), sized so that the words are as wide as 1.9 x the mark."""
    target = 1.9 * height
    px = 100
    f1 = _font(True, px)
    w = f1.getlength("Ultrafast")
    px = int(px * target / w)
    return _font(True, px), _font(False, px)


def large(height: int, dark: bool = False) -> Image.Image:
    """The mark on the left, the text on the right (left-aligned); RGBA, transparent background. `dark`: light text for dark pages."""
    f1, f2 = _text_layout(height)
    col = TEXT_DARK_BG if dark else TEXT_LIGHT_BG
    gapx = round(0.16 * height)
    tw = int(round(max(f1.getlength("Ultrafast"), f2.getlength("Engine"))))
    width = height + gapx + tw + round(0.03 * height)
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    asc1, desc1 = f1.getmetrics()
    asc2, desc2 = f2.getmetrics()
    block = asc1 + desc1 + asc2 + desc2
    y0 = (height - block) // 2 + round(0.03 * height)
    x0 = height + gapx
    d.text((x0, y0), "Ultrafast", font=f1, fill=col + (255,))
    d.text((x0, y0 + asc1 + desc1), "Engine", font=f2, fill=ACCENT + (255,) if not dark else (244, 160, 100, 255))
    img.alpha_composite(mark(height), (0, 0))
    return img


# ------------------------------------------------------------------------------------------------------- SVG
def _png_b64(img: Image.Image) -> str:
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return base64.b64encode(buf.getvalue()).decode("ascii")


def _text_path(text: str, bold: bool, size: float, x: float, y: float) -> str:
    """SVG path data of `text` (baseline at y), from matplotlib's TextPath: no font needed to view the SVG."""
    from matplotlib.font_manager import FontProperties
    from matplotlib.textpath import TextPath
    import matplotlib
    name = "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"
    fp = FontProperties(fname=str(Path(matplotlib.get_data_path()) / "fonts" / "ttf" / name))
    tp = TextPath((0, 0), text, size=size, prop=fp)
    out: List[str] = []
    pts = {1: 1, 2: 1, 3: 2, 4: 3, 79: 0}
    verts, codes = tp.vertices, tp.codes
    i = 0
    while i < len(codes):
        c = codes[i]
        n = pts[c]
        p = [(x + verts[i + j][0], y - verts[i + j][1]) for j in range(n)] if n else []
        if c == 1:
            out.append("M%.2f %.2f" % p[0])
        elif c == 2:
            out.append("L%.2f %.2f" % p[0])
        elif c == 3:
            out.append("Q%.2f %.2f %.2f %.2f" % (p[0] + p[1]))
        elif c == 4:
            out.append("C%.2f %.2f %.2f %.2f %.2f %.2f" % (p[0] + p[1] + p[2]))
        else:
            out.append("Z")
        i += max(n, 1)
    return "".join(out)


def _hex(rgb: Sequence[float]) -> str:
    return "#%02x%02x%02x" % tuple(int(round(v)) for v in rgb)


def svg_small(size: int = 256) -> str:
    """Vector mark of flat rounded cells (the design used below SMALL_BELOW px), `size` user units."""
    _, gap, cell = _layout(size)
    span = 3 * cell + 2 * gap
    off = (size - span) / 2
    rects = []
    for k in range(9):
        r, c = divmod(k, 3)
        rects.append('<rect x="%.2f" y="%.2f" width="%d" height="%d" rx="%.2f" fill="%s"><title>Zernike %s</title></rect>'
                     % (off + c * (cell + gap), off + r * (cell + gap), cell, cell, 0.30 * cell, _hex(palette(np.array(COEFFS[k]))), MODE_NAMES[k]))
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d">'
            '<title>UltrafastEngine</title>%s</svg>' % (size, size, size, size, "".join(rects)))


def svg_large(height: int = 256, dark: bool = False) -> str:
    """Vector logo: text as paths, the nine Zernike maps as embedded PNG tiles."""
    f1, f2 = _text_layout(height)
    col = TEXT_DARK_BG if dark else TEXT_LIGHT_BG
    gapx = round(0.16 * height)
    tw = int(round(max(f1.getlength("Ultrafast"), f2.getlength("Engine"))))
    width = height + gapx + tw + round(0.03 * height)
    x0 = height + gapx
    asc1, desc1 = f1.getmetrics()
    asc2, desc2 = f2.getmetrics()
    block = asc1 + desc1 + asc2 + desc2
    y0 = (height - block) // 2 + round(0.03 * height)
    s1, s2 = f1.size, f2.size
    t1 = _text_path("Ultrafast", True, s1, x0, y0 + asc1)
    t2 = _text_path("Engine", False, s2, x0, y0 + asc1 + desc1 + asc2)
    accent = _hex(ACCENT if not dark else (244, 160, 100))
    _, gap, cell = _layout(height)
    span = 3 * cell + 2 * gap
    off = (height - span) // 2
    tiles = []
    for k in range(9):
        r, c = divmod(k, 3)
        tiles.append('<image x="%d" y="%d" width="%d" height="%d" href="data:image/png;base64,%s"><title>Zernike %s</title></image>'
                     % (off + c * (cell + gap), off + r * (cell + gap), cell, cell,
                        _png_b64(_cell_image(k, cell * 2, False).resize((cell * 2, cell * 2))), MODE_NAMES[k]))
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d"><title>UltrafastEngine</title>'
            '<path fill="%s" d="%s"/><path fill="%s" d="%s"/>%s</svg>' % (width, height, width, height, _hex(col), t1, accent, t2, "".join(tiles)))


# ------------------------------------------------------------------------------------------------------- ICO
def write_ico(path: Path, sizes: Sequence[int] = (16, 24, 32, 48, 64, 128, 256)) -> None:
    """A multi-size .ico with PNG-compressed entries (Windows Vista and later); each size drawn at its own size."""
    blobs = []
    for s in sizes:
        buf = io.BytesIO()
        mark(s).save(buf, "PNG", optimize=True)
        blobs.append(buf.getvalue())
    head = struct.pack("<HHH", 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries = b""
    for s, b in zip(sizes, blobs):
        entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(b), offset)
        offset += len(b)
    path.write_bytes(head + entries + b"".join(blobs))


def generate(out: Path, png_sizes: Sequence[int] = (), ico: Path = None) -> List[Path]:
    """Write the three SVGs into `out`, the icon to `ico` (if given) and, for each of `png_sizes`, PNGs of the mark."""
    out.mkdir(parents=True, exist_ok=True)
    written: List[Path] = []
    for name, text in (("ultrafast-logo-small.svg", svg_small()), ("ultrafast-logo-large.svg", svg_large(256, False)),
                       ("ultrafast-logo-large-dark.svg", svg_large(256, True))):
        (out / name).write_text(text, encoding="utf-8")
        written.append(out / name)
    for sz in png_sizes:
        for img, name in ((mark(sz), "small-%d.png" % sz), (large(sz, False), "large-%d.png" % sz), (large(sz, True), "large-dark-%d.png" % sz)):
            img.save(out / name, "PNG", optimize=True)
            written.append(out / name)
    if ico is not None:
        write_ico(ico)
        written.append(ico)
    return written


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", type=Path, default=HERE, help="output folder (default: this folder)")
    ap.add_argument("--png-sizes", type=int, nargs="*", default=[], help="optional: also write PNGs (mark and large logos) at these heights in px")
    ap.add_argument("--ico", type=Path, default=HERE.parent / "ultrafast.ico", help="application icon (default: assets/ultrafast.ico)")
    ap.add_argument("--no-ico", action="store_true", help="do not write the icon")
    a = ap.parse_args()
    for p in generate(a.out, a.png_sizes, None if a.no_ico else a.ico):
        print(p)


if __name__ == "__main__":
    main()
