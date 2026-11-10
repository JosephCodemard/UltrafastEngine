"""Hero figure of the aberrated high-power beam (python make_figures.py)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import high_power as ex

args = V.parser().parse_args()
r = ex.run()
b = r["bench"]
th = V.series()
HALF = 2.5e-3
f = V.fig(12.5, 6.2)
ax = f.subplots(2, 4)
vm = max(np.max(r[k]["before"]["fluence_map_j_m2"]) for k in ("aberrated", "corrected"))
fm = max(np.max(r[k]["focus_hi"]["fluence_map_j_m2"]) for k in ("aberrated", "corrected"))
bm = max(ex.metrics(r[k], b)["b_peak"] for k in ("aberrated", "corrected"))
for row, k in enumerate(("aberrated", "corrected")):
    c = r[k]
    m = ex.metrics(c, b)
    d = c["before"]
    F, x, y = V.crop(d["fluence_map_j_m2"], d["x_m"], d["y_m"], HALF)
    V.image(ax[row, 0], F, "%s: plate fluence" % k, extent=V.extent_mm(x), vmax=vm, label="J/m$^2$")
    w = c["wfs_hi"]
    X, Y = np.meshgrid(w["x_m"], w["y_m"], indexing="ij")
    Bm = np.where(np.hypot(X, Y) <= 1.5e-3, c["b_map_rad"], np.nan)
    V.image(ax[row, 1], Bm, "nonlinear phase, peak %.2f rad" % m["b_peak"], extent=V.extent_mm(np.asarray(w["x_m"]), np.asarray(w["y_m"])), cmap="magma", vmin=0, vmax=bm, label="rad")
    d = c["focus_hi"]
    F, x, y = V.crop(d["fluence_map_j_m2"], d["x_m"], d["y_m"], 0.8e-3)
    V.image(ax[row, 2], F, "focus, peak %.0f J/m$^2$" % m["focus_peak_hi"], extent=V.extent_mm(x), vmax=fm, label="J/m$^2$")
    d0 = c["focus_lo"]
    F0, _, _ = V.crop(d0["fluence_map_j_m2"], d0["x_m"], d0["y_m"], 0.8e-3)
    V.image(ax[row, 3], F0 * 1e4, "focus without Kerr (scaled)", extent=V.extent_mm(x), vmax=fm, label="J/m$^2$")
    for a in ax[row]:
        a.set_xlabel("x (mm)")
V.headline(f, "0.2 mJ, 100 fs, astigmatism + coma: the DM removes the hot spots (peak fluence %.0f to %.0f J/m$^2$) and the nonlinear phase (%.1f to %.1f rad)"
           % (ex.metrics(r["aberrated"], b)["peak_fluence_before_plate"], ex.metrics(r["corrected"], b)["peak_fluence_before_plate"], ex.metrics(r["aberrated"], b)["b_peak"], ex.metrics(r["corrected"], b)["b_peak"]))
V.save_hero(f, __file__)
