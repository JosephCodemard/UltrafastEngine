"""Beam shaping with a phase-only SLM: Gerchberg-Saxton holograms, 8-bit grey levels, a lens, run through the engine
(python slm_shaping.py [--full])."""
import sys
import time

import numpy as np

import ultrafast as uf
from ultrafast import build as b
from ultrafast.algorithms import holography

uf.plugins.load_python_plugins()
from ultrafast_ao import Slm, devices                                 # noqa: E402

LAM = 800e-9
N, DX, W = 256, 20e-6, 1.4e-3                    # panel pixels per axis, pitch, 1/e^2 beam radius at the panel


def critical_focal_length(n, dx, lam=LAM):
    """f = n dx^2 / lambda: the focal-plane pixels of the engine (pitch dx) are then the FFT bins of the hologram, so design and result share one lattice."""
    return n * dx * dx / lam


def smiley(n, scale=0.72):
    y, x = (np.indices((n, n)) - n / 2 + 0.5) / n * 2                   # -1..1; the first index is drawn upward, so +y is up
    face = np.abs(np.hypot(x, y) - 0.62 * scale) < 0.035 * max(scale, 0.8)
    eyes = (np.hypot(x - 0.24 * scale, y - 0.22 * scale) < 0.075) | (np.hypot(x + 0.24 * scale, y - 0.22 * scale) < 0.075)
    r = np.hypot(x, y + 0.05 * scale)
    mouth = (np.abs(r - 0.36 * scale) < 0.035) & (y < -0.18 * scale)
    return (face | eyes | mouth).astype(float)


def square_outline(n):
    y, x = (np.indices((n, n)) - n / 2 + 0.5) / n * 2
    m = np.maximum(np.abs(x), np.abs(y))
    return (np.abs(m - 0.36) < 0.03).astype(float)


def ring(n):
    y, x = (np.indices((n, n)) - n / 2 + 0.5) / n * 2
    return (np.abs(np.hypot(x, y) - 0.36) < 0.04).astype(float)


