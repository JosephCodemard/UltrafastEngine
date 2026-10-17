"""Builders for the fitting plugin's run-spec keys (plain dicts, like :mod:`ultrafast.build`).

>>> from ultrafast import build as b
>>> from ultrafast_fitting import measured, fit, fit_parameter
>>> spec = b.run_spec(b.pulse(800e-9, 40e-15, energy=200e-9),
...                   instruments=[b.instrument("spectrometer", measured=measured("spectrum.csv", sigma=0.005))],
...                   fit=fit([fit_parameter("pulse/energyJ", 50e-9, 500e-9, name="energy")]))
"""
from __future__ import annotations

from typing import Any, Optional, Sequence


def _clean(d):
    return {k: v for k, v in d.items() if v is not None}


def measured(file: Optional[str] = None, *, values: Optional[dict] = None, sigma: Optional[float] = None,
             normalize: Optional[str] = None, background: Any = None, columns: Optional[dict] = None,
             range_x: Optional[Sequence[float]] = None, value_sigma: Optional[float] = None, weight: Optional[float] = None,
             **extra) -> dict:
    """Recorded data for an instrument (docs/measurements.md): a CSV / NPZ / JSON ``file`` and/or scalar ``values``
    ({name: value | [value, sigma]}). Use as ``instrument("autocorrelator", measured=measured("ac.csv", sigma=0.02))``.
    Relative file names are resolved against the run file's folder (or the working directory for dict specs)."""
    return _clean({"file": file, "values": values, "sigma": sigma, "normalize": normalize, "background": background,
                   "columns": columns, "rangeX": list(range_x) if range_x is not None else None, "valueSigma": value_sigma,
                   "weight": weight, **extra})


def fit_parameter(path: str, min: Optional[float] = None, max: Optional[float] = None, *, name: Optional[str] = None,
                  initial: Optional[float] = None, unit: Optional[str] = None, scale: Optional[float] = None,
                  step: Optional[float] = None) -> dict:
    """One free parameter of a calibration fit: ``path`` like "pulse/energyJ", "element:comp/gddFs2",
    "material:FusedSilica/n2" (docs/measurements.md#calibration-fit)."""
    return _clean({"path": path, "min": min, "max": max, "name": name, "initial": initial, "unit": unit, "scale": scale, "step": step})


def fit(parameters: Sequence[dict], max_iterations: Optional[int] = None, tolerance: Optional[float] = None,
        freeze_grid: Optional[bool] = None) -> dict:
    """The "fit" block of a run spec: calibrate ``parameters`` (from :func:`fit_parameter`) against every instrument
    with measured data. ``uf.run`` then returns the best run with ``summary["fit"]``."""
    return _clean({"parameters": list(parameters), "maxIterations": max_iterations, "tolerance": tolerance, "freezeGrid": freeze_grid})
