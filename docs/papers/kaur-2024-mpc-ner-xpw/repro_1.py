#!/usr/bin/env python3
"""Minimal engine checks behind the NER comparison (no tuning, nothing from the paper).

  (a) B-integral of one 3 m pass of argon with a Gaussian caustic (w0 = 450 um, waist in the middle) against the closed form
      B = 8 pi n2 P / lambda^2 * atan(d / (2 zR))   (n2 of the engine's argon at the pressure, P = peak power of the engine's pulse);
  (b) peak ellipse rotation of the engine against theta = B sin(2 chi) / 3 (instantaneous Kerr, B of linear light).
Run:  python repro_1.py        (prints the numbers; exit 1 if a deviation is above 3 %)
"""
import math
import sys

import numpy as np

import ultrafast as uf
from ultrafast import build as b

LAM, TAU = 800e-9, 30e-15


def spec(e, pol):
    return {"format": "ultrafast-run", "version": 2, "name": "repro1",
            "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": TAU, "energyJ": e, "polarization": pol},
            "physics": {"selfSteepening": False, "raman": False}}


# (a) one pass, Gaussian caustic -------------------------------------------------------------------------------------------------
P_BAR, W0, D = 0.4, 450e-6, 3.0
sysd = b.system("pass", [{"id": "pass", "type": "bulk", "material": "Ar", "lengthM": D, "beam": {"kind": "gaussian", "w0M": W0, "zWaistM": D / 2, "m2": 1.0}}],
                materials={"Ar": b.material(gas=b.gas("Ar", P_BAR, 293.15))})
r = uf.run(sysd, spec(1e-6, "x"))                                  # 1 uJ: B << 1, no broadening, no dispersion effect
n2 = uf.command({"command": "materials.show", "name": "Ar"})["n2"] * P_BAR
ppk = r.summary["elements"][0]["pulse"]["peakPowerW"]
zr = math.pi * W0 ** 2 / LAM
b_formula = 8 * math.pi * n2 * ppk / LAM ** 2 * math.atan(D / (2 * zr))
b_engine = r.summary["totals"]["bIntegralRad"]
print("(a) engine B = %.5f rad, closed form = %.5f rad  (ratio %.4f); n2 = %.4g m2/W, peak power %.4g W, zR = %.3f m"
      % (b_engine, b_formula, b_engine / b_formula, n2, ppk, zr))

# (b) ellipse rotation ----------------------------------------------------------------------------------------------------------------
sys2 = b.system("cell", [{"id": "cell", "type": "bulk", "material": "Ar", "lengthM": 0.2, "beam": {"kind": "constant", "radiusM": 0.3e-3}}],
                materials={"Ar": b.material(gas=b.gas("Ar", 1.0, 293.15))})
dev = 0.0
for alpha in (10.0, 20.0, 30.0):
    chi = math.radians(alpha)
    pol = {"angleDeg": alpha, "ellipticity": math.tan(chi)}
    rl = uf.run(sys2, spec(1e-3, "x"))
    bl = rl.summary["totals"]["bIntegralRad"]
    re = uf.run(sys2, spec(1e-3, pol))
    f = lambda a: np.fft.fftshift(np.fft.fft(np.fft.ifftshift(a)))
    ax = f(np.asarray(re["output_ax_re"]) + 1j * np.asarray(re["output_ax_im"]))
    ay = f(np.asarray(re["output_ay_re"]) + 1j * np.asarray(re["output_ay_im"]))
    ex = f(np.asarray(re["input_ax_re"]) + 1j * np.asarray(re["input_ax_im"]))
    ey = f(np.asarray(re["input_ay_re"]) + 1j * np.asarray(re["input_ay_im"]))
    i = int(np.argmax(np.abs(ax) ** 2 + np.abs(ay) ** 2))
    def azimuth(x, y):
        s1 = abs(x) ** 2 - abs(y) ** 2
        s2 = 2 * (x * np.conj(y)).real
        return 0.5 * math.atan2(s2, s1)
    # input ellipse azimuth at the same instant (time axis shift of the pulse is negligible in 0.2 m)
    j = int(np.argmax(np.abs(ex) ** 2 + np.abs(ey) ** 2))
    theta = azimuth(ax[i], ay[i]) - azimuth(ex[j], ey[j])
    # B is the B-integral of linear light of the same peak intensity; the ellipse rotation is B sin(2 chi)/3 (nonlinear.md)
    # ellipticity of the engine's field: tan(chi_eff) = ellipticity -> sin 2chi = 2 eps / (1 + eps^2)
    eps = math.tan(chi)
    th_formula = bl * (2 * eps / (1 + eps ** 2)) / 3
    print("(b) alpha %2.0f deg: B(lin) = %.3f rad, rotation engine %.4f rad, B sin2chi/3 = %.4f rad  (ratio %.4f)" % (alpha, bl, abs(theta), th_formula, abs(theta) / th_formula))
    dev = max(dev, abs(abs(theta) / th_formula - 1))
ok = abs(b_engine / b_formula - 1) < 0.03 and dev < 0.03
print("OK" if ok else "DEVIATION")
sys.exit(0 if ok else 1)
