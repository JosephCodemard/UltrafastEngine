"""Closed-form nonlinear-optics estimates (pure Python, no engine): critical power, Marburger collapse distance, B-integral, peak intensity, fluence.

>>> from ultrafast.calc import nonlinear
>>> pcr = nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23, n0=1.0003)       # ~3 GW in air
>>> nonlinear.collapse_distance_m(10 * pcr, 800e-9, waist_radius_m=1e-3, n0=1.0003, n2_m2_per_w=3e-23)
>>> r = nonlinear.b_integral_estimate(1030e-9, n2_m2_per_w=2.7e-20, length_m=5e-3, pulse_energy_j=1e-3,
...                                   duration_fwhm_s=200e-15, beam_radius_m=1e-3)
>>> r["bPeakRad"], r["bPulseAveragedRad"], r["bPulseAndBeamAveragedRad"]

Conventions. Beam radius ``w`` is the 1/e^2 intensity radius, durations are intensity FWHM, Gaussian beam ``I(r) = I0 exp(-2 r^2 / w^2)``,
``I0 = 2 P / (pi w^2)``. ``B = (2 pi / lambda) n2 I L`` is the peak nonlinear phase; the averaged values weight the intensity by itself
(the energy-weighted mean phase, what the engine reports as the pulse-averaged B-integral): a Gaussian pulse gives 1/sqrt(2) of the peak,
a sech^2 pulse 2/3, a Gaussian beam 1/2. Critical power ``P_cr = 3.77 lambda^2 / (8 pi n0 n2)`` (Marburger 1975; the same constant the
engine's ``criticalPower`` check uses); Marburger's collapse distance ``z_sf = 0.367 z_R / sqrt((sqrt(P/P_cr) - 0.852)^2 - 0.0219)`` with
``z_R = pi n0 w0^2 / lambda = k w0^2 / 2`` (an empirical fit to simulations, within about 20 % for 2.5 < P/P_cr < 20, valid for ``P > P_cr``).
Bad arguments raise ``ValueError``.
"""
from __future__ import annotations

import math
from typing import Optional, TypedDict

CRITICAL_POWER_FACTOR = 3.77                    # Marburger 1975, P_cr = factor lambda^2 / (8 pi n0 n2); equals PhysicalConstants.CriticalPowerFactor
_TOWNES_FACTOR = 3.7924                         # 2 x 1.8962: the Townes profile, P_cr = 1.8962 lambda^2 / (4 pi n0 n2)

# power-weighted mean of the intensity relative to its peak, int I^2 / (I0 int I)
TEMPORAL_AVERAGE = {"gaussian": 1 / math.sqrt(2), "sech2": 2 / 3}
SPATIAL_AVERAGE = 0.5                           # Gaussian beam
# peak power / (pulse energy / FWHM duration)
_PEAK_POWER_FACTOR = {"gaussian": 2 * math.sqrt(math.log(2) / math.pi), "sech2": 2 * math.log(1 + math.sqrt(2)) / 2.0}


class BIntegralEstimate(TypedDict):
    peakPowerW: float
    peakIntensityWPerM2: float
    peakFluenceJPerM2: float
    bPeakRad: float
    bPulseAveragedRad: float
    bBeamAveragedRad: float
    bPulseAndBeamAveragedRad: float
    criticalPowerW: float
    powerOverCritical: float


def _pos(v: float, name: str) -> float:
    x = float(v)
    if not math.isfinite(x) or x <= 0:
        raise ValueError(f"{name} must be a positive finite number (got {v!r})")
    return x


def _shape(shape: str) -> str:
    s = {"gauss": "gaussian", "sech": "sech2", "sech^2": "sech2"}.get(shape.lower(), shape.lower())
    if s not in TEMPORAL_AVERAGE:
        raise ValueError(f"unknown pulse shape '{shape}' (use gaussian or sech2)")
    return s


def critical_power_w(wavelength_m: float, *, n2_m2_per_w: float, n0: float = 1.0, profile: str = "marburger") -> float:
    """Critical power for self-focusing (W). ``profile``: ``"marburger"`` (factor 3.77, the Gaussian-beam fit used by the engine) or
    ``"townes"`` (3.7924, the self-trapped Townes profile)."""
    factor = {"marburger": CRITICAL_POWER_FACTOR, "townes": _TOWNES_FACTOR}.get(profile)
    if factor is None:
        raise ValueError("profile must be 'marburger' or 'townes'")
    return factor * _pos(wavelength_m, "wavelength_m") ** 2 / (8 * math.pi * _pos(n0, "n0") * _pos(n2_m2_per_w, "n2_m2_per_w"))


