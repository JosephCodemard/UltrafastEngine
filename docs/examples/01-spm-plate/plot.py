"""Figure for example 01: input vs output spectrum and pulse of the SPM plate.

Run after `ultrafast run plate.ufs run.json -o results`, then `python plot.py`.
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
norm = s["input_spectrum"].max()
ax[0].plot(wl, s["input_spectrum"] / norm, "k--", label="input")
ax[0].plot(wl, s["output_spectrum"] / norm, label="after plate")
ax[0].set(xlim=(1010, 1055), xlabel="wavelength (nm)", ylabel="spectral density (norm. to input peak)", title="Spectrum")
ax[0].legend()
ax[1].plot(t["time_s"] * 1e15, t["input_power_w"] / 1e6, "k--", label="input")
ax[1].plot(t["time_s"] * 1e15, t["output_power_w"] / 1e6, label="after plate")
ax[1].plot(c["ideal_compressor_time_s"] * 1e15, c["ideal_compressor_power_w"] / 1e6, label="after ideal compressor")
ax[1].set(xlim=(-600, 600), xlabel="time (fs)", ylabel="power (MW)", title="Pulse")
ax[1].legend()
fig.tight_layout()
fig.savefig("spm-plate.png", dpi=110)
