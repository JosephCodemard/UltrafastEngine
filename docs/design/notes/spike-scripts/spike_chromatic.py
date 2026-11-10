"""S2 spike 3 (chromatic): a blazed grating as a wrapped-phase `mask file` and as a thin `bulk` with a sawtooth indexProfile (vacuum).
Reference: order m leaves at sin(theta) = m lambda / p (so the first order moves with lambda) and the blaze efficiency is
sinc^2(lambda0/lambda - 1). Runs cw at three wavelengths (a pulse is the incoherent sum over these colours for a linear element)."""
import os, sys, tempfile
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "python", "src"))
import ultrafast as uf
from ultrafast import build as b

n, ny, pitch, period, lam0, f_cam, w = 512, 8, 4e-6, 32e-6, 800e-9, 0.2, 0.6e-3
x = (np.arange(n) - n / 2) * pitch; y = (np.arange(ny) - ny / 2) * pitch
d = tempfile.mkdtemp(); fm = os.path.join(d, "m.npz")
saw = np.mod(x / period, 1.0)                                    # 0..1 per period
np.savez(fm, x_m=x, y_m=y, amplitude=np.ones((n, ny)), phase_rad=np.repeat((2 * np.pi * saw)[:, None], ny, 1))
L = 10e-6; dn0 = lam0 / L                                         # OPD depth = lam0 across one period
def tr(ip=None): return b.transverse("cartesian", nx=n, ny=ny, half_width_m=n * pitch / 2, spectral_samples=1, initial_profile=ip, halfWidthYM=ny * pitch / 2)
def sinc2(u): return np.sinc(u) ** 2

def far(system, lam):
    r = uf.run(system, b.run_spec(b.cw(lam, 1.0), instruments=[b.instrument("camera", at="end", name="c", imaging={"plane": "far", "focalLengthM": f_cam},
                                                                          sensor={"pixels": [1024, 1], "pixelSizeM": 20e-6})]),
               fields={"saw": lambda X, Y, Z: dn0 * np.mod(X / period, 1.0)})
    v = r.instrument("c"); m = np.array(v["fluence_j_m2"]).sum(1); xs = np.array(v["x_m"])
    return xs, m

print("lambda_nm  order1 x (mm)  predicted  eff mask  eff index  predicted eff")
for lam in (760e-9, 800e-9, 840e-9):
    out = []
    for kind in ("mask", "index"):
        els = [b.gap("in", 0.0, medium="Vacuum", transverse=tr(b.initial_profile("gaussian", radius_m=w)))]
        els += [b.mask("g", "file", file=fm, design_wavelength_m=lam0)] if kind == "mask" else [b.bulk("g", "Vacuum", L, transverse=tr(), indexProfile="@saw")]
        els += [b.gap("p", 0.0, medium="Vacuum", transverse=tr())]
        xs, m = far(b.system("s", els), lam)
        side = xs > 2.5e-3
        x1 = (xs[side] * m[side]).sum() / m[side].sum()
        out.append((x1, m[side].sum() / m.sum()))
    print(f"{lam*1e9:6.0f}   {out[0][0]*1e3:8.3f} / {out[1][0]*1e3:8.3f}   {f_cam*lam/period*1e3:8.3f}   {out[0][1]:.4f}   {out[1][1]:.4f}   {sinc2(lam0/lam-1):.4f}")
