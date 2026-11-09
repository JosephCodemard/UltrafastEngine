"""One panel per instrument, probes and the measured-versus-simulated figure."""
from __future__ import annotations

import math
from typing import Optional

import numpy as np

from .data import _has, load
from .spatial import _image
from .spectral import _sorted_spectrum, _wl_range
from .style import _Style, _auto_floor, _cmap, _db, _figure, _note, _plt, _sanitize, _si, _theme, fmt
from .time_domain import _time_axis


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
    from . import diagrams as diagram
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
    from . import diagrams as diagram
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
    """Recorded data against the simulation (instruments with ``measured`` data, docs/reference/measured-data.md): one row per instrument with "measured" data -- the measured curve
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

