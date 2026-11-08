"""Kerr-matched mode of a two-mirror multi-pass cell (Herriott or quasi-concentric): the engine's ``cellMode`` command.

>>> from ultrafast import cells
>>> r = cells.cell_mode(1030e-9, mirror_radius=0.35, separation=0.54)            # empty cell, Kogelnik-Li mode
>>> r["emptyCell"]["waistRadiusM"], r["emptyCell"]["radiusAtMirror1M"]
>>> r = cells.cell_mode(800e-9, mirror_radius=1.5, separation=2.9, peak_power=2e9, gas={"species": "Ar", "pressureBar": 0.4})
>>> r["matched"], r["mode"]["waistRadiusM"], r["bIntegralTotalRad"]
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional, Sequence, Union

from .core import command


def cell_mode(wavelength: float, *, mirror_radius: Union[None, float, Sequence[float]] = None, separation: Optional[float] = None,
              reentrant: Optional[Dict[str, int]] = None, passes: int = 2, peak_power: float = 0.0, gas: Optional[Dict[str, Any]] = None,
              plates: Optional[List[Dict[str, Any]]] = None, lens_model: str = "secondMoment", slices: int = 12) -> dict:
    """Eigenmode of a cell of two concave mirrors, with the Kerr lens of the intracavity medium at ``peak_power`` (W).

    Lengths in metres. ``mirror_radius``: one radius (both mirrors) or ``[R1, R2]``. ``separation``: mirror spacing; or
    ``reentrant={"roundTrips": N, "order": k}`` for the spacing that closes a Herriott pattern of equal mirrors after N round trips.
    ``gas``: ``{"species": "Ar", "pressureBar": 0.4, "temperatureK": 293.15}`` fills the cell; ``plates``: list of
    ``{"thicknessM", "positionM" (centre, from mirror 1), "material" or "index", "n2M2PerW"}``. ``lens_model``: ``"secondMoment"`` (default,
    reproduces the rms radius of the radial model) or ``"paraxial"`` (on-axis lens, 4 times stronger). Returns the engine's dict:
    ``emptyCell`` and ``mode`` (waistRadiusM, waistDistanceFromLaunchM, radiusAtLaunchM, radiusAtMirror1M / 2M, q), ``matched``,
    ``stability``, ``bIntegralPerRoundTripRad`` ... The launch plane is mirror 1. Round beams, peak power, instantaneous Kerr effect."""
    req: Dict[str, Any] = {"command": "cellMode", "wavelengthM": wavelength, "passes": passes, "peakPowerW": peak_power, "lensModel": lens_model, "slices": slices}
    if mirror_radius is not None:
        if isinstance(mirror_radius, (int, float)):
            req["mirrorRadiusM"] = float(mirror_radius)
        else:
            req["mirrorRadiiM"] = [float(v) for v in mirror_radius]
    if separation is not None:
        req["separationM"] = separation
    if reentrant is not None:
        req["reentrant"] = reentrant
    if gas is not None:
        req["gas"] = gas
    if plates is not None:
        req["plates"] = plates
    return command(req)
