"""S2 spike 2 (sparse spectral sampling): 800 nm / 30 fs through a phase mask, far field. K stored frequencies against a dense run.
Usage: spike_sparse.py PHASE_PEAK_RAD  (peak phase of a smooth random screen, rad at the design wavelength)"""
import os, sys, time, tempfile
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "python", "src"))
import ultrafast as uf
from ultrafast import build as b

amp_rad = float(sys.argv[1]); n = 128; pitch = 20e-6; lam = 800e-9; w = 0.5e-3
x = (np.arange(n) - n / 2) * pitch
X, Y = np.meshgrid(x, x, indexing="ij")
rng = np.random.default_rng(3)
k = np.fft.fftfreq(n, pitch); KX, KY = np.meshgrid(k, k, indexing="ij")
screen = np.fft.ifft2(np.fft.fft2(rng.standard_normal((n, n))) * np.exp(-(KX**2 + KY**2) / (2 * (400.0) ** 2))).real
screen *= amp_rad / np.abs(screen).max()
d = tempfile.mkdtemp(); f = os.path.join(d, "m.npz")
np.savez(f, x_m=x, y_m=x, amplitude=np.ones((n, n)), phase_rad=screen)

def run(K):
    tr = lambda ip=None: b.transverse("cartesian", nx=n, ny=n, half_width_m=n * pitch / 2, spectral_samples=K, initial_profile=ip)
    s = b.system("s", [b.gap("in", 0.0, medium="Vacuum", transverse=tr(b.initial_profile("gaussian", radius_m=w))),
                       b.mask("slm", "file", file=f, design_wavelength_m=lam), b.gap("p", 0.0, medium="Vacuum", transverse=tr())])
    t = time.time()
    r = uf.run(s, b.run_spec(b.pulse(lam, 30e-15, energy=1e-6), instruments=[
        b.instrument("camera", at="end", name="focus", imaging={"plane": "far", "focalLengthM": 0.5}, sensor={"pixels": [n, n], "pixelSizeM": 20e-6})]))
    v = r.instrument("focus")
    return np.array(v["fluence_j_m2"]), time.time() - t

ref, tref = run(96)
print(f"phase peak {amp_rad} rad; dense K=96: {tref:.1f} s; focal peak {ref.max():.4e}")
for K in (1, 2, 4, 8, 16, 32, 48):
    m, t = run(K)
    print(f"K={K:3d}: {t:5.1f} s  peak ratio {m.max()/ref.max():.5f}  rel L2 {np.linalg.norm(m-ref)/np.linalg.norm(ref):.2e}")
