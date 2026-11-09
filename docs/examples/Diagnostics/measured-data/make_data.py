"""Writes the "recorded" data of example 21 into data/: the same line with the TRUE parameter values (which the
fit should find), measured through realistic instruments, with noise and a background added.
You only need this to regenerate the files; in real life they come from your lab.

    python docs/examples/21-measured-data/make_data.py
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

TRUE = {"chirp_fs2": 450.0, "energy_j": 140e-9, "compressor_fs2": -520.0}
rng = np.random.default_rng(2026)


def jsonc(p):
    return json.loads(re.sub(r"//[^\n]*", "", Path(p).read_text()))


system = jsonc(here / "line.ufs")
system["elements"][1]["gddFs2"] = TRUE["compressor_fs2"]
spec = jsonc(here / "run-compare.json")
spec["pulse"]["chirp"]["gddFs2"] = TRUE["chirp_fs2"]
spec["pulse"]["energyJ"] = TRUE["energy_j"]
for ins in spec["instruments"]:
    ins.pop("measured", None)
res = uf.run(system, spec)
data = here / "data"
data.mkdir(exist_ok=True)

# 1) fibre spectrometer in the far field after the plate: counts on a 0.5 nm grid, background 30 counts, shot noise
lam, s = res["fibre_spectrum_wavelength_m"], res["fibre_spectrum_spectrum"]
grid = np.arange(700, 900.01, 0.5) * 1e-9
counts = 30 + 4000 * np.interp(grid, lam, s, left=0, right=0)
counts = rng.poisson(counts).astype(float)
np.savetxt(data / "fibre_spectrum.csv", np.c_[grid * 1e9, counts], delimiter=",", header="wavelength_nm,counts", comments="", fmt="%.2f")

# 2) intensity autocorrelation of the compressed pulse, delay in fs, 1 % noise
d, a = res["ac_out_delay_s"], res["ac_out_signal"]
dg = np.arange(-400, 400.1, 4.0) * 1e-15
ac = np.interp(dg, d, a, left=0, right=0) + 0.01 * rng.standard_normal(dg.size)
np.savetxt(data / "autocorrelation.csv", np.c_[dg * 1e15, ac], delimiter=",", header="delay_fs,signal", comments="", fmt="%.5g")

# 3) d-scan trace (glass insertion x SHG wavelength) as .npz, 2 % noise
ins, wl, tr = res["dscan_out_insertion_m"], res["dscan_out_shg_wavelength_m"], res["dscan_out_trace"]
tr = tr.reshape(ins.size, wl.size)
o = np.argsort(wl)
wl_m = np.linspace(370e-9, 430e-9, 121)
tr_m = np.array([np.interp(wl_m, wl[o], row[o], left=0, right=0) for row in tr])
tr_m = tr_m / tr_m.max() + 0.02 * rng.standard_normal(tr_m.shape)
np.savez(data / "dscan.npz", insertion_mm=ins * 1e3, wavelength_nm=wl_m * 1e9, trace=tr_m)

# 4) a power meter reading (energy per pulse at 1 kHz), 3 % calibration uncertainty
energy = res.summary["output"]["energyJ"] * (1 + 0.01 * rng.standard_normal())
(data / "power_meter.json").write_text(json.dumps({"energyJ": energy}, indent=1))
print("wrote", sorted(p.name for p in data.iterdir()), "true values", TRUE, "energy", energy)
