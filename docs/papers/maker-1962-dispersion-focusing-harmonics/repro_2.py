#!/usr/bin/env python3
"""Minimal reproduction 2: the default adaptive step does not resolve a strongly phase-mismatched crystal.

Setup: a plain quartz-like crystal (Ghosh 1999 indices, d_eff 0.3 pm/V), 'shg' type 0 along x (o o -> o), 694.3 nm, 5 ps,
1 uJ, plane-wave overlap, chi3 off. Plane-wave theory at low conversion: E_SH ~ (4 / dk^2) sin^2(dk L / 2) x K, with dk = 4.6e5 /m
(coherence length 6.9 um), so E_SH(L) must be periodic in L with period 13.7 um and bounded by its value at L = 6.85 um.
K is fixed at the first maximum (tolerance 1e-9). For L near 0.8 mm (dk L = 370 rad) the ratio E_SH / (K (4/dk^2) sin^2) is printed
for three stepper tolerances. Expected: 1 everywhere. Found (this build): 150-960 at the default 1e-6 (20 steps of 40 um =
18 rad of phase per step), 1.1-4.7 at 1e-9, 1.00-1.02 at 1e-11. The engine gives no warning. The error estimate is relative to
the (huge) fundamental, so a 1e-9 SH amplitude never limits the step.
    python repro_2.py
"""
import math, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "python", "src"))
import numpy as np
import ultrafast as uf
from ultrafast import build as b

LAM = 694.3e-9
QUARTZ = {"crystal": {"name": "quartz (Ghosh 1999)",
                      "no": {"kind": "sellmeier", "A": 1.28604141, "B": [1.07044083, 1.10202242], "C": [1.00585997e-2, 100.0]},
                      "ne": {"kind": "sellmeier", "A": 1.28851804, "B": [1.09509924, 1.15662475], "C": [1.02101864e-2, 100.0]},
                      "pointGroup": "general", "deffPmPerV": 0.3}}


def eta(L, tol):
    el = b.crystal("q", "quartz", float(L), process="shg", type="0", cut={"thetaDeg": 90, "phiDeg": 0},
                   overlap={"model": "planeWave", "radiusM": 1e-3}, chi3=False, deff_pm_per_v=0.3)
    r = uf.run(b.system("q", [el], materials={"quartz": QUARTZ}), b.run_spec(b.pulse(LAM, 5e-12, energy=1e-6), stepper={"tolerance": tol}))
    e = r.summary["elements"][0]
    return e["values"]["conversionEfficiency"], e["values"]["deltaK0PerM"], e["solver"]["accepted"]


dk = eta(6.85e-6, 1e-9)[1]
K = eta(6.85e-6, 1e-9)[0] / (4 / dk ** 2)
for tol in (1e-6, 1e-9, 1e-11):
    ratios = []
    for L in 800e-6 + np.linspace(0, 13.7e-6, 8):
        e, _, n = eta(L, tol)
        ratios.append(e / (K * 4 / dk ** 2 * math.sin(dk * L / 2) ** 2 + 1e-30))
    print("tolerance %g: %d steps; E_SH / theory at 8 thicknesses: %s" % (tol, n, " ".join("%.3g" % x for x in ratios)))
