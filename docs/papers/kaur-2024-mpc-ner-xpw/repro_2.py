#!/usr/bin/env python3
"""XPW is not reproducible: the engine's chi(3) is isotropic, so a linearly polarised pulse never makes an orthogonal wave.

A 1 mJ, 30 fs pulse linearly polarised at 64.5 deg (the angle the paper quotes as optimal for BaF2, p. 8) crosses 600 um of a cubic
fluoride (CaF2 as a stand-in: BaF2 is not a built-in material) with B ~ 1 rad. A cubic crystal with the XPW tensor component
(sigma = chi_1111 - 3 chi_1122 anisotropy) would transfer energy into the polarisation orthogonal to the input; the engine's output
stays linear at 64.5 deg, so the energy behind a crossed polariser is zero to rounding error.
Run:  python repro_2.py   (prints the leak; exit 1 if it is above 1e-6 of the pulse energy)
"""
import math
import sys

import numpy as np

import ultrafast as uf
from ultrafast import build as b

LAM, TAU, E_IN, W = 800e-9, 30e-15, 1e-3, 1.45e-3
ANGLE = 64.5
sysd = b.system("xpw", [{"id": "plate", "type": "bulk", "material": "CaF2", "lengthM": 600e-6, "beam": {"kind": "constant", "radiusM": W}}])
spec = {"format": "ultrafast-run", "version": 2, "name": "xpw",
        "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": TAU, "energyJ": E_IN,
                  "polarization": {"angleDeg": ANGLE, "ellipticity": 0.0}},
        "physics": {"selfSteepening": True, "raman": False}}
r = uf.run(sysd, spec)
ax = np.asarray(r["output_ax_re"]) + 1j * np.asarray(r["output_ax_im"])
ay = np.asarray(r["output_ay_re"]) + 1j * np.asarray(r["output_ay_im"])
a = math.radians(ANGLE)
par = math.cos(a) * ax + math.sin(a) * ay
perp = -math.sin(a) * ax + math.cos(a) * ay
tot = (np.abs(par) ** 2 + np.abs(perp) ** 2).sum()
leak = float((np.abs(perp) ** 2).sum() / tot)
print("B(linear) in the plate = %.2f rad; energy in the orthogonal polarisation = %.3g of the pulse (XPW would give 1e-2 ... 0.3)"
      % (r.summary["totals"]["bIntegralRad"], leak))
print("OK (engine has no XPW)" if leak < 1e-6 else "UNEXPECTED: orthogonal wave present")
sys.exit(0 if leak < 1e-6 else 1)
