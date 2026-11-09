"""Figure for example 09: signal spectrum before and after the NOPCPA stage, gain bandwidth and delay scan.

Run example 08, then `ultrafast run nopcpa.ufs run.json -o results`, then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(name):
    return np.genfromtxt(f"results/{name}.csv", delimiter=",", names=True)


si, so = load("spectrum_input"), load("spectrum")
gb, ds = load("gain_bandwidth"), load("delayScan_nopa")
fig, ax = plt.subplots(1, 3, figsize=(13, 3.8))
ax[0].plot(si["wavelength_m"] * 1e9, si["input_spectrum"] / si["input_spectrum"].max(), "k--", label="seed")
ax[0].plot(so["wavelength_m"] * 1e9, so["output_spectrum"] / so["output_spectrum"].max(), label="amplified signal")
ax[0].set(xlim=(650, 1000), xlabel="wavelength (nm)", ylabel="spectral density (each norm. to its peak)", title="Signal spectrum")
ax[0].legend()
ax[1].plot(gb["gain_bandwidth_wavelength_m"] * 1e9, gb["gain_bandwidth_efficiency_norm"])
ax[1].set(xlabel="signal wavelength (nm)", ylabel="relative low-gain efficiency", title="Phase-matching (gain) bandwidth")
ax[2].plot(ds["delayScan_nopa_delay_s"] * 1e12, ds["delayScan_nopa_output_energy_j"] * 1e6, "o-")
ax[2].set(xlabel="pump delay (ps)", ylabel="signal energy (uJ)", title="Pump-signal delay scan")
fig.tight_layout()
fig.savefig("nopcpa-bbo.png", dpi=110)
