"""Hero figure and animation of the tiled-aperture array (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import tiled_array as ex
from ultrafast import build as b

args = V.parser().parse_args()
r = ex.run()
th = V.series()
ip, lk = r["inphase"], r["lock"]
HALF = 400e-6
cw = b.cw(ex.LAM, 1.0)
ext = [-HALF * 1e6, HALF * 1e6] * 2
x = ip["x"]
vmax = ip["ref"].max()

f = V.fig(12.5, 7.2)
ax = f.subplots(2, 4)
V.image(ax[0, 0], V.crop(ip["single"], x, x, HALF)[0], "one tile", extent=ext, vmax=vmax, colorbar=False)
V.image(ax[0, 1], V.crop(ip["ref"], x, x, HALF)[0], "7 tiles in phase: %.0f x the peak" % ip["gain"], extent=ext, vmax=vmax, colorbar=False)
V.image(ax[0, 2], V.crop(ip["err"], x, x, HALF)[0], "pistons 0.5 rad rms: %.2f of the peak" % ip["piston_ratio"], extent=ext, vmax=vmax, colorbar=False)
n61 = r["n61"]; x61 = np.asarray(n61["x"])
V.image(ax[0, 3], V.crop(n61["ref"], x61, x61, 150e-6)[0], "61 tiles in phase: %.0f x one tile" % n61["gain"], extent=[-150, 150] * 2, colorbar=False)
for a in ax[0]:
    a.set_xlabel("x (um)")
sig = np.array([d["rms_fs"] for d in r["delay"]])
ax[1, 0].plot(sig, [d["formula"] for d in r["delay"]], "-", color=th[1], label="Gaussian-overlap formula")
ax[1, 0].plot(sig, [d["engine"] for d in r["delay"]], "o", color=th[0], label="engine")
ax[1, 0].set_xlabel("rms delay mismatch (fs), 30 fs pulse"); ax[1, 0].set_ylabel("axis fluence / aligned"); ax[1, 0].set_title("delay mismatch"); ax[1, 0].legend()
h = np.asarray(lk["result"].history)
ax[1, 1].plot(np.arange(1, len(h) + 1), h, color=th[0]); ax[1, 1].axhline(1, color="0.6", ls="--", lw=1)
ax[1, 1].set_xlabel("SPGD iteration"); ax[1, 1].set_ylabel("axis fluence / in phase"); ax[1, 1].set_title("phase locking (3 runs per iteration)")
n128 = lk["ref"].shape[0]
mk = lambda u: ex.focus(ex.array(1, piston_rad=(lk["phi0"] + u).tolist()), cw, n=128, f=1.0)
F0, F1 = mk(np.zeros(7))[0], mk(lk["result"].x)[0]
xs = mk(np.zeros(7))[1]
V.image(ax[1, 2], V.crop(F0, xs, xs, 1e-3)[0], "random pistons: %.2f" % lk["start"], extent=[-1000, 1000] * 2, vmax=lk["ref"].max(), colorbar=False)
V.image(ax[1, 3], V.crop(F1, xs, xs, 1e-3)[0], "locked: %.2f" % h[-1], extent=[-1000, 1000] * 2, vmax=lk["ref"].max(), colorbar=False)
for a in ax[1, 2:]:
    a.set_xlabel("x (um)")
V.headline(f, "Seven (and 61) beams combine coherently: N x the peak, exp(-sigma^2) piston cost, delay cost of the pulse overlap, SPGD locks the pistons")
V.save_hero(f, __file__)

if not args.no_animations:
    its = lk["result"].iterates
    pick = [0, 2, 5, 9, 14, 20, 28, 38, 50, 65, 85, len(its) - 1]
    frames = [(k, mk(its[k])[0]) for k in pick]

    def frame(i):
        k, F = frames[i]
        fg = V.fig(9.2, 3.3)
        a = fg.subplots(1, 2)
        V.image(a[0], V.crop(F, xs, xs, 1e-3)[0], "7 beams, SPGD iteration %d" % (k + 1), extent=[-1000, 1000] * 2, vmax=lk["ref"].max(), colorbar=False); a[0].set_xlabel("x (um)")
        a[1].plot(np.arange(1, k + 2), h[:k + 1], color=th[0]); a[1].set_xlim(0, len(h)); a[1].set_ylim(0, 1.1); a[1].axhline(1, color="0.6", ls="--", lw=1)
        a[1].set_xlabel("iteration"); a[1].set_ylabel("axis fluence / in phase"); a[1].set_title("combined %.2f" % h[k])
        return fg
    V.save_animation(frame, range(len(frames)), __file__, fps=3)
