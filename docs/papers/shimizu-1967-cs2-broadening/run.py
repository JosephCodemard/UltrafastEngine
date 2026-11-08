#!/usr/bin/env python3
"""Shimizu, Phys. Rev. Lett. 19, 1097 (1967): frequency broadening of a short ruby pulse in liquid CS2 by an intensity-dependent
refractive index with a finite relaxation time tau.

    python run.py --tier fast            # 5 relaxation times (about 1 min)
    python run.py --tier full --figures  # more relaxation times / pulse durations; committed figures

What the paper models (p. 1098-1099): phase modulation dphi = -(k0/n0) L dn with dn(t) = (n2/tau) int_{-inf}^t <E^2>(t') exp(-(t-t')/tau) dt'
(a single-exponential response), lowest order (the intensity envelope is not distorted).  tau = 0: the shift is proportional to
-d<E^2>/dt, broadening on both sides; tau large: proportional to -<E^2>, broadening on the low-frequency (red) side only.  The periodic
spectral structure comes from the interference of two times t1, t2 with the same instantaneous frequency shift.

What the paper does NOT give (assumptions, none from the paper): n2, intensity, filament size, tau, and the pulse shape.
  * pulse: Gaussian, FWHM 6 ps (the paper's inferred filament duration, p. 1099);
  * tau = 2 ps for the main run (literature value for the orientational relaxation of CS2, NOT verified here; UNVERIFIED);
    a scan from 0 to 18 ps shows how the result depends on it;
  * the strength is set by the peak phase B = k0 n2 I0 L of the stationary response (engine B-integral), 120 rad (about 100-150 cm^-1 of
    broadening, the order of the 100 cm^-1 scale bar of the paper's Fig. 1); a plane wave, 1-D (the filament itself is not modelled);
  * index: Sellmeier-type formula for CS2 of Kedenburg et al., Opt. Mater. Express 2, 1588 (2012), via refractiveindex.info (CC0).
    CS2 is not a built-in material of the engine: supplied here as a user material, with the response as a user delayed response
    h(t) = exp(-t/tau)/tau, fR = 1 (the whole nonlinearity is delayed).
The reference is the paper's own model evaluated in numpy on the engine's input pulse (E(t) exp(i dphi(t)), Debye integral), independent of
the engine's propagation.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402

C0 = 299792458.0
LAM = 694.3e-9          # ruby, p. 1098 (Fig. 1 label 6943 A)
T_FWHM = 6e-12          # p. 1099
LENGTH = 0.075          # m, p. 1098 (7.5 cm cell)
B_PEAK = 120.0          # rad, assumed (gives about 100-150 cm^-1 of broadening, the order of the scale bar of Fig. 1)
N2 = 3e-19              # m2/W: only sets the intensity scale; the result depends on B only
CS2_INDEX = "sqrt(1+1.50387*lambda_um^2/(lambda_um^2-0.03049))"


def system(tau, dispersion=True):
    mat = {"index": {"kind": "expression", "expr": CS2_INDEX if dispersion else "1.6137"}, "n2": N2}   # 1.6137 = n(694.3 nm)
    if tau > 0:
        mat["raman"] = {"kind": "expression", "expr": "exp(-t/%.6g)/%.6g*step(t)" % (tau, tau)}
        mat["ramanFraction"] = 1.0
    return {"format": "ultrafast-system", "version": 2, "name": "shimizu",
            "materials": {"CS2": mat},
            "elements": [{"id": "cell", "type": "bulk", "material": "CS2", "lengthM": LENGTH, "beam": {"kind": "constant", "radiusM": 50e-6}}]}


def spec(energy_j, tfwhm=T_FWHM, n=2 ** 14, window=120e-12):
    # the delayed response is a circular convolution on the grid: the window must hold the pulse and ~25 tau (see README)
    return {"format": "ultrafast-run", "version": 2, "name": "shimizu",
            "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": tfwhm, "energyJ": energy_j, "polarization": "x"},
            "grid": {"n": n, "windowS": window, "auto": False}, "physics": {"selfSteepening": False}}


def b_per_joule(tfwhm):
    """B of the stationary response per joule: dispersion-free calibration (1 mm cell, scaled), the response does not matter for it."""
    s = system(0.0)
    s["elements"][0]["lengthM"] = 1e-3
    r = uf.run(s, spec(1e-8, tfwhm))
    return r.summary["totals"]["bIntegralRad"] / 1e-8 * LENGTH / 1e-3


def engine_spectrum(tau, tfwhm=T_FWHM, b=B_PEAK, dispersion=True):
    big = tau > 8e-12
    r = uf.run(system(tau, dispersion), spec(b / b_per_joule(tfwhm), tfwhm, n=2 ** 15 if big else 2 ** 14, window=480e-12 if big else 120e-12))
    g = r.summary["grid"]
    om = np.asarray(r["output_omega_rad_s"]) - g["omega0"]
    sp = np.asarray(r["output_spectrum"])
    return om, sp / sp.sum(), np.asarray(r["input_time_s"]), np.asarray(r["input_power_w"]), r


def model_phase(t, p, tau, b):
    """The paper's phase: b * (h * I)(t) / I0, h = exp(-t/tau)/tau, as a sampled causal convolution (tau = 0: b I/I0)."""
    i = p / p.max()
    if tau <= 0:
        return b * i
    dt = t[1] - t[0]
    # recursive (exact for a piecewise-constant input): y[k] = y[k-1] e^{-dt/tau} + i[k] (1 - e^{-dt/tau})
    a = math.exp(-dt / tau)
    y = np.empty_like(i)
    acc = 0.0
    for k in range(len(i)):
        acc = acc * a + i[k] * (1 - a)
        y[k] = acc
    return b * y


