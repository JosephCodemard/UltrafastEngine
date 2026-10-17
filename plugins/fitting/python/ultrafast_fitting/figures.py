"""Figures of the fitting plugin: measured data against the simulation, and the calibration fit report.

Registered with :func:`ultrafast.plotting.register_figure` when the plugin is loaded, so ``ultrafast-plot`` and
:func:`ultrafast.plotting.report` draw ``measurements.png`` and ``fit.png`` for runs that have them.
"""
from __future__ import annotations

import numpy as np

from ultrafast.plotting import _figure, _has, _sanitize, _si, _Style, _theme, _cmap, fmt, load

_AXIS_LABELS = {"spectrometer": ("wavelength", "m"), "autocorrelator": ("delay", "s"), "scope": ("time", "s"),
                "beamProfile": ("position", "m"), "delayScan": ("aux delay", "s"), "phaseMatchingCurve": ("scan", "")}


def _named_axis(a, key):
    """Scale an SI axis by its array name ("delay_s", "shg_wavelength_m", "insertion_m", "gdd_s2", "x_m") for display."""
    a = np.asarray(a, float); key = key or ""
    base = key.rsplit("_", 1)[0].replace("_", " ") if "_" in key else key
    if key.endswith("wavelength_m"):
        return a * 1e9, f"{base} (nm)"
    if key.endswith("_s2"):
        return a * 1e30, f"{base} (fs²)"
    for suf, unit in (("_m", "m"), ("_s", "s")):
        if key.endswith(suf):
            sc, p = _si(np.nanmax(np.abs(a)) if a.size else 1)
            return a / sc, f"{base} ({p}{unit})"
    return a, base


def _value_unit(name):
    """Unit of an instrument value from its name suffix (energyJ -> J, fwhmS -> s, powerW -> W)."""
    for suf, u in (("S2", "s^2"), ("J", "J"), ("W", "W"), ("S", "s"), ("M", "m"), ("Hz", "Hz"), ("Rad", "rad")):
        if name.endswith(suf):
            return u
    return ""


