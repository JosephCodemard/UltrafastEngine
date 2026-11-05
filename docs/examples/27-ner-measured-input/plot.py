"""Figure for example 27: the cleaned measured input spectrum, and the OSA recording compared with the simulation.

Run after `ultrafast run plates.ufs run.json -o results`, then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(name):
    return np.genfromtxt(f"results/{name}.csv", delimiter=",", names=True)


s, o = load("spectrum"), load("osa")
raw = np.genfromtxt("data/spectrum_digitised.csv", delimiter=",", skip_header=1)
fig, ax = plt.subplots(1, 2, figsize=(10, 3.8))
wl = s["wavelength_m"] * 1e9
ax[0].plot(raw[:, 0], raw[:, 1] / raw[:, 1].max(), ".", ms=2, color="0.6", label="digitised file (raw)")
ax[0].plot(wl, s["input_spectrum"] / s["input_spectrum"].max(), "k--", label="engine input (cleaned)")
ax[0].plot(wl, s["output_spectrum"] / s["input_spectrum"].max(), label="after 10 plates")
ax[0].set(xlim=(950, 1110), xlabel="wavelength (nm)", ylabel="spectral density (norm. to input peak)", title="Input spectrum and output")
ax[0].legend(fontsize=8)
w = o["osa_wavelength_m"] * 1e9
ax[1].plot(o["osa_measured_x"] * 1e9, o["osa_measured_y"] / o["osa_measured_y"].max(), color="0.6", label="OSA recording")
ax[1].plot(w, o["osa_simulated_on_measured"] / o["osa_measured_y"].max(), "C3", label="simulation (blurred to 0.5 nm)")
ax[1].set(xlabel="wavelength (nm)", ylabel="spectral density (norm. to recording peak)", title="Comparison with the OSA trace")
ax[1].legend(fontsize=8)
fig.tight_layout()
fig.savefig("ner-measured.png", dpi=110)