def model_spectrum(t, p, phi, carrier_offset=0.0):
    """Power spectrum of sqrt(P) exp(i phi) on the engine's frequency grid (convention exp(-i w t): A(W) = int a e^{+iWt} dt).

    carrier_offset (rad/s) = requested carrier minus the lattice frequency nearest to it (summary grid reportOmega0 - omega0).  Since the
    engine builds the input pulse at the REQUESTED carrier (2026-11 change), the envelope carries exp(-i offset t) on the lattice; a
    reference that assumes the carrier sits on a lattice point is shifted by that sub-bin amount (0.02 cm^-1 here) and the total variation
    of the fringe pattern then differs by 4e-3 - 6e-3 (see README, 'regression')."""
    a = np.sqrt(p) * np.exp(1j * phi) * np.exp(-1j * carrier_offset * (t - t[0]))
    A = np.fft.ifft(a)
    S = np.abs(np.fft.fftshift(A)) ** 2
    W = 2 * np.pi * np.fft.fftshift(np.fft.fftfreq(len(t), t[1] - t[0]))
    return W, S / S.sum()


# ---- built-in CS2 (2026-11): Kedenburg index, total n2 3e-18 m2/W of which 90 % is the delayed orientational response
# h(t) = (exp(-t/1.6 ps) - exp(-t/0.2 ps))/1.4 ps (UNVERIFIED typical values, docs/reference/materials.md).
N2_BUILTIN = 3e-18
ORIENT = {"kind": "orientational", "riseS": 0.2e-12, "decayS": 1.6e-12, "fR": 0.9}


def system_builtin():
    return {"format": "ultrafast-system", "version": 2, "name": "shimizu-builtin",
            "elements": [{"id": "cell", "type": "bulk", "material": "CS2", "lengthM": LENGTH, "beam": {"kind": "constant", "radiusM": 50e-6}}]}


def system_orient_user(dispersion):
    """The same response and n2 as the built-in material but written out as a user material (cross-check; dispersion off = index 1.6137)."""
    mat = {"index": {"kind": "expression", "expr": CS2_INDEX if dispersion else "1.6137"}, "n2": N2_BUILTIN, "raman": dict(ORIENT)}
    return {"format": "ultrafast-system", "version": 2, "name": "shimizu-user-orient", "materials": {"X": mat},
            "elements": [{"id": "cell", "type": "bulk", "material": "X", "lengthM": LENGTH, "beam": {"kind": "constant", "radiusM": 50e-6}}]}


