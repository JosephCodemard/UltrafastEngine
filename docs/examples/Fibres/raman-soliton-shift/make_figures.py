"""Hero figure and animation of the soliton self-frequency shift (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import raman_shift as ex

args = V.parser().parse_args()
r = ex.run()
th = V.series()
tr = ex.raman_first_moment()
d = r[ex.T0S[0]]
lam = d["lam"] * 1e9
ok = (lam > 1450) & (lam < 1750); order = np.argsort(lam[ok])
f = V.fig(12.5, 6.4)
ax = f.subplots(2, 3)
sp = d["spectra"][:, ok][:, order]
V.heat(ax[0, 0], 10 * np.log10(np.maximum(sp / sp.max(), 1e-5)), lam[ok][order], d["z"], "T$_0$ = 40 fs: spectrum along the fibre (dB)", xlabel="wavelength (nm)", ylabel="z (m)", cmap="viridis", vmin=-40, vmax=0)
sel = np.abs(d["t"]) < 2.5e-12
V.heat(ax[0, 1], d["power"][:, sel] / d["power"].max(), d["t"][sel] * 1e12, d["z"], "power: the soliton slows as it reddens", xlabel="time (ps)", ylabel="z (m)", cmap="inferno", vmin=0)
for t0, c in zip(ex.T0S, th):
    e = r[t0]
    ax[0, 2].plot(e["z"], -e["shift"] / (2 * np.pi) * 1e-12, color=c, label="T$_0$ = %.0f fs" % (t0 * 1e15))
ax[0, 2].set_xlabel("z (m)"); ax[0, 2].set_ylabel("red shift of the centroid (THz)"); ax[0, 2].set_title("self-frequency shift"); ax[0, 2].legend()
rates = []
for t0 in ex.T0S:
    e = r[t0]; s = e["z"] > ex.LENGTH / 3
    rates.append(-np.polyfit(e["z"][s], e["shift"][s], 1)[0] / (2 * np.pi) * 1e-12)
t = np.array(ex.T0S) * 1e15
tt = np.linspace(35, 70, 50)
g = lambda x: 8 * tr * abs(ex.B2) / (15 * (x * 1e-15) ** 4) / (2 * np.pi) * 1e-12
ax[1, 0].loglog(t, rates, "o", color=th[0], label="engine"); ax[1, 0].loglog(tt, g(tt), "-", color=th[1], label="Gordon 1986, T$_R$ = %.2f fs" % (tr * 1e15))
ax[1, 0].loglog(tt, rates[1] * (tt / 50.0) ** -4, ":", color="0.5", label="slope $-4$")
ax[1, 0].set_xlabel("T$_0$ (fs)"); ax[1, 0].set_ylabel("shift rate (THz/m)"); ax[1, 0].set_title("rate $\\propto$ T$_0^{-4}$"); ax[1, 0].legend(fontsize=7)
for k in (0, len(d["z"]) // 2, len(d["z"]) - 1):
    ax[1, 1].plot(lam[ok][order], sp[k] / sp.max(), label="z = %.1f m" % d["z"][k])
ax[1, 1].set_xlabel("wavelength (nm)"); ax[1, 1].set_title("spectrum at three lengths"); ax[1, 1].legend(fontsize=7)
ax[1, 2].plot(d["z"], np.array([np.max(p) for p in d["power"]]) / d["power"][0].max(), color=th[0]); ax[1, 2].set_xlabel("z (m)"); ax[1, 2].set_ylabel("peak power / input"); ax[1, 2].set_title("soliton peak power (40 fs): drifts down as it reddens")
V.headline(f, "Raman self-frequency shift of fundamental solitons: red shift rate $\\propto$ T$_0^{-4}$ (fitted slope %.2f)" % np.polyfit(np.log(ex.T0S), np.log(np.array(rates)), 1)[0])
V.save_hero(f, __file__)

if not args.no_animations:
    ks = list(range(len(d["z"])))

    def frame(i):
        fg = V.fig(9.0, 3.3)
        a = fg.subplots(1, 2)
        a[0].plot(lam[ok][order], sp[i] / sp.max(), color=th[0]); a[0].plot(lam[ok][order], sp[0] / sp.max(), color="0.75", lw=1); a[0].set_xlabel("wavelength (nm)"); a[0].set_title("z = %.1f m" % d["z"][i]); a[0].set_ylim(0, 1.05)
        a[1].plot(d["z"][:i + 1], -d["shift"][:i + 1] / (2 * np.pi) * 1e-12, color=th[1]); a[1].set_xlim(0, ex.LENGTH); a[1].set_ylim(0, 9); a[1].set_xlabel("z (m)"); a[1].set_ylabel("red shift (THz)")
        return fg
    V.save_animation(frame, range(len(ks)), __file__, fps=6)
