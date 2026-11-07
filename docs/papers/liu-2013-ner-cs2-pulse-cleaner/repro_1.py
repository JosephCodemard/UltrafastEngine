#!/usr/bin/env python3
"""Minimal engine reproduction 1 (Liu 2013 / CS2): two limits of the delayed-response polarisation structure.

(a) `anisotropicFraction` on a user delayed response of kind "expression" is accepted (no error under ULTRAFAST_STRICT_OPTIONS=1) and has NO
    effect: the transmission of a QWP - Kerr - QWP - polariser chain is identical for 0, 0.5, 1.  For `dampedOscillator` the same key works.
    docs/reference/nonlinear.md says to "set it explicitly on any delayed-response model" and lists expression among the models
    whose default is 0.  (ModelRegistry.cs: Split() is applied to silica / blowWood / hollenbeck / dampedOscillator only.)
(b) values above 1 are rejected ("anisotropicFraction must be between 0 and 1").  A purely orientational response (CS2, molecular
    liquids) has h_a = -h_b/3, i.e. h_b = 1.5 h_R, so it cannot be expressed.

    python repro_1.py
"""
import numpy as np
import ultrafast as uf

LAM = 800e-9


def chain(kind, b):
    rm = {"kind": kind}
    if kind == "expression":
        rm["expr"] = "exp(-t/30e-15)/30e-15*step(t)"
    else:
        rm.update({"tau1": 12.2e-15, "tau2": 32e-15})
    if b is not None:
        rm["anisotropicFraction"] = b
    mat = {"index": {"kind": "expression", "expr": "1.6"}, "n2": 3e-19, "raman": rm, "ramanFraction": 0.6}
    el = [{"id": "q1", "type": "waveplate", "plate": "quarter", "axisDeg": 20, "designWavelengthM": LAM},
          {"id": "cs2", "type": "bulk", "material": "CS2", "lengthM": 2e-3, "beam": {"kind": "constant", "radiusM": 100e-6}},
          {"id": "q2", "type": "waveplate", "plate": "quarter", "axisDeg": 110, "designWavelengthM": LAM},
          {"id": "pol", "type": "polarizer", "axisDeg": 90, "extinctionRatio": 1e8}]
    return {"format": "ultrafast-system", "version": 2, "name": "repro", "materials": {"CS2": mat}, "elements": el}


run = {"format": "ultrafast-run", "version": 2, "name": "repro", "physics": {"selfSteepening": False},
       "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": 100e-15, "energyJ": 2e-6, "polarization": "x"},
       "grid": {"n": 2048, "windowS": 4e-12, "auto": False}}


def transmission(kind, b):
    r = uf.run(chain(kind, b), run)
    e = lambda w: np.abs(np.asarray(r[w + "_ax_re"]) + 1j * np.asarray(r[w + "_ax_im"])) ** 2 + np.abs(np.asarray(r[w + "_ay_re"]) + 1j * np.asarray(r[w + "_ay_im"])) ** 2
    return float(e("output").sum() / e("input").sum())


for kind in ("expression", "dampedOscillator"):
    print(kind, ["b=%s: T=%.10f" % (b, transmission(kind, b)) for b in (0.0, 0.5, 1.0)])
try:
    transmission("dampedOscillator", 1.5)
except Exception as ex:
    print("b = 1.5:", str(ex)[:140])
