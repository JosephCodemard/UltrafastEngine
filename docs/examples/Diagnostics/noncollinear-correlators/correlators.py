#!/usr/bin/env python3
"""Beams crossing in a crystal: a background-free autocorrelator, and third-harmonic generation with a crossed second harmonic."""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

C = 299792458.0
TAU, W, ANGLE_DEG = 60e-15, 200e-6, 3.0
beam = {"kind": "gaussian", "w0M": W}
fed = {"role": "b", "fromPort": True, "beam": {"kind": "gaussian", "radiusM": W}}


def autocorrelator(transverse=None):
    """Non-collinear SHG is two beams crossing: sfg, one colour, with the angle outside the crystal."""
    return b.system("autocorrelator", b.Branched([
        b.link(b.beam_splitter("bs"), {"c": "fixed", "d": "stage"}),
        b.link(b.delay("fixed", 0.0), "x"),
        b.link(b.delay("stage", 0.0), "x:b"),
        b.link(b.crystal("x", "BBO", 20e-6, "sfg", "I", external_angle_deg=ANGLE_DEG, aux=[fed], output="sum", record=["a", "b"], beam=beam,
                         chi3=False, transverse=transverse), {"out": "end", "a": "virtual", "b": "virtual"}),
    ]))


def fwhm(x, y):
    peak = y.argmax()
    return np.interp(y[peak] / 2, y[peak:][::-1], x[peak:][::-1]) - np.interp(y[peak] / 2, y[:peak + 1], x[:peak + 1])


spec = b.run_spec(b.pulse(1.03e-6, TAU, energy=1e-9))
delays = np.linspace(-300e-15, 300e-15, 61)
scan = [{"path": "element:stage/delayS", "values": list(delays), "nominal": 0.0}]
line = np.array([r["energy:end"] for r in uf.study(autocorrelator(), spec, parameters=scan, outputs=["energy:end"])["rows"]])
# ny = 1 keeps one transverse axis: the trace's shape is right, its energy is not; use ny > 1 for energy
grid = b.transverse("cartesian", nx=64, ny=1, half_width_m=800e-6, spectral_samples=4)
plane = np.array([r["energy:end"] for r in uf.study(autocorrelator(grid), spec, parameters=scan, outputs=["energy:end"])["rows"]])
smear = W * np.sqrt(np.log(2)) * 2 * np.sin(np.radians(ANGLE_DEG / 2)) / C
print(f"1-D model        FWHM {fwhm(delays, line) * 1e15:.1f} fs (sqrt 2 tau = {np.sqrt(2) * TAU * 1e15:.1f} fs)")
print(f"Cartesian grid   FWHM {fwhm(delays, plane) * 1e15:.1f} fs (with {smear * 1e15:.1f} fs of smearing: {np.hypot(np.sqrt(2) * TAU, smear) * 1e15:.1f} fs)")

# SHG, then THG with the second harmonic arriving 3 degrees off: the tilt of the arm sets the geometry
thg = b.system("crossed-thg", b.Branched([
    b.link(b.crystal("shg", "BBO", 50e-6, "shg", "I", output="sh", record=["fundamental"], beam=beam), {"out": "arm", "fundamental": "thg"}),
    b.link(b.delay("arm", 0.0, tilt_x_rad=np.radians(ANGLE_DEG)), "thg:b"),
    b.link(b.crystal("thg", "BBO", 50e-6, "sfg", "I", aux=[fed], output="sum", record=["a", "b"], beam=beam), {"out": "end", "a": "virtual", "b": "virtual"}),
]))
res = uf.run(thg, b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-6)))
v = next(e for e in res.summary["elements"] if e["id"] == "thg")["values"]
print(f"THG cut {v['thetaDeg']:.2f} deg, inside the crystal {v['noncollinearDeg']:.2f} deg apart")
print(f"leaving at {v['outputTiltDeg_a']:.2f} deg (1030 nm), {v['outputTiltDeg_b']:.2f} deg (515 nm), {v['outputTiltDeg_sum']:.2f} deg (343 nm)")

try:
    import matplotlib.pyplot as plt
    fig, ax = plt.subplots(figsize=(6.4, 3.6))
    ax.plot(delays * 1e15, line / line.max(), color="#1f6fb2", lw=2, label="1-D model")
    ax.plot(delays * 1e15, plane / plane.max(), color="#c8501e", lw=2, ls="--", label="Cartesian grid")
    ax.set(xlabel="delay (fs)", ylabel="second harmonic (normalised)")
    ax.legend(frameon=False)
    fig.tight_layout()
    fig.savefig(os.path.join(os.path.dirname(os.path.abspath(__file__)), "correlators.png"), dpi=120)
except ImportError:
    print("(pip install matplotlib for plots)")
