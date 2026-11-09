"""Figure for example 11: space-time intensity after the plate, and the beam after the plate and the free space.

Run after the two `ultrafast run` commands of the README, then `python plot.py`.
"""
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(path):
    return np.genfromtxt(path, delimiter=",", names=True)


r = load("results-run/space_time_plate.csv")["space_time_plate_position_m"] * 1e6
t = load("results-run/space_time_plate_2.csv")["space_time_plate_time_s"] * 1e15
inten = np.loadtxt("results-run/space_time_plate_intensity_w_m2.csv", delimiter=",")  # rows: radius, columns: time
fig, ax = plt.subplots(1, 2, figsize=(10, 3.8))
im = ax[0].pcolormesh(t, r, inten / 1e15, shading="auto", cmap="viridis")
fig.colorbar(im, ax=ax[0], label="intensity (PW/m$^2$)")
ax[0].set(xlim=(-600, 600), xlabel="time (fs)", ylabel="radius (um)", title="I(r, t) after the plate (4 uJ)")
for name, ls in (("after_plate", "-"), ("after_gap", "--")):
    for folder, label, c in (("results-run-linear", "1 nJ", "0.5"), ("results-run", "4 uJ", "C3")):
        d = load(f"{folder}/{name}.csv")
        rad, flu = d[f"{name}_radius_m"] * 1e6, d[f"{name}_fluence_j_m2"]
        ax[1].plot(rad, flu / flu.max(), ls, color=c, label=f"{label}, {'plate exit' if name == 'after_plate' else '+40 mm'}")
ax[1].set(xlim=(0, 450), xlabel="radius (um)", ylabel="fluence (norm. to each peak)", title="Beam profile")
ax[1].legend(fontsize=8)
fig.tight_layout()
fig.savefig("spatial-kerr.png", dpi=110)