def orient_kernel(dt, n):
    """Unit-sum sampled kernel (e^{-t/1.6ps} - e^{-t/0.2ps}) on t = k dt >= 0 (the engine normalises its delayed responses the same way)."""
    t = np.arange(n) * dt
    h = np.exp(-t / ORIENT["decayS"]) - np.exp(-t / ORIENT["riseS"])
    return h / h.sum()


def model_phase_orient(t, p, b):
    """Paper's lowest-order model with the two-part CS2 response: b * (0.1 I + 0.9 h * I)/I0 (circular convolution, as on the engine's grid)."""
    i = p / p.max()
    h = orient_kernel(t[1] - t[0], len(t))
    conv = np.real(np.fft.ifft(np.fft.fft(i) * np.fft.fft(h)))
    return b * ((1 - ORIENT["fR"]) * i + ORIENT["fR"] * conv)


def tv(a, b):
    return float(0.5 * np.sum(np.abs(a - b)))


def builtin_cs2(tfwhm=T_FWHM, b=B_PEAK):
    """Built-in CS2 vs (i) the same material written out as a user material, (ii) the paper's model in numpy (dispersion off)."""
    e = b / (b_per_joule(tfwhm) * N2_BUILTIN / N2)       # b = k0 n2_total I0 L of the stationary response
    out = {}
    runs = {}
    for name, sysd in (("builtin", system_builtin()), ("userDisp", system_orient_user(True)), ("userNoDisp", system_orient_user(False))):
        r = uf.run(sysd, spec(e, tfwhm))
        runs[name] = r
        sp = np.asarray(r["output_spectrum"])
        runs[name + "_s"] = sp / sp.sum()
    r = runs["userNoDisp"]
    g = r.summary["grid"]
    t, pw = np.asarray(r["input_time_s"]), np.asarray(r["input_power_w"])
    phi = model_phase_orient(t, pw, b)
    Wm, sm = model_spectrum(t, pw, phi, g["reportOmega0"] - g["omega0"])
    wn = to_wavenumber(np.asarray(r["output_omega_rad_s"]) - g["omega0"])
    out["tvUserVsNumpy"] = tv(runs["userNoDisp_s"], sm)
    out["tvBuiltinVsUserSameMaterial"] = tv(runs["builtin_s"], runs["userDisp_s"])
    out["tvDispersionEffect"] = tv(runs["builtin_s"], runs["userNoDisp_s"])
    s = runs["builtin_s"]
    out["redFraction"] = float(s[wn < -1.0].sum() / s[np.abs(wn) > 1.0].sum())
    cum = np.cumsum(s)
    out["redWing0p5pct"] = float(wn[np.searchsorted(cum, 0.005)])
    out["blueWing99p5pct"] = float(wn[np.searchsorted(cum, 0.995)])
    out["bReported"] = float(runs["builtin"].summary["totals"]["bIntegralRad"])
    out["wn"], out["s"], out["sNoDisp"], out["sModel"], out["wnm"] = wn, s, runs["userNoDisp_s"], sm, to_wavenumber(Wm)
    return out


def auto_grid_check(tau=18e-12, b=B_PEAK):
    """Regression check of the 2026-11 grid-planner fix (was repro_1.py): a delayed response is a circular convolution; the auto window
    must include its memory.  Returns the planned window, whether the fixed 120 ps window warns, and the TV of the auto-grid spectrum
    against the paper's model evaluated on the same grid."""
    e = b / b_per_joule(T_FWHM)
    sp_auto = spec(e)
    sp_auto["grid"] = {"auto": True}
    r = uf.run(system(tau, False), sp_auto)
    t, pw = np.asarray(r["input_time_s"]), np.asarray(r["input_power_w"])
    g = r.summary["grid"]
    phi = model_phase(t, pw, tau, b)
    Wm, sm = model_spectrum(t, pw, phi, g["reportOmega0"] - g["omega0"])
    sa = np.asarray(r["output_spectrum"])
    sa = sa / sa.sum()
    rf = uf.run(system(tau, False), spec(e))          # fixed 120 ps window: too short for 18 ps
    return {"windowPs": g["windowS"] * 1e12, "tv": tv(sa, sm), "fixedWindowWarns": float(any("Grid looks too small" in w for w in rf.summary["warnings"]))}


