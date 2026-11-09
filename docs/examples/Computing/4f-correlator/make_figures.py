"""Hero figure and animation of the 4f correlator (python make_figures.py [--no-animations])."""
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _figs
import _optics as op
import numpy as np
import correlator as cr

args = _figs.parser().parse_args()
work = op.workdir(__file__)
r = cr.run(work)
n = cr.SETUP.n
th = _figs.style._theme["series"]

f = _figs.fig(11.5, 3.5)
axs = f.subplots(1, 4)
_figs.show(axs[0], r["scene"], "scene (5 letters + clutter)")
for name, (i, j) in cr.PLACES.items():
    c = name.startswith("T")
    axs[0].add_patch(_figs.matplotlib.patches.Rectangle((j - 1, i - 1), cr.SIZE + 1, cr.SIZE + 1, fill=False, ec=th[1] if c else th[0], lw=1.2))
_figs.show(axs[1], np.angle(np.fft.fftshift(r["H"])), "matched filter H (phase)", cmap=_figs.matplotlib.colormaps["twilight"])
_figs.show(axs[2], r["engine"], "engine: correlation intensity")
pe, pn = op.find_peaks(r["engine"], 5), op.find_peaks(r["numpy"], 5)
x = np.arange(5)
axs[3].bar(x - 0.2, [p[2] / pe[0][2] for p in pe], 0.4, color=th[0], label="engine")
axs[3].bar(x + 0.2, [p[2] / pn[0][2] for p in pn], 0.4, color=th[1], label="numpy")
axs[3].set_xticks(x, [f"({p[0]},{p[1]})" for p in pe], rotation=30, fontsize=7); axs[3].set_ylabel("peak / strongest")
axs[3].set_title("five highest peaks"); axs[3].legend()
f.suptitle("4f correlator: the two T copies give the highest peaks (1.00, 0.99); other letters stay below 0.64", x=0.01, ha="left", fontsize=10.5, fontweight="bold")
_figs.save_hero(f, __file__)

if not args.no_animations:
    FX, FY = op.fourier_axes(cr.SETUP)
    rad = np.hypot(FX, FY) / (0.5 / cr.SETUP.dx)
    fractions = [0.03, 0.05, 0.08, 0.12, 0.17, 0.25, 0.35, 0.5, 0.7, 1.0]
    outs, contrast = [], []
    for fr in fractions:
        res = cr.run(work, H=r["H"] * (rad <= fr))
        outs.append(res["engine"])
        pk = op.find_peaks(res["engine"], 3)
        contrast.append((pk[0][2] / max(pk[2][2], 1e-30), pk[0][2]))
    vmax = max(o.max() for o in outs)

    def frame(k):
        fg = _figs.fig(8.4, 2.9)
        a = fg.subplots(1, 3)
        _figs.show(a[0], r["scene"], "scene")
        _figs.show(a[1], outs[k], f"output, Fourier aperture {fractions[k] * 100:.0f} % of Nyquist", vmax=vmax)
        a[2].plot([f_ * 100 for f_ in fractions[:k + 1]], [c[0] for c in contrast[:k + 1]], "o-", color=th[0])
        a[2].set_xlim(0, 105); a[2].set_ylim(0, max(c[0] for c in contrast) * 1.1)
        a[2].set_xlabel("aperture (% of Nyquist)"); a[2].set_ylabel("T peak / 3rd peak"); a[2].set_title("discrimination")
        return fg
    _figs.save_animation(frame, range(len(fractions)), __file__, fps=3)
