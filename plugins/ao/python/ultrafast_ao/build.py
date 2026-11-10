"""Builders for the ao plugin elements (plain dicts, like :mod:`ultrafast.build`; the keys are the engine's, see docs/plugins/ao.md).

>>> from ultrafast_ao import DeformableMirror, Slm
>>> dm = DeformableMirror("dm", actuators_x=12, pitch_m=0.4e-3, commands_m=[0.0] * 144, stroke_m=2e-6)
>>> slm = Slm("slm", 8e-6, pixels_x=1920, pixels_y=1080, phase_target_rad=target)       # doctest: +SKIP
"""
from __future__ import annotations

from typing import Any, Dict, Optional, Sequence

import numpy as np


def _plain(v):
    """numpy arrays and scalars to plain lists / numbers (the run is JSON)."""
    if v is None:
        return None
    if isinstance(v, np.ndarray):
        return v.tolist()
    if isinstance(v, np.generic):
        return v.item()
    return v


def _clean(d: Dict[str, Any]) -> Dict[str, Any]:
    return {k: _plain(v) for k, v in d.items() if v is not None}


def DeformableMirror(id: str, *, pitch_m: float, actuators_x: Optional[int] = None, actuators_y: Optional[int] = None, layout: Optional[str] = None,
                     ring_counts: Optional[Sequence[int]] = None, positions_x_m: Any = None, positions_y_m: Any = None, commands_m: Any = None,
                     influence_coupling: Optional[float] = None, stroke_m: Optional[float] = None, drive_bits: Optional[int] = None,
                     incidence_angle_rad: Optional[float] = None, clear_aperture_radius_m: Optional[float] = None, margin_pitches: Optional[float] = None,
                     reflectivity: Optional[float] = None, samples_per_actuator: Optional[int] = None, design_wavelength_m: Optional[float] = None, **extra) -> dict:
    """A parametric deformable mirror (generic, no vendor data). ``commands_m``: surface displacement per actuator, positive away from the incoming beam
    (a longer path); a list in layout order, or an ``[actuators_x][actuators_y]`` array for the square layout. The wavefront is ``2 cos(incidence_angle_rad)``
    times the surface. ``influence_coupling`` is the surface value at a neighbouring actuator (default 0.15). Compiles to a ``modulator``."""
    return _clean({"id": id, "type": "deformableMirror", "pitchM": pitch_m, "actuatorsX": actuators_x, "actuatorsY": actuators_y, "layout": layout,
                   "ringCounts": ring_counts, "positionsXM": positions_x_m, "positionsYM": positions_y_m, "commandsM": commands_m,
                   "influenceCoupling": influence_coupling, "strokeM": stroke_m, "driveBits": drive_bits, "incidenceAngleRad": incidence_angle_rad,
                   "clearApertureRadiusM": clear_aperture_radius_m, "marginPitches": margin_pitches, "reflectivity": reflectivity,
                   "samplesPerActuator": samples_per_actuator, "designWavelengthM": design_wavelength_m, **extra})


def Slm(id: str, pitch_m: float, *, pixels_x: Optional[int] = None, pixels_y: Optional[int] = None, grey_level: Any = None, phase_target_rad: Any = None,
        grey_levels: Optional[int] = None, lut_kind: Optional[str] = None, max_phase_rad: Optional[float] = None, lut_steepness: Optional[float] = None,
        lut_phase_rad: Any = None, fill_factor: Optional[float] = None, gap_transmission: Optional[float] = None, outside_transmission: Optional[float] = None,
        design_wavelength_m: Optional[float] = None, dispersion: Optional[str] = None, delta_index_cauchy: Any = None, crosstalk_sigma_cells: Optional[float] = None,
        zero_order_leak: Optional[float] = None, offset_x_m: Optional[float] = None, offset_y_m: Optional[float] = None, file: Optional[str] = None, **extra) -> dict:
    """A parametric phase-only liquid-crystal SLM (generic, no vendor data). Give ``grey_level`` (integers) or ``phase_target_rad`` (wrapped to 2 pi and mapped to
    the nearest grey level of the voltage-to-phase curve), as an array ``[pixels_x][pixels_y]``, a callback ``"@name"`` or an NPZ ``file``. Large panels: use a
    callback or a file, not a list. ``dispersion`` "liquidCrystal" (default): the 2 pi written at ``design_wavelength_m`` is ``2 pi s(lambda)`` at lambda."""
    return _clean({"id": id, "type": "slm", "pitchM": pitch_m, "pixelsX": pixels_x, "pixelsY": pixels_y, "greyLevel": grey_level, "phaseTargetRad": phase_target_rad,
                   "greyLevels": grey_levels, "lutKind": lut_kind, "maxPhaseRad": max_phase_rad, "lutSteepness": lut_steepness, "lutPhaseRad": lut_phase_rad,
                   "fillFactor": fill_factor, "gapTransmission": gap_transmission, "outsideTransmission": outside_transmission,
                   "designWavelengthM": design_wavelength_m, "dispersion": dispersion, "deltaIndexCauchy": delta_index_cauchy,
                   "crosstalkSigmaCells": crosstalk_sigma_cells, "zeroOrderLeak": zero_order_leak, "offsetXM": offset_x_m, "offsetYM": offset_y_m, "file": file, **extra})


