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

LAM, TAU, E_IN = 800e-9, 30e-15, 1e-3
C0 = 299792458.0
PASS_M, NPASS = 3.0, 12
W_CONST, W0_CAUSTIC = 1.45e-3, 450e-6
MIRROR_R = 0.99                                  # enhanced-silver mirrors, ~1 % per bounce (p. 8, 'reflection losses')
LEAK = 5e-3                                      # polariser extinction ratio quoted as 5e-3 (p. 3); read as a power leak


def system(P_bar, beam="constant", passes=NPASS, mirrors=True):
    mats = {"Ar": b.material(gas=b.gas("Ar", P_bar, 293.15))}
    co = {"ag": {"kind": "gdd", "R": MIRROR_R, "gddS2": 0.0}}
    els = []
    for k in range(passes):
        bm = ({"kind": "constant", "radiusM": W_CONST} if beam == "constant"
              else {"kind": "gaussian", "w0M": W0_CAUSTIC, "zWaistM": PASS_M / 2, "m2": 1.0})
        els.append({"id": "pass%02d" % (k + 1), "type": "bulk", "material": "Ar", "lengthM": PASS_M, "beam": bm})
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


def scan(eps, pressures, beam="constant"):
    alpha = math.degrees(math.atan(eps))
    out = []
    for pr in pressures:
        r = uf.run(system(pr, beam), spec(E_IN, alpha))
        rl = uf.run(system(pr, beam), spec(E_IN, alpha, linear=True))
        eff, ty, _ = ner_efficiency(r, alpha)
        out.append({"p": float(pr), "bLin": rl.summary["totals"]["bIntegralRad"], "eff": eff, "trans": r.summary["totals"]["energyTransmission"],
                    "ftlFs": ftl_fwhm(r, ty) * 1e15})
    return out


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

    # ---- 7. XPW side numbers (engine has no anisotropic chi3: repro_2.py) ---------------------------------------------------------------
    # P/Pcr for BaF2 with a literature n2 (UNVERIFIED: not from the paper) at 1 mJ, 30 fs
    n2_baf2 = 1.6e-20            # m^2/W, assumed literature order of magnitude (UNVERIFIED)
    n0_baf2 = 1.47
    pcr = 3.77 * LAM ** 2 / (8 * math.pi * n0_baf2 * n2_baf2)
    numbers["baf2PowerOverCritical"] = 0.9394 * E_IN / TAU / pcr

    # Argon n2 and critical power the engine uses
    numbers["arN2_1bar_m2W"] = float(uf.command({"command": "materials.show", "name": "Ar"})["n2"])
    numbers["peakPowerW"] = 0.9394 * E_IN / TAU

    p.write_numbers(numbers)
    plots(p, b_axis, ana, pr_axis, scans, pr_c, sc, chk, numbers)


def plots(p, b_axis, ana, pr_axis, scans, pr_c, sc, chk, nb):
    # Fig 1: efficiency versus B (linear-light B-integral): the analytic law and the engine, with the paper's stated numbers
    fig, ax = p.figure(1, 2, 10.0, 4.0)
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
    ax[0].set_ylim(0, 85)
    ax[0].legend(fontsize=6.5, loc="upper left")
    # Fig 2: against pressure
    for eps, c in cols.items():
        ax[1].plot(pr_axis * 1e3, [100 * s["eff"] for s in scans[eps]], color=c, label="constant 1.45 mm beam, eps = %.2f" % eps)
    ax[1].plot(pr_c * 1e3, [100 * s["eff"] for s in sc], color=BLUE, ls=":", label="450 um waist caustic, eps = 0.25")
    ax[1].plot([350], [58], "s", color=REF, ms=8, label="paper: 58 % at ~350 mbar (eps 0.25)")
    ax[1].plot([420], [69], "D", color=REF, ms=7, label="paper: 69 % at 420 mbar (eps 0.18)")
    ax[1].set_xscale("log")
    ax[1].set_xlim(10, 2000)
    ax[1].set_xlabel("argon pressure (mbar)")
    ax[1].set_ylabel("internal NER efficiency (%)")
    ax[1].legend(fontsize=6.5, loc="upper left")
    p.save(fig, "efficiency")


if __name__ == "__main__":
    main()
