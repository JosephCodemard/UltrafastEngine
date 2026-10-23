"""Figures of the fitting plugin: measured data against the simulation, and the calibration fit report.

Registered with :func:`ultrafast.plotting.register_figure` when the plugin is loaded, so ``ultrafast-plot`` and
:func:`ultrafast.plotting.report` draw ``measurements.png`` and ``fit.png`` for runs that have them.
"""
from __future__ import annotations

import numpy as np

from ultrafast.plotting import _figure, _has, _sanitize, _si, _Style, _theme, _cmap, fmt, load

_AXIS_LABELS = {"spectrometer": ("wavelength", "m"), "autocorrelator": ("delay", "s"), "scope": ("time", "s"),
                "crossCorrelator": ("delay", "s"), "thirdOrderCorrelator": ("delay", "s"), "contrast": ("time", "s"), "polarimeter": ("wavelength", "m"),
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


_PLAIN_UNITS = {"", "nm", "dB", "fs", "ps", "deg", "rad", "fs^2", "fs^3", "fs^4", "fs^5", "dB/nm", "dB/nm^2"}


def _fmtu(v, unit):
    """Engineering prefixes for SI base units (s, m, J, W, ...); plain numbers for units that already carry a scale
    (nm, fs^2, dB, deg, dimensionless)."""
    if v is None:
        return "–"
    if (unit or "") in _PLAIN_UNITS:
        return f"{v:.4g}" + (f" {unit}" if unit else "")
    return fmt(v, unit)


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
                # high-dynamic-range data (contrast, cross-correlators, spectra in the wings): log scale, so nothing
                # below a few % of the peak disappears into the axis
                pos = ym[np.isfinite(ym) & (ym > 0)]
                # log display when the comparison itself is in dB, or for contrast instruments
                hdr = c["kind"] in ("crossCorrelator", "thirdOrderCorrelator", "contrast") or any("compared in dB" in n for n in c.get("notes", []))
                a_m.plot(x / sx, ym, ".", ms=2.5, color=_theme["text2"], label="measured")
                if _has(res, f"{pre}_nominal_on_measured"):
                    a_m.plot(x / sx, res[f"{pre}_nominal_on_measured"], color=_theme["ref"], lw=1.0, ls="--", label="before the fit")
                a_m.plot(x / sx, ys, color=_theme["series"][0], lw=1.4, label="simulated" if not _has(res, f"{pre}_nominal_on_measured") else "fitted")
                if _has(res, f"{pre}_simulated_sigma"):
                    sg = res[f"{pre}_simulated_sigma"]
                    lo_band = np.maximum(ys - sg, 1e-300) if hdr else ys - sg
                    a_m.fill_between(x / sx, lo_band, ys + sg, color=_theme["series"][0], alpha=0.25, lw=0, label="±1σ (fit)")
                if hdr and pos.size:
                    a_m.set_yscale("log")
                    a_m.set_ylim(10 ** np.floor(np.log10(max(pos.min(), 1e-20)) - 0.3), 10 ** np.ceil(np.log10(pos.max()) + 0.1))
                a_m.set_xlim(x.min() / sx, x.max() / sx)
                a_m.set_xlabel(f"{lab} ({px}{unit})" if unit else lab); a_m.set_ylabel(f"signal ({c.get('normalization', '')})")
                a_m.legend(loc="upper right", fontsize=7); a_m.set_title(title)
                if hdr:
                    with np.errstate(divide="ignore", invalid="ignore"):
                        rdb = 10 * np.log10(np.where((ys > 0) & (ym > 0), ys / ym, np.nan))
                    a_s.plot(x / sx, rdb, color=_theme["series"][1], lw=0.9)
                    a_s.set_ylabel("simulated / measured (dB)")
                else:
                    a_s.plot(x / sx, ys - ym, color=_theme["series"][1], lw=0.9)
                    a_s.set_ylabel("simulated − measured")
                a_s.axhline(0, color=_theme["grid"], lw=0.8)
                a_s.set_xlim(x.min() / sx, x.max() / sx)
                a_s.set_xlabel(f"{lab} ({px}{unit})" if unit else lab)
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
    """The fit: fitted parameters with uncertainties and credible intervals (and where they started), the cost per
    step, the parameter correlation matrix, the influence map (which instrument determines which parameter) and the
    chi² of every measured instrument before and after. Returns None if the run was not a fit."""
    res = load(res)
    f = res.summary.get("fit")
    if not f:
        return None
    ps = f["parameters"]; n = len(ps)
    fig = _figure(13, 8.4)
    with _Style():
        gs = fig.add_gridspec(2, 3, height_ratios=[1.35, 1], width_ratios=[1.5, 1, 1.1], hspace=0.45)
        a_p = fig.add_subplot(gs[0, 0]); a_c = fig.add_subplot(gs[0, 1]); a_m = fig.add_subplot(gs[0, 2])
        a_inf = fig.add_subplot(gs[1, 0:2]); a_i = fig.add_subplot(gs[1, 2])
        a_p.set_axis_off()
        believed = ((f.get("layers") or {}).get("believed") or {}).get("parameters") or {}
        def line(p):
            b = believed.get(p["name"]) or {}
            iv = b.get("interval95") or [None, None]
            unit = p.get("unit", "")
            sig = _fmtu(p["sigma"], unit) if p["sigma"] is not None else "∞ (not determined)"
            txt = f"{p['name']}\n   {_fmtu(p['value'], unit)} ± {sig}   (start {_fmtu(p['initial'], unit)}){'  AT BOUND' if p.get('atBound') else ''}"
            if iv[0] is not None and iv[1] is not None:
                txt += f"\n   95 %: [{_fmtu(iv[0], unit)}, {_fmtu(iv[1], unit)}]"
            return txt
        shown = ps[:8]
        a_p.text(0, 1, f"fitted parameters ({f.get('estimator', 'levenbergMarquardt')})\n\n" + "\n".join(line(p) for p in shown)
                 + (f"\n… and {n - len(shown)} more (fit.json)" if n > len(shown) else "")
                 + f"\n\nχ² {f['chi2Initial']:.4g} → {f['chi2Final']:.4g} ({f['points']} points)"
                 + (f", reduced {f['reducedChi2']:.3g}" if f.get("reducedChi2") is not None else "") + f"\n{f['iterations']} iterations, {f['evaluations']} runs"
                 + ("" if f["converged"] else " (not converged)"), transform=a_p.transAxes, va="top", fontsize=7.5, color=_theme["text"], family="monospace")
        a_c.semilogy(np.arange(len(f["costHistory"])), f["costHistory"], marker="o", ms=3, color=_theme["series"][0])
        a_c.set_xlabel("step"); a_c.set_ylabel("χ²"); a_c.set_title("cost")
        short = [p["name"] if len(p["name"]) <= 22 else "…" + p["name"][-21:] for p in ps]
        if f.get("correlation"):
            C = np.array([[np.nan if v is None else v for v in row] for row in f["correlation"]], float)
            a_m.imshow(C, cmap="RdBu_r", vmin=-1, vmax=1)
            a_m.set_xticks(range(n)); a_m.set_yticks(range(n))
            a_m.set_xticklabels(short, rotation=45, ha="right", fontsize=6.5); a_m.set_yticklabels(short, fontsize=6.5)
            if n <= 10:
                for i in range(n):
                    for j in range(n):
                        if np.isfinite(C[i, j]):
                            a_m.text(j, i, f"{C[i, j]:.2f}", ha="center", va="center", fontsize=6.5, color="white" if abs(C[i, j]) > 0.6 else _theme["text"])
            a_m.set_title("correlation"); a_m.grid(False)
        infl = f.get("influence") or {}
        owners = sorted({o for v in infl.values() for o in v})
        if infl and owners:
            M = np.array([[np.nan if (infl.get(p["name"]) or {}).get(o) is None else infl[p["name"]][o] for o in owners] for p in ps], float)
            a_inf.imshow(M.T, cmap=_cmap(), vmin=0, vmax=1, aspect="auto")
            a_inf.set_xticks(range(n)); a_inf.set_xticklabels(short, rotation=35, ha="right", fontsize=6.5)
            a_inf.set_yticks(range(len(owners))); a_inf.set_yticklabels(owners, fontsize=7)
            if n * len(owners) <= 80:
                for i in range(n):
                    for j in range(len(owners)):
                        if np.isfinite(M[i, j]) and M[i, j] > 0.005:
                            a_inf.text(i, j, f"{M[i, j]:.0%}", ha="center", va="center", fontsize=6.5, color="white" if M[i, j] > 0.6 else _theme["text"])
            a_inf.set_title("influence map: share of each parameter's information from each instrument"); a_inf.grid(False)
        else:
            a_inf.set_axis_off()
        inst = f.get("instruments") or {}
        names = list(inst)
        if names:
            b = [inst[k]["chi2Before"] or 0 for k in names]; a = [inst[k]["chi2After"] or 0 for k in names]
            y = np.arange(len(names))
            a_i.barh(y + 0.2, b, height=0.4, color=_theme["grid"], label="before (simulated)")
            a_i.barh(y - 0.2, a, height=0.4, color=_theme["series"][0], label="after (inferred)")
            a_i.set_yticks(y); a_i.set_yticklabels(names, fontsize=7); a_i.set_xscale("log"); a_i.set_xlabel("χ²")
            a_i.legend(loc="upper center", bbox_to_anchor=(0.5, -0.3), ncol=2, fontsize=7, frameon=False); a_i.set_title("per instrument")
        fig.suptitle(f"Fit · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12, fontweight="bold", color=_theme["text"])
    return fig
