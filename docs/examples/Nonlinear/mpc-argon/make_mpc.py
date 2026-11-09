#!/usr/bin/env python3
"""Generate mpc.ufs: an argon-filled Herriott multipass cell, written with ultrafast.build.

    python make_mpc.py            # writes mpc.ufs next to this script
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
from ultrafast import build as b  # noqa: E402

LAM = 1.03e-6
PASSES = 20
L = 0.55            # mirror separation (m)
W0, M2 = 150e-6, 1.1
ZR = math.pi * W0 ** 2 / (M2 * LAM)
W_MIRROR = W0 * math.sqrt(1 + (0.5 * L / ZR) ** 2)

materials = {"Ar2bar": b.material(gas=b.gas("Ar", 2.0, 293.15))}
coatings = {
    "mpc_mirror": {"kind": "gdd", "R": 0.9990, "gddS2": -30e-30, "lidt": {"fluenceJPerCm2": 0.3, "pulseDurationS": 1e-12}},
    "ar": {"kind": "ar", "R": 0.002},
}
els = [
    b.interface("win_in_front", "Air", "FusedSilica", coating="ar"),
    b.bulk("win_in", "FusedSilica", 3e-3, nonlinear=False),
    b.interface("win_in_back", "FusedSilica", "Ar2bar", coating="ar"),
    b.gap("to_cell", 0.10, medium="Ar2bar"),
]
for k in range(PASSES):
    els.append(b.bulk(f"pass{k + 1:02d}", "Ar2bar", L, beam=b.gaussian_beam(W0, 0.5 * L, M2)))
    if k < PASSES - 1:
        els.append(b.mirror(f"m{k + 1:02d}", coating="mpc_mirror", medium="Ar2bar", semiDiameterM=12.7e-3, beamRadiusM=W_MIRROR))
els += [
    b.gap("from_cell", 0.10, medium="Ar2bar"),
    b.interface("win_out_front", "Ar2bar", "FusedSilica", coating="ar"),
    b.bulk("win_out", "FusedSilica", 3e-3, nonlinear=False),
    b.interface("win_out_back", "FusedSilica", "Air", coating="ar"),
]
sysd = b.system("mpc-argon-2bar", els, materials=materials, coatings=coatings,
                description=f"{PASSES}-pass Herriott cell, {L} m, Ar 2 bar, w0 = {W0 * 1e6:.0f} um (zR = {ZR * 1e3:.1f} mm), -30 fs^2 per bounce")
sysd = {"$schema": "../../../schemas/ufs.schema.json", **sysd}   # editor completion
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mpc.ufs")
b.save(sysd, out)
print("wrote", out, "-", len(els), "elements; beam on mirrors w =", round(W_MIRROR * 1e3, 3), "mm")