def spots(n):
    c = n // 2
    m = np.zeros((n, n))
    for i in (-1, 0, 1):
        for j in (-1, 0, 1):
            m[c + i * n // 8, c + j * n // 8] = 1
    return m


def illumination(n, dx, w=W):
    X = (np.arange(n) - n / 2) * dx                                     # the engine's grid points: a sample on the axis, pixel i of the panel on grid point i
    return np.exp(-(X[:, None] ** 2 + X[None, :] ** 2) / w ** 2)


def engine_focus(phase, n, dx, lam=LAM, f=None, absorbing=True):
    """Write ``phase`` to an 8-bit SLM (256 grey levels, linear 0..2 pi) and return the engine's focal-plane fluence map and axes. The panel is shifted by half a pixel so that its
    pixel centres lie on the engine's grid points (the grid has a sample on the axis, an even panel has a pixel edge there; without the shift every grid cell averages two pixels)."""
    f = f or critical_focal_length(n, dx, lam)
    g = lambda ip=None: b.transverse("cartesian", nx=n, ny=n, half_width_m=n * dx / 2, spectral_samples=1, absorbing_boundary=absorbing, initial_profile=ip)
    sysd = b.system("slm-shaping", [b.gap("src", 0.0, medium="Vacuum", transverse=g(b.initial_profile("gaussian", radius_m=W))),
                                    Slm("slm", dx, pixels_x=n, pixels_y=n, phase_target_rad=phase, grey_levels=256, dispersion="none", offset_x_m=-dx / 2, offset_y_m=-dx / 2),
                                    b.lens("lens", f), b.gap("z", f, medium="Vacuum", transverse=g())])
    d = uf.run(sysd, b.run_spec(b.cw(lam, 1.0), instruments=[b.instrument("beamProfile", at="z", name="cam")])).instrument("cam")
    return np.asarray(d["fluence_map_j_m2"]), np.asarray(d["x_m"]), np.asarray(d["y_m"])


def design(target, iterations=60, seed=1, levels=256):
    return holography.gerchberg_saxton(target, illumination(target.shape[0], DX), iterations, seed=seed, levels=levels)


def exact_prediction(phase, levels=256, lam=LAM, dx=DX):
    """What the written (quantised) phase gives: the sampled field (Gaussian x e^{i phase}) times the thin lens, propagated over f with the exact angular spectrum
    (NumPy, periodic grid, no absorbing edge), on the engine's lattice. Returns the fluence, normalised to unit sum."""
    n = phase.shape[0]
    f = critical_focal_length(n, dx, lam)
    X = (np.arange(n) - n / 2) * dx
    XX, YY = np.meshgrid(X, X, indexing="ij")
    field = illumination(n, dx) * np.exp(1j * holography.quantise_phase(phase, levels)) * np.exp(-1j * np.pi * (XX ** 2 + YY ** 2) / (lam * f))
    fx = np.fft.fftfreq(n, dx)
    FX, FY = np.meshgrid(fx, fx, indexing="ij")
    h = np.exp(2j * np.pi / lam * f * np.sqrt(1 - (lam * FX) ** 2 - (lam * FY) ** 2 + 0j))
    p = np.abs(np.fft.ifft2(np.fft.fft2(field) * h)) ** 2
    return p / p.sum()


def rel_l2(a, b):
    """Relative L2 distance of two fluence maps after normalising each to unit sum."""
    a, b = a / a.sum(), b / b.sum()
    return float(np.linalg.norm(a - b) / np.linalg.norm(b))


def run():
    out = {}
    targets = {"smiley": smiley(N), "square": square_outline(N), "ring": ring(N)}
    for name, t in targets.items():
        h = design(t)
        F, x, y = engine_focus(h.phase, N, DX)
        out[name] = {"target": t, "hologram": h, "fluence": F, "x": x, "open": engine_focus(h.phase, N, DX, absorbing=False)[0], "prediction": exact_prediction(h.phase)}
    wgs = holography.weighted_gs(spots(N), 30, source_amplitude=illumination(N, DX), seed=3)
    F, x, y = engine_focus(wgs.phase, N, DX)
    out["spots"] = {"target": spots(N), "hologram": wgs, "fluence": F, "x": x, "open": engine_focus(wgs.phase, N, DX, absorbing=False)[0], "prediction": exact_prediction(wgs.phase)}
    return out


def energy_on_target(F, t):
    return float(F[t > 0].sum() / F.sum())


def main():
    r = run()
    print("panel %d x %d pixels of %.0f um, 256 grey levels, lambda %.0f nm, f = %.1f mm (focal pixels = FFT bins), beam radius %.1f mm"
          % (N, N, DX * 1e6, LAM * 1e9, critical_focal_length(N, DX) * 1e3, W * 1e3))
    for name, d in r.items():
        t, F, P, h = d["target"], d["fluence"], d["prediction"], d["hologram"]
        e_open, e_abs = rel_l2(d["open"], P), rel_l2(F, P)
        mono = "" if not h.error else ("  GS error %.3f -> %.3f" % (h.error[0], h.error[-1]))
        print("%-7s energy on target pixels %.2f (design %.2f); against the exact propagation of the written phase: open edge %.3f, absorbing edge %.3f%s"
              % (name, energy_on_target(F, t), h.efficiency, e_open, e_abs, mono))
        assert e_open < 0.02, "the engine must reproduce the exact angular-spectrum propagation of the written phase"
        assert energy_on_target(F, t) > 0.5 * h.efficiency
    err = r["smiley"]["hologram"].error
    assert all(err[i + 1] <= err[i] * 1.001 for i in range(len(err) - 1) if i > 2), "GS error should not grow (Fienup 1982)"
    print("GS error is non-increasing after the first iterations (Fienup 1982)")
    print("checks passed")
    if "--full" in sys.argv:
        full()


def full(iterations=25):
    """The full 1920 x 1080 panel, 8 um pixels, 8 bit: GS on the 2048 x 2048 grid the engine uses, then one engine run. The panel's phase goes in as a
    callback (an array of 2 million values is not written to JSON)."""
    n, dx, px, py = 2048, 8e-6, 1920, 1080
    f = critical_focal_length(n, dx)
    o = (n - px) // 2, (n - py) // 2
    X = (np.arange(px) - (px - 1) / 2) * dx; Y = (np.arange(py) - (py - 1) / 2) * dx
    S = np.zeros((n, n)); S[o[0]:o[0] + px, o[1]:o[1] + py] = np.exp(-(X[:, None] ** 2 + Y[None, :] ** 2) / (3.0e-3) ** 2)
    T = np.zeros((n, n)); T[512:1536, 512:1536] = smiley(1024)
    t0 = time.time()
    h = holography.gerchberg_saxton(T, S, iterations, seed=1, levels=256)
    t_gs = time.time() - t0
    panel = h.phase[o[0]:o[0] + px, o[1]:o[1] + py]

    def holo(x, y, z):
        i = np.rint(x / dx + (px - 1) / 2).astype(int); j = np.rint(y / dx + (py - 1) / 2).astype(int)
        return panel[np.clip(i, 0, px - 1), np.clip(j, 0, py - 1)]
    g = lambda ip=None: b.transverse("cartesian", nx=n, ny=n, half_width_m=n * dx / 2, spectral_samples=1, absorbing_boundary=True, initial_profile=ip)
    sysd = b.system("slm-full", [b.gap("src", 0.0, medium="Vacuum", transverse=g(b.initial_profile("gaussian", radius_m=3.0e-3))),
                                 Slm("slm", dx, pixels_x=px, pixels_y=py, phase_target_rad="@holo", grey_levels=256, dispersion="none", offset_x_m=-dx / 2, offset_y_m=-dx / 2),
                                 b.lens("lens", f), b.gap("z", f, medium="Vacuum", transverse=g())])
    spec = b.run_spec(b.cw(LAM, 1.0), instruments=[b.instrument("beamProfile", at="z", name="cam")])
    t0 = time.time()
    d = uf.run(sysd, spec, fields={"holo": holo}).instrument("cam")
    t_run = time.time() - t0
    F = np.asarray(d["fluence_map_j_m2"])
    on = F[T > 0].sum() / F.sum()
    print("full panel %d x %d pixels of %.0f um, 256 grey levels: GS %d iterations on %d x %d in %.0f s, engine run %.0f s" % (px, py, dx * 1e6, iterations, n, n, t_gs, t_run))
    print("energy on the smiley: design %.2f, engine %.2f" % (h.efficiency, on))
    assert on > 0.5 * h.efficiency
    return {"panel": panel, "fluence": F, "target": T, "design": h}


if __name__ == "__main__":
    main()
