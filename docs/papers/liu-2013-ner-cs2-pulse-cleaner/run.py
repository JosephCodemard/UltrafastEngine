#!/usr/bin/env python3
"""Liu et al., Opt. Lett. 38, 1838 (2013): pulse cleaner by nonlinear ellipse rotation (NER) in liquid CS2.

    python run.py --tier fast            # coarse angle / phase grids (~1-2 min)
    python run.py --tier full --figures  # finer grids; committed figures

Chain (Fig. 1 of the paper): linear x input -> QWP1 (axis alpha) -> CS2 Kerr cell -> QWP2 (axis perpendicular to QWP1)
-> crossed polariser.  All of it runs as engine elements (waveplate, bulk, waveplate, polarizer); no Jones shortcut.

CS2 (since 2026-11 a built-in material, used in section 4 of main()).  The paper's Eq. (1) model (instantaneous Kerr effect only) is run with a user material:
  * index: Sellmeier-type formula of Kedenburg et al., Opt. Mater. Express 2, 1588 (2012), 20 C, 0.5-1.6 um, as published on
    refractiveindex.info (CC0): n^2 = 1 + 1.50387 l^2 / (l^2 - 0.03049), l in um.  NOT from either paper.
  * n2 = 3e-19 m^2/W: the value the paper quotes (p. 1838, "n2 = (3 +- 0.6) 1e-19 m^2/W", citing Ganeev 2004), used as an
    instantaneous (electronic-like) Kerr effect, which is what the paper's Eq. (1) assumes.  No delayed response (see README).
1-D plane wave: the spatial model (self-focusing, P/Pcr ~ 1e5 in the experiment) is not part of the paper's Eq. (1) either.

Conventions: B = the B-integral of LINEAR light at the same peak intensity (summary bIntegralRad, k0 n2 I0 L).
The paper never defines phi_SPM for elliptical light; the mapping to B is derived below and checked, not fitted.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402

C0 = 299792458.0
N2_PAPER = 3e-19                      # m^2/W, p. 1838
CS2_INDEX = "sqrt(1+1.50387*lambda_um^2/(lambda_um^2-0.03049))"   # Kedenburg 2012 (external)


def system(alpha_deg, lam, length_m, n2=N2_PAPER, er=1e8, qwp2_offset=90.0, builtin=False):
    """builtin=True: the engine's built-in material CS2 (Kedenburg index, electronic n2 3e-19 plus the delayed orientational response,
    n2_total 3e-18, fR 0.9, 0.2/1.6 ps, anisotropicFraction 1.5; docs/reference/materials.md).  Otherwise the user material of the
    paper's Eq. (1): same index, instantaneous Kerr n2 = 3e-19 only."""
    mat = {"index": {"kind": "expression", "expr": CS2_INDEX}, "n2": n2}
    el = [{"id": "q1", "type": "waveplate", "plate": "quarter", "axisDeg": alpha_deg, "designWavelengthM": lam},
          {"id": "cs2", "type": "bulk", "material": "CS2", "lengthM": length_m, "beam": {"kind": "constant", "radiusM": 100e-6}},
          {"id": "q2", "type": "waveplate", "plate": "quarter", "axisDeg": alpha_deg + qwp2_offset, "designWavelengthM": lam}]
    if er:
        el.append({"id": "pol", "type": "polarizer", "axisDeg": 90, "extinctionRatio": er})
    out = {"format": "ultrafast-system", "version": 2, "name": "liu-ner", "materials": {"CS2": mat}, "elements": el}
    if builtin:
        del out["materials"]
    return out


def spec(lam, tau, energy_j, n=2048, window=3e-12):
    return {"format": "ultrafast-run", "version": 2, "name": "liu-ner",
            "pulse": {"shape": "gaussian", "centerWavelengthM": lam, "durationFwhmS": tau, "energyJ": energy_j, "polarization": "x"},
            "grid": {"n": n, "windowS": window, "auto": False}, "physics": {"selfSteepening": False}}


