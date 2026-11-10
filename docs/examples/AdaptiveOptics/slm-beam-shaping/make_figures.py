"""Hero figures and the GS animation of the SLM beam shaping (python make_figures.py [--no-animations] [--no-full])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import slm_shaping as ex
from ultrafast.algorithms import holography

ap = V.parser()
ap.add_argument("--no-full", action="store_true", help="skip the 1920 x 1080 panel (about 20 s)")
args = ap.parse_args()
r = ex.run()
th = V.series()
names = ["smiley", "square", "ring", "spots"]
titles = {"smiley": "smiley (GS)", "square": "square outline (GS)", "ring": "ring (GS)", "spots": "3 x 3 spots (weighted GS)"}
HALF = ex.N // 2
fext = [-HALF, HALF] * 2

f = V.fig(12.5, 9.4)
ax = f.subplots(3, 4)
for k, nm in enumerate(names):
    d = r[nm]
    from scipy.ndimage import binary_dilation
    tg = binary_dilation(d["target"] > 0, iterations=3 if nm == "spots" else 0) if nm == "spots" else d["target"]
    V.image(ax[0, k], tg, titles[nm], extent=fext, cmap="Greys", colorbar=False, transpose=False)
    V.image(ax[1, k], np.mod(d["hologram"].phase, 2 * np.pi), "SLM phase", extent=[-ex.N / 2 * ex.DX * 1e3, ex.N / 2 * ex.DX * 1e3] * 2, cmap="twilight", vmin=0, vmax=2 * np.pi, colorbar=False, transpose=False)
    F = d["fluence"]
    V.image(ax[2, k], F / F.max(), "engine focus (%.0f %% on target)" % (100 * ex.energy_on_target(F, d["target"])), extent=fext, colorbar=False, transpose=False)
ax[0, 0].set_ylabel("target"); ax[1, 0].set_ylabel("SLM (mm)"); ax[2, 0].set_ylabel("focus (pixels)")
V.headline(f, "A 256 x 256 phase-only SLM with 256 grey levels shapes the focus; the engine reproduces the design")
V.save_hero(f, __file__)

# GS convergence: error and far field versus iteration
f2 = V.fig(11, 3.6)
a = f2.subplots(1, 3)
t = r["smiley"]["target"]
for lev, col, lab in ((None, th[0], "ideal phase"), (256, th[1], "256 levels"), (4, th[2], "4 levels")):
    h = holography.gerchberg_saxton(t, ex.illumination(ex.N, ex.DX), 60, seed=1, levels=lev)
    a[0].semilogy(h.error, color=col, label=lab)
a[0].set_xlabel("iteration"); a[0].set_ylabel("far-field amplitude error"); a[0].set_title("GS converges (Fienup 1982)"); a[0].legend()
h4 = holography.gerchberg_saxton(t, ex.illumination(ex.N, ex.DX), 60, seed=1, levels=4)
I4 = np.abs(h4.field) ** 2
V.image(a[1], I4 / I4.max(), "4-level hologram: far field (%.0f %% on target)" % (100 * h4.efficiency), extent=fext, colorbar=False, transpose=False)
Fs = r["smiley"]["fluence"]
V.image(a[2], Fs / Fs.max(), "256 levels through the engine", extent=fext, colorbar=False, transpose=False)
V.save_hero(f2, __file__, "quantisation.png")

if not args.no_full:
    full = ex.full()
    F = full["fluence"]
    f3 = V.fig(12, 4.2)
    a = f3.subplots(1, 3)
    p = full["panel"]
    V.image(a[0], np.mod(p, 2 * np.pi)[::6, ::6], "hologram on the 1920 x 1080 panel (every 6th pixel)", cmap="twilight", vmin=0, vmax=2 * np.pi, colorbar=False, transpose=False)
    sl = slice(512, 1536)
    V.image(a[1], F[sl, sl] / F.max(), "engine focus, full panel (%.0f %% on the smiley)" % (100 * F[full["target"] > 0].sum() / F.sum()), colorbar=False, transpose=False)
    V.image(a[2], full["target"][sl, sl], "target", cmap="Greys", colorbar=False, transpose=False)
    V.headline(f3, "Full-size 1920 x 1080 SLM, 8 um pixels, 8 bit: designed on 2048 x 2048 in 10 s, propagated in 5 s")
    V.save_hero(f3, __file__, "full-panel.png")

if not args.no_animations:
    steps = [1, 2, 3, 4, 6, 8, 12, 16, 24, 36, 60]
    errs = holography.gerchberg_saxton(t, ex.illumination(ex.N, ex.DX), 60, seed=1, levels=256).error
    fr = []
    for k in steps:
        h = holography.gerchberg_saxton(t, ex.illumination(ex.N, ex.DX), k, seed=1, levels=256)
        fr.append(np.abs(h.field) ** 2)
    vmax = max(x.max() for x in fr) * 0.6

    def frame(i):
        fg = V.fig(10.5, 3.3)
        a = fg.subplots(1, 3)
        V.image(a[0], t, "target", extent=fext, cmap="Greys", colorbar=False, transpose=False)
        V.image(a[1], fr[i], "far field after %d iterations" % steps[i], extent=fext, vmax=vmax, colorbar=False, transpose=False)
        a[2].semilogy(np.arange(1, steps[i] + 1), errs[:steps[i]], color=th[0]); a[2].set_xlim(0, 60); a[2].set_ylim(min(errs) * 0.8, max(errs) * 1.2)
        a[2].set_xlabel("iteration"); a[2].set_ylabel("amplitude error"); a[2].set_title("Gerchberg-Saxton")
        return fg
    V.save_animation(frame, range(len(steps)), __file__, fps=3)
