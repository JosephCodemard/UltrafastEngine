"""Hero figure and animation of the SPM compression (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import spm_compression as ex

args = V.parser().parse_args()
r = ex.run()
th = V.series()
lam = r["lam"] * 1e9
ok = (lam > 900) & (lam < 1200); order = np.argsort(lam[ok])
sp = r["spectrum"][:, ok][:, order]
f = V.fig(12.5, 6.6)
ax = f.subplots(2, 3)
sel = np.abs(r["t"]) < 2.5e-12
V.heat(ax[0, 0], r["power"][:, sel] / r["power"].max(), r["t"][sel] * 1e12, r["z"], "power along the fibre (rel.)", xlabel="time (ps)", ylabel="z (m)", cmap="inferno", vmin=0)
V.heat(ax[0, 1], sp / sp.max(), lam[ok][order], r["z"], "spectrum along the fibre (rel.)", xlabel="wavelength (nm)", ylabel="z (m)", cmap="viridis", vmin=0)
ax[0, 2].plot(r["z"], r["bw"] / r["bw"][0], color=th[0], label="with dispersion"); ax[0, 2].axhline(r["spm_only_growth"], color=th[1], ls="--", label="no dispersion (pure SPM): %.2f x" % r["spm_only_growth"])
ax[0, 2].set_xlabel("z (m)"); ax[0, 2].set_ylabel("rms bandwidth / input"); ax[0, 2].set_title("spectral broadening"); ax[0, 2].legend(fontsize=7)
t = r["input"]["output_time_s"] * 1e15
for key, lab, c in (("input", "input, %.0f fs" % (r["input"].summary["output"]["fwhmS"] * 1e15), "0.6"), ("uncompressed", "after the fibre, %.0f fs" % (r["uncompressed"].summary["output"]["fwhmS"] * 1e15), th[1]),
                    ("compressed", "compressed, %.0f fs" % (r["compressed"].summary["output"]["fwhmS"] * 1e15), th[0])):
    ax[1, 0].plot(t, r[key]["output_power_w"] / r["input"]["output_power_w"].max(), color=c, label=lab)
ax[1, 0].set_xlim(-1500, 1500); ax[1, 0].set_xlabel("time (fs)"); ax[1, 0].set_ylabel("power / input peak"); ax[1, 0].set_title("temporal compression"); ax[1, 0].legend(fontsize=7)
l2 = r["input"]["output_wavelength_m"] * 1e9; o2 = np.argsort(l2)
for key, lab, c in (("input", "input", "0.6"), ("uncompressed", "after the fibre", th[0])):
    s_ = r[key]["output_spectrum"]; ax[1, 1].plot(l2[o2], s_[o2] / s_.max(), color=c, label=lab)
ax[1, 1].set_xlim(950, 1110); ax[1, 1].set_xlabel("wavelength (nm)"); ax[1, 1].set_title("spectrum in and out"); ax[1, 1].legend(fontsize=7)
ax[1, 2].plot(r["z"], r["fwhm"] * 1e15, color=th[0]); ax[1, 2].set_xlabel("z (m)"); ax[1, 2].set_ylabel("FWHM (fs)"); ax[1, 2].set_title("the pulse stretches: SPM chirp + normal dispersion")
V.headline(f, "SPM in a normal-dispersion fibre, then a compressor found from the peak power: 300 fs to %.0f fs (transform limit %.0f fs)" % (r["compressed"].summary["output"]["fwhmS"] * 1e15, r["compressed"].summary["output"]["transformLimitFwhmS"] * 1e15))
V.save_hero(f, __file__)

if not args.no_animations:
    ks = list(range(0, len(r["z"])))

    def frame(i):
        fg = V.fig(9.5, 3.3)
        a = fg.subplots(1, 2)
        a[0].plot(r["t"][sel] * 1e12, r["power"][i][sel] / r["power"].max(), color=th[0]); a[0].set_ylim(0, 1.05); a[0].set_xlabel("time (ps)"); a[0].set_title("z = %.2f m" % r["z"][i])
        a[1].plot(lam[ok][order], sp[i] / sp.max(), color=th[1]); a[1].set_ylim(0, 1.05); a[1].set_xlabel("wavelength (nm)"); a[1].set_title("rms bandwidth %.1f x" % (r["bw"][i] / r["bw"][0]))
        return fg
    V.save_animation(frame, range(len(ks)), __file__, fps=6)
