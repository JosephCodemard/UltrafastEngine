"""Figure for example 25: supercontinuum spectrum and pulse after 15 cm of photonic-crystal fibre.

Run after `ultrafast run pcf.ufs run.json -o results`, then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(name):
    return np.genfromtxt(f"results/{name}.csv", delimiter=",", names=True)


s, t = load("spectrum"), load("temporal")
fig, ax = plt.subplots(1, 2, figsize=(10, 3.8))
ax[0].plot(s["spectrum_wavelength_m"] * 1e9, s["spectrum_spectrum_db"])
ax[0].axvline(835, color="k", ls=":", lw=1, label="pump, 835 nm")
ax[0].axvline(652.3, color="C3", ls=":", lw=1, label="dispersive-wave resonance, 652 nm")
ax[0].set(xlim=(400, 1400), ylim=(-60, 3), xlabel="wavelength (nm)", ylabel="spectral density (dB)", title="Output spectrum")
ax[0].legend(loc="lower right")
tp = t["time_s"] * 1e12
ax[1].plot(tp, t["input_power_w"] / 1e3, "k--", label="input")
ax[1].plot(tp, t["output_power_w"] / 1e3, label="output")
ax[1].set(xlim=(-1.5, 5), xlabel="time (ps)", ylabel="power (kW)", title="Pulse")
ax[1].legend()
fig.tight_layout()
fig.savefig("supercontinuum.png", dpi=110)
