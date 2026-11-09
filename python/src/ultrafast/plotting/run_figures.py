"""Whole-run figures: overview, element-by-element evolution, losses, gain, oscillator, by-products, run comparison."""
from __future__ import annotations

import math
from typing import Optional, Sequence

import numpy as np

from .data import _has, _label, load
from .diagrams import system_diagram
from .spectral import _sorted_spectrum, _wl_range, spectral_phase, spectrum
from .style import _Style, _auto_floor, _cmap, _db, _figure, _note, _plt, _sanitize, _si, _theme, fmt
from .time_domain import _time_axis, temporal


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

