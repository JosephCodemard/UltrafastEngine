"""Writes the "recorded" data of example 26 into data/: the line of line.ufs with the TRUE values of the lab transform
(which the fit should find), measured through realistic instruments with noise. In real life these files come from
your lab; rerun this script to regenerate them.

    python docs/examples/26-transforms/make_data.py
"""
import copy
import json
import re
import sys
from pathlib import Path

import numpy as np

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here.parents[2] / "python" / "src"))
import ultrafast as uf  # noqa: E402

TRUE = {"gddFs2": 800.0, "todFs3": -15000.0, "fodFs4": 4e5, "fwhmNm": 13.0, "centerWavelengthNm": 1031.5,
        "transmission": 0.42, "replicaDb": -46.0, "pedestalDb": -88.0}
rng = np.random.default_rng(26)


def jsonc(p):
    return json.loads(re.sub(r"//[^\n]*", "", Path(p).read_text()))


def truth_system():
    s = jsonc(here / "line.ufs")
    ops = s["elements"][1]["operators"]
    ops[0].update(gddFs2=TRUE["gddFs2"], todFs3=TRUE["todFs3"], fodFs4=TRUE["fodFs4"])
    ops[1].update(fwhmNm=TRUE["fwhmNm"], centerWavelengthNm=TRUE["centerWavelengthNm"])
    ops[2]["transmission"] = TRUE["transmission"]
    ops[3]["relativeIntensityDb"] = TRUE["replicaDb"]
    ops[4]["relativeIntensityDb"] = TRUE["pedestalDb"]
    return s


spec = jsonc(here / "run-fit.json")
spec.pop("fit")
for ins in spec["instruments"]:
    ins.pop("measured", None)
res = uf.run(truth_system(), spec)
data = here / "data"
data.mkdir(exist_ok=True)

# 1) spectrum: counts on a 0.1 nm grid, 25-count dark level, shot noise
lam, s = res["spectrum_wavelength_m"], res["spectrum_spectrum"]
o = np.argsort(lam)
grid = np.arange(1000, 1060.01, 0.1) * 1e-9
counts = rng.poisson(25 + 20000 * np.interp(grid, lam[o], s[o], left=0, right=0)).astype(float)
np.savetxt(data / "spectrum.csv", np.c_[grid * 1e9, counts], delimiter=",", header="wavelength_nm,counts", comments="", fmt="%.2f")

# 2) d-scan (added GDD x SHG wavelength), 2 % noise
g, wl, tr = res["dscan_gdd_s2"], res["dscan_shg_wavelength_m"], res["dscan_trace"].reshape(-1, res["dscan_shg_wavelength_m"].size)
oo = np.argsort(wl)
wl_m = np.linspace(505e-9, 525e-9, 101)
tr_m = np.array([np.interp(wl_m, wl[oo], row[oo], left=0, right=0) for row in tr])
tr_m = tr_m / tr_m.max() + 0.02 * rng.standard_normal(tr_m.shape)
np.savez(data / "dscan.npz", gdd_fs2=g * 1e30, shg_wavelength_nm=wl_m * 1e9, trace=tr_m)

# 3) third-order cross-correlation in dB: delay in ps, 1 dB noise above a -108 dB floor
d, sdb = res["contrast_delay_s"], res["contrast_signal_db"]
meas = np.maximum(sdb + rng.normal(0, 1.0, sdb.size), -108 + rng.normal(0, 1.0, sdb.size))
np.savetxt(data / "cross_correlation.csv", np.c_[d * 1e12, meas], delimiter=",", header="delay_ps,signal_db", comments="", fmt="%.4f")

# 4) power meter
(data / "power.json").write_text(json.dumps({"energyJ": [round(res.summary["output"]["energyJ"], 9), 0.02 * res.summary["output"]["energyJ"]]}))

# 5) the output field as a pulse retrieval would give it (spectrum + phase), for the backward run
lam, s, ph = res["output_wavelength_m"], res["output_spectrum"], res["output_phase_x_rad"]
keep = s > 1e-6 * s.max()
lam, s, ph = lam[keep], s[keep], ph[keep]
o = np.argsort(lam)
per_lam = s / lam ** 2
np.savetxt(data / "output_field.csv", np.c_[lam[o] * 1e9, per_lam[o] / per_lam.max(), ph[o]], delimiter=",",
           header="wavelength_nm,intensity,phase_rad", comments="", fmt="%.6g")

print("true values:", TRUE)
print("output energy %.4g J, FWHM %.4g fs" % (res.summary["output"]["energyJ"], res.summary["output"]["fwhmS"] * 1e15))
