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

import json
import math
from pathlib import Path
from typing import Callable, Dict, Iterable, List, Optional, Sequence, Tuple, Union

import numpy as np

C = 299792458.0

# ----------------------------------------------------------------------------------------------- style
# Categorical slots in a fixed order (colour-vision-deficiency checked); "input" is a neutral reference.
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


# ----------------------------------------------------------------------------------------------- data access
class SavedResult:
    """A results folder written by the CLI / ``Result.write``: same interface as a live Result
    (``summary``, ``provenance``, ``names()``, ``result[name]``, ``instrument(name)``)."""

    def __init__(self, directory: Union[str, Path]):
        self.directory = Path(directory)
        s = self.directory / "summary.json"
        if not s.exists():
            raise FileNotFoundError(f"{s} not found -- is '{directory}' an UltrafastEngine results folder?")
        self.summary = json.loads(s.read_text(encoding="utf-8"))
        npz = self.directory / "result.npz"
        self._npz = np.load(npz) if npz.exists() else None
        if self._npz is None:
            raise FileNotFoundError(f"{npz} not found (enable outputs.npz in the run spec)")
        p = self.directory / "provenance.json"
        self.provenance = json.loads(p.read_text(encoding="utf-8")) if p.exists() else []

    @property
    def name(self) -> str:
        return self.summary.get("run") or self.directory.name

    def names(self) -> List[str]:
        return list(self._npz.files)

    def __contains__(self, k):
        return k in self._npz.files

    def __getitem__(self, k: str) -> np.ndarray:
        return self._npz[k]

    def instrument(self, name: str) -> dict:
        for i in self.summary.get("instruments", []):
            if i["name"] == name:
                d = dict(i["values"])
                for a in i["arrays"]:
                    d[a[len(name) + 1:] if a.startswith(name + "_") else a] = self[a]
                return d
        raise KeyError(name)


def load(source) -> "SavedResult":
    """A results folder (str/Path) -> :class:`SavedResult`; a live Result / SavedResult is returned as is."""
    if isinstance(source, (str, Path)):
        return SavedResult(source)
    return source


def _has(res, k):
    try:
        return k in res.names()
    except Exception:
        return False


def _label(res) -> str:
    n = getattr(res, "name", None)
    return n if isinstance(n, str) else res.summary.get("run", "run")


# ----------------------------------------------------------------------------------------------- units
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


def _time_axis(t: np.ndarray, *ys: np.ndarray, level=1e-3, margin=0.35) -> Tuple[float, float, float, str]:
    """Crop range around where any y exceeds `level` of its peak; returns (lo, hi, scale, label)."""
    lo, hi = np.inf, -np.inf
    for y in ys:
        y = np.asarray(y, float)
        if y.size == 0 or y.size != t.size or not np.any(y > 0):
            continue
        m = np.nonzero(y >= level * y.max())[0]
        lo, hi = min(lo, t[m[0]]), max(hi, t[m[-1]])
    if not np.isfinite(lo):
        lo, hi = t[0], t[-1]
    span = max(hi - lo, (t[1] - t[0]) * 8 if t.size > 1 else 1e-15)
    lo, hi = max(t[0], lo - margin * span), min(t[-1], hi + margin * span)
    s, p = _si(max(abs(lo), abs(hi)))
    if p not in ("a", "f", "p", "n", "µ", "m", ""):
        s, p = 1.0, ""
    return lo, hi, s, f"time ({p}s)"


def _sorted_spectrum(lam, s, *extra):
    ok = np.isfinite(lam) & (lam > 0)
    o = np.argsort(lam[ok])
    return (lam[ok][o], s[ok][o]) + tuple(e[ok][o] for e in extra)


def _wl_range(pairs, level=1e-3, margin=0.15):
    lo, hi = np.inf, -np.inf
    for lam, s in pairs:
        if s.size == 0 or not np.any(s > 0):
            continue
        m = np.nonzero(s >= level * s.max())[0]
        lo, hi = min(lo, lam[m[0]]), max(hi, lam[m[-1]])
    if not np.isfinite(lo):
        return None
    span = hi - lo
    return lo - margin * span, hi + margin * span


def _auto_floor(*dbs, lowest=-120.0, pad=3.0):
    """A dB axis floor that shows every value of the data down to its own floor (an instrument's dynamic range),
    but not the numerical noise below ``lowest``: the minimum of the data, padded, rounded down to 10 dB."""
    vals = [np.nanmin(np.asarray(d, float)[np.isfinite(d)]) for d in dbs if np.size(d) and np.any(np.isfinite(d))]
    m = max(min(vals) if vals else -60.0, lowest)
    return float(10 * np.floor((m - pad) / 10))


def _db(y, floor=-80.0):
    y = np.asarray(y, float)
    m = y.max() if y.size and y.max() > 0 else 1.0
    with np.errstate(divide="ignore"):
        return np.maximum(10 * np.log10(np.maximum(y / m, 1e-300)), floor)


def _residual_phase(lam, phase, spec, level=1e-2):
    """Spectral phase vs frequency with offset and group delay (linear term) removed, inside the
    region where the spectrum exceeds `level` of its peak. Returns (lam, phase) with NaN outside."""
    w = 2 * np.pi * C / lam
    m = spec >= level * spec.max()
    out = np.full_like(phase, np.nan)
    if m.sum() < 4:
        return out
    idx = np.nonzero(m)[0]
    seg = slice(idx[0], idx[-1] + 1)
    ph = np.unwrap(phase[seg])
    wt = spec[seg]
    x = (w[seg] - w[seg].mean()) / max(np.ptp(w[seg]), 1e-300)  # dimensionless, so both columns are O(1)
    A = np.vstack([np.ones_like(x), x]).T * np.sqrt(wt)[:, None]
    coef, *_ = np.linalg.lstsq(A, ph * np.sqrt(wt), rcond=None)
    res = ph - (coef[0] + coef[1] * x)
    out[seg] = np.where(m[seg], res, np.nan)
    return out


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


# ----------------------------------------------------------------------------------------------- panels
def temporal(res, ax=None, compressed: bool = True, normalize: bool = False):
    """Input and output power vs time (plus the ideally compressed output, if an ideal compressor ran)."""
    _, plt = _plt()
    res = load(res)
    with _Style():
        ax = ax or plt.gca()
        t_in, p_in = res["input_time_s"], res["input_power_w"]
        t_out, p_out = res["output_time_s"], res["output_power_w"]
        series = [("input", t_in, p_in, _theme["ref"], "--"), ("output", t_out, p_out, _theme["series"][0], "-")]
        if compressed:
            for i in res.summary.get("instruments", []):
                if i["kind"] == "compressor" and i["arrays"]:
                    tc, pc = res[i["arrays"][0]], res[i["arrays"][1]]
                    series.append((f"{i['name'].replace('_', ' ')}", tc, pc, _theme["series"][1], "-"))
                    break
        # input and output may live on different grids (carrier changes in crystals): crop each on its own axis
        lo1, hi1, _, _ = _time_axis(t_out, p_out)
        lo2, hi2, _, _ = _time_axis(t_in, p_in)
        lo, hi = min(lo1, lo2), max(hi1, hi2)
        ts, pfx = _si(max(abs(lo), abs(hi)))
        if pfx not in ("a", "f", "p", "n", "µ", "m", ""):
            ts, pfx = 1.0, ""
        tlabel = f"time ({pfx}s)"
        peak = max(p.max() for _, _, p, _, _ in series)
        ps, pp = _si(peak)
        for name, t, p, c, ls in series:
            y = p / p.max() if normalize else p / ps
            ax.plot(t / ts, y, color=c, ls=ls, label=name)
        ax.set_xlim(lo / ts, hi / ts)
        ax.set_xlabel(tlabel)
        ax.set_ylabel("power (norm.)" if normalize else f"power ({pp}W)")
        ax.set_ylim(bottom=0)
        ax.legend(loc="upper left")
        o = res.summary["output"]
        _note(ax, f"FWHM {fmt(o['fwhmS'], 's')}\nTL {fmt(o['transformLimitFwhmS'], 's')}\npeak {fmt(o['peakPowerW'], 'W')}")
        ax.set_title("Pulse")
    return ax


