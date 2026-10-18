"""Building mode-locked oscillators (docs/reference/oscillators.md).

>>> from ultrafast.laser import Oscillator
>>> osc = (Oscillator("laser", model="haus", rep_rate_hz=80e6, output_coupler=0.05)
...        .simple_gain(1.5, saturation_energy_j=5e-9, bandwidth_m=100e-9, shape="gaussian")
...        .loss(0.02).dispersion(gdd_fs2=-500).spm(1.5e-6)
...        .absorber("kerrLens", modulation_depth=0.03, kappa_per_w=3e-8))
>>> sysd = osc.system()          # a system file with the oscillator as its only element

The run's "pulse" sets the grid (centre wavelength, window) and, with ``start("pulse")``, the seed.
"""
from __future__ import annotations

import copy
import math
from typing import Any, Dict, List, Optional

from . import build as b

C = 299792458.0


class Oscillator:
    """Fluent builder for an ``oscillator`` element (models "haus", "cavity", "parametric")."""

    def __init__(self, id: str = "laser", model: str = "haus", rep_rate_hz: Optional[float] = None,
                 cavity_length_m: Optional[float] = None, ring: bool = False, output_coupler: float = 0.05):
        if rep_rate_hz is None and cavity_length_m is None and model != "parametric":
            raise ValueError("give rep_rate_hz or cavity_length_m")
        self.d: Dict[str, Any] = {"id": id, "type": "oscillator", "model": model, "outputCoupler": output_coupler}
        if rep_rate_hz is not None:
            self.d["repRateHz"] = rep_rate_hz
        if cavity_length_m is not None:
            self.d["cavityLengthM"] = cavity_length_m
            self.d["ring"] = ring
        self._elements: List[dict] = []

    # ---------------------------------------------------------------- gain
    def simple_gain(self, small_signal_gain: float, saturation_energy_j: Optional[float] = None,
                    saturation_power_w: Optional[float] = None, bandwidth_m: Optional[float] = None,
                    center_wavelength_m: Optional[float] = None, shape: str = "lorentzian",
                    fluence_saturation_energy_j: Optional[float] = None) -> "Oscillator":
        """Lumped gain: power gain per round trip ``small_signal_gain``, saturated by the intracavity pulse energy
        (or average power), spectral shape of FWHM ``bandwidth_m``."""
        self.d["gain"] = b._clean({"smallSignalGain": small_signal_gain, "saturationEnergyJ": saturation_energy_j,
                                   "saturationPowerW": saturation_power_w, "bandwidthM": bandwidth_m,
                                   "centerWavelengthM": center_wavelength_m, "shape": shape,
                                   "fluenceSaturationEnergyJ": fluence_saturation_energy_j})
        return self

    def gain_medium(self, medium: Any, length_m: float, pump: dict, mode_radius_m: float, passes: int = 2,
                    slices: int = 8, relaxation: float = 0.2, **extra) -> "Oscillator":
        """A real gain medium (cavity model): cw pump, inversion from the average powers, spectral gain, host
        dispersion and SPM on every pass."""
        self.d["model"] = "cavity"
        self.d["gain"] = b._clean({"medium": medium, "lengthM": length_m, "pump": pump, "modeRadiusM": mode_radius_m,
                                   "passesPerRoundTrip": passes, "slices": slices, "relaxation": relaxation,
                                   "thermalLens": False, **extra})
        return self

    # ---------------------------------------------------------------- haus terms
    def loss(self, fraction: float) -> "Oscillator":
        self.d["lossPerRoundTrip"] = fraction
        return self

    def dispersion(self, gdd_fs2: float = 0.0, tod_fs3: float = 0.0) -> "Oscillator":
        self.d["gddFs2"], self.d["todFs3"] = gdd_fs2, tod_fs3
        return self

    def spm(self, rad_per_w: float) -> "Oscillator":
        """Self-phase modulation per round trip: phase = rad_per_w x power."""
        self.d["spmRadPerW"] = rad_per_w
        return self

    def absorber(self, kind: str = "slow", **params) -> "Oscillator":
        """Saturable absorber: haus model -> the lumped absorber; cavity model -> a saturableAbsorber element appended
        to the cavity. Parameters as in docs: modulation_depth, nonsaturable_loss, recovery_time_s,
        saturation_fluence_j_per_m2 + spot_radius_m (or saturation_energy_j), saturation_power_w, kappa_per_w."""
        p = {_camel(k): v for k, v in params.items()}
        p["kind"] = kind
        if self.d["model"] == "cavity":
            self._elements.append({"id": p.pop("id", "absorber"), "type": "saturableAbsorber", **p})
        else:
            self.d["absorber"] = p
        return self

    # ---------------------------------------------------------------- cavity elements
    def add(self, *elements: dict) -> "Oscillator":
        """Append engine elements (as built with ultrafast.build) to the cavity round trip, in order."""
        self.d["model"] = "cavity"
        self._elements.extend(elements)
        return self

    # ---------------------------------------------------------------- run control
    def start(self, mode: str = "noise", seed_energy_j: Optional[float] = None, seed: Optional[int] = None) -> "Oscillator":
        self.d["start"] = mode
        if seed_energy_j is not None:
            self.d["seedEnergyJ"] = seed_energy_j
        if seed is not None:
            self.d["seed"] = seed
        return self

    def round_trips(self, max: int = 5000, tolerance: float = 1e-5, check_every: int = 25, snapshots: int = 120) -> "Oscillator":
        self.d["roundTrips"] = {"max": max, "tolerance": tolerance, "checkEvery": check_every, "snapshots": snapshots}
        return self

    # ---------------------------------------------------------------- output
    def element(self) -> dict:
        d = copy.deepcopy(self.d)
        if self._elements:
            d["elements"] = copy.deepcopy(self._elements)
        return d

    def system(self, name: Optional[str] = None, after: Optional[List[dict]] = None) -> dict:
        """A system file: the oscillator, then optionally ``after`` (e.g. a compressor, an amplifier)."""
        return b.system(name or self.d["id"], [self.element(), *(after or [])])

    @staticmethod
    def soliton_fwhm_s(gdd_s2: float, spm_rad_per_w: float, energy_j: float) -> float:
        """Soliton area theorem: FWHM = 3.53 |GDD| / (delta E) (sech^2 pulse, lumped cavity)."""
        return 3.53 * abs(gdd_s2) / (spm_rad_per_w * energy_j)

    @staticmethod
    def kerr_spm_coefficient(n2: float, length_m: float, mode_radius_m: float, wavelength_m: float, passes: int = 2) -> float:
        """delta (rad/W) of a Kerr medium: k0 n2 L passes / (pi w^2 / 2)."""
        return 2 * math.pi / wavelength_m * n2 * length_m * passes / (math.pi * mode_radius_m ** 2 / 2)


def _camel(k: str) -> str:
    special = {"modulation_depth": "modulationDepth", "nonsaturable_loss": "nonsaturableLoss", "recovery_time_s": "recoveryTimeS",
               "saturation_fluence_j_per_m2": "saturationFluenceJPerM2", "spot_radius_m": "spotRadiusM",
               "saturation_energy_j": "saturationEnergyJ", "saturation_power_w": "saturationPowerW", "kappa_per_w": "kappaPerW",
               "area_m2": "areaM2", "saturation_intensity_w_per_m2": "saturationIntensityWPerM2"}
    if k in special:
        return special[k]
    parts = k.split("_")
    return parts[0] + "".join(p.title() for p in parts[1:])
