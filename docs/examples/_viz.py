"""Figure helpers for the AdaptiveOptics, DigitalTwin and Fibres examples (not an example).

Same idea as ``Computing/_figs.py`` (hero figure, GIF animation, size budget) plus small helpers for maps, heat maps and a
shared look. An example's ``make_figures.py`` imports it:

    sys.path.insert(0, str(Path(__file__).resolve().parents[1])); import _viz as V
    f = V.fig(11, 3.6); ax = f.subplots(1, 3); V.image(ax[0], W, "wavefront", extent=V.extent_mm(x), cmap="RdBu_r", symmetric=True, label="waves")
    V.save_hero(f, __file__); V.save_animation(frame, frames, __file__, fps=6)
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import _figures                                                       # noqa: E402  (puts python/src and build/ on sys.path)

import matplotlib                                                     # noqa: E402

matplotlib.use("Agg")
import numpy as np                                                    # noqa: E402
from ultrafast.plotting import animate as _animate                  # noqa: E402
from ultrafast.plotting import style                                  # noqa: E402

parser = _figures.parser
SERIES = None


def series():
    return style._theme["series"]


def fig(w: float, h: float):
    style.set_theme(dark=False)
    return style._figure(w, h)


def folder(example_file) -> Path:
    return Path(example_file).resolve().parent


def save_hero(figure, example_file, name: str = "hero.png", dpi: int = 100) -> Path:
    path = folder(example_file) / name
    size = _figures.save_png(figure, path, dpi)
    style._plt()[1].close(figure)
    print("wrote %s (%d kB)%s" % (path.name, size // 1000, "  OVER BUDGET" if size > _figures.PNG_BUDGET else ""))
    return path


def save_animation(frame, frames, example_file, name: str = "animation.gif", fps: float = 5, dpi: int = 70, pause_last_s: float = 1.5):
    path = folder(example_file) / name
    r = _animate.animate(frame, frames, path, fps=fps, dpi=dpi, pause_last_s=pause_last_s, progress_bar=True, colors=96)
    print("wrote %s (%d frames, %d kB)%s" % (path.name, r["frames"], r["bytes"] // 1000, "  OVER BUDGET" if r["overBudget"] else ""))
    return r


def extent_mm(x, y=None, scale=1e3):
    """imshow extent (mm) for an array indexed [x][y] drawn with origin='lower' after a transpose."""
    y = x if y is None else y
    return [x[0] * scale, x[-1] * scale, y[0] * scale, y[-1] * scale]


def image(ax, z, title="", *, extent=None, cmap=None, symmetric=False, vmin=None, vmax=None, label=None, colorbar=True, transpose=True, log=False):
    """A map z[ix][iy] drawn with x to the right and y up. ``symmetric`` centres a diverging scale on zero."""
    import matplotlib as mpl
    a = np.asarray(z, float)
    a = a.T if transpose else a
    if symmetric:
        m = np.nanmax(np.abs(a)) if vmax is None else vmax
        vmin, vmax = -m, m
        cmap = cmap or "RdBu_r"
    norm = None
    if log:
        norm = mpl.colors.LogNorm(vmin=vmin or max(np.nanmax(a) * 1e-4, 1e-30), vmax=vmax or np.nanmax(a)); vmin = vmax = None
    im = ax.imshow(a, origin="lower", extent=extent, cmap=cmap or style._cmap(), vmin=vmin, vmax=vmax, norm=norm, aspect="equal", interpolation="nearest")
    ax.set_title(title); ax.grid(False)
    if colorbar:
        cb = ax.figure.colorbar(im, ax=ax, fraction=0.046, pad=0.03)
        if label:
            cb.set_label(label)
        cb.ax.tick_params(labelsize=7)
    if extent is None:
        ax.set_xticks([]); ax.set_yticks([])
    return im


def heat(ax, z, x, y, title="", *, xlabel="", ylabel="", cmap=None, label=None, vmin=None, vmax=None, log=False):
    """A heat map z[iy][ix] over axes x (columns) and y (rows), y up."""
    import matplotlib as mpl
    a = np.asarray(z, float)
    norm = mpl.colors.LogNorm(vmin=vmin or max(np.nanmax(a) * 1e-4, 1e-30), vmax=vmax or np.nanmax(a)) if log else None
    im = ax.imshow(a, origin="lower", aspect="auto", extent=[x[0], x[-1], y[0], y[-1]], cmap=cmap or style._cmap(), norm=norm,
                   vmin=None if log else vmin, vmax=None if log else vmax)
    ax.set_title(title); ax.set_xlabel(xlabel); ax.set_ylabel(ylabel); ax.grid(False)
    cb = ax.figure.colorbar(im, ax=ax, fraction=0.046, pad=0.03)
    if label:
        cb.set_label(label)
    cb.ax.tick_params(labelsize=7)
    return im


def crop(z, x, y, half):
    """The part of a map z[ix][iy] with |x|, |y| <= half; returns (z, x, y)."""
    x, y = np.asarray(x), np.asarray(y)
    ix, iy = np.abs(x) <= half, np.abs(y) <= half
    return np.asarray(z)[np.ix_(ix, iy)], x[ix], y[iy]


def disc(z, x, y, radius):
    """z[ix][iy] with NaN outside the disc of the given radius."""
    X, Y = np.meshgrid(x, y, indexing="ij")
    return np.where(np.hypot(X, Y) <= radius, z, np.nan)


def headline(f, text):
    f.suptitle(text, x=0.01, ha="left", fontsize=10.5, fontweight="bold")


def check(label: str, ok: bool, detail: str = ""):
    """Print a verified statement; raise when it does not hold, so a regression breaks the example check."""
    print(("ok   " if ok else "FAIL ") + label + (": " + detail if detail else ""))
    if not ok:
        raise SystemExit("check failed: " + label)
