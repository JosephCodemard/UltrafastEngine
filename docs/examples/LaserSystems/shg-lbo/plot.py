"""Figure for example 08: fundamental and second-harmonic pulse, and the angular phase-matching curve.

Run after `ultrafast run shg.ufs run.json -o results`, then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(name):
    return np.genfromtxt(f"results/{name}.csv", delimiter=",", names=True)


t, pm = load("temporal"), load("phaseMatchingCurve_shg")
fig, ax = plt.subplots(1, 2, figsize=(10, 3.8))
ti = load("temporal_input")
ax[0].plot(ti["time_s"] * 1e12, ti["input_power_w"] / 1e6, "k--", label="input, 1030 nm")
ax[0].plot(t["time_s"] * 1e12, t["output_power_w"] / 1e6, label="output, 515 nm (second harmonic)")
ax[0].set(xlim=(-3, 3), xlabel="time (ps)", ylabel="power (MW)", title="Pulse before and after the crystal")
ax[0].legend(loc="upper left")
ax[1].plot(pm["phaseMatchingCurve_shg_detuning_deg"], pm["phaseMatchingCurve_shg_efficiency_norm"])
ax[1].set(xlabel="crystal angle detuning (deg)", ylabel="relative SHG efficiency", title="Angular phase-matching curve (2 mm LBO)")
fig.tight_layout()
fig.savefig("shg-lbo.png", dpi=110)
