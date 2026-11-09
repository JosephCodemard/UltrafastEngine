"""Spectral panels: spectrum (linear or dB) and spectral phase."""
from __future__ import annotations

from typing import Optional

import numpy as np

from .data import _has, load
from .style import _Style, _auto_floor, _db, _note, _plt, _theme, fmt
from .time_domain import _residual_phase


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