def to_time(a):
    return np.fft.fftshift(np.fft.fft(np.fft.ifftshift(a)))


def power(r, which):
    ex = to_time(r[which + "_ax_re"] + 1j * r[which + "_ax_im"])
    ey = to_time(r[which + "_ay_re"] + 1j * r[which + "_ay_im"])
    return np.abs(ex) ** 2 + np.abs(ey) ** 2


class Chain:
    """One wavelength / duration / cell length; calibrated so that energies are given as B of linear light."""

    def __init__(self, lam, tau, length, n=2048, window=3e-12, builtin=False):
        self.lam, self.tau, self.length, self.n, self.window, self.builtin = lam, tau, length, n, window, builtin
        # B per joule of LINEAR light at the input peak: calibrated on a 10 um cell where dispersion cannot change the pulse
        # (the engine's reported B follows the pulse inside the cell, so in a 2 mm cell it is lower than k0 n2 I0 L), scaled with length
        # (always with the user material of n2 = 3e-19, also when builtin=True: B and the intensity axis are defined by the paper's n2)
        lc = 10e-6
        r = uf.run(system(0.0, lam, lc, er=0), spec(lam, tau, 1e-6, n, window))
        self.b_per_j = r.summary["totals"]["bIntegralRad"] / 1e-6 * length / lc
        r = uf.run(system(0.0, lam, length, er=0), spec(lam, tau, 1e-9, n, window))
        self.gdd_s2 = r.summary["totals"]["systemGddS2"]
        self.k0 = 2 * math.pi / lam

    def run(self, alpha, b, er=1e8, n2=N2_PAPER):
        r = uf.run(system(alpha, self.lam, self.length, n2=n2, er=er, builtin=self.builtin), spec(self.lam, self.tau, b / self.b_per_j, self.n, self.window))
        t = np.asarray(r["output_time_s"])
        pin, pout = power(r, "input"), power(r, "output")
        return t, pin, pout

    def transmission(self, alpha, b, **kw):
        t, pin, pout = self.run(alpha, b, **kw)
        return float(pout.sum() / pin.sum())

    def centre_transmission(self, alpha, b, **kw):
        t, pin, pout = self.run(alpha, b, **kw)
        i = int(np.argmax(pin))
        return float(pout[i] / pin[i])


# ---- the paper's formula (Eq. 1) and the Jones result, as functions of the peak phase
def liu_t(alpha_deg, phi_peak, tau, n=4001):
    """Energy transmissivity of Eq. (1) for a Gaussian pulse: I_NER/I = sin^2(2a) sin^2(phi(t) cos(2a)/6), phi(t) = phi_peak I(t)/I0."""
    t = np.linspace(-3 * tau, 3 * tau, n)
    prof = np.exp(-4 * math.log(2) * (t / tau) ** 2)
    a = np.radians(alpha_deg)
    return float(np.sum(math.sin(2 * a) ** 2 * np.sin(phi_peak * prof * math.cos(2 * a) / 6) ** 2 * prof) / np.sum(prof))


def jones_t(alpha_deg, b):
    """Instantaneous T at the pulse peak: QWP(alpha), rotation theta = B sin(2 alpha)/3, QWP(alpha + 90), crossed polariser."""
    a = math.radians(alpha_deg)
    return math.cos(2 * a) ** 2 * math.sin(b * math.sin(2 * a) / 3) ** 2


def eq2_contrast(c_in, r, eta):
    """Paper Eq. (2): C_out = C_in^3 [1 + R/(C_in^2 eta)] / (1 + R/eta)  (contrast = pedestal / peak, so smaller is better)."""
    return c_in ** 3 * (1 + r / (c_in ** 2 * eta)) / (1 + r / eta)


