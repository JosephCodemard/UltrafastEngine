#!/usr/bin/env python3
"""Python quick start: build a system in code, run it, look at the results, scan a parameter."""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

print("engine", uf.version())

system = b.system("two-plates", [
    b.bulk("plate1", "FusedSilica", 3e-3, beam=b.gaussian_beam(w0_m=150e-6, z_waist_m=1.5e-3)),
    b.gap("air", 0.10),
    b.chirped_mirror("cm", gdd_s2=-150e-30, bounces=2),
    b.bulk("plate2", "FusedSilica", 3e-3, beam=b.gaussian_beam(w0_m=150e-6, z_waist_m=1.5e-3)),
])
spec = b.run_spec(b.pulse(1.03e-6, 180e-15, energy=8e-6), instruments=[b.instrument("compressor")])

res = uf.run(system, spec)
out = res.summary["output"]
print(f"output: {out['fwhmS'] * 1e15:.1f} fs (TL {out['transformLimitFwhmS'] * 1e15:.1f} fs), "
      f"B = {res.summary['totals']['bIntegralRad']:.2f} rad, compressible to "
      f"{res.instrument('compressor')['compressedFwhmS'] * 1e15:.1f} fs")

# a parameter scan: pulse energy
for e in (2e-6, 4e-6, 8e-6, 16e-6):
    r = uf.run(system, b.run_spec(b.pulse(1.03e-6, 180e-15, energy=e)))
    print(f"  E = {e * 1e6:5.1f} uJ -> TL {r.summary['output']['transformLimitFwhmS'] * 1e15:6.1f} fs")

res.write("quickstart_results")
try:
    uf.plot(res).savefig("quickstart.png", dpi=120)
    print("saved quickstart.png")
except ImportError:
    print("(pip install matplotlib for plots)")
