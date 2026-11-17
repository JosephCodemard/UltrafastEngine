"""Styles, colour themes, matplotlib rc, SI-prefix formatting and small axis helpers shared by every figure.

Styles (``use_style``): ``"ultrafast"`` (brand colours from the logo, light, the default), ``"ultrafast-dark"``, ``"paper"`` (publication:
serif text with Computer Modern maths, inward ticks on all four sides, minor ticks, no grid, column widths 3.4 in / 7.0 in, 600 dpi) and
``"science"`` (the SciencePlots look when that package is installed, else ``paper``).

>>> from ultrafast.plotting import use_style
>>> use_style("paper")                      # from now on
>>> with use_style("ultrafast-dark"):       # only inside the block
...     fig = uf.plotting.overview(res)

``ULTRAFAST_PLOT_STYLE`` sets the style at import (and is the default of ``use_style()`` without a name); ``ultrafast-plot --style NAME``
sets it for the command line. Palette checks: ``ultrafast`` and ``ultrafast-dark`` pass the dataviz validator for six series
(lightness band, chroma, colour-vision-deficiency separation, normal-vision separation); on the light surface the yellow, green and pink
are below 3:1 against the background, so lines carry direct labels or a legend. ``paper`` uses the Okabe-Ito colours (a published
colour-blind-safe set) in an order that passes the same checks, and cycles line styles as well, so it also reads in grey.
"""
from __future__ import annotations

import os
import warnings
from typing import Dict, Optional, Tuple

import numpy as np


C = 299792458.0

STYLES = ("ultrafast", "ultrafast-dark", "paper", "science")
FIGURE_WIDTH_IN = {"single": 3.4, "double": 7.0}      # journal column widths (inches) used by the paper styles

# Brand colours of assets/logo/*.svg: blue #2c60a2 .. #336eb0 and orange #dd7538 .. #f4a064 on #f0f3f7 / #18202c. The series start with
# that blue and orange; the remaining hues were chosen so that six series pass the colour-vision checks (see the module docstring).
_LIGHT = dict(name="ultrafast", surface="#fcfcfb", panel="#ffffff", text="#0b0b0b", text2="#52514e", grid="#e4e3df", ref="#8f8e89",
              series=["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"],
              seq=["#fcfcfb", "#cde2fb", "#9ec5f4", "#6da7ec", "#3987e5", "#256abf", "#184f95", "#0d366b"])


_DARK = dict(name="ultrafast-dark", surface="#1a1a19", panel="#26261f", text="#f0efec", text2="#c3c2b7", grid="#34332f", ref="#8a8983",
             series=["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"],
             seq=["#1a1a19", "#0d366b", "#184f95", "#256abf", "#3987e5", "#6da7ec", "#9ec5f4", "#cde2fb"])


def _paper_rc(width_in: float) -> Dict[str, object]:
    import cycler
    return {
        "font.family": ["serif", "DejaVu Sans"], "font.serif": ["STIX Two Text", "Times New Roman", "Times", "DejaVu Serif"], "mathtext.fontset": "cm",
        "font.size": 8, "axes.labelsize": 8, "axes.titlesize": 8, "axes.titleweight": "normal", "axes.titlelocation": "center",
        "xtick.labelsize": 7, "ytick.labelsize": 7, "legend.fontsize": 7,
        "axes.grid": False, "axes.linewidth": 0.6, "axes.spines.top": True, "axes.spines.right": True,
        "xtick.direction": "in", "ytick.direction": "in", "xtick.top": True, "ytick.right": True,
        "xtick.minor.visible": True, "ytick.minor.visible": True,
        "xtick.major.size": 3.0, "ytick.major.size": 3.0, "xtick.minor.size": 1.6, "ytick.minor.size": 1.6,
        "xtick.major.width": 0.6, "ytick.major.width": 0.6, "xtick.minor.width": 0.4, "ytick.minor.width": 0.4,
        "lines.linewidth": 1.0, "lines.markersize": 3.5, "savefig.dpi": 600, "figure.dpi": 150, "pdf.fonttype": 42, "ps.fonttype": 42,
        "axes.prop_cycle": cycler.cycler(color=_PAPER["series"]) + cycler.cycler(linestyle=["-", "--", "-.", ":", (0, (5, 1)), (0, (3, 1, 1, 1)), (0, (1, 1)), (0, (5, 1, 1, 1, 1, 1))]),
    }


# Okabe and Ito (2008) colours, ordered so that six series pass the adjacent-pair colour-vision check (worst delta E 9.6)
_PAPER = dict(name="paper", surface="#ffffff", panel="#ffffff", text="#000000", text2="#222222", grid="#d9d9d9", ref="#666666",
              series=["#0072b2", "#d55e00", "#e69f00", "#009e73", "#56b4e9", "#cc79a7", "#000000", "#999999"],
              seq=["#ffffff", "#d6e6f3", "#a9cbe6", "#6fa6d2", "#3d80b8", "#1f5f9b", "#103f72", "#08234a"], dpi=600)


_theme: Dict[str, object] = dict(_LIGHT)


def _science_theme() -> Optional[Dict[str, object]]:
    """The SciencePlots look (optional extra ``paper``) as a theme, or ``None`` when it is not installed. Only the package's installed
    style sheets are used, nothing is copied into this project."""
    try:
        import scienceplots  # noqa: F401  (registers the 'science' and 'no-latex' styles)
        import matplotlib.pyplot as plt
        rc = dict(plt.style.library["science"])
        rc.update(plt.style.library.get("no-latex", {}))
    except Exception:
        return None
    rc["font.family"] = ["serif", "DejaVu Sans"]       # symbols such as the warning sign fall back to a font that has them
    rc["savefig.dpi"] = max(float(rc.get("savefig.dpi", 0) or 0), 600.0)
    return dict(_PAPER, name="science", rc=rc)


