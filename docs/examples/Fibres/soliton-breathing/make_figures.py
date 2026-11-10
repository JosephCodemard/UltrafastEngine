"""Hero figure and animation of the soliton breathing (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import solitons as ex

args = V.parser().parse_args()
r = ex.run()
th = V.series()
f = V.fig(12.5, 7.0)
ax = f.subplots(2, 3)
for row, n in enumerate((1, 3)):
    e = r[n]
    sel = np.abs(e["t"]) < 0.8e-12
    V.heat(ax[row, 0], (e["power"][:, sel] / ex.peak_power(n)), e["t"][sel] * 1e12, e["z"] / ex.Z0, "N = %d: power (rel. to input peak)" % n, xlabel="time (ps)", ylabel="z / z$_0$", cmap="inferno", vmin=0)
    lam = e["lam"] * 1e9
    ok = (lam > 1450) & (lam < 1650)
    order = np.argsort(lam[ok])
    sp = e["spectrum"][:, ok][:, order]
    V.heat(ax[row, 1], 10 * np.log10(np.maximum(sp / sp.max(), 1e-5)), lam[ok][order], e["z"] / ex.Z0, "N = %d: spectrum (dB)" % n, xlabel="wavelength (nm)", ylabel="z / z$_0$", cmap="viridis", vmin=-40, vmax=0)
    ax[row, 2].plot(e["z"] / ex.Z0, e["peak"] / ex.peak_power(n), color=th[0]); ax[row, 2].axhline(1, color="0.6", ls="--", lw=1)
    ax[row, 2].axvline(1, color=th[1], ls=":", lw=1, label="z = z$_0$ = $\\pi$/2 L$_D$"); ax[row, 2].set_xlabel("z / z$_0$"); ax[row, 2].set_ylabel("peak power / input"); ax[row, 2].set_title("N = %d: peak power" % n); ax[row, 2].legend(fontsize=7)
V.headline(f, "Solitons in fibre: N = 1 keeps its shape (0.03 %%), N = 3 breathes and returns at z$_0$ = $\\pi$/2 L$_D$ = %.2f m" % ex.Z0)
V.save_hero(f, __file__)

if not args.no_animations:
    e = r[3]
    sel = np.abs(e["t"]) < 0.8e-12
    ks = list(range(0, len(e["z"]), 3))

    def frame(i):
        k = ks[i]
        fg = V.fig(9.5, 3.3)
        a = fg.subplots(1, 2)
        a[0].plot(e["t"][sel] * 1e12, e["power"][k][sel] / ex.peak_power(3), color=th[0]); a[0].plot(e["t"][sel] * 1e12, e["power"][0][sel] / ex.peak_power(3), color="0.7", lw=1)
        a[0].set_ylim(0, 7); a[0].set_xlabel("time (ps)"); a[0].set_ylabel("power / input peak"); a[0].set_title("N = 3 soliton at z = %.2f z$_0$" % (e["z"][k] / ex.Z0))
        a[1].plot(e["z"][:k + 1] / ex.Z0, e["peak"][:k + 1] / ex.peak_power(3), color=th[1]); a[1].set_xlim(0, 2); a[1].set_ylim(0, 7); a[1].set_xlabel("z / z$_0$"); a[1].set_ylabel("peak / input")
        return fg
    V.save_animation(frame, range(len(ks)), __file__, fps=6)
