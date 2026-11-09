"""Publication-style plots of UltrafastEngine results (needs matplotlib: ``pip install ultrafast-engine[plot]``).

Works on a live :class:`ultrafast.Result` *or* on a results folder written by the CLI
(``ultrafast run ... -o results``) -- the folder only needs ``summary.json`` and ``result.npz``, so plotting
does not need the engine.

From the shell (see ``python -m ultrafast.plot --help``)::

    ultrafast-plot results/                      # PNGs in results/plots/ + results/plots/report.pdf
    ultrafast-plot results/ --show               # also open the figures in a window
    ultrafast-plot runA/ runB/ runC/             # each run's report + a comparison figure
    ultrafast-plot results/ --only overview,instruments --format svg --dark

From Python::

    from ultrafast import plotting as up
    res = up.load("results")                     # or a live Result
    up.overview(res).savefig("overview.png")
    up.report(res, "results/plots")              # every figure

Figures: ``system`` (schematic: elements, beam colour = carrier wavelength, aux beams, by-products, numbered
instrument locations -- every instrument title carries its number), ``overview`` (the schematic plus pulse in/out,
spectrum, spectral phase, evolution, losses, key numbers),
``instruments`` (one panel per instrument: spectrometer, autocorrelator, d-scan, contrast, polarimeter, ...),
``probes`` (spectral and temporal evolution through the probes), ``elements`` (per-element quantities) and
``compare`` (several runs overlaid).
"""
from __future__ import annotations

from pathlib import Path
from typing import Callable, Dict, Iterable, Optional, Union

from .style import (C, _LIGHT, _DARK, _theme, set_theme, _plt, _rc, _cmap, _figure, _Style, _PREFIX, _si, fmt, _note, _end_label, _db, _auto_floor, _sanitize)  # noqa: F401
from .data import (SavedResult, load, _has, _label)  # noqa: F401
from .time_domain import (_time_axis, _residual_phase, temporal)  # noqa: F401
from .spectral import (_sorted_spectrum, _wl_range, spectrum, spectral_phase)  # noqa: F401
from .run_figures import (evolution, _element_ticks, losses, key_numbers, _wrap, overview, elements, compare, byproducts, gain, oscillator)  # noqa: F401
from .spatial import (_image)  # noqa: F401
from .instrument_panels import (_instrument_panel, _generic_panel, _axis_scale, _dscan_panels, instruments, probes, _AXIS_LABELS, _named_axis, _PLAIN_UNITS, _fmtu, _value_unit, measurements)  # noqa: F401
from .diagrams import system, system_diagram  # noqa: F401
from . import animate, zernike  # noqa: F401  (submodules: plotting.animate, plotting.zernike)


FIGURES = ["system", "overview", "instruments", "measurements", "probes", "elements", "gain", "oscillator", "byproducts"]


_EXTRA_FIGURES: Dict[str, Callable] = {}


def register_figure(name: str, maker: Callable, after: Optional[str] = None) -> None:
    """Add a figure to :func:`report` and ``ultrafast-plot`` (used by plugins, e.g. the fitting plugin's
    ``measurements`` and ``fit``). ``maker(res)`` returns a matplotlib figure, or None when the result has no data
    for it. ``after`` places it after an existing figure in the default order."""
    _EXTRA_FIGURES[name] = maker
    if name not in FIGURES:
        FIGURES.insert(FIGURES.index(after) + 1 if after in FIGURES else len(FIGURES), name)


def report(res, out_dir: Union[str, Path, None] = None, formats: Iterable[str] = ("png",), pdf: bool = True,
           only: Optional[Iterable[str]] = None, db: bool = False, dpi: int = 160, close: bool = True) -> Dict[str, object]:
    """Draw every figure that has data, save them as ``<out_dir>/<name>.<fmt>`` and (optionally) all of them in
    one ``report.pdf``. ``out_dir`` defaults to ``<results folder>/plots``. Returns {name: figure or path}."""
    _, plt = _plt()
    res = load(res)
    if out_dir is None:
        base = getattr(res, "directory", Path("."))
        out_dir = Path(base) / "plots"
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    from .. import plugins as _plugins
    _plugins.load_python_plugins()
    makers = {"system": lambda: system(res), "overview": lambda: overview(res, db=db), "instruments": lambda: instruments(res), "measurements": lambda: measurements(res),
              "probes": lambda: probes(res), "elements": lambda: elements(res), "gain": lambda: gain(res), "oscillator": lambda: oscillator(res), "byproducts": lambda: byproducts(res)}
    for name, mk in _EXTRA_FIGURES.items():
        makers[name] = (lambda m: (lambda: m(res)))(mk)
    wanted = [w for w in (only or FIGURES) if w in makers]
    figs = {}
    for w in wanted:
        f = makers[w]()
        if f is not None:
            figs[w] = f
    written = {}
    for name, f in figs.items():
        for ext in formats:
            p = out_dir / f"{name}.{ext}"
            f.savefig(p, dpi=dpi)
            written.setdefault(name, []).append(p)
    if pdf and figs:
        from matplotlib.backends.backend_pdf import PdfPages
        with PdfPages(out_dir / "report.pdf") as pp:
            for f in figs.values():
                pp.savefig(f)
        written["report.pdf"] = [out_dir / "report.pdf"]
    if close:
        for f in figs.values():
            plt.close(f)
        return written
    return figs


def plot(result, what="overview", ax=None):
    """``uf.plot(result, what)``: "overview" or "measurements" (figures), or a single panel "temporal" / "spectrum" / "phase" /
    "losses" / "elements" on ``ax``."""
    if what == "overview":
        return overview(result)
    if what == "measurements":
        return measurements(result)
    panels = {"temporal": temporal, "spectrum": spectrum, "phase": spectral_phase, "losses": losses,
              "numbers": key_numbers}
    if what == "elements":
        return evolution(result) if ax is None else evolution(result, [ax, ax.twinx(), ax.twinx()])
    if what in panels:
        return panels[what](result, ax)
    raise ValueError(f"unknown plot '{what}' (overview, measurements, temporal, spectrum, phase, losses, elements, numbers)")