def cs2_n(lam_m):
    l2 = (np.asarray(lam_m) * 1e6) ** 2
    return np.sqrt(1 + 1.50387 * l2 / (l2 - 0.03049))


def split_step_check(ch, b, steps=400):
    """Independent scalar split-step in numpy: linear x light (QWP axis along x), instantaneous Kerr phase b*P/Ppeak over the cell,
    CS2 dispersion straight from the index formula (convention e^{-i w t}).  Returns the relative differences (peak power, FWHM)
    of its output from the engine's output for the same input."""
    r = uf.run(system(0.0, ch.lam, ch.length, er=0), spec(ch.lam, ch.tau, b / ch.b_per_j, ch.n, ch.window))
    t = np.asarray(r["output_time_s"])
    pe, pin = power(r, "output"), power(r, "input")
    n = len(t)
    om = 2 * np.pi * np.fft.fftfreq(n, t[1] - t[0])
    w0 = 2 * np.pi * C0 / ch.lam
    kk = lambda w: cs2_n(2 * np.pi * C0 / w) * w / C0
    h = 1e-3 * w0
    k1 = (kk(w0 + h) - kk(w0 - h)) / (2 * h)
    lin = np.exp(1j * (kk(w0 + om) - kk(w0) - k1 * om) * ch.length / steps)
    a = np.sqrt(pin).astype(complex)
    for _ in range(steps):
        a = np.fft.fft(np.fft.ifft(a) * lin)
        a = a * np.exp(1j * b * (np.abs(a) ** 2 / pin.max()) / steps)
    ps = np.abs(a) ** 2
    return {"peak": float(abs(ps.max() - pe.max()) / pe.max()), "fwhm": float(abs(fwhm(t, ps) - fwhm(t, pe)) / fwhm(t, pe)),
            "fwhmEngineFs": fwhm(t, pe) * 1e15, "fwhmSplitStepFs": fwhm(t, ps) * 1e15}


def anisotropic_fraction_check():
    """Regression of the 2026-11 fix (old repro_1.py): the NER chain with a user delayed response of kind 'expression' must depend on
    anisotropicFraction (it was silently ignored), and the value 1.5 of a purely orientational response must be accepted (was an error)."""
    lam = 800e-9
    def t(b):
        mat = {"index": {"kind": "expression", "expr": "1.6"}, "n2": 3e-19, "ramanFraction": 0.6,
               "raman": {"kind": "expression", "expr": "exp(-t/30e-15)/30e-15*step(t)", "anisotropicFraction": b}}
        s = system(20.0, lam, 2e-3)
        s["materials"] = {"CS2": mat}
        r = uf.run(s, spec(lam, 100e-15, 2e-6, 2048, 4e-12))
        return float(power(r, "output").sum() / power(r, "input").sum())
    t0, t1, t15 = t(0.0), t(1.0), t(1.5)
    return {"anisotropicExprT_b0": t0, "anisotropicExprT_b1": t1, "anisotropicExprT_b1p5": t15, "anisotropicExprEffect": abs(t1 - t0)}


def peak_x(x, y):
    """x of the maximum of y(x), refined by a parabola through the three points around the largest sample."""
    i = int(np.argmax(y))
    if 0 < i < len(y) - 1:
        a, b, c = y[i - 1], y[i], y[i + 1]
        d = a - 2 * b + c
        if d != 0:
            return float(x[i] + 0.5 * (a - c) / d * (x[1] - x[0]))
    return float(x[i])


def fwhm(t, p):
    i = int(np.argmax(p))
    h = p[i] / 2
    lo = i
    while lo > 0 and p[lo] > h:
        lo -= 1
    hi = i
    while hi < len(p) - 1 and p[hi] > h:
        hi += 1
    return float(t[hi] - t[lo])


