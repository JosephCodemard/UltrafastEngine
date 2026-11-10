"""Hero figure and animation of the DM correction (python make_figures.py [--no-animations])."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import dm_correction as ex
from ultrafast.plotting import zernike as zp

args = V.parser().parse_args()
r = ex.run()
b = r["bench"]
x = r["x"]
ext = V.extent_mm(x)
c0, c1 = np.zeros(b.n_actuators), r["loop"][-1]["commands"]
cam = lambda c: b.run(c, sensor=False).instrument("cam")
foc0, foc1 = cam(c0), cam(c1)
fx = np.asarray(foc0["x_m"]) * 1e6
fmax = max(np.max(foc0["fluence_map_j_m2"]), np.max(foc1["fluence_map_j_m2"]))
R = 1.3 * b.beam_radius_m
w0, w1 = (V.disc(h["waves"], x, x, R) for h in (r["loop"][0], r["loop"][-1]))
vw = np.nanmax(np.abs(w0))
HALF = 250e-6
def zoom(F):
    return V.crop(F, foc0["x_m"], foc0["y_m"], HALF)[0]
fext = [-HALF * 1e6, HALF * 1e6] * 2

f = V.fig(12, 6.4)
ax = f.subplots(2, 4)
V.image(ax[0, 0], w0, "wavefront, mirror flat", extent=ext, symmetric=True, vmax=vw, label="waves")
V.image(ax[0, 1], w1, "wavefront, loop closed", extent=ext, symmetric=True, vmax=vw, label="waves")
V.image(ax[0, 2], zoom(foc0["fluence_map_j_m2"]), "focus, aberrated (Strehl %.2f)" % r["strehl_before"], extent=fext, vmax=fmax, label="fluence")
V.image(ax[0, 3], zoom(foc1["fluence_map_j_m2"]), "focus, corrected (Strehl %.2f)" % r["loop"][-1]["strehl"], extent=fext, vmax=fmax, label="fluence")
for a in ax[0]:
    a.set_xlabel("x (mm)" if a in ax[0][:2] else "x (um)")
cmd = np.asarray(c1).reshape(b.actuators, b.actuators) * 1e9
V.image(ax[1, 0], cmd, "mirror commands (nm)", cmap="RdBu_r", symmetric=True, label="nm", transpose=False)
th = V.series()
steps = [h["step"] for h in r["loop"]]
ax[1, 1].semilogy(steps, [h["slope_rms"] for h in r["loop"]], "o-", color=th[0], label="measured")
ax[1, 1].semilogy(steps, r["loop"][0]["slope_rms"] * 0.3 ** np.array(steps), "--", color=th[1], label="(1 - gain)$^k$")
ax[1, 1].set_xlabel("loop step"); ax[1, 1].set_ylabel("slope rms (rad)"); ax[1, 1].set_title("integrator, gain 0.7"); ax[1, 1].legend()
ax[1, 2].plot(steps, [h["strehl"] for h in r["loop"]], "o-", color=th[0], label="loop")
for lab, v, k in (("zonal", r["strehl_zonal"], 1), ("modal", r["strehl_modal"], 2)):
    ax[1, 2].axhline(v, color=th[k], ls="--", lw=1, label="one-shot " + lab)
ax[1, 2].set_ylim(0, 1.02); ax[1, 2].set_xlabel("loop step"); ax[1, 2].set_ylabel("Strehl ratio"); ax[1, 2].set_title("focus quality"); ax[1, 2].legend(loc="lower right")
m = np.abs(fx) <= HALF * 1e6
ax[1, 3].plot(fx[m], (np.asarray(foc0["fluence_x_j_m2"]) / fmax)[m], color=th[1], label="aberrated")
ax[1, 3].plot(fx[m], (np.asarray(foc1["fluence_x_j_m2"]) / fmax)[m], color=th[0], label="corrected")
ax[1, 3].set_xlabel("x in the focal plane (um)"); ax[1, 3].set_ylabel("fluence (rel.)"); ax[1, 3].set_title("focal line-out"); ax[1, 3].legend()
V.headline(f, "10 x 10 deformable mirror: Strehl ratio %.2f to %.2f in two loop steps" % (r["strehl_before"], r["loop"][-1]["strehl"]))
V.save_hero(f, __file__)

# the Zernike decomposition of the wavefront before and after (ultrafast.plotting.zernike)
sensor = lambda c: b.run(c, camera=False).instrument("wfs")
fz = zp.decomposition([sensor(c0), sensor(c1)], labels=["mirror flat", "loop closed"])
V.save_hero(fz, __file__, "zernike.png")

if not args.no_animations:
    
    def frame(k):
        h = r["loop"][k]
        fg = V.fig(10.5, 3.3)
        a = fg.subplots(1, 3)
        V.image(a[0], V.disc(h["waves"], x, x, R), "wavefront (waves)", extent=ext, symmetric=True, vmax=vw, colorbar=False)
        F = zoom(cam(h["commands"])["fluence_map_j_m2"])
        V.image(a[1], F, "focus, step %d" % k, extent=fext, vmax=fmax, colorbar=False)
        a[2].plot(steps[:k + 1], [q["strehl"] for q in r["loop"][:k + 1]], "o-", color=th[0])
        a[2].set_xlim(-0.2, steps[-1] + 0.2); a[2].set_ylim(0, 1); a[2].set_xlabel("loop step"); a[2].set_ylabel("Strehl"); a[2].set_title("Strehl ratio %.2f" % h["strehl"])
        return fg
    V.save_animation(frame, range(len(r["loop"])), __file__, fps=2.5)
