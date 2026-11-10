"""S2 spike 1 (scale): one cw frequency through a mask file, a gap and a beam profile on a large cartesian grid.
Usage: spike_scale.py NX NY [spectral_samples]. Prints wall time and peak RSS."""
import os, sys, time, resource, tempfile
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "python", "src"))
import ultrafast as uf
from ultrafast import build as b

nx, ny = int(sys.argv[1]), int(sys.argv[2]); K = int(sys.argv[3]) if len(sys.argv) > 3 else 1
pitch, lam = 8e-6, 800e-9
x = (np.arange(nx) - nx / 2) * pitch; y = (np.arange(ny) - ny / 2) * pitch
X, Y = np.meshgrid(x, y, indexing="ij")
w = 2.0e-3
phase = 2 * np.pi * 3 * np.sin(2 * np.pi * X / 1.0e-3)           # some structure
d = tempfile.mkdtemp(); f = os.path.join(d, "m.npz")
np.savez(f, x_m=x, y_m=y, amplitude=np.ones((nx, ny)), phase_rad=phase)
tr = lambda ip=None: b.transverse("cartesian", nx=nx, ny=ny, half_width_m=nx * pitch / 2, spectral_samples=K, initial_profile=ip, halfWidthYM=ny * pitch / 2)
sysm = b.system("s", [b.gap("in", 0.0, medium="Vacuum", transverse=tr(b.initial_profile("gaussian", radius_m=w))),
                      b.mask("slm", "file", file=f, design_wavelength_m=lam),
                      b.gap("prop", 0.2, medium="Vacuum", transverse=tr())])
t = time.time()
res = uf.run(sysm, b.run_spec(b.cw(lam, 1.0) if K == 1 else b.pulse(lam, 30e-15, energy=1e-6), instruments=[b.instrument("beamProfile", at="end", name="out")]))
dt = time.time() - t
m = np.array(res.instrument("out")["fluence_map_j_m2"])
print(f"{nx}x{ny} K={K}: {dt:.2f} s, peak RSS {resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/(2**30 if sys.platform=='darwin' else 2**20):.2f} GB, map {m.shape}, max {m.max():.3e}")
