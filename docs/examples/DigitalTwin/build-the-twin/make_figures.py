"""Hero figure and animation of the twin build (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import build_twin as ex
from ultrafast_twin import synthetic

args = V.parser().parse_args()
r = ex.run()
th = V.series()
L, S = r["linear"], r["saturating"]
energies = np.linspace(0.2, 3.4, 17)


def curve(twin, e):
    p = twin.predict(variables={"energyJ": e * 1e-6}, set={"pulse/energyJ": e * 1e-6}, n_samples=8)
    return p["x"], p["y"], p["y_sigma"]


cv = [curve(L["twin"], e) for e in energies]
lam = cv[0][0] * 1e9
H = np.array([c[1] for c in cv])
f = V.fig(12.5, 7.2)
ax = f.subplots(2, 3)
V.heat(ax[0, 0], H, lam, energies, "twin: spectrum vs pulse energy", xlabel="wavelength (nm)", ylabel="energy (uJ)", label="spectrum (peak-normalised)")
for e in synthetic.TRAIN_UJ:
    ax[0, 0].plot([lam[0]] * 1, [e], ">", color="k", ms=4)
for e in synthetic.HELD_UJ:
    ax[0, 0].plot([lam[0]] * 1, [e], ">", color="#e15759", ms=5)
ax[0, 0].set_xlim(lam[0], lam[-1])
ax[0, 0].text(0.02, 0.97, "> training   > held out", transform=ax[0, 0].transAxes, fontsize=7, va="top")


def held_panel(a, lab, name, title):
    pr = lab["pred"][name]
    x = pr["x"] * 1e9
    a.fill_between(x, pr["y"] - 2 * pr["y_sigma"], pr["y"] + 2 * pr["y_sigma"], color=th[0], alpha=0.25, label="2$\\sigma$ band")
    a.plot(x, pr["measured"], ".", ms=2.5, color="0.35", label="held-out data"); a.plot(x, pr["y"], color=th[0], lw=1.2, label="twin")
    a.set_xlabel("wavelength (nm)"); a.set_title(title); a.legend(fontsize=7, loc="upper left")


held_panel(ax[0, 1], L, "E3.0uJ", "held-out 3.0 uJ: predicted (ok)")
held_panel(ax[1, 0], S, "E3.0uJ", "saturating lab, held-out 3.0 uJ: flagged")
slope = next(p for p in L["twin"].params if p["name"].startswith("kerr.bRad") and p.get("origin") == "dependence")
ee = np.linspace(0, 3.4, 50)
ax[0, 2].plot(ee, synthetic.SLOPE_RAD_PER_UJ * ee, color="0.4", label="hidden truth (linear lab)")
ax[0, 2].plot(ee, [synthetic.true_b(e, True) for e in ee], color="0.4", ls=":", label="hidden truth (saturating lab)")
ax[0, 2].plot(ee, slope["value"] * ee, "--", color=th[0], label="fitted: %.4f $\\pm$ %.4f rad/uJ" % (slope["value"], slope["sigma"]))
ax[0, 2].axvspan(0.4, 2.0, color=th[0], alpha=0.1); ax[0, 2].set_xlabel("pulse energy (uJ)"); ax[0, 2].set_ylabel("nonlinear phase (rad)"); ax[0, 2].set_title("learned energy law (shaded: training)"); ax[0, 2].legend(fontsize=7)
names = [c["name"] for c in L["report"]["conditions"]]
w = 0.38
for k, (lab, col) in enumerate((("linear", th[0]), ("saturating", th[1]))):
    ax[1, 1].bar(np.arange(len(names)) + (k - 0.5) * w, [max(c["reducedChi2"], 1e-2) for c in r[lab]["report"]["conditions"]], w, color=col, label=lab + " lab")
ax[1, 1].set_yscale("log"); ax[1, 1].axhline(1, color="0.5", ls="--", lw=1); ax[1, 1].set_xticks(np.arange(len(names)), [n[1:-2] for n in names], rotation=45, fontsize=7)
ax[1, 1].set_xlabel("condition (uJ); the last two are held out"); ax[1, 1].set_ylabel("chi$^2$ per point"); ax[1, 1].set_title("validation report"); ax[1, 1].legend(fontsize=7)
cov = [[c["coverage2sigma"] * 100 for c in r[k]["report"]["conditions"]] for k in ("linear", "saturating")]
ax[1, 2].bar(np.arange(len(names)) - w / 2, cov[0], w, color=th[0], label="linear"); ax[1, 2].bar(np.arange(len(names)) + w / 2, cov[1], w, color=th[1], label="saturating")
ax[1, 2].axhline(80, color="0.4", ls="--", lw=1); ax[1, 2].set_xticks(np.arange(len(names)), [n[1:-2] for n in names], rotation=45, fontsize=7)
ax[1, 2].set_ylabel("points inside the 2$\\sigma$ band (%)"); ax[1, 2].set_title("band coverage (flag below 80 %)"); ax[1, 2].legend(fontsize=7)
V.headline(f, "Digital twin: five energies, one set of shared parameters, held-out predictions, and a failing twin caught")
V.save_hero(f, __file__)

if not args.no_animations:
    meas = {e: np.loadtxt(ex.HERE / "results" / "linear" / "data" / ("E%.1fuJ.csv" % e), delimiter=",", skiprows=1) for e in synthetic.TRAIN_UJ + synthetic.HELD_UJ}

    def frame(k):
        e = energies[k]
        fg = V.fig(7.6, 3.4)
        a = fg.subplots(1, 2)
        x, y, s = cv[k]
        a[0].fill_between(x * 1e9, y - 2 * s, y + 2 * s, color=th[0], alpha=0.25); a[0].plot(x * 1e9, y, color=th[0])
        for m, d in meas.items():
            if abs(m - e) < 0.12:
                a[0].plot(d[:, 0], d[:, 1] / d[:, 1].max(), ".", ms=2, color="#e15759" if m in synthetic.HELD_UJ else "0.35")
        a[0].set_ylim(0, 1.15); a[0].set_xlabel("wavelength (nm)"); a[0].set_title("twin at %.2f uJ (dots: data within 0.12 uJ)" % e)
        a[1].plot(ee, slope["value"] * ee, color=th[0]); a[1].plot([e], [slope["value"] * e], "o", color=th[1]); a[1].set_xlabel("energy (uJ)"); a[1].set_ylabel("nonlinear phase (rad)")
        return fg
    V.save_animation(frame, range(len(energies)), __file__, fps=4)
