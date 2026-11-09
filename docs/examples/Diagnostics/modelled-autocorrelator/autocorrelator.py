"""An autocorrelator built from a splitter, a delay stage and a crystal, against the ideal instrument, with the Python API."""
import math

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

import ultrafast as uf
from ultrafast import build as b

BLUE, ORANGE, AQUA = "#2a78d6", "#eb6834", "#1baf7a"
LAM = 1030e-9


def fwhm(x, y):
    y = np.asarray(y) - min(y)
    half = y.max() / 2
    i = np.where(y >= half)[0]
    a, c = i[0], i[-1]
    left = x[a - 1] + (half - y[a - 1]) / (y[a] - y[a - 1]) * (x[a] - x[a - 1])
    right = x[c] + (half - y[c]) / (y[c + 1] - y[c]) * (x[c + 1] - x[c])
    return right - left


def setup(length):
    crystal = b.crystal("x", "BBO", length, "sfg", type="I", output="sum", beam=b.gaussian_beam(100e-6), aux=[b.aux_beam("b", radius_m=100e-6, from_port=True)])
    return b.system("modelled autocorrelator", b.Branched([
        b.link(b.beam_splitter("bs"), {"c": "fixed", "d": "stage"}),
        b.link(b.delay("fixed", delay_s=0.0), "x"),
        b.link(b.delay("stage", delay_s=0.0), "x:b"),
        b.link(crystal, "end"),
    ]))


print("pulse     BBO      model     instrument  ideal   (fs)")
fig, axes = plt.subplots(1, 3, figsize=(11, 3.4), constrained_layout=True)
for ax, (tau, length, color) in zip(axes, ((100e-15, 20e-6, BLUE), (30e-15, 1e-3, ORANGE), (15e-15, 1e-3, AQUA))):
    delays = np.linspace(-6 * tau, 6 * tau, 81)
    spec = b.run_spec(b.pulse(LAM, tau, energy=1e-9), instruments=[b.instrument("autocorrelator", at="input", name="ac")])
    scan = uf.study(setup(length), spec, parameters=[{"path": "element:stage/delayS", "values": list(delays), "nominal": 0.0}],
                    outputs=["energy:end", "instrument:ac/acFwhmS"])
    signal = np.array([row["energy:end"] for row in scan["rows"]])
    ideal = scan["rows"][0]["instrument:ac/acFwhmS"]
    model = fwhm(delays, signal)
    print("%3.0f fs  %5.0f um  %7.2f   %7.2f   %7.2f" % (tau * 1e15, length * 1e6, model * 1e15, ideal * 1e15, tau * math.sqrt(2) * 1e15))
    gauss = np.exp(-4 * math.log(2) * (delays / ideal) ** 2)
    ax.plot(delays * 1e15, gauss, color="#52514e", lw=1, label="ideal instrument")
    ax.plot(delays * 1e15, signal / signal.max(), "o", color=color, ms=3, label="modelled")
    ax.set(xlabel="delay (fs)", title="%.0f fs pulse, %.0f um BBO" % (tau * 1e15, length * 1e6))
    ax.spines[["top", "right"]].set_visible(False)
    ax.legend(frameon=False, loc="upper right", fontsize=8)
axes[0].set_ylabel("sum-frequency energy (peak = 1)")
fig.savefig("autocorrelator.png", dpi=130)