def spectrum(res, ax=None, db: bool = False, floor_db: Optional[float] = None):
    """Input and output spectral density vs wavelength (linear, normalised; or dB). With ``db``, the floor follows
    the data (down to -120 dB) unless ``floor_db`` is given, and the wavelength range covers everything above it."""
    _, plt = _plt()
    res = load(res)
    with _Style():
        ax = ax or plt.gca()
        data = []
        for k, c, ls in (("input", _theme["ref"], "--"), ("output", _theme["series"][0], "-")):
            lam, s = _sorted_spectrum(res[k + "_wavelength_m"], res[k + "_spectrum"])
            data.append((k, lam, s, c, ls))
        if db and floor_db is None:
            floor_db = _auto_floor(*[_db(d[2], -300) for d in data])
        r = _wl_range([(d[1], d[2]) for d in data], level=10 ** (floor_db / 10) if db else 1e-3)
        for k, lam, s, c, ls in data:
            ax.plot(lam * 1e9, _db(s, floor_db - 5) if db else s / s.max(), color=c, ls=ls, label=k)
        if r:
            ax.set_xlim(r[0] * 1e9, r[1] * 1e9)
        ax.set_xlabel("wavelength (nm)")
        ax.set_ylabel("spectral density (dB)" if db else "spectral density (norm.)")
        if db:
            ax.set_ylim(floor_db, 3)
        else:
            ax.set_ylim(bottom=0)
        ax.legend(loc="upper left")
        o = res.summary["output"]
        _note(ax, f"centroid {o['centroidWavelengthM'] * 1e9:.1f} nm\nFWHM {fmt(o['spectralFwhmM'], 'm')}")
        ax.set_title("Spectrum")
    return ax


def spectral_phase(res, ax=None, xlim=None):
    """Output (and input) spectral phase with offset and group delay removed, where the spectrum is > 1 %."""
    _, plt = _plt()
    res = load(res)
    with _Style():
        ax = ax or plt.gca()
        for k, c, ls in (("input", _theme["ref"], "--"), ("output", _theme["series"][0], "-")):
            if not _has(res, k + "_phase_x_rad"):
                continue
            lam, s, ph = _sorted_spectrum(res[k + "_wavelength_m"], res[k + "_spectrum"], res[k + "_phase_x_rad"])
            # unwrap along frequency: wavelength-sorted arrays are frequency-reversed, which unwrap handles
            ax.plot(lam * 1e9, _residual_phase(lam, ph, s), color=c, ls=ls, label=k)
        if xlim is not None:
            ax.set_xlim(*xlim)
        ax.set_xlabel("wavelength (nm)")
        ax.set_ylabel("phase (rad)")
        o = res.summary["output"]
        _note(ax, f"pulse GDD {fmt(o.get('pulseGddS2'), 's^2')}\npulse TOD {fmt(o.get('pulseTodS3'), 's^3')}")
        ax.set_title("Spectral phase (group delay removed)")
    return ax


def evolution(res, axs=None):
    """Energy, FWHM and cumulative B-integral after each element (three stacked panels, shared x)."""
    _, plt = _plt()
    res = load(res)
    els = res.summary["elements"]
    with _Style():
        if axs is None:
            fig = _figure(6, 6)
            axs = fig.subplots(3, 1, sharex=True)
        x = np.arange(len(els))
        e = np.array([el["energyOutJ"] for el in els])
        f = np.array([el["pulse"]["fwhmS"] for el in els])
        b = np.array([el["cumulativeBIntegralRad"] for el in els])
        es, ep = _si(e.max()); fs, fp = _si(np.nanmax(f))
        rows = [(axs[0], e / es, f"energy ({ep}J)"), (axs[1], f / fs, f"FWHM ({fp}s)"), (axs[2], b, "cum. B (rad)")]
        for ax, y, lab in rows:
            ax.plot(x, y, "-", color=_theme["series"][0], marker="o" if len(x) <= 40 else None, ms=3.5)
            ax.set_ylabel(lab)
        axs[0].set_title("Through the system")
        _element_ticks(axs[-1], els)
    return axs


def _element_ticks(ax, els, max_labels=28):
    n = len(els)
    if n <= max_labels:
        ax.set_xticks(range(n))
        ax.set_xticklabels([el["id"] for el in els], rotation=60, ha="right", fontsize=7)
    else:
        step = int(math.ceil(n / max_labels))
        idx = list(range(0, n, step))
        ax.set_xticks(idx)
        ax.set_xticklabels([els[i]["id"] for i in idx], rotation=60, ha="right", fontsize=7)
    ax.set_xlabel("element")


def losses(res, ax=None):
    """Energy budget by mechanism. Losses are positive; energy brought in by auxiliary beams (crystals, amplifiers)
    is negative ("from pump (aux)"). Shown as % of all energy that entered (input + auxiliary beams)."""
    _, plt = _plt()
    res = load(res)
    with _Style():
        ax = ax or plt.gca()
        lb = res.summary.get("energyBudget", res.summary.get("lossBudget", {})) or {}
        e_in = res.summary["input"]["energyJ"] + sum(-v["energyJ"] for v in lb.values() if v["energyJ"] < 0)
        items = sorted(((k, v["energyJ"] / max(e_in, 1e-300)) for k, v in lb.items()
                        if abs(v["energyJ"]) >= 1e-6 * max(e_in, 1e-300)), key=lambda kv: -abs(kv[1]))
        if not items:
            ax.text(0.5, 0.5, "no losses above 1 ppm", ha="center", va="center", transform=ax.transAxes,
                    color=_theme["text2"])
            ax.set_axis_off()
        else:
            names = [k for k, _ in items][::-1]
            vals = np.array([v for _, v in items][::-1]) * 100
            colors = [_theme["series"][0] if v >= 0 else _theme["series"][2] for v in vals]
            ax.barh(names, vals, height=0.55, color=colors, edgecolor=_theme["surface"], linewidth=2)
            for i, v in enumerate(vals):
                ax.annotate(f"{v:.3g} %", (v, i), xytext=(4 if v >= 0 else -4, 0), textcoords="offset points",
                            va="center", ha="left" if v >= 0 else "right", fontsize=8, color=_theme["text2"])
            lo, hi = min(vals.min(), 0), max(vals.max(), 0)
            span = max(hi - lo, 1e-9)
            ax.set_xlim(lo - 0.35 * span * (lo < 0), hi + 0.3 * span)
            if lo < 0:
                ax.axvline(0, color=_theme["text2"], lw=0.8)
            ax.set_xlabel("% of energy in (input + aux)  ·  losses > 0, gain from aux beams < 0" if lo < 0
                          else "loss (% of input energy)")
            ax.set_ylim(-0.6, max(len(vals), 4) - 0.4)  # constant bar thickness however many mechanisms
            ax.grid(axis="y", visible=False)
        T = res.summary["totals"]["energyTransmission"]
        ax.set_title(f"Energy budget (out / in = {T * 100:.4g} %)")
    return ax


