#!/usr/bin/env python3
"""Kaur et al., J. Phys. Photonics 6, 015001 (2024): nonlinear ellipse rotation (NER) in an argon multi-pass cell.

    python run.py --tier fast             # pressure scans in 1-D, seconds to a minute (CI)
    python run.py --tier full --figures   # finer scans, caustic variant, contrast; writes the committed figures

What is modelled (details and every assumption: README.md, paper.json):
  * 1 mJ, 30 fs (Gaussian, transform limited), 800 nm, elliptical input; QWP1 is applied as the ideal input ellipse
    (ellipticity eps = tan(alpha), alpha = QWP1 axis angle, major axis along the QWP axis), QWP2 is the crossed
    (inverse) plate, then a polariser crossed with the input. Both plates are taken as achromatic (broadband in the paper).
  * Cell: 12 passes of 3 m of argon (36 m), 1 % loss per silver-mirror bounce. Two readings of the beam:
      "constant"  w = 1.45 mm on every pass (2.9 mm diameter on the end mirrors, paper p. 2, 'overall constant beam size')
      "caustic"   w0 = 450 um at the centre of each 3 m pass (the stated waist), a Gaussian caustic per pass
  * Internal NER efficiency = energy after / before the polariser (paper p. 3), here from the engine's output field.
  * The analytic reference is the instantaneous-Kerr result: the ellipse rotates by theta(t) = B(t) sin(2 chi) / 3, B = the
    B-integral of LINEAR light of the same intensity (docs/reference/nonlinear.md), no dispersion; the time-averaged
    transmission follows from the Jones matrices.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402
from ultrafast import cells  # noqa: E402

LAM, TAU, E_IN = 800e-9, 30e-15, 1e-3
C0 = 299792458.0
PASS_M, NPASS = 3.0, 12
W_CONST, W0_CAUSTIC = 1.45e-3, 450e-6
MIRROR_R = 0.99                                  # enhanced-silver mirrors, ~1 % per bounce (p. 8, 'reflection losses')
PEAK_W = 0.9394 * E_IN / TAU                     # peak power of the 1 mJ, 30 fs Gaussian pulse (31 GW)
LEAK = 5e-3                                      # polariser extinction ratio quoted as 5e-3 (p. 3); read as a power leak


D_CELL = 2.9                                     # mirror separation of the "cell 2.9 m" reading: its empty mode has 2.87 mm diameter on the mirrors (README)


def system(P_bar, beam="constant", passes=NPASS, mirrors=True, pass_m=PASS_M, w0=W0_CAUSTIC):
    mats = {"Ar": b.material(gas=b.gas("Ar", P_bar, 293.15))}
    co = {"ag": {"kind": "gdd", "R": MIRROR_R, "gddS2": 0.0}}
    els = []
    for k in range(passes):
        bm = ({"kind": "constant", "radiusM": W_CONST} if beam == "constant"
              else {"kind": "gaussian", "w0M": w0, "zWaistM": pass_m / 2, "m2": 1.0})
        els.append({"id": "pass%02d" % (k + 1), "type": "bulk", "material": "Ar", "lengthM": pass_m, "beam": bm})
        if mirrors:
            els.append(b.mirror("m%02d" % (k + 1), coating="ag", medium="Ar"))
    return b.system("kaur-ner", els, materials=mats, coatings=co)


def spec(energy, alpha_deg, linear=False, steepening=True):
    pol = "x" if linear else {"angleDeg": alpha_deg, "ellipticity": math.tan(math.radians(alpha_deg))}
    return {"format": "ultrafast-run", "version": 2, "name": "kaur-ner",
            "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": TAU, "energyJ": energy, "polarization": pol},
            "physics": {"selfSteepening": steepening, "raman": False}}


def spectral_field(r, which="output"):
    return (np.asarray(r[which + "_ax_re"]) + 1j * np.asarray(r[which + "_ax_im"]),
            np.asarray(r[which + "_ay_re"]) + 1j * np.asarray(r[which + "_ay_im"]))


def to_time(a):
    return np.fft.fftshift(np.fft.fft(np.fft.ifftshift(a)))


def qwp(alpha, sign):
    c, s = math.cos(alpha), math.sin(alpha)
    rot = np.array([[c, -s], [s, c]])
    return rot @ np.diag([1, 1j * sign]) @ rot.T


def qwp2(ax, ay, alpha_deg):
    """Crossed second plate = inverse of the input plate (acts on the spectral or the time field)."""
    m = qwp(math.radians(alpha_deg), -1)
    return m[0, 0] * ax + m[0, 1] * ay, m[1, 0] * ax + m[1, 1] * ay


def ner_efficiency(r, alpha_deg, leak=0.0):
    """Internal efficiency (energy behind the polariser / energy in front of it) and the transmitted spectral field."""
    ax, ay = spectral_field(r)
    ox, oy = qwp2(ax, ay, alpha_deg)
    e_in = (np.abs(ox) ** 2 + np.abs(oy) ** 2).sum()
    ty = oy + math.sqrt(leak) * ox
    return float((np.abs(ty) ** 2).sum() / e_in), ty, (ox, oy)


def ftl_fwhm(r, spec_field):
    """FWHM (s) of the Fourier-limited pulse of a spectral amplitude (flat phase)."""
    om = np.asarray(r["output_omega_rad_s"])
    n = len(om)
    p = np.abs(np.fft.fftshift(np.fft.fft(np.abs(spec_field)))) ** 2
    p /= p.max()
    dt = 2 * np.pi / (n * (om[1] - om[0]))
    i = int(np.argmax(p))
    lo = i
    while lo > 0 and p[lo] >= 0.5:
        lo -= 1
    hi = i
    while hi < n - 1 and p[hi] >= 0.5:
        hi += 1
    lo_f = lo + (0.5 - p[lo]) / (p[lo + 1] - p[lo])
    hi_f = hi - 1 + (p[hi - 1] - 0.5) / (p[hi - 1] - p[hi])
    return (hi_f - lo_f) * dt


def analytic_efficiency(b0, eps, n_t=2001, sign=1):
    """Instantaneous Kerr, no dispersion: time-averaged transmission for a Gaussian pulse of peak B (linear light) = b0."""
    chi = math.atan(eps)
    t = np.linspace(-2.5 * TAU, 2.5 * TAU, n_t)
    inten = np.exp(-4 * math.log(2) * (t / TAU) ** 2)
    theta = sign * b0 * inten * math.sin(2 * chi) / 3
    q1 = qwp(chi, 1)
    v0 = q1 @ np.array([1.0, 0.0])
    c, s = np.cos(theta), np.sin(theta)
    vx, vy = c * v0[0] - s * v0[1], s * v0[0] + c * v0[1]
    q2 = qwp(chi, -1)
    oy = q2[1, 0] * vx + q2[1, 1] * vy
    return float((np.abs(oy) ** 2 * inten).sum() / inten.sum())


def cell_mode_ar(p_bar, power_w, d=D_CELL):
    """Kerr-matched mode of the 1.5 m / d cell filled with argon (ultrafast.cells.cell_mode); matched = False if none exists."""
    return cells.cell_mode(LAM, mirror_radius=1.5, separation=d, peak_power=power_w, gas={"species": "Ar", "pressureBar": float(p_bar), "temperatureK": 293.15}, passes=NPASS)


def scan(eps, pressures, beam="constant", pass_m=PASS_M, w0=W0_CAUSTIC, helper=False):
    """Pressure scan. beam 'caustic' uses w0 at the centre of each pass; helper=True takes w0 from the Kerr-matched cell mode at each
    pressure (pressures with no matched mode are skipped)."""
    alpha = math.degrees(math.atan(eps))
    out = []
    for pr in pressures:
        w = w0
        if helper:
            hm = cell_mode_ar(pr, PEAK_W)
            if not hm["matched"]:
                continue
            w = hm["mode"]["waistRadiusM"]
        r = uf.run(system(pr, beam, pass_m=pass_m, w0=w), spec(E_IN, alpha))
        rl = uf.run(system(pr, beam, pass_m=pass_m, w0=w), spec(E_IN, alpha, linear=True))
        eff, ty, _ = ner_efficiency(r, alpha)
        out.append({"p": float(pr), "bLin": rl.summary["totals"]["bIntegralRad"], "eff": eff, "trans": r.summary["totals"]["energyTransmission"],
                    "ftlFs": ftl_fwhm(r, ty) * 1e15})
    return out


xpw_plots_data = {}


def xpw_system(npass, cut, w=840e-6, length=600e-6, gas_loss=0.99 * 0.97, nplates=2):
    """npass passes of the XPW MPC: nplates BaF2 plates (length each, cut along `cut`, [100] along lab x for [001] and [011] cuts) in a
    plane-wave beam of constant radius w, then a mirror/plate loss of gas_loss per pass (1 % silver mirror, 3 % plates, p. 8)."""
    els = []
    for k in range(npass):
        for j in range(nplates):
            els.append({"id": "p%02d_%d" % (k, j), "type": "bulk", "material": "BaF2", "lengthM": length, "kerrCutAxis": cut,
                        "beam": {"kind": "constant", "radiusM": w}})
        if gas_loss < 1.0:
            els.append(b.mirror("m%02d" % k, coating="r"))
    return b.system("kaur-xpw", els, coatings={"r": {"kind": "gdd", "R": gas_loss, "gddS2": 0.0}})


def xpw_spec(beta_deg, energy, tau=TAU):
    return {"format": "ultrafast-run", "version": 2, "name": "kaur-xpw",
            "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": tau, "energyJ": energy,
                      "polarization": {"angleDeg": float(beta_deg), "ellipticity": 0.0}},
            "physics": {"selfSteepening": False, "raman": False}}


def xpw_perp(r, beta_deg):
    """Energy fraction in the polarisation orthogonal to the input's (angle beta from lab x): the XPW share behind a crossed polariser."""
    a = math.radians(beta_deg)
    ax, ay = spectral_field(r)
    par = math.cos(a) * ax + math.sin(a) * ay
    perp = -math.sin(a) * ax + math.cos(a) * ay
    p, q = (np.abs(par) ** 2).sum(), (np.abs(perp) ** 2).sum()
    return float(q / (p + q))


