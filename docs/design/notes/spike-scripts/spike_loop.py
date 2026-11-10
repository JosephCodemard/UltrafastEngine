"""S2 spike 4 (loop): time per trial of a 128^2 cw spatial run: a new mask file per trial (today's only route) and a run with no mask (the floor)."""
import os, sys, time, tempfile
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "python", "src"))
import ultrafast as uf
from ultrafast import build as b

n, pitch, lam = 128, 20e-6, 800e-9
x = (np.arange(n) - n / 2) * pitch
d = tempfile.mkdtemp()
tr = lambda ip=None: b.transverse("cartesian", nx=n, ny=n, half_width_m=n * pitch / 2, spectral_samples=1, initial_profile=ip)
def trial(i, with_mask):
    els = [b.gap("in", 0.0, medium="Vacuum", transverse=tr(b.initial_profile("gaussian", radius_m=0.4e-3)))]
    if with_mask:
        f = os.path.join(d, f"m{i}.npz"); np.savez(f, x_m=x, y_m=x, amplitude=np.ones((n, n)), phase_rad=np.random.default_rng(i).standard_normal((n, n)) * 0.1)
        els.append(b.mask("m", "file", file=f, design_wavelength_m=lam))
    els.append(b.gap("p", 0.05, medium="Vacuum", transverse=tr()))
    r = uf.run(b.system("s", els), b.run_spec(b.cw(lam, 1.0), instruments=[b.instrument("beamProfile", at="end", name="o")]))
    return r
for wm in (False, True):
    trial(0, wm); t = time.time()
    for i in range(1, 21): trial(i, wm)
    print(("with new mask file" if wm else "no mask         "), f"{(time.time()-t)/20*1e3:.1f} ms per trial")