def measurements(res):
    """Recorded data against the simulation: one row per instrument with "measured" data -- the measured curve
    (points), the simulated reading on the same axis (line, with its 1-sigma band after a fit) and the residual; for
    2-D traces (FROG, d-scan, cameras) the measured and simulated traces and their difference; for scalar readings
    a table. Returns None if the run compared nothing."""
    res = load(res)
    comps = res.summary.get("comparisons") or []
    if not comps:
        return None
    ins = {i["name"]: i for i in res.summary["instruments"]}
    fig = _figure(13, 3.0 * len(comps) + 0.9)
    with _Style():
        axs = np.atleast_2d(fig.subplots(len(comps), 3)).reshape(len(comps), 3)
        for k, c in enumerate(comps):
            name = c["instrument"]; pre = _sanitize(name)
            a_m, a_s, a_r = axs[k]
            title = f"{name} ({c['kind']} @ {c['at']})"
            if c["mode"] == "curve" and _has(res, f"{pre}_measured_x"):
                x, ym, ys = res[f"{pre}_measured_x"], res[f"{pre}_measured_y"], res[f"{pre}_simulated_on_measured"]
                lab, unit = _AXIS_LABELS.get(c["kind"], ("x", ""))
                sx, px = _si(max(abs(x.min()), abs(x.max()), 1e-300)) if unit else (1.0, "")
                a_m.plot(x / sx, ym, ".", ms=2.5, color=_theme["text2"], label="measured")
                a_m.plot(x / sx, ys, color=_theme["series"][0], lw=1.4, label="simulated")
                if _has(res, f"{pre}_simulated_sigma"):
                    sg = res[f"{pre}_simulated_sigma"]
                    a_m.fill_between(x / sx, ys - sg, ys + sg, color=_theme["series"][0], alpha=0.25, lw=0, label="±1σ (fit)")
                a_m.set_xlabel(f"{lab} ({px}{unit})" if unit else lab); a_m.set_ylabel(f"signal ({c.get('normalization', '')})")
                a_m.legend(loc="upper right", fontsize=7); a_m.set_title(title)
                a_s.plot(x / sx, ys - ym, color=_theme["series"][1], lw=0.9)
                a_s.axhline(0, color=_theme["grid"], lw=0.8)
                a_s.set_xlabel(f"{lab} ({px}{unit})" if unit else lab); a_s.set_ylabel("simulated − measured")
                a_s.set_title(f"residual · rms {c.get('rmsError', float('nan')):.2%}")
            elif c["mode"] == "trace" and _has(res, f"{pre}_measured_trace"):
                r, cc = res[f"{pre}_measured_rows"], res[f"{pre}_measured_cols"]
                tm = res[f"{pre}_measured_trace"].reshape(len(r), len(cc)); ts = res[f"{pre}_simulated_on_measured"].reshape(len(r), len(cc))
                xs, xl = _named_axis(cc, c.get("colAxis", "columns"))
                ys_, yl = _named_axis(r, c.get("rowAxis", "rows"))
                def img(ax, z, t, center=False):
                    vmax = np.nanmax(np.abs(z)) or 1.0
                    ax.pcolormesh(xs, ys_, z, cmap="RdBu_r" if center else _cmap(), vmin=-vmax if center else 0, vmax=vmax, shading="auto", rasterized=True)
                    ax.set_title(t); ax.grid(False); ax.set_xlabel(xl); ax.set_ylabel(yl)
                img(a_m, tm, title + " · measured"); img(a_s, ts, "simulated"); img(a_r, ts - tm, f"difference · rms {c.get('rmsError', float('nan')):.2%}", center=True)
                continue
            else:
                a_m.set_axis_off(); a_s.set_axis_off()
                rows = [f"{k2}: measured {fmt(v['measured'], _value_unit(k2))} ± {fmt(v['sigma'], _value_unit(k2))}"
                        f"   simulated {fmt(v['simulated'], _value_unit(k2))}  ({(v['simulated'] - v['measured']) / v['sigma']:+.1f}σ)"
                        for k2, v in (c.get("values") or {}).items()]
                a_m.text(0.0, 0.75, title + "\n\n" + "\n".join(rows), transform=a_m.transAxes, va="top", fontsize=9, color=_theme["text"])
            # residual histogram / scalar deviations in the third panel
            vals = c.get("values") or {}
            if vals:
                names = list(vals); dev = [(vals[n_]["simulated"] - vals[n_]["measured"]) / vals[n_]["sigma"] for n_ in names]
                a_r.barh(range(len(names)), dev, height=0.35, color=_theme["series"][2]); a_r.set_yticks(range(len(names))); a_r.set_yticklabels(names, fontsize=8)
                lim = max(3.0, 1.2 * max(abs(d) for d in dev)); a_r.set_xlim(-lim, lim); a_r.set_ylim(-1, len(names))
                a_r.axvspan(-1, 1, color=_theme["grid"], alpha=0.5, lw=0, zorder=0)
                a_r.axvline(0, color=_theme["text2"], lw=0.8); a_r.set_xlabel("(simulated − measured) / σ"); a_r.set_title("scalar readings")
            else:
                a_r.text(0.5, 0.5, f"χ² = {c['chi2']:.4g}\n{c['points']} points\nreduced χ² = {c['reducedChi2']:.3g}", ha="center", va="center",
                         transform=a_r.transAxes, color=_theme["text2"]); a_r.set_axis_off()
        fig.suptitle(f"Measured vs simulated · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12, fontweight="bold", color=_theme["text"])
    return fig


def fit(res):
    """The calibration fit: fitted parameters with 1-sigma uncertainties (and where they started), the cost per
    iteration, the parameter correlation matrix, and the chi² of every measured instrument before and after.
    Returns None if the run was not a fit."""
    res = load(res)
    f = res.summary.get("fit")
    if not f:
        return None
    ps = f["parameters"]; n = len(ps)
    fig = _figure(13, 4.6)
    with _Style():
        a_p, a_c, a_m, a_i = fig.subplots(1, 4, gridspec_kw={"width_ratios": [1.5, 1, 1, 1.2]})
        a_p.set_axis_off()
        lines = [f"{p['name']}\n   {fmt(p['value'], p.get('unit', ''))} ± {fmt(p['sigma'], p.get('unit', '')) if p['sigma'] is not None else '∞ (not determined)'}"
                 f"   (start {fmt(p['initial'], p.get('unit', ''))}){'  AT BOUND' if p.get('atBound') else ''}" for p in ps]
        a_p.text(0, 1, "fitted parameters (±1σ)\n\n" + "\n".join(lines) + f"\n\nχ² {f['chi2Initial']:.4g} → {f['chi2Final']:.4g} ({f['points']} points)"
                 + (f", reduced {f['reducedChi2']:.3g}" if f.get("reducedChi2") is not None else "") + f"\n{f['iterations']} iterations, {f['evaluations']} runs"
                 + ("" if f["converged"] else " (not converged)"), transform=a_p.transAxes, va="top", fontsize=9, color=_theme["text"], family="monospace")
        a_c.semilogy(np.arange(len(f["costHistory"])), f["costHistory"], marker="o", ms=3, color=_theme["series"][0])
        a_c.set_xlabel("accepted step"); a_c.set_ylabel("χ²"); a_c.set_title("cost")
        if f.get("correlation"):
            C = np.array([[np.nan if v is None else v for v in row] for row in f["correlation"]], float)
            m = a_m.imshow(C, cmap="RdBu_r", vmin=-1, vmax=1)
            a_m.set_xticks(range(n)); a_m.set_yticks(range(n))
            short = [p["name"][:14] for p in ps]
            a_m.set_xticklabels(short, rotation=45, ha="right", fontsize=7); a_m.set_yticklabels(short, fontsize=7)
            for i in range(n):
                for j in range(n):
                    if np.isfinite(C[i, j]):
                        a_m.text(j, i, f"{C[i, j]:.2f}", ha="center", va="center", fontsize=7, color="white" if abs(C[i, j]) > 0.6 else _theme["text"])
            a_m.set_title("correlation"); a_m.grid(False)
        inst = f.get("instruments") or {}
        names = list(inst)
        if names:
            b = [inst[k]["chi2Before"] or 0 for k in names]; a = [inst[k]["chi2After"] or 0 for k in names]
            y = np.arange(len(names))
            a_i.barh(y + 0.2, b, height=0.4, color=_theme["grid"], label="before")
            a_i.barh(y - 0.2, a, height=0.4, color=_theme["series"][0], label="after")
            a_i.set_yticks(y); a_i.set_yticklabels(names, fontsize=7); a_i.set_xscale("log"); a_i.set_xlabel("χ²")
            a_i.legend(loc="lower right", fontsize=7); a_i.set_title("per instrument")
        fig.suptitle(f"Calibration fit · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12, fontweight="bold", color=_theme["text"])
    return fig


