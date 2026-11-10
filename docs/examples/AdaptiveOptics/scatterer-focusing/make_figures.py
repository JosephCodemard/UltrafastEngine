"""Hero figure and animation of the focusing through a scatterer (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import scatterer as ex

args = V.parser().parse_args()
r = ex.run()
th = V.series()
e = r["enhancement"]
crop = lambda F: F[16:49, 16:49]
vmax = max(F[tuple(ex.OUT[m])] for m, F in enumerate(r["foc"]))
m0 = int(np.argmax(e))
f = V.fig(12.5, 6.4)
ax = f.subplots(2, 3)
V.image(ax[0, 0], np.mod(r["diffuser"], 2 * np.pi), "the scatterer: random phase per pixel", cmap="twilight", vmin=0, vmax=2 * np.pi, colorbar=False, transpose=False)
V.image(ax[0, 1], crop(r["unfocused"]) / vmax, "camera, flat input: speckle", vmax=1, colorbar=False, transpose=False)
V.image(ax[0, 2], crop(r["foc"][m0]) / vmax, "camera, input set from the matrix", vmax=1, colorbar=False, transpose=False)
for a in ax[0, 1:]:
    a.plot([j - 16 for i, j in ex.OUT], [i - 16 for i, j in ex.OUT], "+", color="0.4", ms=4)
ax[1, 0].bar(np.arange(len(e)), e, color=th[0]); ax[1, 0].axhline(r["theory"], color=th[1], ls="--", label="theory %.0f" % r["theory"])
ax[1, 0].set_xlabel("output pixel"); ax[1, 0].set_ylabel("intensity / mean speckle"); ax[1, 0].set_title("enhancement: mean %.0f" % e.mean()); ax[1, 0].legend()
V.image(ax[1, 1], np.angle(r["H"][:, 1:]), "measured phase of t$_{mk}$ (16 outputs x 63 inputs)", cmap="twilight", vmin=-np.pi, vmax=np.pi, colorbar=False, transpose=False)
ax[1, 1].set_aspect("auto"); ax[1, 1].set_xlabel("input segment"); ax[1, 1].set_ylabel("output pixel")
ax[1, 2].hist(np.concatenate([r["random"].ravel() / r["random"].mean(), np.zeros(0)]), bins=20, color="0.7", density=True)
xs = np.linspace(0, 5, 200); ax[1, 2].plot(xs, np.exp(-xs), color=th[1], label="exp(-I) (fully developed speckle)")
ax[1, 2].set_xlabel("speckle intensity / mean"); ax[1, 2].set_title("random input: speckle statistics"); ax[1, 2].legend()
V.headline(f, "Wavefront shaping through a scatterer: 254 intensity measurements give the matrix, then a %.0fx brighter focus" % e.mean())
V.save_hero(f, __file__)

if not args.no_animations:
    def frame(k):
        fg = V.fig(7.2, 3.3)
        a = fg.subplots(1, 2)
        V.image(a[0], crop(r["foc"][k]) / vmax, "focus on output pixel %d" % k, vmax=1, colorbar=False, transpose=False)
        a[0].plot([ex.OUT[k][1] - 16], [ex.OUT[k][0] - 16], "o", mfc="none", mec="0.3", ms=11)
        a[1].bar(range(len(e)), np.where(np.arange(len(e)) <= k, e, 0), color=th[0]); a[1].set_ylim(0, e.max() * 1.1); a[1].axhline(r["theory"], color=th[1], ls="--", lw=1)
        a[1].set_xlabel("output pixel"); a[1].set_title("enhancement %.0fx" % e[k])
        return fg
    V.save_animation(frame, range(len(e)), __file__, fps=4)