def to_wavenumber(om):
    return om / (2 * math.pi * C0) / 100.0      # cm^-1


def minima(x, y, lo, hi, floor=1e-4):
    """Positions of the local minima of y on [lo, hi] (x ascending), where the envelope is above floor * max."""
    m = (x >= lo) & (x <= hi)
    xs, ys = x[m], y[m]
    idx = np.where((ys[1:-1] < ys[:-2]) & (ys[1:-1] < ys[2:]))[0] + 1
    return xs[idx], ys[idx]


def stationary_spacing(t, phi, wn_grid):
    """Period (cm^-1) of the interference from the paper's formula: 1/(c (t2 - t1)) with -dphi/dt(t1) = -dphi/dt(t2) = shift."""
    w = -np.gradient(phi, t)                       # instantaneous frequency shift (rad/s), physical sign
    k = int(np.argmin(w))                          # red extremum
    out = []
    for s in wn_grid:                              # s in cm^-1 (negative = red)
        ws = 2 * math.pi * C0 * 100.0 * s
        left = np.where((w[:k] - ws)[:-1] * (w[:k] - ws)[1:] < 0)[0]
        right = np.where((w[k:] - ws)[:-1] * (w[k:] - ws)[1:] < 0)[0]
        if len(left) == 0 or len(right) == 0:
            out.append(np.nan)
            continue
        t1 = t[left[0]]
        t2 = t[k + right[-1]]
        out.append(1.0 / (C0 * 100.0 * abs(t2 - t1)))
    return np.array(out)


def analyse(tau, tfwhm=T_FWHM, b=B_PEAK):
    om, s, t, p, r = engine_spectrum(tau, tfwhm, b)
    om2, s2, _, _, r2 = engine_spectrum(tau, tfwhm, b, dispersion=False)
    phi = model_phase(t, p, tau, b)
    g = r.summary["grid"]
    Wm, sm = model_spectrum(t, p, phi, g["reportOmega0"] - g["omega0"])
    _, sm_lattice = model_spectrum(t, p, phi)                 # old reference: carrier assumed on the lattice
    wn = to_wavenumber(om)
    wnm = to_wavenumber(Wm)
    # the model grid and the engine grid are the same frequency grid (same n, dt): compare directly
    same = len(wn) == len(wnm)
    diff_disp = float(np.sum(np.abs(s - np.interp(wn, wnm, sm)))) / 2 if same else float("nan")
    diff = float(np.sum(np.abs(s2 - np.interp(wn, wnm, sm)))) / 2 if same else float("nan")   # dispersion-free engine vs the paper's model
    diff_lattice = float(np.sum(np.abs(s2 - np.interp(wn, wnm, sm_lattice)))) / 2 if same else float("nan")   # same, old lattice-carrier reference
    red = float(s[wn < -1.0].sum() / s[np.abs(wn) > 1.0].sum()) if True else 0.0
    return {"wn": wn, "s": s, "wnm": wnm, "sm": sm, "t": t, "p": p, "phi": phi, "tau": tau, "diff": diff, "diffLattice": diff_lattice, "diffDisp": diff_disp, "bNoDisp": float(r2.summary["totals"]["bIntegralRad"]), "red": red,
            "r": r}


