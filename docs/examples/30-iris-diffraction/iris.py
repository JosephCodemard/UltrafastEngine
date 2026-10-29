#!/usr/bin/env python3
"""Example 30 from Python: a Gaussian beam through an iris, built with ultrafast.build, scanning the iris radius.

For each radius a it prints the transmission against 1 - exp(-2 a^2 / w^2) and the fluence on the axis at the distance
where the iris holds one Fresnel zone (N = a^2 / lambda z = 1) against the Fresnel integral on the axis of a Gaussian
field cut at r = a (F0 = 2 E / pi w^2, p = 1 / w^2 - i k / 2z):
F(0, z) = F0 |(k / 2z) / p|^2 |1 - exp(-a^2 p)|^2,  with exp(-a^2 p) = exp(-a^2 / w^2) exp(i pi N).
"""
import cmath
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM, W, E = 1030e-9, 2e-3, 1e-3


def system(a: float):
    z = a * a / LAM                                                       # N = 1
    grid = b.transverse("radial", points=1024, radius_m=12e-3, absorbing_boundary=True)
    source = b.transverse("radial", points=1024, radius_m=12e-3, absorbing_boundary=True,
                          initial_profile=b.initial_profile("gaussian", radius_m=W))
    return b.system("iris", [
        b.gap("source", 0.0, medium="Vacuum", transverse=source),
        b.aperture("iris", radius_m=a),
        b.gap("screen", z, medium="Vacuum", transverse=grid),
    ]), z


spec = b.run_spec(b.pulse(LAM, 1e-12, energy=E), instruments=[b.instrument("beamProfile", at="screen", name="screen")])
f0 = 2 * E / (math.pi * W * W)
print(" a (mm)   z (m)   T engine   T analytic   F(0) engine   F(0) Fresnel")
for a in (0.5e-3, 1e-3, 1.5e-3, 2e-3, 3e-3):
    sys_, z = system(a)
    res = uf.run(sys_, spec)
    t = next(e for e in res.elements() if e["id"] == "iris")["values"]["transmission"]
    prof = res.instrument("screen")
    u, k = a * a / (W * W), 2 * math.pi / LAM
    pp = 1 / W ** 2 - 1j * k / (2 * z)
    f_axis = f0 * abs(k / (2 * z) / pp) ** 2 * abs(1 - cmath.exp(-a * a * pp)) ** 2
    print(f"{a * 1e3:6.2f} {z:8.3f} {t:10.4f} {1 - math.exp(-2 * u):12.4f} {prof['fluence_j_m2'][0]:13.4g} {f_axis:14.4g}")
