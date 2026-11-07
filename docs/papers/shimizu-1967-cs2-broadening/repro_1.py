#!/usr/bin/env python3
"""Minimal engine reproduction 1: a delayed (user) response wraps around the time window and the grid planner does not warn.

One 1-mm-like cell, a user delayed response h(t) = exp(-t/tau)/tau (fR = 1), Gaussian input. The same physical case is run in a window of
120 ps and of 480 ps (same step). The spectra differ by tens of percent (total variation) although the run summary says
"Configured grid is adequate" and gives no warning: the planner sizes the window from the pulse and dispersion (33 ps required) and
does not add the memory of the response (here 18 ps).  Expected: a warning (or a larger planned window) when a delayed response
is longer than window/10.  The 480 ps result agrees with an independent numpy evaluation of the same model to 3e-4 (run.py).

    python repro_1.py
"""
import numpy as np
import ultrafast as uf

LAM, T, TAU, B = 694.3e-9, 6e-12, 18e-12, 120.0


def run(window, n):
    mat = {"index": {"kind": "expression", "expr": "1.6137"}, "n2": 3e-19,
           "raman": {"kind": "expression", "expr": "exp(-t/%g)/%g*step(t)" % (TAU, TAU)}, "ramanFraction": 1.0}
    system = {"format": "ultrafast-system", "version": 2, "name": "repro", "materials": {"CS2": mat},
              "elements": [{"id": "cell", "type": "bulk", "material": "CS2", "lengthM": 0.075, "beam": {"kind": "constant", "radiusM": 50e-6}}]}
    cal = dict(system, elements=[dict(system["elements"][0], lengthM=1e-3)])
    base = {"format": "ultrafast-run", "version": 2, "name": "repro", "physics": {"selfSteepening": False},
            "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": T, "polarization": "x"}}
    c = uf.run(cal, dict(base, pulse=dict(base["pulse"], energyJ=1e-8), grid={"n": 2 ** 14, "windowS": 120e-12, "auto": False}))
    e = B / (c.summary["totals"]["bIntegralRad"] / 1e-8 * 75)
    r = uf.run(system, dict(base, pulse=dict(base["pulse"], energyJ=e), grid={"n": n, "windowS": window, "auto": False}))
    s = np.asarray(r["output_spectrum"])
    om = np.asarray(r["output_omega_rad_s"])
    return om, s / s.sum(), r


om1, s1, r1 = run(120e-12, 2 ** 14)
om2, s2, r2 = run(480e-12, 2 ** 16)
# compare on a common axis: rebin both to 0.5 cm^-1 bins over +-300 cm^-1
bins = np.arange(-300, 300.5, 0.5)
w0 = r1.summary["grid"]["omega0"]
h1, _ = np.histogram((om1 - w0) / 2 / np.pi / 2.99792458e10, bins, weights=s1)
h2, _ = np.histogram((om2 - r2.summary["grid"]["omega0"]) / 2 / np.pi / 2.99792458e10, bins, weights=s2)
print("window 120 ps vs 480 ps: total variation of the spectra = %.3f" % (0.5 * np.sum(np.abs(h1 / h1.sum() - h2 / h2.sum()))))
print("warnings (120 ps):", r1.summary["warnings"])
print("notes (120 ps):", [n for n in r1.summary["notes"] if "window" in n or "adequate" in n])
