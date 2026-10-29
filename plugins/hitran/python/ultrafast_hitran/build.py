"""Builders for the hitran plugin: the lab air with HITRAN lines (``environment.air``)."""
from __future__ import annotations

from typing import Any, Dict, Mapping, Optional, Sequence, Union

#: HITRAN molecule numbers of the supported species
MOLECULES: Dict[str, int] = {"H2O": 1, "CO2": 2, "O2": 7}


def species(h2o: Union[bool, str, Mapping[str, Any]] = True, co2: Union[bool, str, Mapping[str, Any]] = False,
            o2: Union[bool, str, Mapping[str, Any]] = False) -> Dict[str, Any]:
    """The absorbing species. Each is ``True`` (read ``<name>.par`` or ``<name>.csv`` from the data folder), a file name, or
    ``{"file": ..., "moleFraction": ..., "partitionExponent": ..., "partitionFile": ...}``. The partition sums (TIPS) are read from
    ``<NAME>.partition.csv`` next to the line file when the downloader wrote one; without it Q ~ T^1.5 (H2O) or T (CO2, O2). H2O is required for humid air; CO2 is weak near 1 um;
    O2 matters at 1.27 um. The mole fractions default to the humidity (H2O), ``co2_ppm`` (CO2) and 0.2095 (O2)."""
    out: Dict[str, Any] = {}
    for name, v in (("H2O", h2o), ("CO2", co2), ("O2", o2)):
        if v:
            out[name] = v
    return out


def air(*, humidity: float = 0.4, temperature_c: float = 20.0, pressure_pa: float = 101325.0, co2_ppm: float = 450.0,
        species: Optional[Mapping[str, Any]] = None, range_cm1: Sequence[float] = (6500.0, 14000.0), cutoff_cm1: float = 25.0,
        base_index: str = "ciddor", data_dir: Optional[str] = None, apply_to_gaps: bool = True, line_scale: float = 1.0) -> Dict[str, Any]:
    """Air with HITRAN lines, for ``ultrafast.build.environment(air=...)`` or one ``gap(air=...)``.

    Humidity (relative, 0..1), temperature, pressure and CO2 set the Ciddor index and the number densities; the lines are
    Voigt profiles with temperature-scaled strengths and widths (n_air), the pressure shift (delta_air), a ``cutoff_cm1`` cut-off
    and no continuum. ``base_index="vacuum"`` leaves out the smooth Ciddor index. With ``apply_to_gaps=True`` (the default
    here) every air gap uses it; add ``grid=build.grid(tail=...)`` to carry the free-induction-decay tail of the lines.
    """
    d: Dict[str, Any] = {"kind": "hitran", "applyToGaps": apply_to_gaps, "temperatureC": temperature_c, "pressurePa": pressure_pa,
                         "humidity": humidity, "co2ppm": co2_ppm, "species": dict(species) if species else {"H2O": True},
                         "rangeCm1": [float(range_cm1[0]), float(range_cm1[1])], "cutoffCm1": cutoff_cm1, "baseIndex": base_index}
    if data_dir:
        d["dataDir"] = str(data_dir)
    if line_scale != 1.0:
        d["lineScale"] = line_scale
    return d
