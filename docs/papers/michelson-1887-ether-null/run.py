#!/usr/bin/env python3
"""Michelson & Morley (1887): the fringe shift an ether wind would cause, read out by an interferometer in the engine.

    python run.py --tier fast            # 16 azimuths x 24 fringe samples per case, seconds (CI)
    python run.py --tier full --figures  # more samples, committed figures

What the engine can and cannot do here. The engine has no ether and no moving frame: light travels at c in every direction.
So the null result itself is built in (equal arms, any azimuth: the readout does not move). What the engine can do is the
*instrument*: the paper's prediction for the ether wind is entered as a change of the optical path of each arm,

    arm at angle theta to the wind, round trip:  L(theta) = 2 D (1 + b^2 (1 - sin^2(theta) / 2)),   b = v / c    (p. 335-336)

(a number from the paper's theory, not engine physics), and the engine's beam-splitter / delay / beam-splitter Michelson
reads the resulting fringe position the way the experiment did: as a displacement in fringes while the table turns through 16 marks
(p. 339). The checks are that the engine's interferometer has the right fringe period and visibility, that its readout of the
predicted shift is 0.4 fringe for D = 11 m (2e7 waves of yellow light, p. 341), that equal arms read exactly zero, and that the
readout resolves the paper's bound (0.01 fringe) with room to spare. A second case is a white-light (broadband) source: its fringes
exist only near zero path difference, which is how the experiment found the central fringe (p. 339).
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

C0 = 299792458.0
LAM = 550e-9                 # "yellow light": 2e7 waves = 11 m (p. 341) gives 550 nm
D_SECOND = 11.0              # effective one-way arm length of the 1887 experiment (p. 341)
D_FIRST = 2e6 * LAM          # the 1881 experiment: D = 2e6 waves (p. 336)
BETA = 1e-4                  # v / V for the orbital velocity (p. 336, "10^-8" for beta^2)
MARKS = 16                   # the 16 marks around the trough (p. 339)


def michelson(l_a, l_b):
    """Beam splitter, two arms (round-trip path lengths), the same splitter met again (sameAs, side 2). Port d of the return is the telescope."""
    return b.system("michelson", b.Branched([
        b.link(b.beam_splitter("bs"), {"c": "A", "d": "B"}),
        b.link(b.delay("A", length_m=l_a), "back:1"),
        b.link(b.delay("B", length_m=l_b), "back:2"),
        b.link(b.same_as("back", "bs", side=2), {"c": "virtual", "d": "end"}),
    ]))


def scan_b(system, run, values):
    """Output energy (J) for each round-trip length of arm B."""
    s = uf.study(system, run, parameters=[{"path": "element:B/lengthM", "values": [float(v) for v in values], "nominal": float(values[0])}],
                 outputs=["energy:end"])
    return np.array([row["energy:end"] for row in s["rows"]])


def carrier(system, run, pulse=False):
    """Carrier angular frequency and input energy. A pulse is built at the requested carrier (since the 2026-11 grid fix), reported as
    outputGrid.reportOmega0; a cw source still sits on a grid line (outputGrid.omega0)."""
    r = uf.run(system, run).summary
    return r["outputGrid"]["reportOmega0" if pulse else "omega0"], r["graph"]["energyInJ"]


def fringe_shift_curve(d, beta, nsamp):
    """The table turns through the 16 marks; at each azimuth theta the readout scans arm B over one wave and fits the fringe position."""
    run = b.run_spec(b.cw(LAM, 1.0))
    la = 2 * d
    w0, e_in = carrier(michelson(la, la), run)
    lam_c = 2 * math.pi * C0 / w0
    theta = np.arange(MARKS) * 2 * math.pi / MARKS
    # path difference an ether wind would make: arm 1 at theta, arm 2 at theta + 90 deg
    l1 = 2 * d * (1 + beta ** 2 * (1 - np.sin(theta) ** 2 / 2))
    l2 = 2 * d * (1 + beta ** 2 * (1 - np.cos(theta) ** 2 / 2))
    dp = l1 - l2                                   # = d beta^2 cos(2 theta)
    s = np.linspace(0, lam_c, nsamp, endpoint=False)
    values = np.concatenate([la - dp[k] + s for k in range(MARKS)])      # arm A fixed, arm B carries the whole difference
    sysm = michelson(la, la)
    p = scan_b(sysm, run, values).reshape(MARKS, nsamp) / e_in
    k = 2 * math.pi / lam_c
    basis = np.stack([np.ones(nsamp), np.cos(k * s), np.sin(k * s)], axis=1)
    coef = np.linalg.lstsq(basis, p.T, rcond=None)[0]                    # P = a + c cos(ks) + e sin(ks)
    phase = np.unwrap(np.arctan2(coef[2], coef[1]))                       # = k (b - a) relative position, radians
    fringes = (phase - phase[0]) / (2 * math.pi)
    return theta, fringes, dp, lam_c, p, coef


def main():
    p = Paper(__file__)
    nsamp = 24 if p.fast else 48
    out = {}

    # ---- 1. the 1887 experiment: D = 11 m
    theta, fr, dp, lam_c, pr, coef = fringe_shift_curve(D_SECOND, BETA, nsamp)
    expected = (dp - dp[0]) / lam_c                        # fringes; arm B carries -dp, and the fitted phase of P = (1 - cos k (s - dp)) / 2 is k dp
    out["lambdaCarrierM"] = lam_c
    out["Dwaves"] = D_SECOND / lam_c
    out["shiftPeakToPeakFringes_1887"] = float(fr.max() - fr.min())
    out["shiftFormula_2Dbeta2_over_lambda"] = 2 * D_SECOND * BETA ** 2 / lam_c
    out["shiftFitError_1887"] = float(np.abs(fr - expected).max())
    out["fringeVisibility_sodium"] = float((pr.max() - pr.min()) / (pr.max() + pr.min()))
    out["curve_1887"] = {"x": np.degrees(theta), "y": fr}

    # ---- 2. no ether (b = 0): the table turns, the fringe does not move
    _, fr0, _, _, _, _ = fringe_shift_curve(D_SECOND, 0.0, nsamp)
    out["shiftPeakToPeakFringes_noEther"] = float(np.abs(fr0).max())

    # ---- 3. the 1881 experiment: D = 2e6 waves -> 0.04 fringe (p. 336)
    _, fr1, _, lam1, _, _ = fringe_shift_curve(D_FIRST, BETA, nsamp)
    out["shiftPeakToPeakFringes_1881"] = float(fr1.max() - fr1.min())
    out["shiftFormula_1881"] = 2 * D_FIRST * BETA ** 2 / lam1

    # ---- 4. the shift scales as v^2: the paper's limits on the velocity (p. 341)
    scale = {}
    for f in (1 / 4, 1 / 6):
        _, frf, _, _, _, _ = fringe_shift_curve(D_SECOND, BETA * f, nsamp)
        scale[f] = float(frf.max() - frf.min())
    out["shift_vOver4"] = scale[1 / 4]
    out["shift_vOver6"] = scale[1 / 6]
    pp0 = out["shiftPeakToPeakFringes_1887"]
    out["ratio_vOver4_to_full"] = scale[1 / 4] / pp0
    out["vBoundFromCertain"] = math.sqrt(0.02 / pp0)        # observed < 1/20 of the predicted 0.4 (p. 341)
    out["vBoundFromProbable"] = math.sqrt(0.01 / pp0)       # probably < 1/40
    out["paperBoundCertain"] = 0.25
    out["paperBoundProbable"] = 1 / 6

    # ---- 5. interferogram: monochromatic and white light, one arm moved through zero path difference
    run_cw = b.run_spec(b.cw(LAM, 1.0))
    w0, e_in = carrier(michelson(1.0, 1.0), run_cw)
    lamc = 2 * math.pi * C0 / w0
    dpath = np.linspace(-3 * lamc, 3 * lamc, 241)
    pcw = scan_b(michelson(1.0, 1.0), run_cw, 1.0 + dpath) / e_in
    out["sodiumFringeError"] = float(np.abs(pcw - (1 - np.cos(w0 * dpath / C0)) / 2).max())
    out["interferogram_sodium"] = {"x": dpath * 1e6, "y": pcw}

    dl_nm = 150.0
    tl = 0.4413 * LAM ** 2 / (C0 * dl_nm * 1e-9)               # transform-limited FWHM giving ~150 nm of spectrum
    run_wl = b.run_spec(b.pulse(LAM, tl, energy=1e-9), grid=b.grid(n=8192, window_s=2e-12))
    w0w, e_inw = carrier(michelson(1.0, 1.0), run_wl, pulse=True)
    dpw = np.linspace(-8e-6, 8e-6, 401 if p.fast else 801)
    pw = scan_b(michelson(1.0, 1.0), run_wl, 1.0 + dpw) / e_inw
    dom = 2 * math.pi * 0.4413 / tl                            # FWHM of the intensity spectrum, rad/s
    env = np.exp(-((dpw / C0) * dom) ** 2 / (16 * math.log(2)))
    analytic = (1 - env * np.cos(w0w * dpw / C0)) / 2
    out["whiteLightError"] = float(np.abs(pw - analytic).max())
    out["whiteLightCoherenceLengthUm"] = float(C0 * math.sqrt(16 * math.log(2)) / dom * 1e6)   # path where V = 1/e
    out["interferogram_white"] = {"x": dpw * 1e6, "y": pw}
    out["whiteLightEnvelope"] = {"x": dpw * 1e6, "y": env}
    out["whiteLightTlFs"] = tl * 1e15

    # ------------------------------------------------------------ figures
    fig, ax = p.figure(width_in=6.4, height_in=4.0)
    th = np.linspace(0, 360, 361)
    ax.plot(th, (D_SECOND * BETA ** 2 * np.cos(np.radians(2 * th)) - dp[0]) / lam_c, color=REF, lw=1.6, label="prediction for an ether wind, D = 11 m (paper's formula)")
    ax.plot(np.degrees(theta), fr, "o", color=BLUE, ms=6, label="engine Michelson, fringe fit at the 16 marks")
    ax.plot(np.degrees(theta), fr0, "s", color=ORANGE, ms=5, label="engine, no ether (equal arms at every azimuth)")
    ax.axhspan(-0.01, 0.01, color=REF2, alpha=0.18, lw=0, label="paper's observed limit, 0.01 fringe")
    ax.set(xlabel="azimuth of the table (degrees)", ylabel="fringe displacement (fringes)", xticks=range(0, 361, 45))
    ax.legend(fontsize=8, loc="lower left")
    p.save(fig, "fringe-shift-vs-azimuth")

    fig, (a1, a2) = p.figure(ncols=2, width_in=9.0, height_in=3.8)
    a1.plot(dpath * 1e6, (1 - np.cos(w0 * dpath / C0)) / 2, color=REF, lw=1.6, label="(1 - cos(2 pi dP / lambda)) / 2")
    a1.plot(dpath * 1e6, pcw, "o", color=BLUE, ms=3.5, label="engine, narrow line")
    a1.set(xlabel="path difference dP (um)", ylabel="power at the telescope port / input", title="narrow line: fringes at every dP")
    a1.set_ylim(-0.05, 1.45)
    a1.legend(fontsize=8, loc="upper center", ncol=1)
    a2.plot(dpw * 1e6, pw, color=BLUE, lw=1.2, label="engine, broadband (150 nm)")
    a2.plot(dpw * 1e6, (1 + env) / 2, color=REF, lw=1.2, ls="--", label="analytic envelope (1 +/- V) / 2")
    a2.plot(dpw * 1e6, (1 - env) / 2, color=REF, lw=1.2, ls="--")
    a2.set(xlabel="path difference dP (um)", title="white light: fringes only near dP = 0")
    a2.set_xlim(-4, 4)
    a2.legend(fontsize=8, loc="lower right")
    p.save(fig, "interferograms")
    p.write_numbers(out)


if __name__ == "__main__":
    main()
