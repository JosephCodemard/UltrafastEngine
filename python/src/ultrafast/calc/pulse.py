"""Pulse calculator: time-bandwidth product (TBP), transform limit, stretch factor, chirp (also ``ultrafast pulse`` on the CLI).

>>> from ultrafast.calc import pulse
>>> pulse.limit("sech2", duration_s=100e-15)["bandwidthFwhmHz"]                  # transform-limited bandwidth
>>> r = pulse.analyze(duration_s=300e-15, bandwidth_m=10e-9, center_m=1030e-9)   # TBP, ratio to the limit, GDD
>>> r["timeBandwidthProduct"], r["stretchFactor"], r["gddMagnitudeFs2"]
>>> pulse.measure(time_s, intensity, spectrum, wavelength_m=wl)                  # from measured arrays (FWHM and rms TBP)

``shapes()`` and ``tbp()`` are closed forms and work without the engine (decision E7); ``limit``, ``analyze`` and ``measure`` call the
engine's ``PulseCalculator`` (one implementation for the CLI, the C API and Python). Durations and bandwidths are intensity FWHM; the
bandwidth is in Hz or in metres (with ``center_m``). Errors in the arguments raise ``ValueError``.

Main functions: shapes() -> list[ShapeRow], tbp(shape) -> float, limit(...), analyze(...), measure(...).
"""
from __future__ import annotations

import ctypes
import json
import math
from typing import Any, Dict, List, Optional, Sequence, TypedDict

from ._compat import engine_call, renamed_kwargs

_RENAMES = {"duration": "duration_s", "center": "center_m"}


class ShapeRow(TypedDict):
    """One row of :func:`shapes`."""
    shape: str
    timeBandwidthProduct: float


class PulseReport(TypedDict, total=False):
    """Result of :func:`limit`, :func:`analyze` and :func:`measure` (the keys present depend on the call)."""
    shape: str
    durationFwhmS: float
    bandwidthFwhmHz: float
    bandwidthFwhmM: float
    centerWavelengthM: float
    timeBandwidthProduct: float
    timeBandwidthProductRms: float
    rmsLimitProduct: float
    transformLimitProduct: float
    ratioToLimit: float
    transformLimitedDurationS: float
    stretchFactor: float
    gddMagnitudeFs2: float
    note: str


# Closed forms, intensity FWHM in time x intensity FWHM in frequency (mirrors PulseCalculator.TbpOf; a test compares both).
_X_SINC = 1.3915574                                                         # sinc^2(x) = 1/2
_TBP = {
    "gaussian": 2 * math.log(2) / math.pi,                                  # 0.4413
    "sech2": (2 * math.log(1 + math.sqrt(2)) / math.pi) ** 2,               # 0.3148
    "lorentzian": math.log(2) * math.sqrt(math.sqrt(2) - 1) / math.pi,      # 0.1420; field 1/(1+(t/tau)^2)
    "asymmetric-exponential": math.log(2) / (2 * math.pi),                  # 0.1103; I = exp(-t/tau), t >= 0
    "rectangular": 2 * _X_SINC / math.pi,                                   # 0.8859
}
_ALIASES = {"gauss": "gaussian", "sech": "sech2", "sech^2": "sech2", "square": "rectangular", "rect": "rectangular",
            "lorentz": "lorentzian", "asym-exp": "asymmetric-exponential", "asymmetric_exponential": "asymmetric-exponential",
            "exponential": "asymmetric-exponential"}


def _alias(shape: str) -> str:
    s = shape.lower()
    return _ALIASES.get(s, s)


def _call(req: Dict[str, Any]) -> dict:
    from ..core import _b, _check, _lib, _take_string

    def go() -> dict:
        pp = ctypes.c_void_p()
        _check(_lib().fn("uf_pulse_calc")(_b(json.dumps(req)), ctypes.byref(pp)))
        return json.loads(_take_string(pp))
    return engine_call(go)


def _widths(req: Dict[str, Any], duration, bandwidth_hz, bandwidth_m, center) -> Dict[str, Any]:
    for key, v in (("durationFwhmS", duration), ("bandwidthFwhmHz", bandwidth_hz), ("bandwidthFwhmM", bandwidth_m), ("centerWavelengthM", center)):
        if v is not None:
            req[key] = float(v)
    return req


def shapes() -> List[ShapeRow]:
    """The pulse shapes with a closed-form TBP: ``[{"shape": "gaussian", "timeBandwidthProduct": 0.4413}, ...]`` (no engine needed)."""
    return [{"shape": k, "timeBandwidthProduct": v} for k, v in _TBP.items()]


def tbp(shape: str = "gaussian") -> float:
    """Transform-limited time-bandwidth product (intensity FWHM x FWHM) of a shape: gaussian 0.4413, sech2 0.3148, lorentzian 0.1420
    (field 1/(1+(t/tau)^2)), asymmetric-exponential 0.1103 (I = exp(-t/tau), t >= 0), rectangular 0.8859. No engine needed."""
    key = _alias(shape)
    if key not in _TBP:
        raise ValueError(f"unknown pulse shape '{shape}' (use {', '.join(_TBP)})")
    return _TBP[key]


@renamed_kwargs(_RENAMES)
def limit(shape: str = "gaussian", *, duration_s: Optional[float] = None, bandwidth_hz: Optional[float] = None,
          bandwidth_m: Optional[float] = None, center_m: Optional[float] = None) -> PulseReport:
    """Transform limit: give the duration (s) *or* a bandwidth (Hz, or m with ``center_m``) and get the other."""
    return _call(_widths({"op": "limit", "shape": _alias(shape)}, duration_s, bandwidth_hz, bandwidth_m, center_m))  # type: ignore[return-value]


@renamed_kwargs(_RENAMES)
def analyze(*, duration_s: float, bandwidth_hz: Optional[float] = None, bandwidth_m: Optional[float] = None,
            center_m: Optional[float] = None, shape: str = "gaussian") -> PulseReport:
    """TBP of a pulse from its FWHM duration and bandwidth, the ratio to the limit of ``shape``, the stretch factor and
    (Gaussian) the GDD magnitude in fs^2 that would compress it."""
    return _call(_widths({"op": "analyze", "shape": _alias(shape)}, duration_s, bandwidth_hz, bandwidth_m, center_m))  # type: ignore[return-value]


def measure(time_s: Sequence[float], intensity: Sequence[float], spectrum: Sequence[float], *, wavelength_m: Optional[Sequence[float]] = None,
            frequency_hz: Optional[Sequence[float]] = None, shape: str = "gaussian") -> PulseReport:
    """TBP from measured arrays: a temporal intensity and a spectrum (per wavelength or per frequency). Also returns
    ``timeBandwidthProductRms`` (limit 1/4pi for a Gaussian)."""
    req: Dict[str, Any] = {"op": "measure", "shape": _alias(shape), "timeS": [float(x) for x in time_s], "intensity": [float(x) for x in intensity],
                           "spectrum": [float(x) for x in spectrum]}
    if wavelength_m is not None:
        req["wavelengthM"] = [float(x) for x in wavelength_m]
    if frequency_hz is not None:
        req["frequencyHz"] = [float(x) for x in frequency_hz]
    return _call(req)  # type: ignore[return-value]
