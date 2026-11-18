"""Hero figure of the dependence-model comparison (python make_figures.py)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import compare as ex
from ultrafast_twin import synthetic

args = V.parser().parse_args()
r = ex.run()
th = V.series()
f = V.fig(12.5, 4.4)
ax = f.subplots(1, 3)
ee = np.linspace(0.05, 3.5, 60)
ax[0].plot(ee, [synthetic.true_b(e, True) for e in ee], color="k", lw=2, label="hidden truth")
for k, (key, d) in enumerate(r.items()):
    dep = d["twin"].deps[0]
    ax[0].plot(ee, [next(iter(dep.values(e * 1e-6).values())) for e in ee], color=th[k % len(th)], lw=1.2, ls="--" if "H=3" in key else "-", label=key)
ax[0].axvspan(0.4, 2.0, color="0.5", alpha=0.12)
for e in synthetic.HELD_UJ:
    ax[0].axvline(e, color="#e15759", lw=0.8, ls=":")
ax[0].set_xlabel("pulse energy (uJ)"); ax[0].set_ylabel("nonlinear phase b (rad)"); ax[0].set_title("learned energy law (grey: training, dotted: held out)"); ax[0].legend(fontsize=7)
names = list(r)
x = np.arange(len(names))
for j, (cond, col, lab) in enumerate((("E1.4uJ", th[0], "1.4 uJ (inside)"), ("E3.0uJ", th[1], "3.0 uJ (outside)"))):
    ax[1].bar(x + (j - 0.5) * 0.38, [max(r[k]["rows"][cond]["reducedChi2"], 1e-2) for k in names], 0.38, color=col, label=lab)
ax[1].set_yscale("log"); ax[1].axhline(1, color="0.5", ls="--", lw=1); ax[1].set_xticks(x, [n.replace(" (", "\n(") for n in names], fontsize=7)
ax[1].set_ylabel("held-out chi$^2$ per point"); ax[1].set_title("validation report"); ax[1].legend(fontsize=7)
ax[2].bar(x, [r[k]["params"] for k in names], color="0.55"); ax[2].set_xticks(x, [n.replace(" (", "\n(") for n in names], fontsize=7)
ax[2].set_ylabel("fitted parameters (filter + law)"); ax[2].set_title("what each model costs")
V.headline(f, "Which energy law should a twin use? A saturating lab, five training energies, two held-out ones")
V.save_hero(f, __file__)
