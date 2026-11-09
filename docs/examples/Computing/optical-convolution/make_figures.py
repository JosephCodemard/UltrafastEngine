"""Hero figure and animation of the optical convolution example (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _figs
import _optics as op
import numpy as np
import convolution as cv

args = _figs.parser().parse_args()
work = op.workdir(__file__)
th = _figs.style._theme["series"]
kernels = ("blur", "edges", "motion")
runs = {k: cv.run_kernel(work, cv.transfer_function(k)) for k in kernels}
scene = cv.make_scene()

f = _figs.fig(10.5, 8.2)
axs = f.subplots(3, 4)
for row, k in enumerate(kernels):
    r = dict(runs[k])
    m = op.match(r["engine"], r["numpy"])
    r["engine"] = r["engine"] * r["numpy"].sum() / r["engine"].sum()          # the engine reports J/m^2: same total as numpy for display
    vmax = max(r["engine"].max(), r["numpy"].max())
    _figs.show(axs[row, 0], scene, "scene" if row == 0 else "")
    _figs.show(axs[row, 1], r["engine"], f"engine: {k}" if row == 0 else f"{k}", vmax=vmax)
    _figs.show(axs[row, 2], r["numpy"], "numpy (convolution theorem)" if row == 0 else "", vmax=vmax)
    _figs.show(axs[row, 3], (r["engine"] - r["numpy"]) / vmax, "difference / peak" if row == 0 else "", signed=True, vmax=0.05)
    axs[row, 3].set_xlabel(f"relative l2 error {m['relativeL2']:.4f}", fontsize=8)
f.suptitle("A 4f system convolves in one pass: the kernel is the Fourier-plane filter (difference scale +-5 % of the peak)",
           x=0.01, ha="left", fontsize=10, fontweight="bold")
_figs.save_hero(f, __file__, dpi=80)

if not args.no_animations:
    sigmas = np.concatenate([[0.0], np.geomspace(10e-6, 160e-6, 11)])
    outs = []
    for s_ in sigmas:
        H = cv.transfer_function("blur", sigma_m=max(s_, 1e-9))
        outs.append(cv.run_kernel(work, H)["engine"])
    vmax = outs[0].max()

    def frame(k):
        fg = _figs.fig(7.2, 3.1)
        a = fg.subplots(1, 3, width_ratios=[1, 1, 0.9])
        _figs.show(a[0], runs["blur"]["scene"], "scene")
        _figs.show(a[1], outs[k], f"output, kernel rms width {sigmas[k] * 1e6:.0f} um", vmax=vmax)
        a[2].plot(sigmas[:k + 1] * 1e6, [o.max() / vmax for o in outs[:k + 1]], "o-", color=th[0])
        a[2].set_xlim(0, 165); a[2].set_ylim(0, 1.05); a[2].set_xlabel("kernel width (um)"); a[2].set_ylabel("peak / unblurred peak")
        a[2].set_title("peak intensity")
        return fg
    _figs.save_animation(frame, range(len(sigmas)), __file__, fps=3)
