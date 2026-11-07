#!/usr/bin/env python3
"""Minimal reproduction 1: the engine's d_eff for KDP type-I SHG is not a smooth function of the azimuth phi.

Setup: crystal element, KDP (-42m, d36), 1.5 mm, type I (o o -> e), 694.3 nm, plane-wave overlap, theta fixed at the
phase-matching angle; only cut.phiDeg changes. Expected (analytic): |d_eff| = c sin(2 phi) with c = d36 sin(theta) (times a
slowly varying factor), so that eta ~ d_eff^2 is smooth in phi. Printed: every phi (step 0.25 deg) whose |d_eff| differs by
more than 1 % from the smooth envelope (median of the neighbours' ratio), together with the reported conversion.
    python repro_1.py [theta_deg]
"""
import math, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "python", "src"))
import numpy as np
import ultrafast as uf
from ultrafast import build as b

LAM = 694.3e-9
theta = float(sys.argv[1]) if len(sys.argv) > 1 else 50.547755074363714
rows = []
for phi in np.arange(5.0, 85.01, 0.25):
    el = b.crystal("k", "KDP", 1.5e-3, process="shg", type="I", cut={"thetaDeg": theta, "phiDeg": float(phi)},
                   overlap={"model": "planeWave", "radiusM": 1e-3}, chi3=False)
    r = uf.run(b.system("k", [el]), b.run_spec(b.pulse(LAM, 5e-12, energy=1e-9)))
    v = r.summary["elements"][0]["values"]
    rows.append((phi, abs(v["deffPmPerV"]), v["conversionEfficiency"]))
rows = np.array(rows)
ratio = rows[:, 1] / np.abs(np.sin(2 * np.radians(rows[:, 0])))
c = np.median(ratio)
bad = np.abs(ratio / c - 1) > 0.01
print("theta = %.4f deg, median d_eff / sin(2 phi) = %.4f pm/V (d36 sin(theta) = %.4f)" % (theta, c, 0.39 * math.sin(math.radians(theta))))
print("anomalous phi: %d of %d points" % (bad.sum(), len(rows)))
for (phi, d, eta), r_ in zip(rows[bad], ratio[bad]):
    print("  phi %.2f  |d_eff| %.4f  ratio to envelope %.3f  eta %.3e" % (phi, d, r_ / c, eta))