def collapse_distance_m(peak_power_w: float, wavelength_m: float, *, waist_radius_m: float, n2_m2_per_w: float, n0: float = 1.0) -> float:
    """Marburger's self-focusing (collapse) distance of a collimated Gaussian beam of waist ``waist_radius_m`` (1/e^2 radius), in metres.
    Raises ``ValueError`` for ``peak_power_w <= P_cr``: below the critical power the beam does not collapse."""
    pcr = critical_power_w(wavelength_m, n2_m2_per_w=n2_m2_per_w, n0=n0)
    p = _pos(peak_power_w, "peak_power_w")
    if p <= pcr:
        raise ValueError(f"peak_power_w = {p:.4g} W is not above the critical power {pcr:.4g} W: no collapse (Marburger's formula needs P > P_cr)")
    zr = math.pi * n0 * _pos(waist_radius_m, "waist_radius_m") ** 2 / wavelength_m
    return 0.367 * zr / math.sqrt((math.sqrt(p / pcr) - 0.852) ** 2 - 0.0219)


def peak_power_w(pulse_energy_j: float, duration_fwhm_s: float, shape: str = "gaussian") -> float:
    """Peak power of a transform-limited-shape pulse: ``0.9394 E / tau`` (Gaussian), ``0.8814 E / tau`` (sech^2)."""
    return _PEAK_POWER_FACTOR[_shape(shape)] * _pos(pulse_energy_j, "pulse_energy_j") / _pos(duration_fwhm_s, "duration_fwhm_s")


def peak_intensity_w_per_m2(peak_power_w: float, beam_radius_m: float) -> float:
    """Peak intensity of a Gaussian beam: ``I0 = 2 P / (pi w^2)``."""
    return 2 * _pos(peak_power_w, "peak_power_w") / (math.pi * _pos(beam_radius_m, "beam_radius_m") ** 2)


def peak_fluence_j_per_m2(pulse_energy_j: float, beam_radius_m: float) -> float:
    """Peak fluence of a Gaussian beam: ``F0 = 2 E / (pi w^2)`` (J/m^2; divide by 1e4 for J/cm^2)."""
    return 2 * _pos(pulse_energy_j, "pulse_energy_j") / (math.pi * _pos(beam_radius_m, "beam_radius_m") ** 2)


def b_integral_rad(wavelength_m: float, *, n2_m2_per_w: float, peak_intensity_w_per_m2: float, length_m: float) -> float:
    """B-integral of a constant intensity over ``length_m``: ``B = (2 pi / lambda) n2 I L`` (rad)."""
    return 2 * math.pi / _pos(wavelength_m, "wavelength_m") * float(n2_m2_per_w) * _pos(peak_intensity_w_per_m2, "peak_intensity_w_per_m2") * _pos(length_m, "length_m")


def b_integral_estimate(wavelength_m: float, *, n2_m2_per_w: float, length_m: float, pulse_energy_j: float, duration_fwhm_s: float,
                        beam_radius_m: float, shape: str = "gaussian", n0: Optional[float] = None) -> BIntegralEstimate:
    """B-integral of a Gaussian beam carrying a ``gaussian`` or ``sech2`` pulse through a medium of length ``length_m`` (no diffraction, no
    dispersion, instantaneous Kerr effect): the peak value (peak intensity on axis), the pulse-averaged value (x 1/sqrt(2) Gaussian, x 2/3
    sech^2), the beam-averaged value (x 1/2) and both. With ``n0`` the ratio to the critical power is also given (``nan`` otherwise)."""
    p = peak_power_w(pulse_energy_j, duration_fwhm_s, shape)
    i0 = peak_intensity_w_per_m2(p, beam_radius_m)
    b = b_integral_rad(wavelength_m, n2_m2_per_w=n2_m2_per_w, peak_intensity_w_per_m2=i0, length_m=length_m)
    t = TEMPORAL_AVERAGE[_shape(shape)]
    pcr = critical_power_w(wavelength_m, n2_m2_per_w=n2_m2_per_w, n0=n0 if n0 is not None else 1.0) if n0 is not None and n2_m2_per_w > 0 else math.nan
    return {"peakPowerW": p, "peakIntensityWPerM2": i0, "peakFluenceJPerM2": peak_fluence_j_per_m2(pulse_energy_j, beam_radius_m),
            "bPeakRad": b, "bPulseAveragedRad": b * t, "bBeamAveragedRad": b * SPATIAL_AVERAGE, "bPulseAndBeamAveragedRad": b * t * SPATIAL_AVERAGE,
            "criticalPowerW": pcr, "powerOverCritical": p / pcr if pcr == pcr else math.nan}
