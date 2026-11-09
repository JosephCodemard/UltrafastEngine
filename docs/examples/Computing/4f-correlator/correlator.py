"""A 4f correlator: find a pattern in a scene in one pass of light, and compare with the correlation theorem in numpy.

    python correlator.py            # run and check
(`make_figures.py` draws the figure and the animation.)
"""
from __future__ import annotations

import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import _optics as op

SETUP = op.Setup(n=256, dx=20e-6, wavelength=800e-9)
SIZE = 24                                                   # the letters are 24 x 24 pixels (480 um)
PLACES = {"T": (60, 70), "L": (150, 60), "E": (100, 160), "O": (170, 170), "T (second copy)": (40, 180)}


def letter(kind: str) -> np.ndarray:
    a = np.zeros((SIZE, SIZE))
    if kind[0] == "T":
        a[:5, :] = 1; a[:, SIZE // 2 - 2:SIZE // 2 + 3] = 1
    elif kind[0] == "L":
        a[:, :5] = 1; a[-5:, :] = 1
    elif kind[0] == "E":
        a[:5, :] = 1; a[-5:, :] = 1; a[SIZE // 2 - 2:SIZE // 2 + 3, :] = 1; a[:, :5] = 1
    elif kind[0] == "O":
        yy, xx = np.mgrid[:SIZE, :SIZE] - SIZE / 2 + 0.5
        r = np.hypot(xx, yy)
        a = ((r < SIZE / 2) & (r > SIZE / 2 - 5)) * 1.0
    return a


def make_scene(seed: int = 3) -> np.ndarray:
    scene = np.zeros((SETUP.n, SETUP.n))
    for name, (i, j) in PLACES.items():
        scene[i:i + SIZE, j:j + SIZE] += letter(name)
    return scene + 0.1 * np.random.default_rng(seed).random(scene.shape)      # a background of clutter


def make_pattern() -> np.ndarray:
    p = np.zeros((SETUP.n, SETUP.n))
    c = SETUP.n // 2
    p[c - SIZE // 2:c + SIZE // 2, c - SIZE // 2:c + SIZE // 2] = letter("T")
    return p


def matched_filter(pattern: np.ndarray) -> np.ndarray:
    """H = conj(FFT(pattern)), FFT-ordered: the transfer function of the matched filter (correlation = convolution with the flipped conjugate)."""
    return np.conj(np.fft.fft2(np.fft.ifftshift(pattern)))


def run(work: str, H: np.ndarray | None = None) -> dict:
    """Engine and numpy for the scene and the filter ``H`` (default: the matched filter). Returns the maps and the scale of the mask."""
    scene, pattern = make_scene(), make_pattern()
    H = matched_filter(pattern) if H is None else H
    sf = op.save_scene(os.path.join(work, "scene.npz"), SETUP, scene.astype(complex))
    scale = op.save_filter(os.path.join(work, "filter.npz"), SETUP, H)
    t = time.time()
    img = op.run_intensity(op.system_4f(SETUP, sf, os.path.join(work, "filter.npz")), SETUP)
    ref = op.reference_4f(scene.astype(complex), H / scale)
    return {"scene": scene, "pattern": pattern, "H": H, "engine": op.upright(img), "numpy": op.upright(ref), "seconds": time.time() - t}


def main() -> None:
    r = run(op.workdir(__file__))
    n = SETUP.n
    m = op.match(r["engine"], r["numpy"])
    print(f"grid {n} x {n}, pitch {SETUP.dx * 1e6:.0f} um, wavelength {SETUP.wavelength * 1e9:.0f} nm, focal length f = N dx^2 / lambda = {SETUP.focal_length * 1e3:.1f} mm")
    print(f"engine against numpy (correlation theorem): correlation above 0.998, relative l2 error below {op.bound(m['relativeL2']):g}")
    pe, pn = op.find_peaks(r["engine"], 5), op.find_peaks(r["numpy"], 5)
    print("correlation peaks (pixel i, j; height relative to the strongest):")
    for (i, j, v), (i2, j2, v2) in zip(pe, pn):
        print(f"  engine ({i:3d}, {j:3d}) {v / pe[0][2]:.3f}   numpy ({i2:3d}, {j2:3d}) {v2 / pn[0][2]:.3f}")
    # the two copies of T are the two highest peaks, at the pattern's centre offset from where each copy sits
    true = sorted((i + SIZE // 2, j + SIZE // 2) for name, (i, j) in PLACES.items() if name.startswith("T"))
    found = sorted((i, j) for i, j, _ in pe[:2])
    off = [(a - c, b - d) for (a, b), (c, d) in zip(found, true)]
    print(f"the two T copies are the two highest peaks; offsets from their true centres (pixels): {off}")
    decoy = pe[2][2] / pe[1][2]
    print(f"strongest decoy (another letter) / weakest T peak: {decoy:.2f}")
    macs = float(n) ** 4
    print(f"multiply-accumulates of the equivalent digital correlation (N x N filter on N x N pixels): N^4 = {macs:.2e}")
    print(f"light transit through the 4f system: 4 f / c = {4 * SETUP.focal_length / op.C * 1e9:.2f} ns; the engine took {r['seconds']:.1f} s of wall time to simulate it")
    assert m["correlation"] > 0.998, m
    assert all(abs(a) <= 1 and abs(b) <= 1 for a, b in off), off
    assert abs(pe[0][2] / pe[1][2] - pn[0][2] / pn[1][2]) < 0.02
    assert decoy < 0.8
    print("checks passed")


if __name__ == "__main__":
    main()
