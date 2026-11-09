"""Writes data/spectrum_digitised.csv: a 1030 nm spectrum (6.5 nm FWHM) as a digitiser would hand it over -- a 950 to
1110 nm trace with a flat floor of 0.66 % of the peak, 29 rows out of wavelength order, one repeated wavelength, a
little noise -- and data/spectrum_osa.csv: the spectrum after the plates as an OSA with 0.5 nm resolution would record
it (1010-1050 nm only). Rerun to regenerate them.

    python docs/examples/27-ner-measured-input/make_data.py
"""
import json
import re
import sys
from pathlib import Path

import numpy as np

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here.parents[2] / "python" / "src"))
rng = np.random.default_rng(27)

wl = np.linspace(950, 1110, 1201)
s = np.exp(-4 * np.log(2) * ((wl - 1030) / 6.5) ** 2) + 0.0066 + 2e-4 * rng.standard_normal(wl.size)
idx = np.sort(rng.choice(np.arange(10, wl.size - 10), 29, replace=False))
for i in idx:                                  # digitiser: neighbouring points swapped
    wl[i], wl[i + 1] = wl[i + 1], wl[i]
    s[i], s[i + 1] = s[i + 1], s[i]
wl[600] = wl[601]                              # and one wavelength repeated
data = here / "data"
data.mkdir(exist_ok=True)
np.savetxt(data / "spectrum_digitised.csv", np.c_[wl, s * 1000], delimiter=",", fmt="%.4f",
           header="wavelength_nm,counts", comments="")

# the OSA recording after the plates: simulated with the clean input and a 0.5 nm spectrometer, plus noise
import ultrafast as uf  # noqa: E402


def jsonc(p):
    return json.loads(re.sub(r"//[^\n]*", "", Path(p).read_text()))


spec = jsonc(here / "run.json")
spec["pulse"]["spectrum"] = {"wavelengthsNm": list(np.linspace(950, 1110, 1201)),
                             "intensity": list(np.exp(-4 * np.log(2) * ((np.linspace(950, 1110, 1201) - 1030) / 6.5) ** 2))}
spec["instruments"] = [{"kind": "spectrometer", "name": "osa", "resolutionM": 0.5e-9,
                        "axis": {"from": 1010e-9, "to": 1050e-9, "step": 0.1e-9}}]
res = uf.run(jsonc(here / "plates.ufs"), spec)
lam, y = res["osa_wavelength_m"], res["osa_spectrum"]
o = np.argsort(lam)
y = y[o] / y.max() * 5000 + 40 + 5 * rng.standard_normal(y.size)
np.savetxt(data / "spectrum_osa.csv", np.c_[lam[o] * 1e9, y], delimiter=",", fmt="%.4f", header="wavelength_nm,counts", comments="")
print("wrote", data / "spectrum_digitised.csv", "and", data / "spectrum_osa.csv")
