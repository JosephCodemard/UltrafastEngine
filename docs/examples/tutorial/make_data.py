#!/usr/bin/env python3
"""Tutorial 4: writes the "lab recordings" in data/ for run-measured.json and run-fit.json.

They are simulated with values the run files do not know (energy 260 nJ, input chirp +120 fs^2), with noise
added. In your own work these files come from your instruments.

    python docs/examples/tutorial/make_data.py
"""
import json
import os
import re
import sys
from pathlib import Path

import numpy as np

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here.parents[2] / "python" / "src"))
import ultrafast as uf  # noqa: E402

rng = np.random.default_rng(7)
spec = json.loads(re.sub(r"//[^\n]*", "", (here / "run.json").read_text()))
spec["pulse"]["energyJ"] = 260e-9
spec["pulse"]["chirp"] = {"gddFs2": 120}
res = uf.run(uf.System.load(str(here / "plate.ufs")), spec)
(here / "data").mkdir(exist_ok=True)

# a spectrometer export: wavelength in nm, arbitrary counts, a dark offset and some noise; a comment line on top
lam, s = res["spectrometer_wavelength_m"], res["spectrometer_spectrum"]
o = np.argsort(lam)
grid = np.arange(740, 860.01, 0.25)
counts = 12 + 3000 * np.interp(grid * 1e-9, lam[o], s[o] / s.max()) + rng.normal(0, 15, grid.size)
with open(here / "data" / "spectrum_end.csv", "w") as f:
    f.write("# exported by MySpectrometer, integration 10 ms\n")
    f.write("wavelength_nm,counts\n")
    for x, y in zip(grid, counts):
        f.write(f"{x:.2f},{y:.1f}\n")

# an autocorrelator export: delay in ps, normalised signal
d, a = res["autocorrelator_delay_s"], res["autocorrelator_signal"]
dg = np.arange(-0.3, 0.3001, 0.003)
ac = np.interp(dg * 1e-12, d, a / a.max()) + rng.normal(0, 0.01, dg.size)
np.savetxt(here / "data" / "autocorrelation_end.csv", np.c_[dg, ac], delimiter=",", header="delay_ps,signal", comments="", fmt="%.6g")

# a power meter reading (average power at 1 kHz -> energy per pulse)
(here / "data" / "power_end.json").write_text(json.dumps({"energyJ": res.summary["output"]["energyJ"] * 1.01}, indent=1))
print("wrote", sorted(p.name for p in (here / "data").iterdir()))
