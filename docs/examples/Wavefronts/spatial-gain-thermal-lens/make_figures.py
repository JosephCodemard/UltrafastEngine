"""Run the example and draw its hero figure: input and output beam, wavefront and focus, with and without the thermal lens
(python make_figures.py)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import _figures
import numpy as np
from ultrafast.plotting import zernike as zp

folder = Path(__file__).resolve().parent
args = V.parser().parse_args()
_figures.run_readme(folder)
runs = {"60 W pump": np.load(folder / "results-60W" / "result.npz"), "5 W pump": np.load(folder / "results-5W" / "result.npz")}
th = V.series()
f = V.fig(12.5, 7.0)
ax = f.subplots(2, 4)
for row, (lab, r) in enumerate(runs.items()):
    x = r["nf_rod_x_m"]
    F = r["nf_rod_fluence_j_m2"]
    V.image(ax[row, 0], F, "%s: beam in rod" % lab, extent=V.extent_mm(x), label="J/m$^2$")
    W = r["wfs_rod_wavefront_waves"]
    V.image(ax[row, 1], W - np.nanmean(W), "wavefront after the rod (waves)", extent=V.extent_mm(r["wfs_rod_x_m"]), cmap="RdBu_r", symmetric=True, label="waves")
    V.image(ax[row, 2], r["nf_400mm_fluence_j_m2"], "beam 0.4 m behind the rod", extent=V.extent_mm(r["nf_400mm_x_m"]), label="J/m$^2$")
    z = r["wfs_rod_zernike_waves"]; j = r["wfs_rod_zernike_index"]
    ax[row, 3].bar(range(len(j)), z, color=[th[0] if int(k) == 4 else "0.6" for k in j]); ax[row, 3].set_xticks(range(len(j)), [str(int(k)) for k in j], fontsize=6)
    ax[row, 3].set_xlabel("Noll index (4 = defocus)"); ax[row, 3].set_ylabel("waves rms"); ax[row, 3].set_title("Zernike content")
    for a in ax[row, :3]:
        a.set_xlabel("x (mm)")
V.headline(f, "End-pumped Yb:YAG rod: the heat load writes a thermal lens (defocus) into the wavefront; the beam 0.4 m behind converges at 60 W")
V.save_hero(f, __file__)
