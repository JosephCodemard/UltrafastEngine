"""Phase-matching calculator (the engine's ``PhaseMatchCalculator``, also ``ultrafast crystal`` on the CLI).

>>> from ultrafast.calc import crystal
>>> r = crystal.phase_match("BBO", "opa", pump_m=515e-9, signal_m=800e-9, noncollinear="magic", length_m=3e-3)
>>> r["best"]["thetaDeg"], r["best"]["noncollinearDeg"], r["best"]["deffPmPerV"]

Built-in crystals: :func:`crystals`. A user crystal is a dict (``{"class": "uniaxial", "no": {...index model...},
"ne": {...}, "pointGroup": "3m", "d": {"d22": 2.2}}``, or ``{"base": "BBO", ...}`` to modify a built-in).
"""
from __future__ import annotations

import ctypes
import json
from typing import Any, Dict, Optional, Union

from ._compat import engine_call, renamed_kwargs


def crystals() -> list:
    """Names of the built-in chi(2) crystals."""
    from ..core import capabilities
    return capabilities().get("crystals", [])


_RENAMES = {"fundamental": "fundamental_m", "pump": "pump_m", "signal": "signal_m", "idler": "idler_m", "a": "a_m", "b": "b_m",
            "length": "length_m", "pump_intensity": "pump_intensity_w_per_m2"}


@renamed_kwargs(_RENAMES)
def phase_match(crystal: Union[str, dict], process: str = "shg", type: Optional[str] = None, *, fundamental_m: Optional[float] = None,
                pump_m: Optional[float] = None, signal_m: Optional[float] = None, idler_m: Optional[float] = None,
                a_m: Optional[float] = None, b_m: Optional[float] = None, plane: Optional[str] = None,
                theta_deg: Optional[float] = None, phi_deg: Optional[float] = None,
                noncollinear: Union[None, float, str] = None, qpm: Union[None, bool, float] = None,
                length_m: float = 1e-3, temperature_c: Optional[float] = None, pump_intensity_w_per_m2: Optional[float] = None,
                polarizations: Optional[Dict[str, str]] = None, **extra: Any) -> dict:
    """Solve phase matching. Wavelengths (``*_m``) and ``length_m`` in metres; ``noncollinear`` in degrees or ``"magic"``;
    ``qpm=True`` (period found) or a period in metres. ``type`` defaults to "I" (birefringent) or "0" (QPM). Returns the calculator's JSON as a dict with
    ``phaseMatched``, ``best`` (angles, deffPmPerV, waves, acceptance, gvm, gainSpectrum...) and ``solutions``."""
    wl = {k: v for k, v in dict(fundamental=fundamental_m, pump=pump_m, signal=signal_m, idler=idler_m, a=a_m, b=b_m).items() if v is not None}
    req: Dict[str, Any] = {"crystal": crystal, "process": process, "wavelengthsM": wl, "lengthM": length_m}
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
    if pump_intensity_w_per_m2 is not None:
        req["pumpIntensityWPerM2"] = pump_intensity_w_per_m2
    if polarizations:
        req["polarizations"] = polarizations
    req.update(extra)
    from ..core import _b, _check, _lib, _take_string

    def go() -> dict:
        pp = ctypes.c_void_p()
        _check(_lib().fn("uf_crystal_phase_match")(_b(json.dumps(req)), ctypes.byref(pp)))
        return json.loads(_take_string(pp))
    return engine_call(go)