def main():
    p = Paper(__file__)
    num = {}
    # ---------------- 1. theory case of the paper: 800 nm, 100 fs, thin cell (no dispersion effect), instantaneous Kerr
    th = Chain(800e-9, 100e-15, 10e-6)      # 10 um: dispersion-free in effect (at 0.1 mm the GDD already changes T by 5 % at B = 8 pi)
    pts = [(5, math.pi), (12.5, 4 * math.pi), (20, 4 * math.pi), (34, 8 * math.pi), (40, 2 * math.pi)]
    e1 = [abs(th.centre_transmission(a, b) - jones_t(a, b)) for a, b in pts]
    num["jonesVsEngineMaxErr"] = float(max(e1))

    # transmissivity map T(alpha_J, B): engine vs the paper's Eq. (1) with the DERIVED mapping alpha_L = 45 - alpha_J, phi = 2 B
    alphas = np.arange(2.0, 44.1, 3.0 if p.fast else 1.5)
    bs = np.pi * np.arange(0.0, 6.01, 0.75 if p.fast else 0.5)
    eng = np.zeros((len(alphas), len(bs)))
    mapped = np.zeros_like(eng)
    literal = np.zeros_like(eng)
    for i, a in enumerate(alphas):
        for j, b in enumerate(bs):
            eng[i, j] = th.transmission(a, b) if b > 0 else 0.0
            mapped[i, j] = liu_t(45.0 - a, 2 * b, 100e-15)
            literal[i, j] = liu_t(a, b, 100e-15)
    num["liuMappedVsEngineMaxErr"] = float(np.max(np.abs(eng - mapped)))
    num["liuLiteralVsEngineMaxErr"] = float(np.max(np.abs(eng - literal)))   # information: the paper's definitions taken literally

    # the paper's own Eq. (1) maximum (its Fig. 2a), on a fine grid, with its own phi axis
    aa = np.arange(5.0, 85.0, 0.5)
    pp = np.pi * np.arange(0.25, 12.01, 0.25)
    tl = np.array([[liu_t(a, ph, 100e-15, 801) for ph in pp] for a in aa])
    i, j = np.unravel_index(int(np.argmax(tl)), tl.shape)
    num["liuEq1Tmax"] = float(tl[i, j])
    num["liuEq1TmaxAlphaDeg"] = float(aa[i])
    num["liuEq1TmaxPhiOverPi"] = float(pp[j] / math.pi)
    # the best alpha at phi = 9 pi (where Fig. 2a peaks, read off) and the maximum of T(phi) with alpha = 34 deg
    best_phi = tl.max(axis=0)
    num["liuEq1TmaxEnvelopeAtPhi9Pi"] = float(best_phi[int(np.argmin(np.abs(pp - 9 * math.pi)))])
    t34 = np.array([liu_t(34.0, ph, 100e-15, 801) for ph in pp])
    num["liuEq1At34Tmax"] = float(t34.max())
    num["liuEq1At34PhiOverPi"] = float(pp[int(np.argmax(t34))] / math.pi)
    # engine optimum over the same domain (with the derived mapping the engine B range is half of phi's)
    k = int(np.argmax(eng))
    ia, ib = np.unravel_index(k, eng.shape)
    num["engineTmax"] = float(eng.max())
    num["engineTmaxAlphaFromFastAxisDeg"] = float(alphas[ia])
    num["engineTmaxAlphaPaperConventionDeg"] = float(45 - alphas[ia])
    num["engineTmaxBOverPi"] = float(bs[ib] / math.pi)

    # ---------------- 2. contrast (Eq. 2): engine at the pedestal intensity vs at the peak
    c_in = 1e-5
    b_pk = 4 * math.pi
    a_opt = float(alphas[ia])
    cont = {}
    for er in (1e12, 1e4):
        tpk = th.centre_transmission(a_opt, b_pk, er=er)
        tpd = th.centre_transmission(a_opt, b_pk * c_in, er=er)
        cont[er] = {"tpk": tpk, "tped": tpd, "cout": c_in * tpd / tpk}
    eta = th.transmission(a_opt, b_pk, er=1e4)
    num["contrastOutIdealEr"] = cont[1e12]["cout"]
    num["contrastOutIdealOrdersGain"] = float(math.log10(c_in / cont[1e12]["cout"]))
    num["contrastOutEr1e4"] = cont[1e4]["cout"]
    num["contrastEr1e4OrdersGain"] = float(math.log10(c_in / cont[1e4]["cout"]))
    num["eq2OrdersGainEr1e4"] = float(math.log10(c_in / eq2_contrast(c_in, 1e-4, eta)))
    num["eq2OrdersGainEr1e4Eta0p6"] = float(math.log10(c_in / eq2_contrast(c_in, 1e-4, 0.6)))
    num["eq2CubeLawLog10"] = float(math.log10(eq2_contrast(c_in, 0.0, 1.0)))
    num["engineTransmissionAtContrastPoint"] = float(eta)

    # ---------------- 3. the experiment: 808 nm, 70 fs, 2 mm CS2 with its dispersion
    ex = Chain(808e-9, 70e-15, 2e-3, n=2048, window=4e-12)
    k_n2 = ex.k0 * N2_PAPER * 2e-3                      # B per (W/m^2)
    to_gw_cm2 = lambda b, n2=N2_PAPER: b / (ex.k0 * n2 * 2e-3) / 1e4 / 1e9
    num["experimentBAt320GWcm2"] = float(k_n2 * 320e9 * 1e4)
    b320 = num["experimentBAt320GWcm2"]
    ints = np.linspace(100, 520, 22 if p.fast else 43)   # GW/cm^2 (Fig. 4b axis)
    sweeps = {}
    for label, a in (("alpha = 12.5 deg (= 32.5 deg in Eq. 1 convention)", 12.5), ("alpha = 32.5 deg from the fast axis, literal", 32.5)):
        sweeps[label] = np.array([ex.transmission(a, k_n2 * g * 1e13) for g in ints])
    keys = list(sweeps)
    s0 = sweeps[keys[0]]
    num["expSweepPeakIntensityGWcm2"] = peak_x(ints, s0)
    num["expSweepPeakT"] = float(s0.max())
    num["expSweepLiteralPeakIntensityGWcm2"] = peak_x(ints, sweeps[keys[1]])
    num["expSweepLiteralPeakT"] = float(sweeps[keys[1]].max())
    # the same with n2 -20 % / +20 % (the paper's +-0.6e-19): the optimum intensity scales as 1/n2 (exact for this response)
    num["expPeakIntensityN2low"] = float(num["expSweepPeakIntensityGWcm2"] * 3.0 / 2.4)
    num["expPeakIntensityN2high"] = float(num["expSweepPeakIntensityGWcm2"] * 3.0 / 3.6)
    # angle scan at B(320 GW/cm^2)
    angs = np.arange(2.0, 50.1, 3.0 if p.fast else 1.5)
    ta = np.array([ex.transmission(a, b320) for a in angs])
    num["expAngleScanBestAlphaFromFastAxisDeg"] = float(angs[int(np.argmax(ta))])
    num["expAngleScanBestAlphaPaperConventionDeg"] = float(45 - angs[int(np.argmax(ta))])
    num["expAngleScanTmax"] = float(ta.max())
    # pulse width of the cleaned pulse at the best operating point of the intensity sweep
    b_best = k_n2 * num["expSweepPeakIntensityGWcm2"] * 1e13
    t, pin, pout = ex.run(12.5, b_best)
    num["expInputFwhmFs"] = fwhm(t, pin) * 1e15
    num["expOutputFwhmFs"] = fwhm(t, pout) * 1e15
    num["expOutputOverInputWidth"] = num["expOutputFwhmFs"] / num["expInputFwhmFs"]
    num["pOverPcrAt320"] = float(0.94 * 6e-3 / 70e-15 / (3.77 * (808e-9) ** 2 / (8 * math.pi * 1.62 * N2_PAPER)))
    num["impliedBeamAreaCm2"] = float(0.94 * 6e-3 / (70e-15 * 320e9))
    # output width vs intensity: with CS2's own dispersion (2 mm) and dispersion-free in effect (10 um, same B)
    thin = Chain(808e-9, 70e-15, 10e-6, n=2048, window=4e-12)
    wid_thick, wid_thin, t_thin = [], [], []
    for g in ints:
        b = k_n2 * g * 1e13
        tt_, pi_, po_ = ex.run(12.5, b)
        wid_thick.append(fwhm(tt_, po_) * 1e15)
        tt_, pi_, po_ = thin.run(12.5, b)
        wid_thin.append(fwhm(tt_, po_) * 1e15)
        t_thin.append(float(po_.sum() / pi_.sum()))
    wid_thick, wid_thin, t_thin = np.array(wid_thick), np.array(wid_thin), np.array(t_thin)
    i320 = int(np.argmin(np.abs(ints - 320)))
    num["expWidthAt320ThickFs"] = float(wid_thick[i320])
    num["expWidthAt320ThinFs"] = float(wid_thin[i320])
    num["expThinWidthOverInputAtPeakT"] = float(wid_thin[int(np.argmax(t_thin))] / num["expInputFwhmFs"])
    num["expThinPeakT"] = float(t_thin.max())
    num["expThinPeakIntensityGWcm2"] = peak_x(ints, t_thin)
    num["systemGddFs2"] = float(ex.gdd_s2 * 1e30)
    ss = split_step_check(ex, b320)
    num["splitStepPeakRelErr"], num["splitStepFwhmRelErr"] = ss["peak"], ss["fwhm"]
    num["splitStepLinearFwhmEngineFs"], num["splitStepLinearFwhmNumpyFs"] = ss["fwhmEngineFs"], ss["fwhmSplitStepFs"]

    # ---------------- 4. the same experiment with the BUILT-IN CS2 (electronic 3e-19 + delayed orientational part, anisotropicFraction 1.5)
    bi = Chain(808e-9, 70e-15, 2e-3, n=32768, window=40e-12, builtin=True)
    us = Chain(808e-9, 70e-15, 2e-3, n=32768, window=40e-12, builtin=False)      # user material on the same fine grid (controls the grid)
    ints_b = np.linspace(100, 900, 9 if p.fast else 17)
    tb_us, tb_bi, wb_us, wb_bi = [], [], [], []
    for g in ints_b:
        b = k_n2 * g * 1e13
        _, pi_, po_ = us.run(12.5, b)
        tb_us.append(float(po_.sum() / pi_.sum()))
        wb_us.append(fwhm(_, po_) * 1e15)
        t_, pi_, po_ = bi.run(12.5, b)
        tb_bi.append(float(po_.sum() / pi_.sum()))
        wb_bi.append(fwhm(t_, po_) * 1e15)
    tb_us, tb_bi, wb_us, wb_bi = map(np.array, (tb_us, tb_bi, wb_us, wb_bi))
    num["builtinSweepPeakIntensityGWcm2"] = peak_x(ints_b, tb_bi)
    num["builtinSweepPeakT"] = float(tb_bi.max())
    num["userFineGridSweepPeakIntensityGWcm2"] = peak_x(ints_b, tb_us)
    num["userFineGridSweepPeakT"] = float(tb_us.max())
    for tag, ch in (("builtin", bi), ("userFineGrid", us)):      # exactly 320 GW/cm2 (not on the sweep axis)
        t_, pi_, po_ = ch.run(12.5, b320)
        num[tag + "TAt320"] = float(po_.sum() / pi_.sum())
        num[tag + "WidthAt320Fs"] = float(fwhm(t_, po_) * 1e15)
    angs_b = np.arange(2.0, 50.1, 4.0 if p.fast else 2.0)
    ta_b = np.array([bi.transmission(a, b320) for a in angs_b])
    num["builtinAngleScanBestAlphaPaperConventionDeg"] = float(45 - peak_x(angs_b, ta_b))
    num["builtinAngleScanTmax"] = float(ta_b.max())
    # grid adequacy at the highest intensity of the sweep (Chain.run drops the summary, so look directly)
    rg = uf.run(system(12.5, 808e-9, 2e-3, builtin=True), spec(808e-9, 70e-15, k_n2 * ints_b.max() * 1e13 / bi.b_per_j, bi.n, bi.window))
    num["builtinGridWarningsAtTopIntensity"] = float(sum("Grid" in w for w in rg.summary["warnings"]))
    # anisotropicFraction acts on a delayed response of kind 'expression', and 1.5 (purely orientational) is accepted (was repro_1.py, fixed 2026-11)
    num.update(anisotropic_fraction_check())
    num["curveBuiltinSweep"] = {"x": ints_b, "y": tb_bi}
    num["curveEng"] = {"x": alphas, "y": eng[:, int(np.argmin(np.abs(bs - 4 * math.pi)))]}
    num["curveSweep"] = {"x": ints, "y": s0}

    p.write_numbers(num)
    plots(p, num, alphas, bs, eng, mapped, aa, pp, tl, cont, ints, sweeps, angs, ta, (t, pin, pout), keys, th, a_opt, wid_thick, wid_thin)
    fig, ax = p.figure(2, 1, 9.6, 3.9)
    ax[0].plot(ints_b, tb_us, color=BLUE, label="user CS2: instantaneous n2 = 3e-19 (paper's Eq. 1 model)")
    ax[0].plot(ints_b, tb_bi, color=ORANGE, label="built-in CS2: + delayed orientational part")
    ax[0].axvline(320, color=REF, ls="--", lw=1)
    ax[0].set_xlabel("peak intensity of linear light (GW/cm2), electronic n2 = 3e-19")
    ax[0].set_ylabel("energy transmissivity (engine)")
    ax[0].legend(fontsize=7, loc="lower right")
    ax[1].plot(ints_b, wb_us, color=BLUE, label="user CS2")
    ax[1].plot(ints_b, wb_bi, color=ORANGE, label="built-in CS2")
    ax[1].axhline(70, color=REF2, ls=":", lw=1)
    ax[1].plot([320], [60], "s", color=REF, label="paper: ~60 fs (Fig. 5b)")
    ax[1].set_xlabel("peak intensity of linear light (GW/cm2)")
    ax[1].set_ylabel("cleaned-pulse FWHM (fs), input 70 fs dotted")
    ax[1].legend(fontsize=7, loc="upper left")
    p.save(fig, "builtin-cs2")