def key_numbers(res, ax=None):
    """Text panel with the headline numbers, warnings and notes."""
    _, plt = _plt()
    res = load(res)
    s = res.summary
    i, o, tot = s["input"], s["output"], s["totals"]
    rows = [
        ("energy", f"{fmt(i['energyJ'], 'J')} → {fmt(o['energyJ'], 'J')}"),
        ("transmission", f"{tot['energyTransmission'] * 100:.2f} %"),
        ("FWHM", f"{fmt(i['fwhmS'], 's')} → {fmt(o['fwhmS'], 's')}"),
        ("transform limit", f"{fmt(o['transformLimitFwhmS'], 's')}"),
        ("peak power", f"{fmt(i['peakPowerW'], 'W')} → {fmt(o['peakPowerW'], 'W')}"),
        ("B-integral", f"{tot['bIntegralRad']:.3g} rad"),
        ("system GDD / TOD", f"{fmt(tot['systemGddS2'], 's^2')} / {fmt(tot['systemTodS3'], 's^3')}"),
        ("group delay", fmt(tot["systemGroupDelayS"], "s")),
        ("centroid λ", f"{i['centroidWavelengthM'] * 1e9:.1f} → {o['centroidWavelengthM'] * 1e9:.1f} nm"),
    ]
    for el in s.get("elements", []):
        v = el.get("values") or {}
        if "conversionEfficiency" in v:
            rows.append((f"{el['id']} conversion", f"{v['conversionEfficiency'] * 100:.3g} %  (→ {el['carrierWavelengthM'] * 1e9:.0f} nm)"
                         if el.get("carrierWavelengthM") else f"{v['conversionEfficiency'] * 100:.3g} %"))
    for ins in s.get("instruments", []):
        v = ins["values"]
        if ins["kind"] == "compressor" and "compressedFwhmS" in v:
            rows.append((f"{ins['name'].replace('_', ' ')}", f"{fmt(v['compressedFwhmS'], 's')} at {fmt(v.get('compressorGddS2'), 's^2')}"))
    with _Style():
        ax = ax or plt.gca()
        ax.set_axis_off()
        ax.set_title(f"{s.get('system', '')}  ·  {s.get('run', '')}")
        y = 0.97
        for k, v in rows:
            ax.text(0.0, y, k, transform=ax.transAxes, fontsize=8.5, color=_theme["text2"], va="top")
            ax.text(0.42, y, v, transform=ax.transAxes, fontsize=8.5, color=_theme["text"], va="top")
            y -= 0.075
        msgs = [("⚠ " + w) for w in s.get("warnings", [])][:3]
        if msgs:
            y -= 0.02
            for m in msgs:
                ax.text(0.0, y, _wrap(m, 70), transform=ax.transAxes, fontsize=7.5, color=_theme["series"][7], va="top")
                y -= 0.07 * (1 + m.count("\n") + len(m) // 70)
        eng = s.get("engine", {})
        ax.text(0.0, 0.0, f"{eng.get('name', 'UltrafastEngine')} {eng.get('version', '')} · grid {s['grid']['n']} pts, "
                f"window {fmt(s['grid']['windowS'], 's')} · {s.get('elapsedS', 0):.2f} s",
                transform=ax.transAxes, fontsize=7, color=_theme["text2"], va="bottom")
    return ax


def _wrap(text, width):
    import textwrap
    return "\n".join(textwrap.wrap(text, width))


# ----------------------------------------------------------------------------------------------- figures
def overview(res, db: bool = False):
    """Six-panel summary: pulse, spectrum, spectral phase, evolution, losses, key numbers."""
    res = load(res)
    fig = _figure(15, 11.0)
    with _Style():
        gs = fig.add_gridspec(3, 3, height_ratios=[0.62, 1, 1.15])
        ax_d = fig.add_subplot(gs[0, :])
        system_diagram(res, ax_d, compact=True)
        temporal(res, fig.add_subplot(gs[1, 0]))
        ax_s = fig.add_subplot(gs[1, 1])
        spectrum(res, ax_s, db=db)
        spectral_phase(res, fig.add_subplot(gs[1, 2]), xlim=ax_s.get_xlim())
        sub = gs[2, 0].subgridspec(3, 1, hspace=0.05)
        a0 = fig.add_subplot(sub[0]); a1 = fig.add_subplot(sub[1], sharex=a0); a2 = fig.add_subplot(sub[2], sharex=a0)
        for a in (a0, a1):
            a.tick_params(labelbottom=False)
        evolution(res, [a0, a1, a2])
        losses(res, fig.add_subplot(gs[2, 1]))
        key_numbers(res, fig.add_subplot(gs[2, 2]))
        fig.suptitle(f"UltrafastEngine · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12,
                     fontweight="bold", color=_theme["text"])
    return fig


def system_diagram(res, ax=None, compact: bool = False):
    """Schematic of the system on ``ax``: elements, beam line coloured by carrier wavelength, aux beams in,
    by-products out, probes and numbered instrument locations (the numbers used in every instrument title)."""
    _, plt = _plt()
    from . import diagram
    res = load(res)
    with _Style():
        ax = ax or plt.gca()
        diagram.draw_system(res, ax, _theme, compact=compact)
        if compact:
            ax.set_title("System")
    return ax


def system(res):
    """Stand-alone system schematic with a numbered list of the instruments and where they sit."""
    from . import diagram
    res = load(res)
    locs = diagram.instrument_locations(res.summary)
    n = len(locs)
    rows = max(1, (n + 2) // 3)
    fig = _figure(15, 4.2 + 0.25 * rows)
    with _Style():
        gs = fig.add_gridspec(2, 1, height_ratios=[3.4, 0.35 + 0.25 * rows])
        system_diagram(res, fig.add_subplot(gs[0]))
        ax = fig.add_subplot(gs[1]); ax.set_axis_off()
        cols = 3
        for k, d in enumerate(locs):
            c, r = k % cols, k // cols
            txt = f"{d['tag']}  {d['name'].replace('_', ' ')}  ({d['kind']}{', virtual' if d['virtual'] else ''}) · {d['where']}"
            ax.text(c / cols, 1 - (r + 0.5) / rows, txt, transform=ax.transAxes,
                    fontsize=7.5, va="center", color=_theme["text2"] if d["virtual"] else _theme["text"])
        if not locs:
            ax.text(0, 0.5, "no instruments in this run", transform=ax.transAxes, fontsize=8, color=_theme["text2"])
        fig.suptitle(f"System · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12,
                     fontweight="bold", color=_theme["text"])
    return fig


def _image(ax, x, y, z, cmap=None, unit="", center=False, cbar_label=None):
    """z[x][y] as an image with physical axes (auto-scaled) and equal aspect."""
    sx, px = _si(max(np.nanmax(np.abs(x)), np.nanmax(np.abs(y)), 1e-30))
    z = np.asarray(z, float)
    vmax = np.nanmax(np.abs(z)) if center else np.nanmax(z)
    if not (np.isfinite(vmax) and vmax > 0):
        vmax = 1.0
    m = ax.pcolormesh(x / sx, y / sx, z.T, cmap=cmap or _cmap(), shading="auto", rasterized=True,
                      vmin=-vmax if center else 0, vmax=vmax)
    ax.set_aspect("equal"); ax.grid(False)
    ax.set_xlabel(f"x ({px}m)"); ax.set_ylabel(f"y ({px}m)")
    ax.figure.colorbar(m, ax=ax, shrink=0.85, pad=0.01, label=cbar_label or unit)
    return m


def _instrument_panel(res, ins, ax, loc=None):
    kind, name, v = ins["kind"], ins["name"], ins["values"]
    arr = {a[len(name) + 1:] if a.startswith(name + "_") else a: res[a] for a in ins["arrays"]}
    if loc is not None:
        title = f"{loc['tag']}  {name.replace('_', ' ')} · {loc['where']}" + ("  (virtual)" if loc["virtual"] else "")
    else:
        title = f"{name.replace('_', ' ')}  ({kind} @ {ins['at']})"
    c0 = _theme["series"][0]
    if kind == "spectrometer":
        lam = arr["wavelength_m"]
        y = arr["spectrum_db"] if "spectrum_db" in arr else _db(arr["spectrum"], -300)
        floor = _auto_floor(y)
        ax.plot(lam * 1e9, y, color=c0)
        r = _wl_range([(lam, 10 ** (np.asarray(y) / 10))], level=10 ** (floor / 10), margin=0.05)
        if r:
            ax.set_xlim(max(r[0], np.nanmin(lam)) * 1e9, min(r[1], np.nanmax(lam)) * 1e9)
        ax.set_ylim(floor, 3)
        ax.set_xlabel("wavelength (nm)"); ax.set_ylabel("dB")
        _note(ax, f"peak {v.get('peakWavelengthM', np.nan) * 1e9:.1f} nm\nFWHM {fmt(v.get('fwhmM'), 'm')}\n"
                  f"−10 dB {fmt(v.get('width10dBM'), 'm')}")
    elif kind == "frog" and "trace" in arr:
        d, lam, tr = arr["delay_s"], arr["wavelength_m"], np.asarray(arr["trace"], float)
        tr = tr / max(tr.max(), 1e-300)
        o = np.argsort(lam)
        prof_l, prof_d = tr.max(axis=0)[o], tr.max(axis=1)
        ls, ds = lam[o], d
        def _rng(x, y):
            k = np.where(y > 1e-3)[0]
            if len(k) == 0:
                return x.min(), x.max()
            a, b = x[k[0]], x[k[-1]]; m = 0.3 * (b - a) + 1e-30
            return max(x.min(), a - m), min(x.max(), b + m)
        if tr.shape == (len(d), len(lam)):
            ax.pcolormesh(ls * 1e9, ds * 1e15, tr[:, o], cmap=_cmap(), shading="auto", rasterized=True)
        else:
            ax.pcolormesh(ls * 1e9, ds * 1e15, tr.T[:, o], cmap=_cmap(), shading="auto", rasterized=True)
            prof_l, prof_d = tr.max(axis=1)[o], tr.max(axis=0)
        l0, l1 = _rng(ls, prof_l); d0, d1 = _rng(ds, prof_d)
        ax.set_xlim(l0 * 1e9, l1 * 1e9); ax.set_ylim(d0 * 1e15, d1 * 1e15); ax.grid(False)
        ax.set_xlabel("signal wavelength (nm)")
        ax.set_ylabel("delay (fs)")
        _note(ax, f"delay marginal FWHM {fmt(v.get('delayMarginalFwhmS'), 's')}")
    elif kind == "autocorrelator":
        d = arr["delay_s"]
        lo, hi, s, lab = _time_axis(d, arr["signal"], level=1e-2, margin=0.1)
        ax.plot(d / s, arr["signal"], color=c0)
        ax.set_xlim(lo / s, hi / s); ax.set_xlabel(lab.replace("time", "delay")); ax.set_ylabel("signal (norm.)")
        ax.set_ylim(bottom=0)
        _note(ax, f"AC FWHM {fmt(v.get('acFwhmS'), 's')}\n→ Gaussian {fmt(v.get('pulseFwhmGaussianS'), 's')}\n"
                  f"→ sech² {fmt(v.get('pulseFwhmSech2S'), 's')}")
    elif kind == "compressor" or kind == "scope":
        keys = list(arr)
        t, p = arr[keys[0]], arr[keys[1]]
        lo, hi, s, lab = _time_axis(t, p, level=1e-3 if kind == "compressor" else 1e-2)
        ps, pp = _si(p.max())
        ax.plot(t / s, p / ps, color=c0)
        ax.set_xlim(lo / s, hi / s); ax.set_xlabel(lab); ax.set_ylabel(f"power ({pp}W)"); ax.set_ylim(bottom=0)
        if kind == "compressor":
            _note(ax, f"FWHM {fmt(v.get('compressedFwhmS'), 's')} (TL {fmt(v.get('transformLimitFwhmS'), 's')})\n"
                      f"GDD {fmt(v.get('compressorGddS2'), 's^2')}, TOD {fmt(v.get('compressorTodS3'), 's^3')}\n"
                      f"Strehl {v.get('temporalStrehl', float('nan')):.2f}")
        else:
            _note(ax, f"FWHM {fmt(v.get('fwhmS'), 's')}\nrise {fmt(v.get('riseTimeS'), 's')}")
    elif kind == "contrast":
        t = arr["time_s"]
        y = arr["intensity_db"]
        s, p = _si(max(abs(t[0]), abs(t[-1])))
        ax.plot(t / s, y, color=c0)
        ax.set_xlim(t[0] / s, t[-1] / s)   # the whole window: pre-pulses far from the peak stay visible
        ax.set_xlabel(f"time ({p}s)"); ax.set_ylabel("intensity (dB)"); ax.set_ylim(_auto_floor(y, lowest=-200), 3)
        _note(ax, f"pre {v.get('prePulseContrastDb', np.nan):.1f} dB\npost {v.get('postPulseContrastDb', np.nan):.1f} dB\n"
                  f"pedestal {v.get('pedestalEnergyFraction', np.nan) * 100:.2g} %")
    elif kind in ("crossCorrelator", "thirdOrderCorrelator"):
        d = arr["delay_s"]
        y = arr["signal_db"] if "signal_db" in arr else _db(arr["signal"], -300)
        s, p = _si(max(abs(d[0]), abs(d[-1])))
        if "signal_ideal" in arr:
            ax.plot(d / s, _db(arr["signal_ideal"], -300), color=_theme["ref"], lw=0.8, ls="--", label="without noise floor")
        ax.plot(d / s, y, color=c0, lw=1.0, label="measured")
        ax.set_xlim(d[0] / s, d[-1] / s)
        ax.set_ylim(_auto_floor(y, lowest=-200), 3)
        ax.set_xlabel(f"delay ({p}s)"); ax.set_ylabel("signal (dB)")
        ax.legend(loc="upper right", fontsize=7)
        _note(ax, f"pre {v.get('prePulseContrastDb', np.nan):.1f} dB\npost {v.get('postPulseContrastDb', np.nan):.1f} dB\n"
                  f"floor −{v.get('dynamicRangeDb', np.nan):.0f} dB", loc="upper left")
    elif kind == "polarimeter":
        lam = arr["wavelength_m"] * 1e9
        for j, k in enumerate(("s1", "s2", "s3")):
            ax.plot(lam, arr[k], color=_theme["series"][j], label=k)
        ax.set_ylim(-1.08, 1.08); ax.set_xlabel("wavelength (nm)"); ax.set_ylabel("normalised Stokes")
        ax.legend(loc="lower left", ncol=3)
        _note(ax, f"DOP {v.get('degreeOfPolarization', np.nan):.3f}\nazimuth {v.get('azimuthDeg', np.nan):.1f}°\n"
                  f"ellipticity {v.get('ellipticityAngleDeg', np.nan):.1f}°")
    elif kind == "phaseMatchingCurve":
        xk = next(k for k in arr if k not in ("efficiency_norm", "delta_k_per_m"))
        x, xl = _axis_scale(arr[xk], xk, "m" if xk.endswith("_m") else "")
        ax.plot(x, arr["efficiency_norm"], color=c0)
        ax.set_xlabel(xl); ax.set_ylabel("sinc²(Δk L/2)"); ax.set_ylim(0, 1.08)
        fw = v.get("fwhm", np.nan)
        fw_txt = (f"{fw * 1e9:.3g} nm" if xk.endswith("_m") else f"{fw:.3g} °" if xk.endswith("deg") else f"{fw:.3g} K")
        _note(ax, f"FWHM {fw_txt}")
    elif kind == "delayScan":
        d, e = arr["delay_s"], arr["output_energy_j"]
        s_, p_ = _si(max(abs(d[0]), abs(d[-1])))
        es, ep = _si(e.max())
        ax.plot(d / s_, e / es, color=c0, marker="o", ms=3)
        ax.axvline(v.get("optimalDelayS", np.nan) / s_, color=_theme["series"][1], lw=1, ls="--")
        ax.set_xlabel(f"aux delay ({p_}s)"); ax.set_ylabel(f"output energy ({ep}J)"); ax.set_ylim(bottom=0)
        _note(ax, f"best {fmt(v.get('optimalDelayS'), 's')}\nmax {fmt(v.get('maxOutputEnergyJ'), 'J')}", loc="lower center")
    elif kind == "beamProfile":
        if "radius_m" in arr:
            r, f = arr["radius_m"], arr["fluence_j_m2"]
            ax.plot(np.concatenate([-r[::-1], r]) * 1e3, np.concatenate([f[::-1], f]) * 1e-4, color=c0)
            ax.set_xlabel("radius (mm)")
        else:
            ax.plot(arr["x_m"] * 1e3, arr["fluence_x_j_m2"] * 1e-4, color=c0, label="x")
            if "y_m" in arr:
                ax.plot(arr["y_m"] * 1e3, arr["fluence_y_j_m2"] * 1e-4, color=_theme["series"][1], label="y")
                ax.legend(loc="upper left")
            ax.set_xlabel("position (mm)")
        ax.set_ylabel("fluence (J/cm²)"); ax.set_ylim(bottom=0)
        rx = v.get("radiusXM", np.nan)
        lim = 3 * rx * 1e3 if np.isfinite(rx) else None
        if lim:
            ax.set_xlim(-lim, lim)
        _note(ax, f"w = {fmt(rx, 'm')}\nM² = {v.get('m2', np.nan):.3f}\npeak {v.get('peakFluenceJPerM2', np.nan) * 1e-4:.3g} J/cm²")
    elif kind in ("nearFieldCamera", "camera", "farFieldCamera"):
        _image(ax, arr["x_m"], arr["y_m"], arr["fluence_j_m2"] * 1e-4, unit="J/cm²")
        txt = f"w = {fmt(v.get('radiusXM'), 'm')} × {fmt(v.get('radiusYM'), 'm')}\npeak {v.get('peakFluenceJPerM2', np.nan) * 1e-4:.3g} J/cm²"
        if kind == "farFieldCamera" or v.get("imagingPlane") == "far":
            txt += f"\nf = {v.get('focalLengthM', np.nan):.3g} m, Strehl {v.get('strehlRatio', np.nan):.2f}"
        elif v.get("m2") is not None:
            txt += f"\nM² = {v.get('m2'):.3f}"
        _note(ax, txt, loc="upper left")
    elif kind == "wavefrontSensor":
        matplotlib, _ = _plt()
        _image(ax, arr["x_m"], arr["y_m"], arr["wavefront_waves"], cmap=matplotlib.colormaps["RdBu_r"], center=True, unit="waves")
        R = v.get("radiusOfCurvatureM")
        _note(ax, f"λ = {v.get('wavelengthM', np.nan) * 1e9:.0f} nm\nrms {v.get('rmsWaves', np.nan):.3g} λ, PV {v.get('pvWaves', np.nan):.3g} λ\n"
                  f"R = {fmt(R, 'm') if R is not None else '∞'}\nw/o defocus {v.get('rmsWithoutDefocusWaves', np.nan):.2g} λ rms", loc="upper left")
    elif kind == "wigner":
        matplotlib, _ = _plt()
        key = "distribution" if "distribution" in arr else "spectrogram"
        W = arr[key]; t = arr["time_s"]; lam = arr["wavelength_m"]
        prof_t = np.sum(np.abs(W), axis=1); prof_w = np.sum(np.abs(W), axis=0)
        lo, hi, ts, tl = _time_axis(t, prof_t, level=1e-3)
        ok = np.isfinite(lam) & (prof_w >= 1e-3 * prof_w.max())
        idx = np.nonzero(ok)[0]
        i0, i1 = (idx[0], idx[-1]) if idx.size else (0, len(lam) - 1)
        sl = slice(max(0, i0 - 3), min(len(lam), i1 + 4))
        center = key == "distribution"
        m = ax.pcolormesh(t / ts, lam[sl] * 1e9, W[:, sl].T, cmap=matplotlib.colormaps["RdBu_r"] if center else _cmap(),
                          vmin=-1 if center else 0, vmax=1, shading="auto", rasterized=True)
        ax.set_xlim(lo / ts, hi / ts); ax.set_xlabel(tl); ax.set_ylabel("wavelength (nm)"); ax.grid(False)
        ax.ticklabel_format(axis="y", useOffset=False)
        ax.figure.colorbar(m, ax=ax, shrink=0.85, pad=0.01, label="norm.")
    elif kind == "fieldProbe":
        t, pw = arr["time_s"], arr["power_w"]
        lo, hi, ts, tl = _time_axis(t, pw, level=1e-3)
        ps, pp = _si(pw.max())
        ax.plot(t / ts, pw / ps, color=c0, label="power")
        ax.set_xlim(lo / ts, hi / ts); ax.set_xlabel(tl); ax.set_ylabel(f"power ({pp}W)"); ax.set_ylim(bottom=0)
        a2 = ax.twinx(); a2.grid(False)
        a2.plot(t / ts, arr["instantaneous_wavelength_m"] * 1e9, color=_theme["series"][1], lw=1.2)
        a2.set_ylabel("instantaneous λ (nm)", color=_theme["series"][1])
        a2.ticklabel_format(axis="y", useOffset=False)
        _note(ax, f"CEP {v.get('cepRad', np.nan):.2f} rad\nchirp {v.get('chirpRateRadPerS2', np.nan):.3g} rad/s²", loc="upper left")
    elif kind == "spaceTime":
        I = arr["intensity_w_m2"]; t = arr["time_s"]; r = arr["position_m"]
        prof_t = I.max(axis=0)
        lo, hi, ts, tl = _time_axis(t, prof_t, level=1e-3)
        sr, pr = _si(np.max(np.abs(r)))
        o = np.argsort(r)
        m = ax.pcolormesh(t / ts, r[o] / sr, I[o] / I.max(), cmap=_cmap(), shading="auto", rasterized=True, vmin=0, vmax=1)
        ax.plot(arr["arrival_time_s"][o] / ts, r[o] / sr, color=_theme["series"][1], lw=1, ls="--")
        ax.set_xlim(lo / ts, hi / ts); ax.set_xlabel(tl); ax.set_ylabel(f"position ({pr}m)"); ax.grid(False)
        ax.figure.colorbar(m, ax=ax, shrink=0.85, pad=0.01, label="I / I_max")
        _note(ax, f"on axis {fmt(v.get('onAxisFwhmS'), 's')}, at w {fmt(v.get('fwhmAtRadiusS'), 's')}\n"
                  f"pulse-front delay {fmt(v.get('pulseFrontDelayS'), 's')}", loc="upper left")
    else:
        _generic_panel(arr, v, ax, res, name)
    ax.set_title(title)


def _generic_panel(arr, values, ax, res, name):
    """2-D trace -> image with matching 1-D axes; 1-D arrays -> curves against the first axis-like array."""
    units = {a["name"]: a.get("unit", "") for a in res.summary.get("arrays", [])}
    two = [k for k, a in arr.items() if np.ndim(a) == 2]
    one = {k: a for k, a in arr.items() if np.ndim(a) == 1}
    if two:
        k = two[0]
        z = arr[k]
        rows = next((a for kk, a in one.items() if a.size == z.shape[0]), np.arange(z.shape[0]))
        rk = next((kk for kk, a in one.items() if a.size == z.shape[0]), "row")
        cols = next((a for kk, a in one.items() if a.size == z.shape[1] and kk != rk), np.arange(z.shape[1]))
        ck = next((kk for kk, a in one.items() if a.size == z.shape[1] and kk != rk), "column")
        rx, rl = _axis_scale(rows, rk, units.get(f"{name}_{rk}", ""))
        cx, cl = _axis_scale(cols, ck, units.get(f"{name}_{ck}", ""))
        ax.pcolormesh(cx, rx, z / max(np.max(z), 1e-300), cmap=_cmap(), shading="auto", rasterized=True)
        ax.set_xlabel(cl); ax.set_ylabel(rl); ax.grid(False)
    elif one:
        keys = list(one)
        xk = keys[0]
        x, xl = _axis_scale(one[xk], xk, units.get(f"{name}_{xk}", ""))
        for j, k in enumerate(keys[1:]):
            if one[k].size == x.size:
                ax.plot(x, one[k], color=_theme["series"][j % 8], label=k)
        ax.set_xlabel(xl)
        if len(keys) > 2:
            ax.legend(loc="best")
    txt = "\n".join(f"{k}: {fmt(val) if isinstance(val, float) else val}" for k, val in list(values.items())[:4])
    if txt:
        _note(ax, txt)


def _axis_scale(a, key, unit):
    a = np.asarray(a, float)
    if key.endswith("wavelength_m") or unit == "m" and key.endswith("_m"):
        return a * 1e9, key.replace("_m", "").replace("_", " ") + " (nm)"
    if key.endswith("_s2") or unit == "s^2":
        return a * 1e30, key.replace("_s2", "").replace("_", " ") + " (fs²)"
    if key.endswith("_s") or unit == "s":
        s, p = _si(np.max(np.abs(a)) if a.size else 1)
        return a / s, key.replace("_s", "").replace("_", " ") + f" ({p}s)"
    return a, key.replace("_", " ") + (f" ({unit})" if unit else "")


def _dscan_panels(res, ins, ax_trace, ax_fwhm):
    name, v = ins["name"], ins["values"]
    if _has(res, f"{name}_gdd_s2"):
        g, scale, label, opt = res[f"{name}_gdd_s2"], 1e30, "added GDD (fs²)", v.get("optimalGddS2", np.nan)
    else:
        g, scale, label, opt = res[f"{name}_insertion_m"], 1e3, f"{v.get('material', 'glass')} insertion (mm)", v.get("optimalInsertionM", np.nan)
    lam, tr = res[f"{name}_shg_wavelength_m"], res[f"{name}_trace"]
    ax_trace.pcolormesh(lam * 1e9, g * scale, tr / tr.max(), cmap=_cmap(), shading="auto", rasterized=True)
    ax_trace.set_xlabel("SHG wavelength (nm)"); ax_trace.set_ylabel(label); ax_trace.grid(False)
    if np.isfinite(opt):
        ax_trace.axhline(opt * scale, color=_theme["series"][1], lw=1, ls="--")
    from . import diagram
    loc = {d["name"]: d for d in diagram.instrument_locations(res.summary)}.get(name)
    ax_trace.set_title(f"{loc['tag']}  {name.replace('_', ' ')} · {loc['where']}" if loc else f"{name.replace('_', ' ')}  (d-scan @ {ins['at']})")
    if ax_fwhm is not None and _has(res, f"{name}_fwhm_s"):
        f = res[f"{name}_fwhm_s"]
        s, p = _si(np.nanmin(f))
        ax_fwhm.plot(g * scale, f / s, color=_theme["series"][0])
        ax_fwhm.set_xlabel(label); ax_fwhm.set_ylabel(f"FWHM ({p}s)")
        ax_fwhm.set_ylim(0, min(np.nanmax(f / s), 6 * np.nanmin(f / s)))
        ax_fwhm.set_title("d-scan: pulse duration vs scan")
        opt_txt = fmt(opt, "s^2") if _has(res, f"{name}_gdd_s2") else fmt(opt, "m")
        _note(ax_fwhm, f"optimum {opt_txt}\nshortest {fmt(v.get('shortestFwhmS'), 's')}", loc="upper left")


def instruments(res):
    """One panel per instrument (d-scan / FROG traces as images). Returns None if no instrument has data."""
    from . import diagram
    res = load(res)
    locs = {d["name"]: d for d in diagram.instrument_locations(res.summary)}
    ins = [i for i in res.summary.get("instruments", []) if i["arrays"]]
    ins.sort(key=lambda i: locs[i["name"]]["number"] if i["name"] in locs else 999)
    if not ins:
        return None
    n_panels = sum(2 if i["kind"] == "dscan" else 1 for i in ins)
    cols = 3 if n_panels > 4 else max(1, min(n_panels, 2))
    rows = int(math.ceil(n_panels / cols))
    fig = _figure(5.0 * cols, 3.6 * rows)
    with _Style():
        axs = list(np.atleast_1d(fig.subplots(rows, cols)).ravel())
        k = 0
        for i in ins:
            if i["kind"] == "dscan":
                _dscan_panels(res, i, axs[k], axs[k + 1]); k += 2
            else:
                _instrument_panel(res, i, axs[k], locs.get(i["name"])); k += 1
        for a in axs[k:]:
            a.set_axis_off()
        fig.suptitle(f"Instruments · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12,
                     fontweight="bold", color=_theme["text"])
    return fig


def probes(res, db: bool = True, floor_db: Optional[float] = None):
    """Spectral and temporal evolution through the probes (heat maps, one row per probe, input at the bottom).
    The dB floor follows the data (down to -60 dB) unless ``floor_db`` is given.
    Returns None if the run recorded fewer than two probes."""
    res = load(res)
    names = [p for p in res.summary.get("probes", []) if _has(res, f"probe_{p}_spectrum")]
    if len(names) < 2:
        return None
    rows = [("input", res["input_wavelength_m"], res["input_spectrum"], res["input_time_s"], res["input_power_w"])]
    rows += [(p, res[f"probe_{p}_wavelength_m"], res[f"probe_{p}_spectrum"], res[f"probe_{p}_time_s"],
              res[f"probe_{p}_power_w"]) for p in names]
    specs = [_sorted_spectrum(l, s) for _, l, s, _, _ in rows]
    if db and floor_db is None:
        floor_db = _auto_floor(*[_db(sp[1], -300) for sp in specs], lowest=-60.0)
    r = _wl_range(specs, level=10 ** (floor_db / 10) if db else 1e-3, margin=0.05)
    lam = np.linspace(r[0], r[1], 600) if r else np.linspace(specs[0][0][0], specs[0][0][-1], 600)
    S = np.array([np.interp(lam, l, s, left=0, right=0) for l, s in specs])
    S = np.array([_db(s, floor_db) if db else s / max(s.max(), 1e-300) for s in S])
    tmin = min(t[0] for *_, t, _ in rows); tmax = max(t[-1] for *_, t, _ in rows)
    lo, hi, ts, tlabel = _time_axis(np.linspace(tmin, tmax, 4096),
                                    *[np.interp(np.linspace(tmin, tmax, 4096), t, p, left=0, right=0) for *_, t, p in rows])
    tt = np.linspace(lo, hi, 600)
    P = np.array([np.interp(tt, t, p, left=0, right=0) for *_, t, p in rows])
    P = P / max(P.max(), 1e-300)

    fig = _figure(13, 0.9 + 0.32 * len(rows) + 2.2)
    with _Style():
        a1, a2 = fig.subplots(1, 2)
        y = np.arange(len(rows))
        m1 = a1.pcolormesh(lam * 1e9, y, S, cmap=_cmap(), shading="nearest", rasterized=True,
                           vmin=floor_db if db else 0, vmax=0 if db else 1)
        a1.set_xlabel("wavelength (nm)"); a1.set_title("Spectrum through the system")
        fig.colorbar(m1, ax=a1, label="dB" if db else "norm.", shrink=0.9, pad=0.01)
        m2 = a2.pcolormesh(tt / ts, y, P, cmap=_cmap(), shading="nearest", rasterized=True, vmin=0, vmax=1)
        a2.set_xlabel(tlabel); a2.set_title("Power through the system (common scale)")
        fig.colorbar(m2, ax=a2, label="norm.", shrink=0.9, pad=0.01)
        for a in (a1, a2):
            a.set_yticks(y); a.set_yticklabels([r[0] for r in rows], fontsize=7.5); a.grid(False)
        a2.set_yticklabels([])
    return fig


def elements(res):
    """Per-element quantities as small multiples (shared x = element)."""
    res = load(res)
    els = res.summary["elements"]
    def col(f):
        return np.array([np.nan if (v := f(e)) is None else v for e in els], float)
    panels = [
        ("energy out", col(lambda e: e["energyOutJ"]), "J"),
        ("pulse FWHM", col(lambda e: e["pulse"]["fwhmS"]), "s"),
        ("peak power", col(lambda e: e["pulse"]["peakPowerW"]), "W"),
        ("cumulative B-integral", col(lambda e: e["cumulativeBIntegralRad"]), "rad"),
        ("system GDD", col(lambda e: e["system"]["gddS2"]), "s^2"),
        ("peak intensity", col(lambda e: e.get("peakIntensityWPerM2")), "W/m²"),
        ("LIDT margin (fluence / LIDT)", col(lambda e: e.get("lidtMargin")), ""),
        ("pulse GDD", col(lambda e: e["pulse"].get("pulseGddS2")), "s^2"),
        ("carrier wavelength", col(lambda e: e.get("carrierWavelengthM")), "m"),
        ("beam radius (transverse models)", col(lambda e: (e.get("beam") or {}).get("radiusXM")), "m"),
        ("M² (transverse models)", col(lambda e: (e.get("beam") or {}).get("m2")), ""),
    ]
    panels = [p for p in panels if np.any(np.isfinite(p[1]) & (p[1] != 0))]
    cols = 2
    rows = int(math.ceil(len(panels) / cols))
    fig = _figure(13, 2.3 * rows + 1.2)
    with _Style():
        axs = np.atleast_2d(fig.subplots(rows, cols, sharex=True)).reshape(rows, cols)
        x = np.arange(len(els))
        for k, (title, y, unit) in enumerate(panels):
            ax = axs[k // cols, k % cols]
            if unit == "s^2":
                ax.plot(x, y * 1e30, color=_theme["series"][0], marker="o" if len(x) <= 40 else None, ms=3)
                ax.set_ylabel("fs²")
            elif unit in ("rad", ""):
                ax.plot(x, y, color=_theme["series"][0], marker="o" if len(x) <= 40 else None, ms=3)
                ax.set_ylabel(unit)
            else:
                s, p = _si(np.nanmax(np.abs(y)))
                ax.plot(x, y / s, color=_theme["series"][0], marker="o" if len(x) <= 40 else None, ms=3)
                ax.set_ylabel(f"{p}{unit}")
            ax.set_title(title)
        for k in range(len(panels), rows * cols):
            axs[k // cols, k % cols].set_axis_off()
        for c in range(cols):
            last = rows - 1 if (rows - 1) * cols + c < len(panels) else rows - 2
            if last >= 0:
                _element_ticks(axs[last, c], els)
                axs[last, c].tick_params(labelbottom=True)
        fig.suptitle(f"Elements · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12,
                     fontweight="bold", color=_theme["text"])
    return fig


def compare(results: Sequence, labels: Optional[Sequence[str]] = None, db: bool = False):
    """Overlay the output pulse and spectrum of several runs (max. 8; colours follow the run order)."""
    res = [load(r) for r in results][:8]
    labels = list(labels) if labels else [_label(r) for r in res]
    fig = _figure(13, 4.4)
    with _Style():
        a1, a2 = fig.subplots(1, 2)
        lo, hi = np.inf, -np.inf
        for k, r in enumerate(res):
            t, p = r["output_time_s"], r["output_power_w"]
            l_, h_, ts, tl = _time_axis(t, p)
            lo, hi = min(lo, l_), max(hi, h_)
        s_, pfx = _si(max(abs(lo), abs(hi)))
        peak = max(r["output_power_w"].max() for r in res); ps, pp = _si(peak)
        pairs = []
        for k, r in enumerate(res):
            c = _theme["series"][k]
            a1.plot(r["output_time_s"] / s_, r["output_power_w"] / ps, color=c, label=labels[k])
            lam, s = _sorted_spectrum(r["output_wavelength_m"], r["output_spectrum"])
            pairs.append((lam, s))
            a2.plot(lam * 1e9, _db(s, -300) if db else s / s.max(), color=c, label=labels[k])
        a1.set_xlim(lo / s_, hi / s_); a1.set_xlabel(f"time ({pfx}s)"); a1.set_ylabel(f"power ({pp}W)")
        a1.set_ylim(bottom=0); a1.set_title("Output pulse"); a1.legend(loc="upper left")
        floor = _auto_floor(*[_db(sp[1], -300) for sp in pairs]) if db else None
        rr = _wl_range(pairs, level=10 ** (floor / 10) if db else 1e-3)
        if rr:
            a2.set_xlim(rr[0] * 1e9, rr[1] * 1e9)
        a2.set_xlabel("wavelength (nm)"); a2.set_ylabel("dB" if db else "spectral density (norm.)")
        if db:
            a2.set_ylim(floor, 3)
        else:
            a2.set_ylim(bottom=0)
        a2.set_title("Output spectrum"); a2.legend(loc="upper left")
    return fig


def byproducts(res):
    """Fields that left an active element alongside the main beam (crystal idler / unconverted pump, ...): one
    row per element, spectrum and power of the main output and of every recorded by-product. Returns None if the run
    has none."""
    res = load(res)
    rows = []
    for el in res.summary["elements"]:
        bps = [k for k in (el.get("byproducts") or {}) if _has(res, f"probe_{el['id']}_{k}_spectrum")]
        if bps:
            rows.append((el, bps))
    if not rows:
        return None
    fig = _figure(13, 3.3 * len(rows) + 0.8)
    with _Style():
        axs = np.atleast_2d(fig.subplots(len(rows), 2)).reshape(len(rows), 2)
        for k, (el, bps) in enumerate(rows):
            curves = []
            if _has(res, f"probe_{el['id']}_spectrum"):
                curves.append((f"{el['id']} (main)", f"probe_{el['id']}"))
            curves += [(f"{el['id']}#{b}", f"probe_{el['id']}_{b}") for b in bps]
            a_s, a_t = axs[k]
            peak = max(res[f"{pre}_power_w"].max() for _, pre in curves)
            ps, pp = _si(peak)
            lo, hi, ts, tl = _time_axis(res[f"{curves[0][1]}_time_s"], *[np.interp(res[f"{curves[0][1]}_time_s"],
                                        res[f"{pre}_time_s"], res[f"{pre}_power_w"]) for _, pre in curves])
            for j, (lab, pre) in enumerate(curves):
                c = _theme["series"][j % 8]
                lam, sp = _sorted_spectrum(res[f"{pre}_wavelength_m"], res[f"{pre}_spectrum"])
                a_s.plot(lam * 1e9, sp / max(sp.max(), 1e-300), color=c, label=lab)
                e = (el["byproducts"].get(lab.split("#")[1], {}) if "#" in lab else el["pulse"]).get("energyJ", np.nan)
                a_t.plot(res[f"{pre}_time_s"] / ts, res[f"{pre}_power_w"] / ps, color=c, label=f"{lab}: {fmt(e, 'J')}")
            a_s.set_xscale("log")
            a_s.set_xlabel("wavelength (nm)"); a_s.set_ylabel("spectrum (each norm.)"); a_s.set_ylim(0, 1.08)
            a_s.set_title(f"{el['id']}: fields leaving the element"); a_s.legend(loc="upper right")
            a_t.set_xlim(lo / ts, hi / ts); a_t.set_xlabel(tl); a_t.set_ylabel(f"power ({pp}W)"); a_t.set_ylim(bottom=0)
            a_t.set_title("power (common scale)"); a_t.legend(loc="upper right")
        fig.suptitle(f"By-products · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12,
                     fontweight="bold", color=_theme["text"])
    return fig


def gain(res):
    """Gain and amplifier elements: one row per element with the single-pass small-signal gain spectrum (and the
    output spectrum), the inversion along the crystal before / after the pulse (or across the beam for spatial
    models), the build-up per pass / round trip and the pulse-train convergence. Returns None if the run has none."""
    res = load(res)
    els = [e for e in res.summary["elements"] if e["type"].lower() in ("gain", "amplifier") and e.get("arrays")]
    if not els:
        return None
    fig = _figure(13, 3.1 * len(els) + 0.8)
    with _Style():
        axs = np.atleast_2d(fig.subplots(len(els), 4)).reshape(len(els), 4)
        for k, el in enumerate(els):
            pre = f"el_{_sanitize(el['id'])}_"
            arr = lambda n: res[pre + n] if _has(res, pre + n) else None
            v = el.get("values") or {}
            a_g, a_i, a_b, a_t = axs[k]
            lam, G = arr("gainSpectrumWavelengthM"), arr("gainSpectrum")
            if lam is not None:
                a_g.plot(lam * 1e9, G, color=_theme["series"][0], label="small-signal gain (1 pass)")
                a_g.axhline(1, color=_theme["grid"], lw=0.8)
                a_g.set_xlabel("wavelength (nm)"); a_g.set_ylabel("gain")
                if _has(res, f"probe_{el['id']}_spectrum"):
                    l2, s2 = _sorted_spectrum(res[f"probe_{el['id']}_wavelength_m"], res[f"probe_{el['id']}_spectrum"])
                    a2 = a_g.twinx()
                    a2.fill_between(l2 * 1e9, s2 / max(s2.max(), 1e-300), color=_theme["series"][1], alpha=0.25, lw=0)
                    a2.set_ylim(0, 1.15); a2.set_yticks([]); a_g.plot([], [], color=_theme["series"][1], alpha=0.5, lw=6, label="output spectrum")
                    rr = _wl_range([(l2, s2)], level=1e-3, margin=1.5)
                    if rr:
                        a_g.set_xlim(max(lam.min(), rr[0]) * 1e9, min(lam.max(), rr[1]) * 1e9)
                a_g.legend(loc="lower right", fontsize=7)
                a_g.set_title(f"{el['id']}: gain spectrum (peak {fmt(v.get('gainPeakWavelengthM'), 'm')})")
            if arr("inversionVsCellBefore") is not None and arr("cellXM") is not None:
                r = arr("cellXM"); o = np.argsort(r)
                a_i.plot(r[o] * 1e3, arr("inversionVsCellBefore")[o], color=_theme["series"][0], label="before")
                if arr("inversionVsCellAfter") is not None:
                    a_i.plot(r[o] * 1e3, arr("inversionVsCellAfter")[o], color=_theme["series"][2], label="after")
                a_i.set_xlabel("x / r (mm)"); a_i.set_title("inversion across the beam (z-averaged)")
            elif arr("zM") is not None:
                z = arr("zM")
                a_i.plot(z * 1e3, arr("inversionBefore"), color=_theme["series"][0], label="before")
                if arr("inversionAfter") is not None:
                    a_i.plot(z * 1e3, arr("inversionAfter"), color=_theme["series"][2], label="after")
                a_i.set_xlabel("z (mm)"); a_i.set_title("inversion along the crystal (on axis)")
            a_i.set_ylabel("N₂ / N_t"); a_i.set_ylim(bottom=0); a_i.legend(loc="best", fontsize=7)
            bu = arr("energyPerRoundTrip"); lab = "round trip"
            if bu is None:
                bu = arr("energyPerPass"); lab = "pass"
            if bu is not None:
                a_b.semilogy(np.arange(len(bu)), bu, color=_theme["series"][0], marker="o" if len(bu) <= 40 else None, ms=3)
                a_b.set_xlabel(lab); a_b.set_ylabel("energy (J)")
                a_b.set_title(f"build-up: {fmt(bu[0], 'J')} → {fmt(el['energyOutJ'], 'J')}")
            else:
                a_b.text(0.5, 0.5, f"single pass\ngain {v.get('gain', float('nan')):.4g}\nextracted {fmt(v.get('extractedEnergyJ'), 'J')}",
                         ha="center", va="center", transform=a_b.transAxes, color=_theme["text2"]); a_b.set_axis_off()
            tr = arr("energyVsPulse")
            if tr is not None:
                s_, p_ = _si(np.nanmax(tr))
                a_t.plot(np.arange(1, len(tr) + 1), tr / s_, color=_theme["series"][0], marker="o" if len(tr) <= 60 else None, ms=2.5)
                a_t.set_xlabel("pulse number"); a_t.set_ylabel(f"output energy ({p_}J)")
                st = "converged" if v.get("converged") else ("period doubling" if v.get("periodDoubling") else "not converged")
                a_t.set_title(f"train: {fmt(v.get('averageOutputPowerW'), 'W')} avg · {st}")
            else:
                lines = [f"{k}: {fmt(v[k], u)}" for k, u in (("storedEnergyJ", "J"), ("extractionEfficiency", ""), ("absorbedPumpPowerW", "W"),
                                                              ("heatW", "W"), ("thermalLensFocalLengthM", "m")) if k in v]
                a_t.text(0.02, 0.95, "\n".join(lines) or "no pulse train (single shot)", ha="left", va="top", transform=a_t.transAxes, fontsize=8, color=_theme["text2"])
                a_t.set_axis_off()
        fig.suptitle(f"Gain media · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12, fontweight="bold", color=_theme["text"])
    return fig


def _sanitize(s):
    import re
    return re.sub(r"[^A-Za-z0-9_]", "_", s)


def oscillator(res):
    """Oscillator elements: build-up of the output energy over the round trips, the evolution of the output pulse
    (time) and spectrum (wavelength) from start-up to steady state, and the final pulse with the key numbers.
    Returns None if the run has no iterated oscillator."""
    res = load(res)
    els = [e for e in res.summary["elements"] if e["type"].lower() == "oscillator" and any(a.endswith("_roundTrip") for a in (e.get("arrays") or []))]
    if not els:
        return None
    fig = _figure(13, 3.3 * len(els) + 0.8)
    with _Style():
        axs = np.atleast_2d(fig.subplots(len(els), 4)).reshape(len(els), 4)
        for k, el in enumerate(els):
            pre = f"el_{_sanitize(el['id'])}_"
            arr = lambda n: res[pre + n] if _has(res, pre + n) else None
            v = el.get("values") or {}
            a_e, a_t, a_s, a_f = axs[k]
            rt, eo = arr("roundTrip"), arr("outputEnergyPerRoundTrip")
            a_e.semilogy(rt, eo, color=_theme["series"][0], label="output energy")
            a_e.set_xlabel("round trip"); a_e.set_ylabel("output energy (J)")
            st = "steady state" if v.get("converged") else "not converged"
            a_e.set_title(f"{el['id']}: build-up ({int(v.get('roundTrips', len(rt)))} round trips, {st})")
            g = arr("netGainPerRoundTrip")
            if g is not None:
                a2 = a_e.twinx(); a2.plot(rt, g, color=_theme["series"][1], lw=0.8, alpha=0.8); a2.set_ylabel("ln gain per round trip", color=_theme["series"][1])
            ev_rt, t, P = arr("evolutionRoundTrip"), arr("evolutionTimeS"), arr("evolutionPower")
            if P is not None:
                P = P.reshape(len(ev_rt), len(t))
                Pn = P / np.maximum(P.max(axis=1, keepdims=True), 1e-300)
                s_, pfx = _si(max(abs(t[0]), abs(t[-1])))
                a_t.pcolormesh(t / s_, ev_rt, Pn, cmap=_cmap(), shading="auto", rasterized=True)
                a_t.set_xlabel(f"time ({pfx}s)"); a_t.set_ylabel("round trip"); a_t.grid(False)
                a_t.set_title("pulse (rows normalised)")
                lam, S = arr("evolutionWavelengthM"), arr("evolutionSpectrum").reshape(len(ev_rt), -1)
                o = np.argsort(lam)
                Sd = 10 * np.log10(np.maximum(S[:, o] / np.maximum(S.max(axis=1, keepdims=True), 1e-300), 1e-6))
                a_s.pcolormesh(lam[o] * 1e9, ev_rt, Sd, cmap=_cmap(), shading="auto", rasterized=True, vmin=-40, vmax=0)
                a_s.set_xlabel("wavelength (nm)"); a_s.set_ylabel("round trip"); a_s.grid(False)
                a_s.set_title("spectrum (dB, rows normalised)")
                ps, pp = _si(P[-1].max())
                lo, hi, s2, lab2 = _time_axis(t, P[-1], level=1e-3, margin=1.0)
                a_f.plot(t / s2, P[-1] / ps, color=_theme["series"][0]); a_f.set_xlim(lo / s2, hi / s2)
                a_f.set_xlabel(lab2); a_f.set_ylabel(f"power ({pp}W)"); a_f.set_ylim(bottom=0)
                a_f.set_title("output pulse")
                lines = [f"{fmt(v.get('averagePowerW'), 'W')} at {fmt(v.get('repRateHz'), 'Hz')}",
                         f"{fmt(v.get('outputEnergyJ'), 'J')}, FWHM {fmt(v.get('outputFwhmS'), 's')}",
                         f"spectrum {fmt(v.get('outputSpectralFwhmM'), 'm')}, TBP {v.get('timeBandwidthProduct', float('nan')):.3f}"]
                if "solitonFwhmS" in v:
                    lines.append(f"soliton theory {fmt(v['solitonFwhmS'], 's')}")
                if "qmlParameter" in v:
                    lines.append(f"QML parameter {v['qmlParameter']:.3g} ({'stable' if v['qmlParameter'] > 1 else 'QML'})")
                if v.get("pulsesInCavity", 1) > 1:
                    lines.append(f"{int(v['pulsesInCavity'])} pulses (multi-pulsing)")
                _note(a_f, "\n".join(lines))
        fig.suptitle(f"Oscillators · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12, fontweight="bold", color=_theme["text"])
    return fig


FIGURES = ["system", "overview", "instruments", "probes", "elements", "gain", "oscillator", "byproducts"]
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
    from . import plugins as _plugins
    _plugins.load_python_plugins()
    makers = {"system": lambda: system(res), "overview": lambda: overview(res, db=db), "instruments": lambda: instruments(res),
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


# ----------------------------------------------------------------------------------------------- legacy API
def plot(result, what="overview", ax=None):
    """``uf.plot(result, what)``: "overview" (figure), or a single panel "temporal" / "spectrum" / "phase" /
    "losses" / "elements" on ``ax``."""
    if what == "overview":
        return overview(result)
    panels = {"temporal": temporal, "spectrum": spectrum, "phase": spectral_phase, "losses": losses,
              "numbers": key_numbers}
    if what == "elements":
        return evolution(result) if ax is None else evolution(result, [ax, ax.twinx(), ax.twinx()])
    if what in panels:
        return panels[what](result, ax)
    raise ValueError(f"unknown plot '{what}' (overview, temporal, spectrum, phase, losses, elements, numbers)")
