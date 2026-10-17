"""Example 16: the same GRIN lens written four ways, plus a tapered one -- position-dependent properties from
an expression, a table, a Python function and a z-dependent function.

    python docs/examples/16-position-dependent-index/grin_variants.py

Delta n(r) = -1/2 n0 g^2 r^2 over L acts as a thin lens f = 1 / (n0 g^2 L); a Gaussian beam of waist w0 at the rod
focuses to w_f = lambda f / (pi w0) at distance f. A profile that tapers linearly to zero along the rod has half
the mean strength: f doubles.
"""
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "python" / "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM, W0, L, N0, F = 1.03e-6, 0.5e-3, 5e-3, 1.45, 0.2
G2 = 1 / (N0 * F * L)


def grin_system(profile, gap_m=F):
    return b.system("grin", [
        b.bulk("rod", "FusedSilica", L, nonlinear=False, beam=b.gaussian_beam(W0),
               transverse=b.transverse("radial", points=96, radius_m=2e-3), indexProfile=profile),
        b.gap("to_focus", gap_m, medium="Vacuum", transverse=b.transverse("radial", points=128, radius_m=2.5e-3)),
    ])


def focus_radius(profile, gap_m=F, fields=None):
    spec = b.run_spec(b.pulse(LAM, 300e-15, energy=1e-9))
    res = uf.run(grin_system(profile, gap_m), spec, fields=fields)
    return {e["id"]: e for e in res.elements()}["to_focus"]["beam"]["radiusXM"]


r_tab = np.linspace(0, 2.5e-3, 251)
variants = [
    ("expression", {"kind": "expression", "expr": "-0.5*n0*g2*r^2", "params": {"n0": N0, "g2": G2}}, None),
    ("table (r)", {"kind": "table", "axes": "r", "r": list(r_tab), "values": list(-0.5 * N0 * G2 * r_tab ** 2)}, None),
    ("Python f(x, y, z)", "@grin", {"grin": lambda x, y, z: -0.5 * N0 * G2 * (x * x + y * y)}),
]
print(f"thin-lens prediction: w(f) = {LAM * F / (math.pi * W0) * 1e6:.1f} um at f = {F * 1e3:.0f} mm")
for name, prof, fields in variants:
    print(f"  {name:<20s}: w = {focus_radius(prof, fields=fields) * 1e6:6.1f} um")

# a tapered GRIN: strength falls linearly to zero along the rod -> mean strength halves, f doubles
taper = {"taper": lambda x, y, z: -0.5 * N0 * G2 * (x * x + y * y) * (1 - z / L)}
w2 = focus_radius("@taper", gap_m=2 * F, fields=taper)
print(f"tapered x (1 - z/L): focus expected at 2f = {2 * F * 1e3:.0f} mm with w = {LAM * 2 * F / (math.pi * W0) * 1e6:.1f} um:"
      f" w = {w2 * 1e6:.1f} um")
