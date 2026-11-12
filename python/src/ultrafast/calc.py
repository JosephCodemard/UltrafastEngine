"""Pulse calculator: time-bandwidth product (TBP), transform limit, stretch factor, chirp (also ``ultrafast pulse`` on the CLI).

>>> from ultrafast import calc
>>> calc.limit("sech2", duration=100e-15)["bandwidthFwhmHz"]                  # transform-limited bandwidth
>>> r = calc.analyze(duration=300e-15, bandwidth_m=10e-9, center=1030e-9)     # TBP, ratio to the limit, GDD
>>> r["timeBandwidthProduct"], r["stretchFactor"], r["gddMagnitudeFs2"]
>>> calc.measure(time_s, intensity, spectrum, wavelength_m=wl)                # from measured arrays (FWHM and rms TBP)

Durations and bandwidths are intensity FWHM; the bandwidth is in Hz or in metres (with ``center``). Shapes: :func:`shapes`.
"""
from __future__ import annotations

import ctypes
import json
from typing import Any, Dict, Optional, Sequence

from .core import _b, _check, _lib, _take_string


def _call(req: Dict[str, Any]) -> dict:
    pp = ctypes.c_void_p()
    _check(_lib().fn("uf_pulse_calc")(_b(json.dumps(req)), ctypes.byref(pp)))
    return json.loads(_take_string(pp))


def _widths(req: Dict[str, Any], duration, bandwidth_hz, bandwidth_m, center) -> Dict[str, Any]:
    for key, v in (("durationFwhmS", duration), ("bandwidthFwhmHz", bandwidth_hz), ("bandwidthFwhmM", bandwidth_m), ("centerWavelengthM", center)):
        if v is not None:
            req[key] = float(v)
    return req


def shapes() -> list:
    """The pulse shapes with a closed-form TBP: ``[{"shape": "gaussian", "timeBandwidthProduct": 0.4413}, ...]``."""
    return _call({"op": "shapes"})["shapes"]


def tbp(shape: str = "gaussian") -> float:
    """Transform-limited time-bandwidth product (intensity FWHM x FWHM) of a shape: gaussian 0.4413, sech2 0.3148."""
    return next(s["timeBandwidthProduct"] for s in shapes() if s["shape"] == _alias(shape))


def _alias(shape: str) -> str:
    s = shape.lower()
    return {"gauss": "gaussian", "sech": "sech2", "sech^2": "sech2", "square": "rectangular", "rect": "rectangular"}.get(s, s)


def limit(shape: str = "gaussian", *, duration: Optional[float] = None, bandwidth_hz: Optional[float] = None,
          bandwidth_m: Optional[float] = None, center: Optional[float] = None) -> dict:
    """Transform limit: give the duration (s) *or* a bandwidth (Hz, or m with ``center``) and get the other."""
    return _call(_widths({"op": "limit", "shape": shape}, duration, bandwidth_hz, bandwidth_m, center))


def analyze(*, duration: float, bandwidth_hz: Optional[float] = None, bandwidth_m: Optional[float] = None,
            center: Optional[float] = None, shape: str = "gaussian") -> dict:
    """TBP of a pulse from its FWHM duration and bandwidth, the ratio to the limit of ``shape``, the stretch factor and
    (Gaussian) the GDD magnitude in fs^2 that would compress it."""
    return _call(_widths({"op": "analyze", "shape": shape}, duration, bandwidth_hz, bandwidth_m, center))


def measure(time_s: Sequence[float], intensity: Sequence[float], spectrum: Sequence[float], *, wavelength_m: Optional[Sequence[float]] = None,
            frequency_hz: Optional[Sequence[float]] = None, shape: str = "gaussian") -> dict:
    """TBP from measured arrays: a temporal intensity and a spectrum (per wavelength or per frequency). Also returns
    ``timeBandwidthProductRms`` (limit 1/4pi for a Gaussian)."""
    req: Dict[str, Any] = {"op": "measure", "shape": shape, "timeS": [float(x) for x in time_s], "intensity": [float(x) for x in intensity],
                           "spectrum": [float(x) for x in spectrum]}
    if wavelength_m is not None:
        req["wavelengthM"] = [float(x) for x in wavelength_m]
    if frequency_hz is not None:
        req["frequencyHz"] = [float(x) for x in frequency_hz]
    return _call(req)
