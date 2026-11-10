"""Hero figure and animation of the temporal shaping (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import temporal_shaping as ex

args = V.parser().parse_args()
r = ex.run()
th = V.series()
fs = 1e15


def tp(ax, d, label, color, norm=None, **kw):
    n = norm if norm is not None else d["p"].max()
    ax.plot(d["t"] * fs, d["p"] / n, color=color, label=label, **kw)


ref = r["reference"]
f = V.fig(12.5, 7.4)
ax = f.subplots(2, 3)
tp(ax[0, 0], ref, "input 30 fs", "0.6", norm=ref["p"].max()); tp(ax[0, 0], r["double"], "AOPDF replica, 150 fs", th[0], norm=ref["p"].max())
ax[0, 0].set_xlim(-300, 300); ax[0, 0].set_title("double pulse"); ax[0, 0].legend()
tp(ax[0, 1], ref, "input", "0.6", norm=ref["p"].max()); tp(ax[0, 1], r["train"], "pixelated shaper, 3 pulses at 120 fs", th[0], norm=ref["p"].max())
ax[0, 1].set_xlim(-300, 300); ax[0, 1].set_title("pulse train from a spectral mask"); ax[0, 1].legend()
tp(ax[0, 2], ref, "input", "0.6", norm=ref["p"].max()); tp(ax[0, 2], r["flat"], "GDD 6000 fs$^2$", th[0], norm=ref["p"].max())
ax[0, 2].set_xlim(-700, 700); ax[0, 2].set_title("spectral phase stretches it: %.0f fs = GDD x bandwidth" % (r["flat"]["sum"]["fwhmS"] * fs)); ax[0, 2].legend()
g = r["gaps"]
ax[1, 0].semilogy(g["t"] * 1e12, np.maximum(g["p"] / g["p"].max(), 1e-8), color=th[0])
dw = 2 * np.pi * ex.C / ex.LAM ** 2 * (r["pix"] / 1e6)
for k in (-1, 1):
    ax[1, 0].axvline(k * 2 * np.pi / dw * 1e12, color=th[1], ls="--", lw=1, label="2$\\pi$ / pixel bandwidth" if k == 1 else None)
ax[1, 0].set_xlim(-8, 8); ax[1, 0].set_ylim(1e-6, 2); ax[1, 0].set_xlabel("time (ps)"); ax[1, 0].set_title("pixel gaps (fill 0.8): side pulses"); ax[1, 0].legend()
for d, lab, c in ((r["broad"], "broadband input", "0.6"), (r["narrowed"], "gain-narrowed", th[1]), (r["restored"], "AOPDF amplitude table", th[0])):
    ax[1, 1].plot(d["lam"] * 1e9, d["s"] / d["s"].max(), color=c, label="%s, %.0f fs (own peak = 1)" % (lab, d["sum"]["fwhmS"] * fs))
ax[1, 1].set_xlim(700, 900); ax[1, 1].set_xlabel("wavelength (nm)"); ax[1, 1].set_title("narrowed spectrum restored"); ax[1, 1].legend(fontsize=7)
L = r["loop"]
pk = [s["run"]["sum"]["peakPowerW"] / L["ref_peak"] for s in L["steps"]]
ax[1, 2].plot(range(len(pk)), pk, "o-", color=th[0]); ax[1, 2].axhline(1, color="0.6", ls="--", lw=1)
ax[1, 2].set_xlabel("modal-search sweep"); ax[1, 2].set_ylabel("peak power / transform limit"); ax[1, 2].set_title("closed loop: GDD %.0f, TOD %.0f found" % tuple(L["steps"][-1]["x"]))
for a in (ax[0, 0], ax[0, 1], ax[0, 2]):
    a.set_xlabel("time (fs)"); a.set_ylabel("power (rel. to input peak)")
V.headline(f, "Temporal shaping with the ao plugin: every pulse is engine output, every number is checked against theory")
V.save_hero(f, __file__)

if not args.no_animations:
    steps = L["steps"]

    def frame(k):
        s = steps[k]["run"]
        fg = V.fig(10.5, 3.3)
        a = fg.subplots(1, 3)
        a[0].plot(ref["t"] * fs, ref["p"] / ref["p"].max(), color="0.6", label="transform limit"); a[0].plot(s["t"] * fs, s["p"] / ref["p"].max(), color=th[0], label="shaped")
        a[0].set_xlim(-300, 600); a[0].set_ylim(0, 1.1); a[0].set_xlabel("time (fs)"); a[0].set_title("sweep %d" % k); a[0].legend(fontsize=7)
        a[1].plot(range(k + 1), pk[:k + 1], "o-", color=th[0]); a[1].set_xlim(-0.3, len(pk)); a[1].set_ylim(0, 1.1); a[1].set_xlabel("sweep"); a[1].set_title("peak %.2f of the limit" % pk[k])
        a[2].plot(range(k + 1), [-q["x"][0] for q in steps[:k + 1]], "o-", color=th[1], label="-GDD (fs$^2$)")
        a[2].axhline(L["chirp"][0], color=th[1], ls=":"); a[2].set_ylim(0, 1100); a[2].set_xlim(-0.3, len(pk)); a[2].set_xlabel("sweep"); a[2].set_title("found GDD %.0f fs$^2$" % steps[k]["x"][0]); a[2].legend(fontsize=7)
        return fg
    V.save_animation(frame, range(len(steps)), __file__, fps=2.5)
