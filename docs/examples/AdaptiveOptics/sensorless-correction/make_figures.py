"""Hero figure and animation of the sensorless correction (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import sensorless as ex

args = V.parser().parse_args()
r = ex.run()
b, metric, th = r["bench"], r["metric"], V.series()
HALF = 200e-6


def focus(a):
    cmd = b.zernike_commands(dict(zip(ex.MODES, a)))
    d = b.run(cmd, sensor=False).instrument("cam")
    return V.crop(d["fluence_map_j_m2"], d["x_m"], d["y_m"], HALF)[0]


a0, am, asg = np.zeros(len(ex.MODES)), r["modal"]["result"].x, r["spgd"]["result"].x
F0, Fm, Fs = focus(a0), focus(am), focus(asg)
vmax = max(F0.max(), Fm.max(), Fs.max())
fe = [-HALF * 1e6, HALF * 1e6] * 2
f = V.fig(12.5, 6.2)
ax = f.subplots(2, 3)
for a_, F, t in ((ax[0, 0], F0, "no correction: Strehl %.2f" % r["strehl_start"]), (ax[0, 1], Fm, "modal search: Strehl %.2f" % r["modal"]["result"].history[-1]),
                 (ax[0, 2], Fs, "SPGD: Strehl %.2f" % r["spgd"]["result"].history[-1])):
    V.image(a_, F, t, extent=fe, vmax=vmax, colorbar=False); a_.set_xlabel("x (um)")
ax[0, 0].set_ylabel("y (um)")
mh = np.asarray(r["modal"]["result"].history); sh = np.asarray(r["spgd"]["result"].history)
evals_m = np.linspace(1, r["modal"]["runs"], len(mh))
ax[1, 0].plot(np.arange(1, len(sh) + 1) * 3, sh, color=th[1], label="SPGD (3 runs / iteration)")
ax[1, 0].plot(np.r_[1, evals_m[1:]], mh, "o-", color=th[0], label="modal search (per sweep)")
ax[1, 0].set_xlabel("engine runs (camera exposures)"); ax[1, 0].set_ylabel("Strehl ratio"); ax[1, 0].set_title("cost of the metric"); ax[1, 0].legend(loc="lower right"); ax[1, 0].set_ylim(0, 1.02)
x = np.arange(len(ex.MODES)); w = 0.27
ax[1, 1].bar(x - w, r["truth"], w, color="0.6", label="needed")
ax[1, 1].bar(x, am, w, color=th[0], label="modal")
ax[1, 1].bar(x + w, asg, w, color=th[1], label="SPGD")
ax[1, 1].set_xticks(x, ["Z%d" % j for j in ex.MODES]); ax[1, 1].set_ylabel("correction (waves rms)"); ax[1, 1].set_title("mode coefficients found"); ax[1, 1].legend()
fx = np.linspace(-HALF, HALF, F0.shape[0]) * 1e6
ax[1, 2].plot(fx, F0[:, F0.shape[1] // 2] / vmax, color="0.5", label="before"); ax[1, 2].plot(fx, Fm[:, Fm.shape[1] // 2] / vmax, color=th[0], label="modal"); ax[1, 2].plot(fx, Fs[:, Fs.shape[1] // 2] / vmax, color=th[1], label="SPGD")
ax[1, 2].set_xlabel("x (um)"); ax[1, 2].set_ylabel("fluence (rel.)"); ax[1, 2].set_title("focal line-out"); ax[1, 2].legend()
V.headline(f, "No wavefront sensor: the peak fluence alone recovers six modes (modal %d runs, SPGD %d runs)" % (r["modal"]["runs"], r["spgd"]["runs"]))
V.save_hero(f, __file__)

if not args.no_animations:
    its = r["spgd"]["result"].iterates
    pick = list(range(0, len(its), 8)) + [len(its) - 1]
    frames = [(k, focus(its[k])) for k in pick]

    def frame(i):
        k, F = frames[i]
        fg = V.fig(9.2, 3.3)
        a = fg.subplots(1, 2)
        V.image(a[0], F, "SPGD iteration %d" % (k + 1), extent=fe, vmax=vmax, colorbar=False); a[0].set_xlabel("x (um)")
        a[1].plot(np.arange(1, k + 2), sh[:k + 1], color=th[1]); a[1].set_xlim(0, len(sh)); a[1].set_ylim(0, 1)
        a[1].set_xlabel("iteration"); a[1].set_ylabel("Strehl"); a[1].set_title("peak fluence %.2f of the limit" % sh[k])
        return fg
    V.save_animation(frame, range(len(frames)), __file__, fps=3)
