"""Figure for example 02: spectra, stretched pulse and compressed pulse of the CPA line.

Run after `ultrafast run cpa.ufs run.json -o results`, then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(name):
    return np.genfromtxt(f"results/{name}.csv", delimiter=",", names=True)


si, so = load("spectrum_in"), load("spectrum_out")
pd, t = load("photodiode_stretched"), load("temporal")
fig, ax = plt.subplots(1, 3, figsize=(13, 3.8))
ax[0].plot(si["spectrum_in_wavelength_m"] * 1e9, si["spectrum_in_spectrum"] / si["spectrum_in_spectrum"].max(), "k--", label="input")
ax[0].plot(so["spectrum_out_wavelength_m"] * 1e9, so["spectrum_out_spectrum"] / so["spectrum_out_spectrum"].max(), label="after compressor")
ax[0].set(xlim=(1000, 1060), xlabel="wavelength (nm)", ylabel="spectral density (each norm. to its peak)", title="Spectrum")
ax[0].legend()
ax[1].plot(pd["photodiode_stretched_time_s"] * 1e12, pd["photodiode_stretched_signal_w"] / pd["photodiode_stretched_signal_w"].max())
ax[1].set(xlabel="time (ps)", ylabel="power (norm.)", title="Stretched pulse (20 ps photodiode)")
tf = t["time_s"] * 1e15
ax[2].plot(tf, t["input_power_w"] / t["input_power_w"].max(), "k--", label="input")
ax[2].plot(tf, t["output_power_w"] / t["input_power_w"].max(), label="compressed")
ax[2].set(xlim=(-1000, 1000), xlabel="time (fs)", ylabel="power (norm. to input peak)", title="Compressed pulse")
ax[2].legend()
fig.tight_layout()
fig.savefig("cpa-line.png", dpi=110)
