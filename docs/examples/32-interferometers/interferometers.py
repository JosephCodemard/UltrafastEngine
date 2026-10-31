"""A Mach-Zehnder and an etalon as Branched systems, written with the Python API and checked against their analytic results."""
import math

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

import ultrafast as uf
from ultrafast import build as b

BLUE, ORANGE = "#2a78d6", "#eb6834"
C, LAM = 299792458.0, 1030e-9
TAU = 300e-15

# Mach-Zehnder: one arm 300 fs longer. Port c holds (1 - cos(w tau)) / 2 of each frequency
mz = b.system("mach-zehnder", b.Branched([
    b.link(b.beam_splitter("bs1"), {"c": "armA", "d": "armB"}),
    b.link(b.delay("armA", delay_s=0.0), "bs2:1"),
    b.link(b.delay("armB", delay_s=TAU), "bs2:2"),
    b.link(b.beam_splitter("bs2"), {"c": "end", "d": "virtual"}),
]))
res = uf.run(mz, b.run_spec(b.pulse(LAM, 100e-15, energy=1e-9), grid=b.grid(n=8192, window_s=20e-12)))
graph = res.summary["graph"]
w = res["output_omega_rad_s"]
s_in, s_out = res["input_spectrum"], res["output_spectrum"]
fringes = (1 - np.cos(w * TAU)) / 2
band = s_in > 1e-3 * s_in.max()
print("Mach-Zehnder     output / input %.4f   analytic %.4f" % (s_out.sum() / s_in.sum(), (s_in * fringes).sum() / s_in.sum()))
print("spectral fringes largest error  %.1e of the peak" % (np.abs(s_out - s_in * fringes)[band].max() / s_in.max()))
print("budget closure   %.1e J" % graph["energyClosureJ"])

# etalon: two mirrors of R = 0.8 joined by a loop, and a cw source. Scanning a path over one wave passes one Airy peak
R, GAP = 0.8, 0.01
etalon = b.system("etalon", b.Branched([
    b.link(b.beam_splitter("m1", R), {"c": "forth", "d": "virtual"}, loop=b.loop("ringDown", max_round_trips=2000, tolerance=1e-16)),
    b.link(b.gap("forth", GAP, medium="Vacuum"), "m2:2"),
    b.link(b.beam_splitter("m2", R), {"c": "back", "d": "end"}),
    b.link(b.gap("back", GAP, medium="Vacuum"), "m1:2"),
]))
offsets = np.linspace(-LAM / 2, LAM / 2, 81)
scan = uf.study(etalon, b.run_spec(b.cw(LAM, 1.0)), parameters=[{"path": "element:back/lengthM", "values": list(GAP + offsets), "nominal": GAP}], outputs=["energy:end"])
transmission = np.array([row["energy:end"] for row in scan["rows"]])
nominal = uf.run(etalon, b.run_spec(b.cw(LAM, 1.0))).summary
transmission /= nominal["graph"]["energyInJ"]
delta = nominal["outputGrid"]["omega0"] * (2 * GAP + offsets) / C      # the grid's carrier, which is the source's frequency in the run
airy = (1 - R) ** 2 / (1 + R * R - 2 * R * np.cos(delta))
print("etalon           peak %.4f   Airy %.4f, largest error %.1e" % (transmission.max(), airy.max(), np.abs(transmission - airy).max()))

fig, (left, right) = plt.subplots(1, 2, figsize=(10, 3.6), constrained_layout=True)
lam_nm = 2e9 * math.pi * C / w[band]
left.plot(lam_nm, s_in[band] / s_in.max(), color=BLUE, lw=1.5, label="input")
left.plot(lam_nm, s_out[band] / s_in.max(), color=ORANGE, lw=1.5, label="port c")
left.set(xlabel="wavelength (nm)", ylabel="spectrum (peak of input = 1)", title="Mach-Zehnder, 300 fs path difference")
left.legend(frameon=False)
right.plot(offsets * 1e9, airy, color=BLUE, lw=1.5, label="Airy function")
right.plot(offsets * 1e9, transmission, "o", color=ORANGE, ms=4, label="graph loop")
right.set(xlabel="change of one path (nm)", ylabel="transmission", title="Etalon, R = 0.8")
right.legend(frameon=False)
for ax in (left, right):
    ax.spines[["top", "right"]].set_visible(False)
fig.savefig("interferometers.png", dpi=130)