def main():
    p = Paper(__file__)
    num = {}
    taus = [0.0, 0.6e-12, 2e-12, 6e-12, 18e-12] if p.fast else [0.0, 0.2e-12, 0.6e-12, 1e-12, 2e-12, 4e-12, 6e-12, 12e-12, 18e-12]
    res = {tau: analyse(tau) for tau in taus}
    main_run = res[2e-12]
    wn, s = main_run["wn"], main_run["s"]

    # --- energy balance, grid
    num["energyTransmission"] = float(main_run["r"].summary["totals"]["energyTransmission"])
    num["bIntegralReported"] = float(main_run["r"].summary["totals"]["bIntegralRad"])
    num["gddOverT0Sq"] = float(abs(main_run["r"].summary["totals"]["systemGddS2"]) / (T_FWHM / 1.665) ** 2)

    # --- engine vs the paper's lowest-order model, spectrum (total variation distance of the two normalised spectra)
    for tau, rr in res.items():
        num["modelVsEngineTV_tau%gps" % (tau * 1e12)] = rr["diff"]
    num["modelVsEngineTVMax"] = float(max(rr["diff"] for rr in res.values()))
    num["modelVsEngineTVMaxLatticeCarrierRef"] = float(max(rr["diffLattice"] for rr in res.values()))   # the old reference (carrier on the lattice)
    g0 = main_run["r"].summary["grid"]
    num["carrierOffsetCm1"] = float(to_wavenumber(g0["reportOmega0"] - g0["omega0"]))
    num["latticeStepCm1"] = float(to_wavenumber(g0["domega"]))
    for tau, rr in res.items():
        num["dispersionEffectTV_tau%gps" % (tau * 1e12)] = rr["diffDisp"]
    num["dispersionEffectTVMax"] = float(max(rr["diffDisp"] for rr in res.values()))
    num["bNoDispersionTau0"] = res[0.0]["bNoDisp"]

    # --- direction and sides
    om0 = res[0.0]
    num["redFractionTau0"] = om0["red"]
    num["redFractionTau2ps"] = main_run["red"]
    num["redFractionTau18ps"] = res[18e-12]["red"]
    num["redFractionMonotonic"] = float(all(res[a]["red"] <= res[b]["red"] + 1e-3 for a, b in zip(taus[:-1], taus[1:])))
    num["redFractionCurve"] = {"x": np.array(taus) / T_FWHM, "y": [res[tt]["red"] for tt in taus]}
    num["redFractionTau6ps"] = res[6e-12]["red"]
    num["paperRedFraction"] = 0.9               # p. 1098: "about 90 %" on the low-frequency side
    num["paperMaxOverMinLowerBound"] = 10.0     # p. 1098: ratio between maxima and minima "at least 10"
    num["paperPeriodCm1"] = 10.0                # p. 1098: average period 10 +- 3 cm^-1
    num["tau0Symmetry"] = float(abs(om0["red"] - 0.5))

    # --- red-side extent and fringes for the main run
    cum = np.cumsum(s)
    lo_edge = float(wn[np.searchsorted(cum, 0.005)])       # 0.5 % point of the red wing
    hi_edge = float(wn[np.searchsorted(cum, 0.995)])
    num["redWing0p5pctWavenumber"] = lo_edge
    num["blueWing99p5pctWavenumber"] = hi_edge
    num["maxShiftOverCarrier"] = float(abs(lo_edge) * 100 * LAM)       # |dnu| / nu
    # red extremum of the spectrum: the maximum of the envelope near the extremal instantaneous shift
    wins = (wn < -3)
    red_peak_pos = float(wn[wins][np.argmax(s[wins])])
    # the extremum from the model's instantaneous frequency
    wmin = float(to_wavenumber(np.min(-np.gradient(main_run["phi"], main_run["t"]))))
    num["instantaneousRedExtremeWavenumber"] = wmin
    num["spectrumMaxRedSideWavenumber"] = red_peak_pos
    num["spectrumMaxMinusExtremeWavenumber"] = float(red_peak_pos - wmin)
    num["peakAtLaserFrequency"] = float(s[np.abs(wn) < 1.0].max() > 0.0)
    mn_x, mn_y = minima(wn, s, wmin * 0.95, -2.0)
    num["fringeMinimaCount"] = float(len(mn_x))
    sp = np.diff(mn_x)
    mid = 0.5 * (mn_x[1:] + mn_x[:-1])
    if len(sp) >= 3:
        # spacing grows towards the red extremum: spacing at the outer third vs the inner third
        order = np.argsort(np.abs(mid))
        inner = float(np.mean(sp[order[: max(1, len(sp) // 3)]]))
        outer = float(np.mean(sp[order[-max(1, len(sp) // 3):]]))
        num["fringeSpacingInnerCm1"] = inner
        num["fringeSpacingOuterCm1"] = outer
        num["fringeSpacingOuterOverInner"] = outer / inner
        num["fringeMeanSpacingCm1"] = float(np.mean(sp))
        # max / min ratio of the structure on the red side (adjacent maximum over minimum, median)
        ratios = []
        for i in range(len(mn_x) - 1):
            seg = (wn >= mn_x[i]) & (wn <= mn_x[i + 1])
            ratios.append(s[seg].max() / s[seg][np.argmin(np.abs(wn[seg] - (mn_x[i] if False else mn_x[i])))] if False else s[seg].max() / min(mn_y[i], mn_y[i + 1]))
        num["fringeMaxOverMinMedian"] = float(np.median(ratios))
        # against the paper's stationary-phase formula, same shifts
        pred = stationary_spacing(main_run["t"], main_run["phi"], mid)
        ok = np.isfinite(pred)
        num["fringeSpacingVsFormulaMedianRatio"] = float(np.median(sp[ok] / pred[ok]))
        num["fringeSpacingCurve"] = {"x": mid, "y": sp}
        num["fringeFormulaCurve"] = {"x": mid, "y": pred}
        # period grows linearly with the distance from the line centre (paper p. 1098): coefficient of determination of a line
        xs = np.abs(mid)
        co = np.polyfit(xs, sp, 1)
        res_ = sp - np.polyval(co, xs)
        num["fringeSpacingLinearR2"] = float(1 - np.sum(res_ ** 2) / np.sum((sp - sp.mean()) ** 2))
        num["fringeSpacingSlopePerCm1"] = float(co[0])
        bx, by = minima(wn, s, 2.0, 0.95 * hi_edge)
        num["blueMinimaCount"] = float(len(bx))
        num["blueMeanSpacingCm1"] = float(np.mean(np.diff(bx))) if len(bx) > 2 else float("nan")
        num["blueOverRedMeanSpacing"] = float(num["blueMeanSpacingCm1"] / np.mean(sp)) if len(bx) > 2 else float("nan")
        num["fringeMeanTimesCT"] = float(np.mean(sp) * 100 * C0 * T_FWHM)        # mean period (1/s) x T_FWHM
    num["paperMeanPeriodTimesDuration"] = 10.0 * 100 * C0 * 6e-12                   # the paper's own numbers: 10 cm^-1 x 6 ps
    num["curveSpectrum"] = {"x": wn, "y": s}

    # --- built-in CS2 (orientational response) vs the numpy model and vs the same material written as a user material
    bc = builtin_cs2()
    num["builtinCs2VsUserMaterialTV"] = bc["tvBuiltinVsUserSameMaterial"]
    num["builtinOrientationalVsNumpyModelTV"] = bc["tvUserVsNumpy"]
    num["builtinDispersionEffectTV"] = bc["tvDispersionEffect"]
    num["builtinRedFraction"] = bc["redFraction"]
    num["builtinRedWing0p5pctWavenumber"] = bc["redWing0p5pct"]
    num["builtinBlueWing99p5pctWavenumber"] = bc["blueWing99p5pct"]
    num["builtinBIntegralReported"] = bc["bReported"]
    # --- grid planner regression (was repro_1.py)
    ag = auto_grid_check()
    num["autoGridTau18psWindowPs"] = ag["windowPs"]
    num["autoGridTau18psVsModelTV"] = ag["tv"]
    num["fixedShortWindowWarns"] = ag["fixedWindowWarns"]
    num["curveBuiltin"] = {"x": bc["wn"], "y": bc["s"]}

    p.write_numbers(num)
    plots(p, res, num, taus, main_run)
    plot_builtin(p, bc, main_run)


def plots(p, res, num, taus, main_run):
    wn, s = main_run["wn"], main_run["s"]
    fig, ax = p.figure(2, 1, 9.6, 3.9)
    m = (wn > -330) & (wn < 250)
    ax[0].semilogy(wn[m], s[m] / s.max(), color=BLUE, lw=1.0, label="engine (CS2 dispersion), tau = 2 ps, B = 120 rad")
    mm = (main_run["wnm"] > -330) & (main_run["wnm"] < 250)
    ax[0].semilogy(main_run["wnm"][mm], main_run["sm"][mm] / s.max(), color=ORANGE, lw=0.8, ls="--", label="paper's lowest-order model (numpy)")
    ax[0].axvline(0, color=REF2, lw=0.8)
    ax[0].set_ylim(1e-5, 3)
    ax[0].set_xlabel("shift from the laser line (cm^-1); negative = low-frequency side")
    ax[0].set_ylabel("spectrum (peak = 1)")
    ax[0].legend(fontsize=7.5)
    if "fringeSpacingCurve" in num:
        c = num["fringeSpacingCurve"]
        ax[1].plot(c["x"], c["y"], "o", color=BLUE, ms=4, label="engine: spacing of adjacent minima")
        f = num["fringeFormulaCurve"]
        ax[1].plot(f["x"], f["y"], "-", color=REF, label="paper's formula 1/(c (t2 - t1))")
        ax[1].axhspan(7, 13, color=REF2, alpha=0.25, label="paper: average period 10 +- 3 cm^-1")
    ax[1].set_xlabel("shift (cm^-1)")
    ax[1].set_ylabel("fringe period (cm^-1)")
    ax[1].legend(fontsize=7.5)
    p.save(fig, "spectrum-fringes")

    fig, ax = p.figure(2, 1, 9.6, 3.9)
    for tau, col in zip(taus, (REF2, AQUA, BLUE, ORANGE, "#c4122f", "#7a4ad6", "#0b0b0b", "#999999", "#2a78d6")):
        r = res[tau]
        mk = (r["wn"] > -400) & (r["wn"] < 400)
        ax[0].semilogy(r["wn"][mk], r["s"][mk] / r["s"].max(), lw=0.9, color=col, label="tau = %g ps" % (tau * 1e12))
    ax[0].set_ylim(1e-5, 3)
    ax[0].set_xlabel("shift (cm^-1)")
    ax[0].set_ylabel("spectrum (peak = 1)")
    ax[0].legend(fontsize=7, ncol=2)
    cur = num["redFractionCurve"]
    ax[1].plot(cur["x"], cur["y"], "o-", color=BLUE, label="engine")
    ax[1].axhline(0.5, color=REF2, ls=":", label="tau = 0: symmetric (paper p. 1099)")
    ax[1].axhline(0.9, color=REF, ls="--", label="paper: about 90 % on the low-frequency side (p. 1098)")
    ax[1].set_xscale("symlog", linthresh=0.05)
    ax[1].set_xlabel("tau / pulse FWHM")
    ax[1].set_ylabel("energy fraction at shifts < 0 (|shift| > 1 cm^-1)")
    ax[1].legend(fontsize=7.5, loc="lower right")
    p.save(fig, "tau-scan")


def plot_builtin(p, bc, main_run):
    fig, ax = p.figure(1, 1, 6.8, 4.0)
    m = (bc["wn"] > -330) & (bc["wn"] < 250)
    ax.semilogy(bc["wn"][m], bc["s"][m] / bc["s"].max(), color=BLUE, lw=1.0, label="engine, built-in CS2 (with dispersion)")
    ax.semilogy(bc["wn"][m], bc["sNoDisp"][m] / bc["s"].max(), color=AQUA, lw=0.8, label="engine, same response, dispersion off")
    ax.semilogy(bc["wnm"][m], bc["sModel"][m] / bc["s"].max(), color=ORANGE, lw=0.8, ls="--", label="paper's model (numpy), 10 % instant + 90 % orientational")
    ax.semilogy(main_run["wn"][m], main_run["s"][m] / main_run["s"].max(), color=REF2, lw=0.8, label="single exponential tau = 2 ps (main run)")
    ax.set_ylim(1e-5, 3)
    ax.set_xlabel("shift from the laser line (cm^-1); negative = low-frequency side")
    ax.set_ylabel("spectrum (peak = 1)")
    ax.legend(fontsize=7)
    p.save(fig, "builtin-cs2")


if __name__ == "__main__":
    main()
