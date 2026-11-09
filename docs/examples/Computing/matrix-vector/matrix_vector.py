"""Matrix-vector products by light: a lens as the DFT matrix, and a 4f system as a circulant matrix, against ``A @ x``.

    python matrix_vector.py

The field is constant along y on a grid of 4 rows, so the system is one-dimensional. A general matrix needs the anamorphic layout
(cylindrical lenses and a mask, as in Goodman's vector-matrix multiplier); the engine's ``lens`` is round, so only the two
shift-invariant matrices are shown.
"""
from __future__ import annotations

import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import _optics as op

SETUP = op.Setup(n=128, dx=20e-6, wavelength=800e-9, ny=4)
ROW = SETUP.ny_ // 2


def vector(seed: int = 5) -> np.ndarray:
    """A complex input vector (amplitude and phase on each of the N pixels)."""
    rng = np.random.default_rng(seed)
    return (rng.random(SETUP.n) * np.exp(2j * np.pi * rng.random(SETUP.n))) * (rng.random(SETUP.n) > 0.4)


def dft_by_lens(work: str, x: np.ndarray):
    """|A x|^2 from the engine (one lens) and from numpy (the matrix)."""
    field = np.tile(x[:, None], (1, SETUP.ny_))
    sf = op.save_scene(os.path.join(work, "vector.npz"), SETUP, field)
    img = op.run_intensity(op.system_lens(SETUP, sf), SETUP)[:, ROW]
    return img, np.abs(op.dft_matrix(SETUP.n) @ x) ** 2


def circulant_kernel() -> np.ndarray:
    """First column of a circulant matrix C: a smoothing with a Gaussian of 3 pixels (rms) followed by a one-pixel step."""
    d = np.arange(SETUP.n) - SETUP.n // 2
    g = np.exp(-0.5 * (d / 3.0) ** 2)
    return np.roll(g / g.sum(), -SETUP.n // 2)


def circulant_by_4f(work: str, x: np.ndarray, c: np.ndarray):
    """|C x|^2 from the engine (a 4f system whose filter is FFT(c)) and from numpy (the matrix)."""
    H1 = np.fft.fft(c)
    H = np.tile(H1[:, None], (1, SETUP.ny_))
    field = np.tile(x[:, None], (1, SETUP.ny_))
    sf = op.save_scene(os.path.join(work, "vector.npz"), SETUP, field)
    scale = op.save_filter(os.path.join(work, "filter.npz"), SETUP, H)
    img = op.upright(op.run_intensity(op.system_4f(SETUP, sf, os.path.join(work, "filter.npz")), SETUP))[:, ROW]
    C = np.array([np.roll(c, k) for k in range(SETUP.n)]).T           # C[i, j] = c[(i - j) mod N]
    # the input origin is index N/2, so C acts on the origin-shifted vector
    y = np.fft.fftshift(C @ np.fft.ifftshift(x))
    return img, np.abs(y) ** 2


def rel(a: np.ndarray, b: np.ndarray) -> float:
    a, b = a / a.sum(), b / b.sum()
    return float(np.linalg.norm(a - b) / np.linalg.norm(b))


def main() -> None:
    work = op.workdir(__file__)
    n = SETUP.n
    x = vector()
    print(f"vector of N = {n} complex numbers on N pixels of {SETUP.dx * 1e6:.0f} um; wavelength {SETUP.wavelength * 1e9:.0f} nm; lens f = N dx^2 / lambda = {SETUP.focal_length * 1e3:.1f} mm")
    a, ra = dft_by_lens(work, x)
    e1 = rel(a, ra)
    print(f"y = A x, A = DFT matrix (one lens):       relative l2 error of |y|^2 {e1:.2e}, correlation {np.corrcoef(a, ra)[0, 1]:.6f}")
    c = circulant_kernel()
    bimg, rb = circulant_by_4f(work, x, c)
    e2 = rel(bimg, rb)
    print(f"y = C x, C = circulant matrix (4f system): relative l2 error of |y|^2 {e2:.2e}, correlation {np.corrcoef(bimg, rb)[0, 1]:.6f}")
    print(f"multiply-accumulates done by the optics in one pass: N^2 = {n * n} for each product")
    assert e1 < 0.01 and e2 < 0.05, (e1, e2)
    print("checks passed")


if __name__ == "__main__":
    main()
