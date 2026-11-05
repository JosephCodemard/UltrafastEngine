"""Figure for example 03: spectral broadening in the argon multipass cell and the ideal compression.

Run after `ultrafast run mpc.ufs run.json -o results`, then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(name):
    return np.genfromtxt(f"results/{name}.csv", delimiter=",", names=True)


s, t, c = load("spectrum"), load("temporal"), load("ideal_compressor")
fig, ax = plt.subplots(1, 2, figsize=(10, 3.8))
wl = s["wavelength_m"] * 1e9
ref = s["input_spectrum"].max()
for key, label, style in (("input_spectrum", "input", "k--"), ("output_spectrum", "after the cell", "-")):
    ax[0].plot(wl, 10 * np.log10(np.maximum(s[key] / ref, 1e-12)), style, label=label)
ax[0].set(xlim=(900, 1180), ylim=(-70, 5), xlabel="wavelength (nm)", ylabel="spectral density (dB, rel. to input peak)", title="Spectrum")
ax[0].legend()
ax[1].plot(t["time_s"] * 1e15, t["input_power_w"] / 1e6, "k--", label="input")
ax[1].plot(t["time_s"] * 1e15, t["output_power_w"] / 1e6, label="after the cell")
ax[1].plot(c["ideal_compressor_time_s"] * 1e15, c["ideal_compressor_power_w"] / 1e6, label="after ideal compressor")
ax[1].set(xlim=(-500, 500), xlabel="time (fs)", ylabel="power (MW)", title="Pulse")
ax[1].legend()
fig.tight_layout()
fig.savefig("mpc-broadening.png", dpi=110)
