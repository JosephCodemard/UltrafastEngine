"""The synthetic lab: a hidden truth the twin has to find, with data you can generate anywhere.

The lab is a spectral filter (centre and bandwidth different from the nominal description) followed by a Kerr-like
nonlinear phase whose strength grows with the pulse energy (``bRad = slope x energy``, hidden slope), read by a spectrometer.
``saturating=True`` bends the energy law at high energy: a twin trained at low energies fits its training data and fails on
the held-out high energy -- the case the validation report must flag.

Everything is written as files (``system.ufs`` is the *believed* description, the data are CSVs of the *hidden* lab with noise),
so the project looks exactly like one made from real measurements.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List

import numpy as np

LAB_NAME = "synthetic-lab"
CENTRE_NM, FWHM_NM, SLOPE_RAD_PER_UJ = 1033.0, 8.5, 3.0     # the hidden truth
SATURATION_UJ = 2.5                                         # saturating lab: b = slope E / (1 + E / SATURATION_UJ)
TRAIN_UJ = (0.4, 0.8, 1.2, 1.6, 2.0)
HELD_UJ = (1.4, 3.0)                                        # inside and outside the training range
GRID = {"auto": False, "n": 4096, "windowS": 20e-12}
RESOLUTION_M = 0.2e-9


def lab_system(centre_nm: float, fwhm_nm: float, b_rad: float) -> Dict[str, Any]:
    from ultrafast import build as b
    return b.system(LAB_NAME, [
        {"id": "filter", "type": "transform", "operators": [
            {"kind": "spectralAmplitude", "shape": "gaussian", "fwhmNm": fwhm_nm, "centerWavelengthNm": centre_nm}]},
        {"id": "kerr", "type": "transform", "operators": [{"kind": "nonlinearPhase", "bRad": b_rad}]},
    ])


def true_b(energy_uj: float, saturating: bool) -> float:
    return SLOPE_RAD_PER_UJ * energy_uj / (1 + energy_uj / SATURATION_UJ) if saturating else SLOPE_RAD_PER_UJ * energy_uj


def base_run() -> Dict[str, Any]:
    from ultrafast import build as b
    return b.run_spec(b.pulse(1030e-9, 100e-15, energy=1e-6), grid=GRID,
                      instruments=[b.instrument("spectrometer", name="spec", resolutionM=RESOLUTION_M)])


def measure(energy_uj: float, saturating: bool, noise: float, rng: np.random.Generator) -> np.ndarray:
    """The hidden lab's spectrometer reading as an (N, 2) array: wavelength [nm], intensity."""
    import ultrafast as uf
    sysd = lab_system(CENTRE_NM, FWHM_NM, true_b(energy_uj, saturating))
    run = base_run()
    run["pulse"]["energyJ"] = energy_uj * 1e-6
    res = uf.run(sysd, run)
    lam, s = res["spec_wavelength_m"][::4], res["spec_spectrum"][::4]
    return np.c_[lam * 1e9, s + noise * s.max() * rng.standard_normal(s.size)]


def build_lab(folder: Path, saturating: bool = False, noise: float = 0.01, seed: int = 7) -> Dict[str, Any]:
    """Write ``system.ufs``, ``data/*.csv`` and return the ``twin.json`` content."""
    rng = np.random.default_rng(seed)
    folder = Path(folder)
    (folder / "data").mkdir(parents=True, exist_ok=True)
    # what the owner believes: nominal filter, a guess for the nonlinear phase
    (folder / "system.ufs").write_text(json.dumps(lab_system(1030.0, 12.0, 2.0), indent=2), encoding="utf-8")
    conditions: List[Dict[str, Any]] = []
    for held, energies in ((False, TRAIN_UJ), (True, HELD_UJ)):
        for e in energies:
            name = f"E{e:.1f}uJ"
            data = measure(e, saturating, noise, rng)
            np.savetxt(folder / "data" / f"{name}.csv", data, delimiter=",", header="wavelength_nm,intensity", comments="")
            conditions.append({"name": name, "heldOut": held, "variables": {"energyJ": e * 1e-6},
                               "set": {"pulse/energyJ": e * 1e-6}, "data": f"data/{name}.csv"})
    return {
        "format": "ultrafast-twin", "name": LAB_NAME + ("-saturating" if saturating else ""),
        "description": "A synthetic lab: a spectral filter and an energy-dependent nonlinear phase read by a spectrometer. The truth is hidden in "
                       "the data generator (ultrafast_twin.synthetic).",
        "system": "system.ufs", "run": base_run(), "instrument": "spec", "sigma": noise,
        "conditions": conditions,
        "parameters": [
            {"path": "element:filter/operators/0/fwhmNm", "name": "filter.fwhmNm", "initial": 12.0, "min": 2.0, "max": 30.0},
            {"path": "element:filter/operators/0/centerWavelengthNm", "name": "filter.centerNm", "initial": 1030.0, "min": 1015.0, "max": 1045.0},
            {"path": "element:kerr/operators/0/bRad", "name": "kerr.bRad",
             "dependence": {"kind": "proportional", "variable": "energyJ", "reference": 1e-6, "coefficients": [{"initial": 2.0, "min": 0.0, "max": 10.0}]}},
        ],
        "fit": {"freezeGrid": False, "maxIterations": 40},
    }