def plots(p, num, alphas, bs, eng, mapped, aa, pp, tl, cont, ints, sweeps, angs, ta, pulses, keys, th, a_opt, wthick, wthin):
    import matplotlib.pyplot as plt
    # A: transmissivity vs phase (paper's Fig. 2a axis): Eq. (1) envelope over alpha, Eq. (1) at 34 deg, engine at its optimum
    fig, ax = p.figure(2, 1, 9.6, 3.9)
    ax[0].plot(pp / math.pi, tl.max(axis=0), color=REF, label="paper Eq. (1), best alpha at each phi")
    ax[0].plot(pp / math.pi, [liu_t(34.0, ph, 100e-15, 801) for ph in pp], color=REF2, ls="--", label="paper Eq. (1), alpha = 34 deg")
    ib = np.argmax(eng.max(axis=1))
    bopt = eng.max(axis=0)
    ax[0].plot(2 * bs / math.pi, bopt, "o-", color=BLUE, ms=4, label="engine, best alpha at each B (phi = 2B)")
    ax[0].axhline(0.6, color=ORANGE, lw=1, ls=":")
    ax[0].text(0.3, 0.61, "paper: maximum > 60 %", color=ORANGE, fontsize=8)
    ax[0].set_xlabel("phi (units of pi rad); engine: phi = 2 B(linear)")
    ax[0].set_ylabel("energy transmissivity")
    ax[0].legend(fontsize=7, loc="lower right")
    j = int(np.argmin(np.abs(bs - 4 * math.pi)))
    ax[1].plot(45 - alphas, eng[:, j], "o", color=BLUE, ms=4, label="engine, B = 4 pi, 45 - alpha")
    ax[1].plot(45 - alphas, mapped[:, j], "-", color=REF, label="paper Eq. (1), phi = 8 pi")
    ax[1].plot(alphas, eng[:, j], "x", color=ORANGE, ms=4, label="engine, alpha from the fast axis")
    ax[1].set_xlabel("alpha (deg), paper convention")
    ax[1].set_ylabel("energy transmissivity")
    ax[1].legend(fontsize=7, loc="upper left")
    p.save(fig, "transmissivity")

    # B: contrast
    fig, ax = p.figure(1, 1, 6.4, 4.0)
    cin = np.logspace(-8, -1, 29)
    for r, col in ((1e-3, BLUE), (1e-4, ORANGE), (1e-5, AQUA), (1e-6, REF2)):
        ax.loglog(cin, [eq2_contrast(c, r, 0.6) for c in cin], color=col, lw=1.4, label="paper Eq. (2), R = %.0e" % r)
    ax.loglog(cin, cin ** 3, color=REF, ls=":", label="C_in^3 (ideal polariser)")
    pts_c = [num["contrastOutIdealEr"], num["contrastOutEr1e4"]]
    ax.loglog([1e-5, 1e-5], pts_c, "o", color="#c4122f", ms=7, label="engine at C_in = 1e-5 (ER 1e12, 1e4)")
    ax.set_xlabel("input contrast (pedestal / peak)")
    ax.set_ylabel("output contrast")
    ax.legend(fontsize=7.5)
    p.save(fig, "contrast")

    # C: experiment-like sweep and pulse
    t, pin, pout = pulses
    fig, ax = p.figure(2, 1, 9.6, 3.9)
    for (lab, s), col in zip(sweeps.items(), (BLUE, ORANGE)):
        ax[0].plot(ints, s, color=col, label=lab, lw=1.6)
    ax[0].axvline(320, color=REF, ls="--", lw=1)
    ax[0].text(325, ax[0].get_ylim()[1] * 0.9, "paper: optimum at\n320 GW/cm2 (read off Fig. 4b)", fontsize=7.5)
    ax[0].axhline(0.30, color=REF2, ls=":", lw=1)
    ax[0].text(105, 0.31, "paper, measured 30 % (incl. optics losses)", fontsize=7.5, color=REF2)
    ax[0].set_xlabel("peak intensity of linear light (GW/cm2), n2 = 3e-19")
    ax[0].set_ylabel("energy transmissivity (engine)")
    ax[0].legend(fontsize=7, loc="lower right")
    tt = (t - t[int(np.argmax(pin))]) * 1e15
    ax[1].plot(ints, wthick, color=BLUE, label="engine, 2 mm CS2, GDD %.0f fs2" % num["systemGddFs2"])
    ax[1].plot(ints, wthin, color=AQUA, label="engine, same B, no dispersion (10 um)")
    ax[1].axhline(70, color=REF2, ls=":", lw=1)
    ax[1].plot([320], [60], "s", color=REF, label="paper: ~60 fs (Fig. 5b)")
    ax[1].set_xlabel("peak intensity of linear light (GW/cm2)")
    ax[1].set_ylabel("cleaned-pulse FWHM (fs), input 70 fs dotted")
    ax[1].legend(fontsize=7, loc="upper left")
    p.save(fig, "experiment")


if __name__ == "__main__":
    main()