def xpw_energy_for_b(b_target, w=100e-6, length=1e-3, tau=100e-15):
    """Pulse energy for which one `length` of BaF2 in a beam of radius w has B = k0 n2 I0 L = b_target (low-conversion regime)."""
    n2 = float(uf.command({"command": "materials.show", "name": "BaF2"})["n2"])
    i0 = b_target / (2 * math.pi / LAM * n2 * length)
    return i0 * math.pi * w * w / 2 * tau / math.sqrt(4 * math.log(2) / math.pi)


def main():
    p = Paper(__file__)
    fast = p.fast
    numbers = {}

    # ---- 1. conventions: the crossed QWP pair is transparent to the input, so the polariser blocks it -------------------------------
    alpha = math.degrees(math.atan(0.25))
    r0 = uf.run(system(1e-6), spec(1e-9, alpha))
    e0, _, _ = ner_efficiency(r0, alpha)
    numbers["linearLeak"] = e0

    # ---- 2. engine vs the instantaneous-Kerr analytic law (the same Jones calculus), small dispersion -----------------------------
    # 0.5 m of argon at 1 bar in a 0.3 mm beam: B(linear) up to ~ 10 rad, dispersion ~ 9 fs2 (negligible against T0^2 = 324 fs2)
    sysd = b.system("ner-plane", [{"id": "cell", "type": "bulk", "material": "Ar", "lengthM": 0.5, "beam": {"kind": "constant", "radiusM": 0.3e-3}}],
                    materials={"Ar": b.material(gas=b.gas("Ar", 1.0, 293.15))})
    chk = []
    for en in ([0.5e-3, 1e-3, 2e-3] if fast else [0.25e-3, 0.5e-3, 1e-3, 1.5e-3, 2e-3, 3e-3]):
        r = uf.run(sysd, spec(en, alpha, steepening=False))
        rl = uf.run(sysd, spec(en, alpha, linear=True, steepening=False))
        eff, _, _ = ner_efficiency(r, alpha)
        b0 = rl.summary["totals"]["bIntegralRad"]
        chk.append((b0, eff, analytic_efficiency(b0, 0.25)))
    chk = np.array(chk)
    numbers["engineVsAnalyticMaxDev"] = float(np.max(np.abs(chk[:, 1] - chk[:, 2])))
    numbers["engineVsAnalyticB"] = chk[:, 0].tolist()

    # ---- 3. analytic ceilings (time-averaged) -----------------------------------------------------------------------------------------
    b_axis = np.linspace(0, 30, 301)
    ana = {}
    for eps in (0.25, 0.18, 0.15):
        eta = np.array([analytic_efficiency(x, eps, 801) for x in b_axis])
        j = int(np.argmax(eta))
        ana[eps] = eta
        key = "%02d" % round(eps * 100)
        numbers["analyticMaxEff_eps" + key] = float(eta[j])
        numbers["analyticBAtMax_eps" + key] = float(b_axis[j])
    numbers["analyticEffAtB6_eps25"] = analytic_efficiency(6.0, 0.25)
    numbers["analyticThetaPeakDegAtB6_eps25"] = math.degrees(6.0 * math.sin(2 * math.atan(0.25)) / 3)
    numbers["analyticBFor90degPeak_eps25"] = (math.pi / 2) * 3 / math.sin(2 * math.atan(0.25))

    # ---- 4. pressure scans, constant 1.45 mm beam ---------------------------------------------------------------------------------------
    pr_axis = np.arange(0.05, 3.01, 0.05 if fast else 0.025)
    scans = {}
    for eps in (0.25, 0.18, 0.15):
        scans[eps] = scan(eps, pr_axis)
        key = "%02d" % round(eps * 100)
        e = np.array([s["eff"] for s in scans[eps]])
        j = int(np.argmax(e))
        numbers["maxEff_eps" + key] = float(e[j])
        numbers["pAtMaxEff_eps%s_bar" % key] = float(pr_axis[j])
        numbers["bLinAtMaxEff_eps" + key] = scans[eps][j]["bLin"]
        numbers["effAt420mbar_eps" + key] = float(np.interp(0.42, pr_axis, e))
        numbers["effAt350mbar_eps" + key] = float(np.interp(0.35, pr_axis, e))
        numbers["ftlAtMaxFs_eps" + key] = scans[eps][j]["ftlFs"]
        numbers["ftlAt420mbarFs_eps" + key] = float(np.interp(0.42, pr_axis, [s["ftlFs"] for s in scans[eps]]))
        numbers["bLinAt420mbar_eps" + key] = float(np.interp(0.42, pr_axis, [s["bLin"] for s in scans[eps]]))
    numbers["curve_eps25"] = {"x": pr_axis, "y": [s["eff"] for s in scans[0.25]]}
    numbers["bLinPerBar_const"] = scans[0.25][8]["bLin"] / pr_axis[8]

    # ---- 5. mirror losses, contrast (leak 5e-3) ----------------------------------------------------------------------------------------
    numbers["mirrorTransmission12"] = MIRROR_R ** NPASS
    pr18 = numbers["pAtMaxEff_eps18_bar"]
    a18 = math.degrees(math.atan(0.18))
    r = uf.run(system(pr18), spec(E_IN, a18))
    eff, ty, (ox, oy) = ner_efficiency(r, a18, leak=LEAK)
    eff0 = ner_efficiency(r, a18)[0]
    # contrast improvement for light too weak to rotate: its transmission is the leak; the peak sees eff-ish transmission
    tt = to_time(oy)
    ttx = to_time(ox)
    ipk = int(np.argmax(np.abs(tt) ** 2 + np.abs(ttx) ** 2))
    t_peak = abs(tt[ipk]) ** 2 / (abs(tt[ipk]) ** 2 + abs(ttx[ipk]) ** 2)
    numbers["peakTransmissionAtOptimum_eps18"] = float(t_peak)
    numbers["contrastImprovementLimit_eps18"] = float(t_peak / LEAK)
    numbers["contrastImprovementLimitOrders"] = float(math.log10(t_peak / LEAK))
    numbers["effWithLeak_eps18"] = eff

    # ---- 6. caustic reading (stated waist 450 um): B per bar and where the optimum falls ------------------------------------------------
    pr_c = np.array([0.02, 0.05, 0.1, 0.2, 0.4, 0.6] if fast else [0.01, 0.02, 0.03, 0.05, 0.075, 0.1, 0.15, 0.2, 0.3, 0.4, 0.6, 0.8])
    sc = scan(0.25, pr_c, beam="caustic")
    e_c = np.array([s["eff"] for s in sc])
    numbers["bLinAt400mbar_caustic"] = float(np.interp(0.4, pr_c, [s["bLin"] for s in sc])) if pr_c[-1] >= 0.4 else float("nan")
    jc = int(np.argmax(e_c))
    numbers["pAtMaxEff_eps25_caustic_bar"] = float(pr_c[jc])
    numbers["maxEff_eps25_caustic"] = float(e_c[jc])
    sc18 = scan(0.18, [0.42], beam="caustic")[0]
    numbers["effAt420mbar_eps18_caustic"] = sc18["eff"]
    numbers["bLinAt420mbar_caustic"] = sc18["bLin"]
    numbers["ftlAt420mbarFs_eps18_caustic"] = sc18["ftlFs"]

    # ---- 7. Kerr-matched cell mode (cellMode helper) for the stated mirrors (R = 1.5 m) --------------------------------------------------
    e29 = cells.cell_mode(LAM, mirror_radius=1.5, separation=D_CELL)["emptyCell"]
    numbers["cell29_emptyWaistUm"] = e29["waistRadiusM"] * 1e6
    numbers["cell29_emptyMirrorDiameterMm"] = 2 * e29["radiusAtMirror1M"] * 1e3
    numbers["cell300_emptyWaistUm"] = cells.cell_mode(LAM, mirror_radius=1.5, separation=2.99)["emptyCell"]["waistRadiusM"] * 1e6
    numbers["confocalWaistUm"] = math.sqrt(LAM * 1.5 / 2 / math.pi) * 1e6
    cm = {}
    for pr in (0.05, 0.1, 0.2, 0.35, 0.42):
        if cell_mode_ar(pr, PEAK_W)["matched"]:
            frac = 1.0
        else:
            lo, hi = 0.0, 1.0
            for _ in range(12):
                mid = (lo + hi) / 2
                if cell_mode_ar(pr, mid * PEAK_W)["matched"]:
                    lo = mid
                else:
                    hi = mid
            frac = lo
        cm["%03d" % round(pr * 100)] = frac
        numbers["cell29_maxMatchedPowerFraction_%dmbar" % round(pr * 1000)] = frac
    numbers["argonCriticalPowerGW_350mbar"] = cell_mode_ar(0.35, 1e9)["criticalPowerGasW"] / 1e9
    hm = cell_mode_ar(0.05, PEAK_W)
    numbers["cell29_matched050mbar_waistUm"] = hm["mode"]["waistRadiusM"] * 1e6
    numbers["cell29_matched050mbar_mirrorDiameterMm"] = 2 * hm["mode"]["radiusAtMirror1M"] * 1e3
    numbers["cell29_matched050mbar_bTotalRad"] = hm["bIntegralTotalRad"]

    # NER pressure scans in the 2.9 m cell: (a) its empty-cell mode as a caustic, (b) the Kerr-matched mode where one exists
    pr_e = np.array([0.005, 0.01, 0.02, 0.03, 0.05, 0.075, 0.1, 0.15, 0.2, 0.35] if fast else
                    [0.005, 0.0075, 0.01, 0.015, 0.02, 0.03, 0.04, 0.05, 0.065, 0.08, 0.1, 0.125, 0.15, 0.2, 0.3, 0.42])
    w29 = e29["waistRadiusM"]
    sc29 = scan(0.25, pr_e, beam="caustic", pass_m=D_CELL, w0=w29)
    e_29 = np.array([x["eff"] for x in sc29])
    j29 = int(np.argmax(e_29))
    numbers["pAtMaxEff_eps25_cell29_bar"] = float(pr_e[j29])
    numbers["maxEff_eps25_cell29"] = float(e_29[j29])
    numbers["effAt350mbar_eps25_cell29"] = float(np.interp(0.35, pr_e, e_29)) if pr_e[-1] >= 0.35 else float("nan")
    numbers["bLinPerBar_cell29"] = float(np.interp(0.05, pr_e, [x["bLin"] for x in sc29])) / 0.05
    s18 = scan(0.18, [0.42], beam="caustic", pass_m=D_CELL, w0=w29)[0]
    numbers["effAt420mbar_eps18_cell29"], numbers["ftlAt420mbarFs_eps18_cell29"], numbers["bLinAt420mbar_cell29"] = s18["eff"], s18["ftlFs"], s18["bLin"]
    pr_h = np.array([0.01, 0.02, 0.03, 0.05] if fast else [0.005, 0.01, 0.015, 0.02, 0.03, 0.04, 0.05, 0.06, 0.07])
    sch = scan(0.25, pr_h, beam="caustic", pass_m=D_CELL, helper=True)
    numbers["helperScan_nPressuresWithMode"] = len(sch)
    if sch:
        e_h = np.array([x["eff"] for x in sch])
        numbers["maxEff_eps25_helperMode"] = float(e_h.max())
        numbers["pAtMaxEff_eps25_helperMode_bar"] = float(sch[int(np.argmax(e_h))]["p"])

    # ---- 8. XPW with the engine's cubic Kerr anisotropy (BaF2, sigma -1.2 UNVERIFIED) ---------------------------------------------------------
    n2_baf2 = float(uf.command({"command": "materials.show", "name": "BaF2"})["n2"])
    numbers["baf2N2_m2W"] = n2_baf2
    pcr = 3.77 * LAM ** 2 / (8 * math.pi * 1.47 * n2_baf2)
    numbers["baf2PowerOverCritical"] = PEAK_W / pcr
    numbers["baf2N2ImpliedByPaper5e3_m2W"] = n2_baf2 * (PEAK_W / pcr) / 5000.0
    betas = np.arange(0.0, 90.01, 0.5)
    low = {}
    for name, cut in (("001", [0, 0, 1]), ("011", [0, 1, 1])):
        fr = []
        for be in betas:
            e0 = xpw_energy_for_b(0.05)
            fr.append(xpw_perp(uf.run(xpw_system(1, cut, w=100e-6, length=1e-3, gas_loss=1.0, nplates=1), xpw_spec(be, e0, tau=100e-15)), be))
        low[name] = np.array(fr)
        k = int(np.argmax(low[name][betas <= 45])) if name == "001" else int(np.argmax(low[name]))
        m45 = betas <= 45 if name == "001" else betas >= 0          # [001]: peaks at 22.5 and 67.5 deg; keep the first for the location check
        numbers["xpwCurveLowB_" + name] = {"x": betas[m45], "y": low[name][m45]}
        numbers["xpwOptAngleDeg_" + name] = float(betas[k])
    numbers["xpwLowBRatio_011_over_001"] = float(low["011"].max() / low["001"].max())
    numbers["xpwOptAngleFrom01m1Deg_011"] = 90.0 - numbers["xpwOptAngleDeg_011"]
    numbers["xpwZeroAngleDeg_011"] = float(betas[int(np.argmin(np.abs(betas - 54.7)))])
    numbers["xpwFractionAt54p7deg_011"] = xpw_perp(uf.run(xpw_system(1, [0, 1, 1], w=100e-6, length=1e-3, gas_loss=1.0, nplates=1),
                                                           xpw_spec(54.7356, xpw_energy_for_b(0.05), tau=100e-15)), 54.7356)
    # the MPC: 2 x 600 um BaF2 per pass, 1 % mirror + 3 % plates per pass, 1 mJ, 30 fs; plate beam radius is NOT given consistently (README)
    passes = [1, 2, 4, 6, 8, 10, 12, 14, 16]
    xp = {}
    for wname, w in (("w840", 840e-6), ("w1410", 1.41e-3), ("w2500", 2.5e-3)):
        for cname, cut, be in (("011", [0, 1, 1], 25.5), ("001", [0, 0, 1], 22.5)):
            y_int, y_tot = [], []
            for n in passes:
                r = uf.run(xpw_system(n, cut, w=w), xpw_spec(be, E_IN))
                f = xpw_perp(r, be)
                y_int.append(f)
                y_tot.append(f * r.summary["totals"]["energyTransmission"])
            xp[(wname, cname)] = (y_int, y_tot)
            numbers["xpw_%s_%s_internal16" % (wname, cname)] = y_int[-1]
            numbers["xpw_%s_%s_overall16" % (wname, cname)] = y_tot[-1]
            numbers["xpw_%s_%s_internalMax" % (wname, cname)] = max(y_int)
            numbers["xpw_%s_%s_passesAtMax" % (wname, cname)] = passes[int(np.argmax(y_int))]
            numbers["xpw_%s_%s_B1pass" % (wname, cname)] = uf.run(xpw_system(1, cut, w=w), xpw_spec(be, E_IN)).summary["totals"]["bIntegralRad"]
    numbers["xpwCurve_w840_011"] = {"x": passes, "y": xp[("w840", "011")][0]}
    numbers["xpw_011_beats_001_w1410"] = xp[("w1410", "011")][0][-1] - xp[("w1410", "001")][0][-1]
    numbers["xpwPaperInternal16"] = 0.65
    numbers["zeroRef"] = 0.0
    xpw_plots_data.update(dict(betas=betas, low=low, passes=passes, xp=xp))

    # Argon n2 and critical power the engine uses
    numbers["arN2_1bar_m2W"] = float(uf.command({"command": "materials.show", "name": "Ar"})["n2"])
    numbers["paperCeiling"] = 0.75
    numbers["peakPowerW"] = 0.9394 * E_IN / TAU

    numbers.pop("xpw_%s_%s_internal16", None)
    p.write_numbers(numbers)
    plots(p, b_axis, ana, pr_axis, scans, pr_c, sc, chk, numbers, sc29, pr_e, sch, pr_h)


