"""Measured data: recordings compared with the simulated instruments, and typed access to the comparisons.

Measured data is part of the engine (no plugin needed). Two places take recordings:

* the **input pulse** from a measured spectrum -- :func:`ultrafast.build.measured_spectrum` /
  :func:`ultrafast.build.measured_spectrum_csv` (sorted, repeated wavelengths merged, background removed, the
  recording's resolution deconvolved on request, partial ranges extended with Gaussian tails on request);
* an **instrument's** ``measured`` key -- :func:`ultrafast.build.measured` -- which compares the simulated reading
  with the recording on the recording's own axis, only where the simulated instrument has readings, after matching
  the recording's resolution.

The comparisons come back in ``result.summary["comparisons"]``; this module gives them types::

    import ultrafast as uf
    from ultrafast import build as b
    spec = b.run_spec(b.pulse(1.03e-6, 200e-15, energy=1e-6), instruments=[
        b.instrument("spectrometer", name="osa", resolutionM=0.1e-9,
                     measured=b.measured("osa.csv", background="auto", resolution_m=0.5e-9))])
    res = uf.run(system, spec)
    c = res.comparison("osa")            # Comparison (TypedDict): chi2, rmsError, notes, ...
    curve = uf.measured.curve(res, "osa")  # measured_x, measured_y, simulated_on_measured (NumPy)

Docs: docs/reference/measured-data.md.
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional, TypedDict

import numpy as np

__all__ = ["Comparison", "ComparedValue", "MeasuredCurve", "MeasuredTrace", "comparisons", "comparison", "curve", "trace", "total_chi2"]


class ComparedValue(TypedDict, total=False):
    """One scalar reading compared with its recorded value."""
    measured: float
    simulated: float
    sigma: float
    deviation: float
    """simulated - measured"""


class Comparison(TypedDict, total=False):
    """An instrument compared with recorded data (one entry of ``summary["comparisons"]``)."""
    instrument: str
    """Instrument name."""
    kind: str
    """Instrument kind (spectrometer, autocorrelator, dscan, ...)."""
    at: str
    """Where the instrument looks (element id, "input", "end")."""
    mode: str
    """"curve", "trace" (2-D) or "values" (scalar readings only)."""
    points: int
    """Number of residuals (compared points + scalar values)."""
    chi2: float
    """Sum of squared residuals ((simulated - measured) / sigma)^2, after the weight."""
    reducedChi2: float
    """chi2 / points."""
    rmsError: float
    """rms(simulated - measured) / peak(measured) on the compared curve or trace (the FROG G error for traces)."""
    file: str
    """The data file as given."""
    normalization: str
    """peak | maximum | area | scale | none."""
    xAxis: str
    """Name of the simulated array the curve was compared on (e.g. "wavelength_m")."""
    rowAxis: str
    colAxis: str
    values: Dict[str, ComparedValue]
    """Scalar readings: {name: {measured, simulated, sigma, deviation}}."""
    notes: List[str]
    """What was done: units assumed, background subtracted, points outside the simulated range, resolution matched..."""


class MeasuredCurve(TypedDict):
    """A compared curve on the measured axis (SI units), normalised as in the comparison."""
    measured_x: np.ndarray
    measured_y: np.ndarray
    simulated_on_measured: np.ndarray
    measured_sigma: Optional[np.ndarray]


class MeasuredTrace(TypedDict):
    """A compared 2-D trace on the measured axes, [rows, cols]."""
    measured_rows: np.ndarray
    measured_cols: np.ndarray
    measured_trace: np.ndarray
    simulated_on_measured: np.ndarray


def comparisons(result: Any) -> List[Comparison]:
    """Every instrument compared with recorded data (a :class:`ultrafast.Result` or a results folder), or []."""
    return list(_summary(result).get("comparisons") or [])


def comparison(result: Any, instrument: str) -> Comparison:
    """The comparison of one instrument (by name). KeyError if it has none."""
    for c in comparisons(result):
        if c.get("instrument") == instrument:
            return c
    raise KeyError(f"instrument '{instrument}' was not compared with measured data "
                   f"(compared: {', '.join(c.get('instrument', '?') for c in comparisons(result)) or 'none'})")


def total_chi2(result: Any) -> float:
    """Sum of the chi^2 of all comparisons (``summary["measuredChi2"]``), 0 if none."""
    return float(_summary(result).get("measuredChi2") or 0.0)


def curve(result: Any, instrument: str) -> MeasuredCurve:
    """The measured curve and the simulated reading on the same (measured) axis, as compared."""
    comparison(result, instrument)
    p = _sanitize(instrument)
    sig = f"{p}_measured_sigma"
    return MeasuredCurve(measured_x=result[f"{p}_measured_x"], measured_y=result[f"{p}_measured_y"],
                         simulated_on_measured=result[f"{p}_simulated_on_measured"],
                         measured_sigma=result[sig] if _has(result, sig) else None)


def trace(result: Any, instrument: str) -> MeasuredTrace:
    """The measured 2-D trace and the simulated trace on the same axes, as compared ([rows, cols])."""
    comparison(result, instrument)
    p = _sanitize(instrument)
    r, c = result[f"{p}_measured_rows"], result[f"{p}_measured_cols"]
    return MeasuredTrace(measured_rows=r, measured_cols=c,
                         measured_trace=np.asarray(result[f"{p}_measured_trace"]).reshape(len(r), len(c)),
                         simulated_on_measured=np.asarray(result[f"{p}_simulated_on_measured"]).reshape(len(r), len(c)))


def _summary(result: Any) -> Dict[str, Any]:
    if isinstance(result, dict):
        return result
    if not hasattr(result, "summary"):
        from .plotting import load
        result = load(result)
    return result.summary


def _sanitize(s: str) -> str:
    return "".join(ch if ch.isalnum() or ch == "_" else "_" for ch in s)


def _has(result: Any, name: str) -> bool:
    try:
        return name in result.names()
    except Exception:
        try:
            result[name]
            return True
        except Exception:
            return False
