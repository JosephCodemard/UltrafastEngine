"""Repro: a round aperture on an astigmatic tracked beam uses T = 1 - exp(-2 a^2 / (wx wy)) (CircularAperture.PowerTransmission),
exact only for wx = wy. For wx = 0.5 mm, wy = 1.2 mm, a = 0.8 mm: engine 0.8816, 2-D quadrature 0.7897. Expected: the quadrature."""
import math, sys
sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parents[1]))
import numpy as np
from _common import b, uf

wx, wy, a = 0.5e-3, 1.2e-3, 0.8e-3
s = b.system("ap", [b.aperture("a", radius_m=a)])
spec = b.run_spec(b.pulse(1030e-9, 1e-12, energy=1e-9), physics={"nonlinear": False})
spec["inputBeam"] = {"kind": "gaussian", "w0xM": wx, "w0yM": wy, "wavelengthM": 1030e-9}
T = uf.run(s, spec).summary["totals"]["energyTransmission"]
n = 2000
x = (np.arange(n) + 0.5) * (2 * a / n) - a
X, Y = np.meshgrid(x, x)
ref = float((np.exp(-2 * X ** 2 / wx ** 2 - 2 * Y ** 2 / wy ** 2) * (X ** 2 + Y ** 2 <= a * a)).sum() * (2 * a / n) ** 2 * 2 / (math.pi * wx * wy))
print(f"engine {T:.4f}   quadrature {ref:.4f}   geometric-mean formula {1 - math.exp(-2 * a * a / (wx * wy)):.4f}")