def plots(p, b_axis, ana, pr_axis, scans, pr_c, sc, chk, nb, sc29=None, pr_e=None, sch=None, pr_h=None):
    # Fig 1: efficiency versus B (linear-light B-integral): the analytic law and the engine, with the paper's stated numbers
    fig, ax = p.figure(2, 1, 11.0, 4.2)
    cols = {0.25: BLUE, 0.18: ORANGE, 0.15: AQUA}
    for eps, c in cols.items():
        ax[0].plot(b_axis, 100 * ana[eps], color=c, lw=1.6, ls="--", label="analytic, eps = %.2f" % eps)
        ax[0].plot([s["bLin"] for s in scans[eps]], [100 * s["eff"] for s in scans[eps]], color=c, label="engine, eps = %.2f" % eps)
    ax[0].plot(chk[:, 0], 100 * chk[:, 1], "x", color=REF, ms=7, label="engine, short plane-wave cell")
    ax[0].plot([6.0], [58.0], "s", color=REF, ms=8, label="paper: 58 % at B ~ 6 rad (eps 0.25)")
    ax[0].axhspan(69 - 1, 69 + 1, xmin=0.0, xmax=1.0, color=REF2, alpha=0.3, label="paper: 69 % (eps 0.18)")
    ax[0].axhline(75, color=REF2, ls=":", label="paper: <= 75 % ceiling")
    ax[0].set_xlabel("B-integral of linear light (rad)")
    ax[0].set_ylabel("internal NER efficiency (%)")
    ax[0].set_xlim(0, 20)
    ax[0].set_ylim(0, 105)
    ax[0].legend(fontsize=6, loc="upper left", ncol=2)
    # Fig 2: against pressure
    for eps, c in cols.items():
        ax[1].plot(pr_axis * 1e3, [100 * s["eff"] for s in scans[eps]], color=c, label="constant 1.45 mm beam, eps = %.2f" % eps)
    ax[1].plot(pr_c * 1e3, [100 * s["eff"] for s in sc], color=BLUE, ls=":", label="450 um waist caustic, eps = 0.25")
    ax[1].plot([350], [58], "s", color=REF, ms=8, label="paper: 58 % at ~350 mbar (eps 0.25)")
    ax[1].plot([420], [69], "D", color=REF, ms=7, label="paper: 69 % at 420 mbar (eps 0.18)")
    ax[1].set_xscale("log")
    ax[1].set_xlim(10, 3000)
    ax[1].set_ylim(0, 100)
    ax[1].set_xlabel("argon pressure (mbar)")
    ax[1].set_ylabel("internal NER efficiency (%)")
    ax[1].legend(fontsize=6, loc="upper left")
    # panel on the right: add the 2.9 m cell readings
    if sc29:
        ax[1].plot(pr_e * 1e3, [100 * x["eff"] for x in sc29], color=BLUE, ls="-.", label="empty-cell caustic of the 2.9 m cell (w0 262 um), eps = 0.25")
    if sch:
        ax[1].plot([x["p"] * 1e3 for x in sch], [100 * x["eff"] for x in sch], "o", color=ORANGE, ms=4, label="Kerr-matched mode (cellMode), eps = 0.25")
    ax[1].legend(fontsize=5.5, loc="upper left")
    p.save(fig, "efficiency")
    xd = xpw_plots_data
    if xd:
        fig, ax = p.figure(2, 1, 11.0, 4.2)
        for name, c in (("001", AQUA), ("011", BLUE)):
            y = xd["low"][name]
            ax[0].plot(xd["betas"], y / y.max(), color=c, label="engine, cut [%s] (B = 0.05 per mm)" % name)
        ax[0].axvline(22.5, color=AQUA, ls=":", label="tensor: [001] cut 22.5 deg")
        ax[0].axvline(25.5, color=BLUE, ls=":", label="tensor: [011] cut 25.5 deg from [100]")
        ax[0].axvline(64.5, color=REF, lw=2, alpha=0.6, label="paper: 64.5 deg (= 90 - 25.5)")
        ax[0].set_xlabel("input polarisation angle beta from [100] (deg)")
        ax[0].set_ylabel("XPW energy fraction (peak = 1)")
        ax[0].legend(fontsize=6)
        for (wn, cn), (yi, yt) in xd["xp"].items():
            if cn != "011":
                continue
            ax[1].plot(xd["passes"], [100 * v for v in yi], "o-", ms=3, label="internal, [011], plate beam radius %s" % {"w840": "0.84 mm", "w1410": "1.41 mm", "w2500": "2.5 mm"}[wn])
        ax[1].plot([16], [65], "D", color=REF, ms=8, label="paper: 65 % internal, 16 passes (p. 8)")
        ax[1].plot([16], [57], "s", color=REF, ms=8, label="paper: 57 % internal (measured, p. 8)")
        ax[1].set_xlabel("passes through the two BaF2 plates")
        ax[1].set_ylabel("XPW share of the output energy (%)")
        ax[1].legend(fontsize=6)
        p.save(fig, "xpw")


if __name__ == "__main__":
    main()
