"""Hero figure and animation of exploring the twin (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import explore as ex
from ultrafast_twin import synthetic

args = V.parser().parse_args()
r = ex.run()
th = V.series()
w = r["whatif"]
f = V.fig(12.5, 7.0)
ax = f.subplots(2, 3)
for (label, v), c in zip(w["variants"].items(), (th[0], th[1], th[2])):
    x = np.asarray(v["x"]) * 1e9
    ax[0, 0].fill_between(x, v["y"] - 2 * v["y_sigma"], v["y"] + 2 * v["y_sigma"], color=c, alpha=0.25); ax[0, 0].plot(x, v["y"], color=c, label=label)
ax[0, 0].set_xlim(960, 1110); ax[0, 0].set_xlabel("wavelength (nm)"); ax[0, 0].set_title("what-if: spectra at three energies (2$\\sigma$ bands)"); ax[0, 0].legend(fontsize=7)
d = r["design"]
names = [c["name"].replace("energyJ=", "") for c in d["ranking"]]
ax[0, 1].barh(range(len(names)), [c["gain"] for c in d["ranking"]], color=th[0]); ax[0, 1].set_yticks(range(len(names)), [("%.1f uJ" % (float(n) * 1e6)) for n in names])
ax[0, 1].invert_yaxis(); ax[0, 1].set_xlabel("expected information gain (nats)"); ax[0, 1].set_title("next shot: most informative energy")
p = r["placement"]["ranking"]
ax[0, 2].barh(range(len(p)), [c["gain"] for c in p], color=th[1]); ax[0, 2].set_yticks(range(len(p)), [c["name"] for c in p]); ax[0, 2].invert_yaxis()
ax[0, 2].set_xlabel("expected information gain (nats)"); ax[0, 2].set_title("where to put the spectrometer")
h = r["tracker"].history
days = np.arange(len(h) - 1)
mu = np.array([s["mean"]["filter.centerNm"] for s in h[1:]]); sg = np.array([s["sigma"]["filter.centerNm"] for s in h[1:]])
truth = synthetic.CENTRE_NM + np.array(ex.DRIFT_NM)
ax[1, 0].fill_between(days, mu - 3 * sg, mu + 3 * sg, color=th[0], alpha=0.25, label="tracker $\\pm 3\\sigma$"); ax[1, 0].plot(days, mu, "o-", color=th[0], label="tracker")
ax[1, 0].plot(days, truth, "s", mfc="none", color=th[1], label="hidden truth")
fit0 = h[0]["mean"]["filter.centerNm"]; ax[1, 0].axhline(fit0, color="0.5", ls="--", lw=1, label="fitted before drift")
ax[1, 0].set_xlabel("measurement day"); ax[1, 0].set_ylabel("filter centre (nm)"); ax[1, 0].set_title("drift tracking (ensemble Kalman filter)"); ax[1, 0].legend(fontsize=7)
ax[1, 1].bar(days, [abs(s["zFromFit"]["filter.centerNm"]) for s in h[1:]], color=[th[1] if s["drifted"] else th[0] for s in h[1:]]); ax[1, 1].axhline(3, color="0.4", ls="--", lw=1)
ax[1, 1].set_xlabel("measurement day"); ax[1, 1].set_ylabel("|shift| in fitted sigmas"); ax[1, 1].set_title("drift alarm at 3 sigma")
sc = w["scalars"]
ax[1, 2].errorbar([0, 1, 2], [sc[k]["spectralFwhmM"]["value"] * 1e9 for k in sc], yerr=[2 * sc[k]["spectralFwhmM"]["sigma"] * 1e9 for k in sc], fmt="o", color=th[0])
ax[1, 2].set_xticks([0, 1, 2], list(sc)); ax[1, 2].set_ylabel("output spectral FWHM (nm)"); ax[1, 2].set_title("predicted bandwidth, 2$\\sigma$")
V.headline(f, "Explore the twin without beam time: compare settings, choose the next shot, follow a drifting filter")
V.save_hero(f, __file__)

if not args.no_animations:
    import numpy as np
    twin = r["twin"]
    xs = []
    for k in range(len(days)):
        dat = np.loadtxt(ex.HERE / "results" / ("day%d.csv" % k), delimiter=",", skiprows=1)
        vec = twin.values.copy(); vec[twin.names().index("filter.centerNm")] = mu[k]
        res = twin.simulate(vec, {"energyJ": 1.2e-6}, {"pulse/energyJ": 1.2e-6})
        xs.append((dat, res["spec_wavelength_m"] * 1e9, res["spec_spectrum"] / res["spec_spectrum"].max()))

    def frame(k):
        fg = V.fig(9.0, 3.3)
        a = fg.subplots(1, 2)
        dat, x, y = xs[k]
        a[0].plot(dat[:, 0], dat[:, 1] / dat[:, 1].max(), ".", ms=2.5, color="0.4", label="measured day %d" % k); a[0].plot(x, y, color=th[0], label="twin, tracked centre"); a[0].set_xlim(1000, 1070); a[0].set_xlabel("wavelength (nm)"); a[0].legend(fontsize=7)
        a[1].fill_between(days[:k + 1], (mu - 3 * sg)[:k + 1], (mu + 3 * sg)[:k + 1], color=th[0], alpha=0.25); a[1].plot(days[:k + 1], mu[:k + 1], "o-", color=th[0]); a[1].plot(days[:k + 1], truth[:k + 1], "s", mfc="none", color=th[1])
        a[1].set_xlim(-0.3, len(days)); a[1].set_ylim(fit0 - 0.3, fit0 + 0.9); a[1].set_xlabel("day"); a[1].set_ylabel("centre (nm)"); a[1].set_title("%s" % ("DRIFT FLAGGED" if h[k + 1]["drifted"] else "stable"))
        return fg
    V.save_animation(frame, range(len(days)), __file__, fps=1.5)
