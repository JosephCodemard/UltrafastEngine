#!/usr/bin/env python3
"""A third-order cross-correlator (the SEQUOIA / TOCC layout): SHG, a delay on the second harmonic, THG, a detector."""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

TAU, ENERGY = 100e-15, 1e-6
beam = b.gaussian_beam(w0_m=300e-6)


def bbo(id, process, output, **keys):
    return {"id": id, "type": "crystal", "crystal": "BBO", "lengthM": 50e-6, "interaction": {"process": process, "type": "I"},
            "cut": "auto", "beam": beam, "output": output, **keys}


system = b.system("tocc", b.Branched([
    # the second harmonic leaves on `out`, what is left of the fundamental on `fundamental`
    bbo("shg", "shg", "sh", record=["fundamental"], next={"out": "stage", "fundamental": "thg"}),
    {"id": "stage", "type": "delay", "delayS": 0.0, "next": "thg:b"},
    # both colours meet here, collinear; the unused waves are declared
    bbo("thg", "sfg", "sum", record=["a", "b"], next={"out": "end", "a": "virtual", "b": "virtual"},
        aux=[{"role": "b", "fromPort": True, "beam": {"kind": "gaussian", "radiusM": 300e-6}}]),
]))
spec = b.run_spec(b.pulse(1.03e-6, TAU, energy=ENERGY))

res = uf.run(system, spec)
shg = next(e for e in res.summary["elements"] if e["id"] == "shg")["values"]
thg = next(e for e in res.summary["elements"] if e["id"] == "thg")["values"]
print(f"SHG cut {shg['thetaDeg']:.2f} deg, THG cut {thg['thetaDeg']:.2f} deg")
print(f"out of {ENERGY * 1e6:.0f} uJ: fundamental {thg['energy_a_J'] * 1e9:.1f} nJ, "
      f"second harmonic {thg['energy_b_J'] * 1e9:.1f} nJ, third harmonic {thg['energy_sum_J'] * 1e12:.2f} pJ")

delays = np.linspace(-600e-15, 600e-15, 241)
study = uf.study(system, spec, parameters=[{"path": "element:stage/delayS", "values": list(delays), "nominal": 0.0}], outputs=["energy:end"])
signal = np.array([row["energy:end"] for row in study["rows"]])
peak = signal.argmax()
left = np.interp(signal[peak] / 2, signal[:peak + 1], delays[:peak + 1])
right = np.interp(signal[peak] / 2, signal[peak:][::-1], delays[peak:][::-1])
print(f"trace FWHM {(right - left) * 1e15:.1f} fs (Gaussians: {np.hypot(TAU, TAU / np.sqrt(2)) * 1e15:.1f} fs)")

try:
    import matplotlib.pyplot as plt
    fig, ax = plt.subplots(figsize=(6.4, 3.6))
    ax.semilogy(delays * 1e15, signal / signal.max(), color="#1f6fb2", lw=2)
    ax.set(xlabel="delay of the second harmonic (fs)", ylabel="third harmonic (normalised)", ylim=(1e-6, 2))
    fig.tight_layout()
    fig.savefig(os.path.join(os.path.dirname(os.path.abspath(__file__)), "tocc.png"), dpi=120)
except ImportError:
    print("(pip install matplotlib for plots)")
