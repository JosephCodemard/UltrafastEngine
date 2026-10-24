"""Figures of the fitting plugin: the calibration fit report.

Registered with :func:`ultrafast.plotting.register_figure` when the plugin is loaded, so ``ultrafast-plot`` and
:func:`ultrafast.plotting.report` draw ``fit.png`` for fits. The measured-vs-simulated figure (``measurements``) is part
of the core (:func:`ultrafast.plotting.measurements`).
"""
from __future__ import annotations

import numpy as np

from ultrafast.plotting import _figure, _has, _sanitize, _si, _Style, _theme, _cmap, fmt, load

# measured-vs-simulated figure and its helpers are part of the core (measured data is native): re-exported here for
# code written against earlier versions of the plugin
from ultrafast.plotting import _AXIS_LABELS, _fmtu, _named_axis, _value_unit, measurements  # noqa: F401,E402


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
