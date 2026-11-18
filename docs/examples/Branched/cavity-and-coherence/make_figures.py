"""Run this example and write its hero figure (python make_figures.py): the Fox-Li mode against the ABCD eigenmode, the Gaussian-Schell beam against w(z).

Runs the README's commands (as ``build/example_check.py`` does), then draws three panels with the shared look of ``ultrafast.plotting`` styles:
the mode radius of the resonator by three routes, the fluence profile on the screen after one Rayleigh range for M^2 = 2.5 (engine and
analytic, with the coherent beam for comparison), and w(z) of both beams with the engine's two readings.
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _figures
import _viz as V
import numpy as np

args = _figures.parser().parse_args()
here = Path(__file__).resolve().parent
_figures.run_readme(here)

LAM, F, L, W0, M2 = 1030e-9, 0.5, 0.6, 0.5e-3, 2.5
zr = math.pi * W0 ** 2 / LAM
cav = json.load(open(here / "results-cavity" / "summary.json"))["elements"][0]["values"]
w_abcd = math.sqrt(LAM / math.pi * math.sqrt(L * (4 * F - L)) / 2)
m2s = json.load(open(here / "results-m2" / "summary.json"))
w_el = {e["id"]: e["beam"]["radiusXM"] for e in m2s["elements"]}
z = np.load(here / "results-m2" / "result.npz")
x, flu = z["screen_x_m"], z["screen_fluence_x_j_m2"]
flu = flu / flu.max()

th = V.series()
f = V.fig(11.5, 3.6)
a0, a1, a2 = f.subplots(1, 3)

names = ["analytic\nABCD", "tracker\n(ABCD)", "Fox-Li\n(wave)"]
vals = [w_abcd, cav["modeRadiusXM"], cav["foxLiRadiusXM"]]
a0.bar(range(3), np.array(vals) * 1e3, color=[th[2], th[0], th[1]], width=0.6)
a0.set_ylim(0.380, 0.392)
a0.set_xticks(range(3), names)
a0.set_ylabel("mode radius (mm, axis from 0.380)")
a0.set_title("resonator eigenmode")
for i, v in enumerate(vals):
    a0.text(i, v * 1e3 + 0.0003, "%.4f" % (v * 1e3), ha="center", fontsize=8)
a0.text(0.5, 0.92, "round-trip loss %.3f %% (aperture %.1f mode radii)" % (cav["roundTripLoss"] * 100, 1.1e-3 / w_abcd), transform=a0.transAxes, ha="center", fontsize=8)

w_gs = W0 * math.sqrt(1 + M2 ** 2)
xm = x * 1e3
a1.plot(xm, flu, color=th[0], lw=2.2, label="engine, M² = 2.5 (11 modes)")
a1.plot(xm, np.exp(-2 * x ** 2 / w_gs ** 2), "--", color=th[1], lw=1.4, label="Gaussian-Schell, w = %.3f mm" % (w_gs * 1e3))
a1.plot(xm, np.exp(-2 * x ** 2 / (W0 * math.sqrt(2)) ** 2), ":", color=th[2], lw=1.4, label="coherent, w = %.3f mm" % (W0 * math.sqrt(2) * 1e3))
a1.set_xlim(-4, 4)
a1.set_xlabel("x (mm)")
a1.set_ylabel("fluence (peak = 1)")
a1.set_title("screen at z = z_R")
a1.legend(fontsize=7.5, loc="upper right")

zz = np.linspace(0, 1.5 * zr, 200)
a2.plot(zz * 1e3, W0 * np.sqrt(1 + (M2 * zz / zr) ** 2) * 1e3, color=th[1], label="M² = 2.5")
a2.plot(zz * 1e3, W0 * np.sqrt(1 + (zz / zr) ** 2) * 1e3, color=th[2], ls=":", label="M² = 1")
a2.plot([0, zr * 1e3], [w_el["waist"] * 1e3, w_el["screen"] * 1e3], "o", color=th[0], ms=7, label="engine (beamProfile)")
a2.set_xlabel("z (mm)")
a2.set_ylabel("beam radius w (mm)")
a2.set_title("w(z) = w₀ √(1 + (M² z / z_R)²)")
a2.legend(fontsize=7.5, loc="upper left")
f.suptitle("Fox-Li and ABCD agree to 3·10⁻⁴; the Gaussian-Schell beam is 1.9 times larger after one Rayleigh range", x=0.01, ha="left", fontsize=10.5, fontweight="bold")
V.save_hero(f, __file__)
