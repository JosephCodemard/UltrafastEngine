"""Figure helpers shared by the Computing examples' make_figures.py (not an example)."""
from __future__ import annotations

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))        # docs/examples (_figures.py)
import _figures                                                      # noqa: E402  (also puts python/src and build/ on sys.path)

import matplotlib                                                    # noqa: E402

matplotlib.use("Agg")
from ultrafast.plotting import animate as _animate               # noqa: E402
from ultrafast.plotting import style                               # noqa: E402

parser = _figures.parser


def fig(w: float, h: float):
    style.set_theme(dark=False)
    return style._figure(w, h)


def folder(example_file: str) -> Path:
    return Path(example_file).resolve().parent


def save_hero(figure, example_file: str, dpi: int = 100) -> Path:
    path = folder(example_file) / "hero.png"
    size = _figures.save_png(figure, path, dpi)
    style._plt()[1].close(figure)
    print("wrote %s (%d kB)" % (path.name, size // 1000))
    return path


def save_animation(frame, frames, example_file: str, fps: float = 5, dpi: int = 70, pause_last_s: float = 1.5):
    path = folder(example_file) / "animation.gif"
    r = _animate.animate(frame, frames, path, fps=fps, dpi=dpi, pause_last_s=pause_last_s, progress_bar=True, colors=96)
    print("wrote %s (%d frames, %d kB)%s" % (path.name, r["frames"], r["bytes"] // 1000, "  OVER BUDGET" if r["overBudget"] else ""))
    return r


def show(ax, img, title, cmap=None, extent=None, vmax=None, signed=False):
    """An array drawn as an image: the first index runs down the page, the second across (so letters stored as rows read upright)."""
    import matplotlib as mpl
    cm = mpl.colormaps["RdBu_r"] if signed else (cmap or style._cmap())
    ax.imshow(img, origin="upper", cmap=cm, extent=extent, vmax=vmax, vmin=(-vmax if signed and vmax else None), interpolation="nearest")
    ax.set_title(title); ax.grid(False)
    ax.set_xticks([]); ax.set_yticks([])
