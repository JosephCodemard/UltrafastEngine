"""Example 18 from Python: the same n(x, y, z, t, I, F) written as a Python function, then one that is not
axisymmetric -- a Kerr coefficient that grows across the rod (an x gradient, e.g. a doping gradient) on a
cartesian grid: the nonlinear phase is tilted, and the beam is steered towards +x.

    python docs/examples/18-custom-nonlinear-medium/custom_medium.py

A time response is called for every transverse grid point (ctx.x, ctx.y, ctx.r, ctx.theta) at every step
(ctx.z) with the co-moving time ctx.t, lab time ctx.t_lab and the intensity ctx.intensity (W/m^2) at that
point; it returns Delta n(t) (and optionally an imaginary part for gain/loss).
"""
import copy
import json
import math
import re
import sys
from pathlib import Path

import numpy as np

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here.parents[2] / "python" / "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402


def load_jsonc(p):
    txt = re.sub(r"//[^\n]*", "", Path(p).read_text())
    return json.loads(txt)


system = load_jsonc(here / "medium.ufs")
spec = load_jsonc(here / "run.json")
spec["instruments"] = [i for i in spec["instruments"] if i["kind"] in ("camera", "spectrometer")]
prm = system["elements"][0]["timeResponse"]["params"]
L = system["elements"][0]["lengthM"]


def dn_python(ctx):
    """The expression of medium.ufs, written in NumPy."""
    I = ctx.intensity
    F = np.concatenate(([0.0], np.cumsum(0.5 * (I[1:] + I[:-1]) * np.diff(ctx.t))))   # running fluence (J/m^2)
    return (prm["n2"] * (1 + ctx.z / L) * math.exp(-(ctx.r / prm["rc"]) ** 2) * I
            - prm["kF"] * F + prm["dn_mod"] * np.sin(2 * math.pi * prm["f_mod"] * ctx.t_lab))


def summary(res, label):
    els = {e["id"]: e for e in res.elements()}
    ins = {i["name"]: i["values"] for i in res.summary["instruments"]}
    nf = ins["nf_200mm"]
    print(f"{label:<36s} B = {els['rod']['bIntegralRad']:.3f} rad, spectrum FWHM {ins['spectrum_out']['fwhmM'] * 1e9:.2f} nm, "
          f"beam after 200 mm {nf['radiusXM'] * 1e6:.0f} x {nf['radiusYM'] * 1e6:.0f} um, centroid x = {nf['centroidXM'] * 1e6:+.1f} um")


# 1) expression (from the file) vs the same function in Python
summary(uf.run(system, spec), "expression (medium.ufs)")
sys_py = copy.deepcopy(system)
sys_py["elements"][0]["timeResponse"] = "@my_medium"
summary(uf.run(sys_py, spec, callbacks={"my_medium": dn_python}), "Python callback, same physics")

# 2) a Kerr coefficient with a transverse gradient, n2 (1 + x / 0.5 mm), on a cartesian grid: the Kerr phase is
#    tilted, so the beam is steered towards +x (and focused a little more on that side)
sys_xy = copy.deepcopy(sys_py)
for e in sys_xy["elements"]:
    e["transverse"] = {"model": "cartesian", "nx": 32, "ny": 32, "halfWidthM": 1.2e-3 if e["id"] == "rod" else 2.0e-3}


def dn_gradient(ctx):
    return prm["n2"] * max(0.0, 1 + ctx.x / 0.5e-3) * math.exp(-(ctx.r / prm["rc"]) ** 2) * ctx.intensity


summary(uf.run(sys_xy, spec, callbacks={"my_medium": lambda c: 0 * c.intensity}), "cartesian grid, no Kerr effect")
summary(uf.run(sys_xy, spec, callbacks={"my_medium": dn_gradient}), "Python: n2 (1 + x / 0.5 mm)")
