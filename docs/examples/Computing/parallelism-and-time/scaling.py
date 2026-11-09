"""How the cost of a convolution grows with the number of pixels: the engine's wall time, the digital FFT time, and the time light needs.

    python scaling.py            # N = 64 ... 1024
    python scaling.py --max 256  # a quicker run

For each N x N pixels (pitch 20 um, so the aperture and the focal length f = N dx^2 / lambda grow with N) the same Gaussian blur is done
three ways: by the engine (it *simulates* the 4f system on a digital machine, so its wall time is not the physical time), by numpy
(two FFTs and a product: the digital way), and "optically": the light crosses 4 f once, plus the sensor's integration time.
"""
from __future__ import annotations

import argparse
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import _optics as op

SIZES = (64, 128, 256, 512, 1024)
SENSOR_INTEGRATION_S = 1e-6        # ASSUMPTION: a fast sensor exposes for 1 us. Real SLM + camera chains are slower: that is the point of the table's last column.
SIGMA_M = 40e-6


def one(n: int, work: str, repeats: int = 5) -> dict:
    s = op.Setup(n=n, dx=20e-6, wavelength=800e-9)
    X, Y = np.meshgrid(s.x, s.x, indexing="ij")
    scene = ((np.hypot(X, Y) < 0.18 * n * s.dx) * 1.0 + 0.5 * (abs(X - 0.2 * n * s.dx) < 0.03 * n * s.dx) * (abs(Y) < 0.25 * n * s.dx))
    FX, FY = op.fourier_axes(s)
    H = np.exp(-2 * np.pi ** 2 * SIGMA_M ** 2 * (FX ** 2 + FY ** 2)).astype(complex)
    sf = op.save_scene(os.path.join(work, f"scene{n}.npz"), s, scene.astype(complex))
    op.save_filter(os.path.join(work, f"filter{n}.npz"), s, H)
    system = op.system_4f(s, sf, os.path.join(work, f"filter{n}.npz"))
    t = time.perf_counter()
    img = op.run_intensity(system, s)
    engine_s = time.perf_counter() - t
    ref = op.reference_4f(scene.astype(complex), H)
    err = op.match(img, ref)["relativeL2"]
    # the digital way: FFT, multiply, inverse FFT (best of several runs)
    a = np.fft.ifftshift(scene).astype(complex)
    best = np.inf
    for _ in range(repeats):
        t = time.perf_counter()
        np.fft.ifft2(np.fft.fft2(a) * H)
        best = min(best, time.perf_counter() - t)
    transit = 4 * s.focal_length / op.C
    return {"n": n, "pixels": n * n, "macs": float(n) ** 4, "engineS": engine_s, "digitalS": best, "transitS": transit,
            "opticalS": transit + SENSOR_INTEGRATION_S, "errorL2": err, "focalLengthM": s.focal_length}


def slope(x, y) -> float:
    return float(np.polyfit(np.log(x), np.log(y), 1)[0])


def main(argv=None) -> list:
    ap = argparse.ArgumentParser()
    ap.add_argument("--max", type=int, default=1024, help="largest N")
    a = ap.parse_args(argv)
    work = op.workdir(__file__)
    one(SIZES[0], work, repeats=1)                      # warm-up: the first engine call pays start-up costs
    rows = [one(n, work) for n in SIZES if n <= a.max]
    print("N      pixels     MACs (direct, N^4)  f (mm)   light 4f/c (ns)  error vs numpy")
    for r in rows:
        print(f"{r['n']:<6d} {r['pixels']:<10d} {r['macs']:<19.3e} {r['focalLengthM'] * 1e3:<8.1f} {r['transitS'] * 1e9:<16.2f} {r['errorL2']:.4f}")
    print("N      engine wall time (s)   digital FFT time (s)   optical time (s) = 4f/c + 1 us sensor (assumed)")
    for r in rows:
        print(f"{r['n']:<6d} {r['engineS']:<22.3f} {r['digitalS']:<22.5f} {r['opticalS']:.3e}")
    big = rows[-3:]                                    # the largest sizes: fixed overheads no longer dominate
    px = [r["pixels"] for r in big]
    se, sd = slope(px, [r["engineS"] for r in big]), slope(px, [r["digitalS"] for r in big])
    print(f"log-log slope of time against pixel count over the three largest N: engine {se:.2f}, digital FFT {sd:.2f} (N^2 log N gives a little over 1); optical: none, the path grows as N^2 but stays nanoseconds")
    print("the engine's time is the cost of simulating the light on a digital machine; the optical time is a model (path / c plus an assumed exposure)")
    assert all(r["errorL2"] < 0.05 for r in rows), [r["errorL2"] for r in rows]
    assert rows[-1]["digitalS"] > 5 * rows[0]["digitalS"] and rows[-1]["engineS"] > rows[0]["engineS"]
    print("checks passed")
    return rows


if __name__ == "__main__":
    main()