def _resolve(name: str, column: Optional[str]) -> Dict[str, object]:
    key = name.strip().lower().replace("_", "-")
    if key not in STYLES:
        raise ValueError(f"unknown plot style '{name}' (use {', '.join(STYLES)})")
    if key == "ultrafast":
        return dict(_LIGHT)
    if key == "ultrafast-dark":
        return dict(_DARK)
    if column not in (None, "single", "double"):
        raise ValueError("column must be 'single' (3.4 in) or 'double' (7.0 in)")
    width = FIGURE_WIDTH_IN[column or "double"]
    if key == "science":
        t = _science_theme()
        if t is None:
            warnings.warn("plot style 'science' needs SciencePlots (pip install SciencePlots, extra 'paper'); using 'paper'", stacklevel=3)
            key = "paper"
        else:
            t["figure_width_in"] = width
            return t
    t = dict(_PAPER)
    t["rc"] = _paper_rc(width)
    t["figure_width_in"] = width
    return t


class _StyleHandle:
    """What :func:`use_style` returns: the style is already active; ``with`` restores the previous one on exit."""

    def __init__(self, previous: Dict[str, object], name: str):
        self._previous, self.name = previous, name

    def __enter__(self) -> "_StyleHandle":
        return self

    def __exit__(self, *exc) -> None:
        _theme.clear()
        _theme.update(self._previous)

    def __repr__(self) -> str:
        return f"<plot style '{self.name}'>"


def use_style(name: Optional[str] = None, *, column: Optional[str] = None) -> _StyleHandle:
    """Select the plot style for figures created afterwards: ``"ultrafast"``, ``"ultrafast-dark"``, ``"paper"`` or ``"science"``.

    Works as a plain call (stays in force) and as a context manager (restored on exit). Without a name the style comes from the
    environment variable ``ULTRAFAST_PLOT_STYLE`` (default ``"ultrafast"``). ``column`` (paper styles) is ``"single"`` (3.4 in) or
    ``"double"`` (7.0 in, default): figures are scaled to that width, text stays at its point size, and files are written at 600 dpi.
    ``"science"`` uses SciencePlots when installed and otherwise warns and uses ``"paper"``."""
    resolved = name if name is not None else os.environ.get("ULTRAFAST_PLOT_STYLE", "ultrafast") or "ultrafast"
    new = _resolve(resolved, column)
    previous = dict(_theme)
    _theme.clear()
    _theme.update(new)
    return _StyleHandle(previous, str(new["name"]))


def current_style() -> str:
    """Name of the style in force."""
    return str(_theme.get("name", "ultrafast"))


def default_dpi(fallback: int = 160) -> int:
    """Resolution of saved figures: the style's own (600 for the paper styles) or ``fallback``."""
    return int(_theme.get("dpi", 0) or (_theme.get("rc", {}) or {}).get("savefig.dpi", 0) or fallback)


def save_dpi(fig, dpi: Optional[int] = None, max_pixels_wide: int = 4800) -> int:
    """Resolution for saving ``fig``: ``dpi`` if given, else the style's (600 for the paper styles, 160 otherwise) but never more than
    ``max_pixels_wide`` pixels across (a 15-inch dashboard at 600 dpi would be 9000 pixels wide)."""
    if dpi:
        return int(dpi)
    d = default_dpi()
    return int(min(d, max(160, max_pixels_wide / max(fig.get_figwidth(), 1e-6))))


def set_theme(dark: bool = False) -> None:
    """Switch the colour theme for figures created afterwards (``use_style("ultrafast-dark" | "ultrafast")``)."""
    use_style("ultrafast-dark" if dark else "ultrafast")


def _plt():
    try:
        import matplotlib
        import matplotlib.pyplot as plt
    except ImportError as e:  # pragma: no cover
        raise ImportError("Plotting needs matplotlib: pip install matplotlib (or ultrafast-engine[plot])") from e
    return matplotlib, plt


def _rc():
    t = _theme
    rc = {
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
    extra = t.get("rc")
    if extra:
        rc.update({"axes.grid": False, "axes.spines.top": True, "axes.spines.right": True, "xtick.major.size": 3.0, "ytick.major.size": 3.0})
        rc.update(extra)
        rc.update({"figure.facecolor": t["surface"], "axes.facecolor": t["surface"], "savefig.facecolor": t["surface"]})
        if t.get("name") == "paper":                       # the paper style prints in black ink
            rc.update({"axes.edgecolor": t["text"], "axes.labelcolor": t["text"], "xtick.color": t["text"], "ytick.color": t["text"]})
    return rc


def _cmap():
    matplotlib, _ = _plt()
    return matplotlib.colors.LinearSegmentedColormap.from_list("uf_seq", _theme["seq"])


def _figure(w, h):
    _, plt = _plt()
    width = _theme.get("figure_width_in")
    if width and w <= 9:                                    # paper styles: a figure of up to ~one panel row is set to the journal column width
        w, h = float(width), float(h) * float(width) / float(w)
    with plt.rc_context(_rc()):
        fig = plt.figure(figsize=(w, h), layout="constrained")
    fig._uf_rc = _rc()  # remembered so later drawing uses the same style
    return fig


if os.environ.get("ULTRAFAST_PLOT_STYLE"):                  # the environment picks the style at import
    try:
        use_style()
    except ValueError as _e:  # pragma: no cover
        warnings.warn(f"ULTRAFAST_PLOT_STYLE ignored: {_e}")


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