def SpectralShaper(id: str, *, pixels: int, pixel_pitch_m: float, dispersion_m_per_m: Optional[float] = None, spot_radius_m: Optional[float] = None,
                   grating_lines_per_mm: Optional[float] = None, incidence_angle_rad: Optional[float] = None, focal_length_m: Optional[float] = None,
                   input_beam_radius_m: Optional[float] = None, order: Optional[float] = None, phase_rad: Any = None, amplitude: Any = None,
                   fill_factor: Optional[float] = None, gap_transmission: Optional[float] = None, outside_transmission: Optional[float] = None,
                   center_wavelength_m: Optional[float] = None, offset_m: Optional[float] = None, phase_wrap_rad: Optional[float] = None,
                   grey_levels: Optional[int] = None, **extra) -> dict:
    """A pixelated spectral phase / amplitude mask in the Fourier plane of a 4f line (the 1-D SLM pulse shaper). Give ``dispersion_m_per_m`` (focal-plane
    position per wavelength) or the grating (``grating_lines_per_mm``, ``incidence_angle_rad``, ``focal_length_m``), and ``spot_radius_m`` or
    ``input_beam_radius_m``. The pulse's grid must cover the replica pulses at ``2 pi / pixel_bandwidth`` (``run_spec(grid={"windowS": ...})``)."""
    return _clean({"id": id, "type": "spectralShaper", "pixels": pixels, "pixelPitchM": pixel_pitch_m, "dispersionMPerM": dispersion_m_per_m, "spotRadiusM": spot_radius_m,
                   "gratingLinesPerMm": grating_lines_per_mm, "incidenceAngleRad": incidence_angle_rad, "focalLengthM": focal_length_m,
                   "inputBeamRadiusM": input_beam_radius_m, "order": order, "phaseRad": phase_rad, "amplitude": amplitude, "fillFactor": fill_factor,
                   "gapTransmission": gap_transmission, "outsideTransmission": outside_transmission, "centerWavelengthM": center_wavelength_m,
                   "offsetM": offset_m, "phaseWrapRad": phase_wrap_rad, "greyLevels": grey_levels, **extra})


def Aopdf(id: str, *, delay_fs: Optional[float] = None, gdd_fs2: Optional[float] = None, tod_fs3: Optional[float] = None, fod_fs4: Optional[float] = None,
          center_wavelength_m: Optional[float] = None, spectral_phase_rad: Any = None, amplitude_table: Any = None, table_wavelengths_m: Any = None,
          window_s: Optional[float] = None, efficiency: Optional[float] = None, replica_amplitude: Optional[float] = None, replica_delay_s: Optional[float] = None,
          **extra) -> dict:
    """An acousto-optic programmable dispersive filter: a programmed spectral phase (delay, GDD, TOD, FOD at ``center_wavelength_m``, or a table), a finite
    acoustic window ``window_s`` (components whose programmed group delay leaves +-window/2 are not diffracted), a diffraction ``efficiency`` and an optional
    spurious replica pulse."""
    return _clean({"id": id, "type": "aopdf", "delayFs": delay_fs, "gddFs2": gdd_fs2, "todFs3": tod_fs3, "fodFs4": fod_fs4, "centerWavelengthM": center_wavelength_m,
                   "spectralPhaseRad": spectral_phase_rad, "amplitudeTable": amplitude_table, "tableWavelengthsM": table_wavelengths_m, "windowS": window_s,
                   "efficiency": efficiency, "replicaAmplitude": replica_amplitude, "replicaDelayS": replica_delay_s, **extra})
