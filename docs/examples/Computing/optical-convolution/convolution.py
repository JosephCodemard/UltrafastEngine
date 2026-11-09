"""A 4f system as a convolution engine: blur, edge detection and a motion blur, each a filter in the Fourier plane, against numpy.

    python convolution.py
(`make_figures.py` draws the figure and the animation.)
"""
from __future__ import annotations

import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import _optics as op

SETUP = op.Setup(n=256, dx=20e-6, wavelength=800e-9)


def make_scene() -> np.ndarray:
    """A disc, a bar and a ring: edges in every direction."""
    X, Y = np.meshgrid(SETUP.x, SETUP.x, indexing="ij")
    r = np.hypot(X - 0.9e-3, Y + 0.8e-3)
    return (((X + 0.9e-3) ** 2 + (Y - 0.7e-3) ** 2 < (0.6e-3) ** 2) * 1.0
            + 0.6 * ((abs(X - 0.7e-3) < 0.9e-3) & (abs(Y - 0.9e-3) < 0.12e-3))
            + 0.8 * ((r < 0.5e-3) & (r > 0.35e-3)))


def transfer_function(kind: str, sigma_m: float = 40e-6, length_m: float = 0.5e-3) -> np.ndarray:
    """H(fx, fy), FFT-ordered. blur: Gaussian of rms ``sigma_m``; edges: Laplacian of that Gaussian (signed); motion: a box of ``length_m`` along x."""
    FX, FY = op.fourier_axes(SETUP)
    f2 = FX ** 2 + FY ** 2
    g = np.exp(-2 * np.pi ** 2 * sigma_m ** 2 * f2)
    if kind == "blur":
        return g.astype(complex)
    if kind == "edges":
        return (-4 * np.pi ** 2 * f2 * g).astype(complex)
    if kind == "motion":
        return np.sinc(length_m * FX).astype(complex)
    raise ValueError(kind)


def run_kernel(work: str, H: np.ndarray, scene: np.ndarray | None = None) -> dict:
    scene = make_scene() if scene is None else scene
    sf = op.save_scene(os.path.join(work, "scene.npz"), SETUP, scene.astype(complex))
    scale = op.save_filter(os.path.join(work, "filter.npz"), SETUP, H)
    img = op.run_intensity(op.system_4f(SETUP, sf, os.path.join(work, "filter.npz")), SETUP)
    ref = op.reference_4f(scene.astype(complex), H / scale)
    return {"scene": scene, "engine": op.upright(img), "numpy": op.upright(ref), "scale": scale}


def main() -> None:
    work = op.workdir(__file__)
    print(f"grid {SETUP.n} x {SETUP.n}, pitch {SETUP.dx * 1e6:.0f} um, f = N dx^2 / lambda = {SETUP.focal_length * 1e3:.1f} mm; the output is intensity |scene * kernel|^2")
    print(f"{'kernel':8s} {'correlation':>12s} {'relative l2':>12s}   (engine against numpy: FFT, multiply by H, inverse FFT)")
    worst = 0.0
    for kind in ("blur", "edges", "motion"):
        r = run_kernel(work, transfer_function(kind))
        m = op.match(r["engine"], r["numpy"])
        worst = max(worst, m["relativeL2"])
        print(f"{kind:8s} {m['correlation']:12.5f} {m['relativeL2']:12.4f}")
        assert m["correlation"] > 0.999 and m["relativeL2"] < 0.05, (kind, m)
    taps = float(SETUP.n) ** 4
    print(f"each kernel has N x N taps: N^4 = {taps:.2e} multiply-accumulates for a direct digital convolution, done in one pass of light (4 f / c = {4 * SETUP.focal_length / op.C * 1e9:.2f} ns)")
    print(f"worst relative l2 error {worst:.3f} (the mask and the lens phase are sampled on the grid)")
    print("checks passed")


if __name__ == "__main__":
    main()
