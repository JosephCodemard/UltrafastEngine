"""Hero figure of the ao component gallery (python make_figures.py)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import _viz as V
import numpy as np
import ao_components as ex

args = V.parser().parse_args()
r = ex.run()
th = V.series()
f = V.fig(11.5, 7.6)
ax = f.subplots(2, 2)
dm = r["dm"]
V.image(ax[0, 0], dm["waves"], "deformable mirror: one actuator poked 100 nm", extent=V.extent_mm(dm["x"]), cmap="RdBu_r", symmetric=True, label="wavefront (waves)") if hasattr(V, "image") else None
ax[0, 0].plot([dm["actuator"][0] * 1e3], [dm["actuator"][1] * 1e3], "k+", ms=8)
s = r["slm"]
F = s["F"]; x = s["x"] * 1e3
ax[0, 1].imshow(np.log10(F / F.max() + 1e-6).T, origin="lower", extent=(x[0], x[-1], x[0], x[-1]), cmap="magma", vmin=-4, vmax=0)
ax[0, 1].set_title("SLM blazed grating: spot at %.0f um (lambda f / period = %.0f um)" % (abs(s["offset_m"]) * 1e6, s["expected_m"] * 1e6), fontsize=8)
ax[0, 1].set_xlabel("x (mm)"); ax[0, 1].set_ylabel("y (mm)")
d = r["double"]
ax[1, 0].plot(d["t"] * 1e15, d["p"] / d["p"].max(), color=th[0]); ax[1, 0].set_xlim(-800, 800)
ax[1, 0].set_xlabel("time (fs)"); ax[1, 0].set_ylabel("power (norm.)"); ax[1, 0].set_title("spectral shaper: two pulses %.0f fs apart (programmed %.0f fs)" % ((d["peaks_t"][-1] - d["peaks_t"][0]) * 1e15, d["expected_s"] * 1e15), fontsize=8)
c = r["chirp"]
ax[1, 1].plot(c["ref"]["t"] * 1e15, c["ref"]["p"] / c["ref"]["p"].max(), color="0.4", label="%.1f fs" % (c["tau0"] * 1e15))
ax[1, 1].plot(c["out"]["t"] * 1e15, c["out"]["p"] / c["ref"]["p"].max(), color=th[1], label="AOPDF GDD 1000 fs$^2$: %.1f fs" % (c["tau"] * 1e15))
ax[1, 1].set_xlim(-300, 300); ax[1, 1].set_xlabel("time (fs)"); ax[1, 1].set_ylabel("power (norm. to the input peak)"); ax[1, 1].set_title("AOPDF: a chirp stretches the pulse", fontsize=8); ax[1, 1].legend(fontsize=7)
V.headline(f, "The four ao elements, each doing one thing with a known answer")
V.save_hero(f, __file__)
