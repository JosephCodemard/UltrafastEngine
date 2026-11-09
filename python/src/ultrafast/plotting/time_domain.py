"""Temporal panels: the pulse in time, input and output."""
from __future__ import annotations

from typing import Tuple

import numpy as np

from .data import load
from .style import C, _Style, _note, _plt, _si, _theme, fmt


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

