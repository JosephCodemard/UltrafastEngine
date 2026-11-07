"""Minimal reproduction 1: GDD of a 3 mm fused-silica plate at 1200 nm, engine vs the Malitson Sellmeier formula.

The paper (p. 3009) estimates 25.6 fs^2 for its 3 mm fused-silica dichroic substrate. The engine gives 18.2 fs^2; the
independent second derivative of Malitson's formula (J. Opt. Soc. Am. 55, 1205 (1965)) gives 18.1 fs^2. So the engine is
right for 3 mm of fused silica; 25.6 fs^2 would need about 4.2 mm, or a different glass / extra material in the path.
    python repro_1.py
"""
import numpy as np
import ultrafast as uf
from ultrafast import build as b

r = uf.run(uf.System.load(b.system("s", [b.bulk("sub", "FusedSilica", 3e-3)])), b.run_spec(b.pulse(1200e-9, 40e-15, energy=1e-9)))
engine = r.summary["elements"][0]["own"]["gddS2"] * 1e30
n = lambda l: np.sqrt(1 + 0.6961663 * l ** 2 / (l ** 2 - 0.0684043 ** 2) + 0.4079426 * l ** 2 / (l ** 2 - 0.1162414 ** 2) + 0.8974794 * l ** 2 / (l ** 2 - 9.896161 ** 2))
l, h = 1.2, 1e-3
d2 = (n(l + h) - 2 * n(l) + n(l - h)) / h ** 2 * 1e12            # d2n/dlambda2 in 1/m^2
malitson = (1.2e-6) ** 3 / (2 * np.pi * 299792458.0 ** 2) * d2 * 3e-3 * 1e30
print("engine %.2f fs^2, Malitson %.2f fs^2, paper 25.6 fs^2; thickness for 25.6 fs^2: %.2f mm" % (engine, malitson, 3 * 25.6 / malitson))
