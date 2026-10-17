"""Phase-matching calculator (the engine's ``PhaseMatchCalculator``, also ``ultrafast crystal`` on the CLI).

>>> from ultrafast import crystal
>>> r = crystal.phase_match("BBO", "opa", pump=515e-9, signal=800e-9, noncollinear="magic", length=3e-3)
>>> r["best"]["thetaDeg"], r["best"]["noncollinearDeg"], r["best"]["deffPmPerV"]

Built-in crystals: :func:`crystals`. A user crystal is a dict (``{"class": "uniaxial", "no": {...index model...},
"ne": {...}, "pointGroup": "3m", "d": {"d22": 2.2}}``, or ``{"base": "BBO", ...}`` to modify a built-in).
"""
from __future__ import annotations

import ctypes
import json
from typing import Any, Dict, Optional, Union

from .core import _b, _check, _lib, _take_string, capabilities


def crystals() -> list:
    """Names of the built-in chi(2) crystals."""
    return capabilities().get("crystals", [])


def phase_match(crystal: Union[str, dict], process: str = "shg", type: Optional[str] = None, *, fundamental: Optional[float] = None,
                pump: Optional[float] = None, signal: Optional[float] = None, idler: Optional[float] = None,
                a: Optional[float] = None, b: Optional[float] = None, plane: Optional[str] = None,
                theta_deg: Optional[float] = None, phi_deg: Optional[float] = None,
                noncollinear: Union[None, float, str] = None, qpm: Union[None, bool, float] = None,
                length: float = 1e-3, temperature_c: Optional[float] = None, pump_intensity: Optional[float] = None,
                polarizations: Optional[Dict[str, str]] = None, **extra: Any) -> dict:
    """Solve phase matching. Wavelengths in metres; ``noncollinear`` in degrees or ``"magic"``;
    ``qpm=True`` (period found) or a period in metres. ``type`` defaults to "I" (birefringent) or "0" (QPM). Returns the calculator's JSON as a dict with
    ``phaseMatched``, ``best`` (angles, deffPmPerV, waves, acceptance, gvm, gainSpectrum...) and ``solutions``."""
    wl = {k: v for k, v in dict(fundamental=fundamental, pump=pump, signal=signal, idler=idler, a=a, b=b).items() if v is not None}
    req: Dict[str, Any] = {"crystal": crystal, "process": process, "wavelengthsM": wl, "lengthM": length}
    if type is not None:
        req["type"] = type
    if plane is not None:
        req["plane"] = plane
    if theta_deg is not None:
        req["thetaDeg"] = theta_deg
    if phi_deg is not None:
        req["phiDeg"] = phi_deg
    if noncollinear is not None:
        req["noncollinearDeg"] = noncollinear
    if qpm is not None:
        req["qpm"] = qpm if isinstance(qpm, (bool, dict)) else {"periodM": float(qpm)}
    if temperature_c is not None:
        req["temperatureC"] = temperature_c
    if pump_intensity is not None:
        req["pumpIntensityWPerM2"] = pump_intensity
    if polarizations:
        req["polarizations"] = polarizations
    req.update(extra)
    pp = ctypes.c_void_p()
    _check(_lib().fn("uf_crystal_phase_match")(_b(json.dumps(req)), ctypes.byref(pp)))
    return json.loads(_take_string(pp))
