"""Colour themes, matplotlib style, SI-prefix formatting and small axis helpers shared by every figure."""
from __future__ import annotations

from typing import Tuple

import numpy as np




C = 299792458.0


_LIGHT = dict(surface="#fcfcfb", text="#0b0b0b", text2="#52514e", grid="#e4e3df", ref="#8f8e89",
              series=["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"],
              seq=["#fcfcfb", "#cde2fb", "#9ec5f4", "#6da7ec", "#3987e5", "#256abf", "#184f95", "#0d366b"])


_DARK = dict(surface="#1a1a19", text="#f0efec", text2="#c3c2b7", grid="#34332f", ref="#8a8983",
             series=["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"],
             seq=["#1a1a19", "#0d366b", "#184f95", "#256abf", "#3987e5", "#6da7ec", "#9ec5f4", "#cde2fb"])


_theme = dict(_LIGHT)


def set_theme(dark: bool = False) -> None:
    """Switch the colour theme for figures created afterwards."""
    _theme.clear()
    _theme.update(_DARK if dark else _LIGHT)


def _plt():
    try:
        import matplotlib
        import matplotlib.pyplot as plt
    except ImportError as e:  # pragma: no cover
        raise ImportError("Plotting needs matplotlib: pip install matplotlib (or ultrafast-engine[plot])") from e
    return matplotlib, plt


def _rc():
    t = _theme
    return {
        "figure.facecolor": t["surface"], "axes.facecolor": t["surface"], "savefig.facecolor": t["surface"],
        "axes.edgecolor": t["grid"], "axes.labelcolor": t["text2"], "axes.titlecolor": t["text"],
        "axes.titlesize": 10.5, "axes.titleweight": "bold", "axes.titlelocation": "left", "axes.labelsize": 9,
        "axes.grid": True, "grid.color": t["grid"], "grid.linewidth": 0.6, "axes.axisbelow": True,
        "axes.spines.top": False, "axes.spines.right": False,
        "xtick.color": t["text2"], "ytick.color": t["text2"], "xtick.labelsize": 8, "ytick.labelsize": 8,
        "xtick.major.size": 0, "ytick.major.size": 0, "text.color": t["text"],
        "legend.fontsize": 8, "legend.frameon": False, "lines.linewidth": 1.6, "font.size": 9,
        "axes.prop_cycle": __import__("cycler").cycler(color=t["series"]),
    }


def _cmap():
    matplotlib, _ = _plt()
    return matplotlib.colors.LinearSegmentedColormap.from_list("uf_seq", _theme["seq"])


def _figure(w, h):
    _, plt = _plt()
    with plt.rc_context(_rc()):
        fig = plt.figure(figsize=(w, h), layout="constrained")
    fig._uf_rc = _rc()  # remembered so later drawing uses the same style
    return fig


class _Style:
    """``with _Style(): ...`` applies the theme to everything drawn inside."""

    def __enter__(self):
        _, plt = _plt()
        self._ctx = plt.rc_context(_rc())
        self._ctx.__enter__()
        return self

    def __exit__(self, *a):
        self._ctx.__exit__(*a)


_PREFIX = [(1e-18, "a"), (1e-15, "f"), (1e-12, "p"), (1e-9, "n"), (1e-6, "µ"), (1e-3, "m"), (1.0, ""),
           (1e3, "k"), (1e6, "M"), (1e9, "G"), (1e12, "T"), (1e15, "P")]


def _si(x: float) -> Tuple[float, str]:
    """Scale and prefix so that x / scale is in [1, 1000)."""
    x = abs(float(x)) if np.isfinite(x) else 0.0
    if x == 0:
        return 1.0, ""
    for s, p in reversed(_PREFIX):
        if x >= s * 0.9995:
            return s, p
    return _PREFIX[0]


def fmt(x, unit: str = "", digits: int = 3) -> str:
    """Engineering formatting: fmt(2.2e-13, 's') -> '220 fs'."""
    if x is None or (isinstance(x, float) and not np.isfinite(x)):
        return "–"
    if unit in ("s^2", "s2"):
        return f"{x * 1e30:.{digits}g} fs²"
    if unit in ("s^3", "s3"):
        return f"{x * 1e45:.{digits}g} fs³"
    s, p = _si(x)
    return f"{x / s:.{digits}g} {p}{unit}".strip()


def _note(ax, text, loc="upper right"):
    xy = {"upper right": (0.98, 0.95, "right", "top"), "upper left": (0.02, 0.95, "left", "top"),
          "lower right": (0.98, 0.05, "right", "bottom"), "lower left": (0.02, 0.05, "left", "bottom"),
          "lower center": (0.5, 0.05, "center", "bottom")}[loc]
    ax.text(xy[0], xy[1], text, transform=ax.transAxes, ha=xy[2], va=xy[3], fontsize=8, color=_theme["text2"],
            linespacing=1.4)


def _end_label(ax, x, y, text, color):
    """Direct label at the right end of a line (identity not by colour alone)."""
    i = np.nanargmax(np.where(np.isfinite(y), np.arange(len(y)), -1))
    ax.annotate(text, (x[i], y[i]), xytext=(4, 0), textcoords="offset points", fontsize=8, color=_theme["text2"],
                va="center")


def _db(y, floor=-80.0):
    y = np.asarray(y, float)
    m = y.max() if y.size and y.max() > 0 else 1.0
    with np.errstate(divide="ignore"):
        return np.maximum(10 * np.log10(np.maximum(y / m, 1e-300)), floor)


def _auto_floor(*dbs, lowest=-120.0, pad=3.0):
    """A dB axis floor that shows every value of the data down to its own floor (an instrument's dynamic range),
    but not the numerical noise below ``lowest``: the minimum of the data, padded, rounded down to 10 dB."""
    vals = [np.nanmin(np.asarray(d, float)[np.isfinite(d)]) for d in dbs if np.size(d) and np.any(np.isfinite(d))]
    m = max(min(vals) if vals else -60.0, lowest)
    return float(10 * np.floor((m - pad) / 10))


def _sanitize(s):
    import re
    return re.sub(r"[^A-Za-z0-9_]", "_", s)

