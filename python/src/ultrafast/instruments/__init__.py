"""Instruments, one factory per kind, plus :func:`axis` and :func:`detector`.

    from ultrafast.instruments import Spectrometer, Axis
    Spectrometer(at="end", name="osa", resolutionM=0.1e-9)

Each factory is :func:`ultrafast.build.instrument` with the kind fixed; keyword arguments are the instrument's parameters in the
engine's names (docs/reference/instruments.md).
"""
from typing import Any, Optional

from ..build import axis, detector, detector_preset, detector_response, detector_segment, instrument, measured

Instrument = instrument
Axis = axis
Detector = detector
Measured = measured

__all__ = ["Instrument", "Axis", "Detector", "Measured", "instrument", "axis", "detector", "detector_preset", "detector_response",
           "detector_segment", "measured", "EnergyMeter", "Spectrometer", "Scope", "Autocorrelator", "Frog", "XFrog", "DScan", "Contrast", "Polarimeter", "PulseSummary", "Compressor", "Conversion", "PhaseMatchingCurve", "DelayScan", "BeamProfile", "CrossCorrelator", "Camera", "WavefrontSensor"]


def EnergyMeter(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Pulse energy (or power) through a range. Kind ``energyMeter``; parameters: docs/reference/instruments.md."""
    return instrument("energyMeter", at, name, **params)


def Spectrometer(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Spectrum on a wavelength axis, with resolution. Kind ``spectrometer``; parameters: docs/reference/instruments.md."""
    return instrument("spectrometer", at, name, **params)


def Scope(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Photodiode / oscilloscope trace with a finite rise time. Kind ``scope``; parameters: docs/reference/instruments.md."""
    return instrument("scope", at, name, **params)


def Autocorrelator(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Intensity or interferometric autocorrelation. Kind ``autocorrelator``; parameters: docs/reference/instruments.md."""
    return instrument("autocorrelator", at, name, **params)


def Frog(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Second-harmonic, polarisation-gate or self-diffraction FROG trace. Kind ``frog``; parameters: docs/reference/instruments.md."""
    return instrument("frog", at, name, **params)


def XFrog(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Cross-correlation FROG with a known reference pulse. Kind ``xfrog``; parameters: docs/reference/instruments.md."""
    return instrument("xfrog", at, name, **params)


def DScan(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Dispersion scan: SHG spectrum versus inserted material or GDD. Kind ``dscan``; parameters: docs/reference/instruments.md."""
    return instrument("dscan", at, name, **params)


def Contrast(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Intensity in dB versus time: pre- and post-pulses. Kind ``contrast``; parameters: docs/reference/instruments.md."""
    return instrument("contrast", at, name, **params)


def Polarimeter(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Stokes parameters. Kind ``polarimeter``; parameters: docs/reference/instruments.md."""
    return instrument("polarimeter", at, name, **params)


def PulseSummary(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Every scalar pulse metric. Kind ``pulseSummary``; parameters: docs/reference/instruments.md."""
    return instrument("pulseSummary", at, name, **params)


def Compressor(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Ideal compressor: the GDD / TOD that compresses best. Kind ``compressor``; parameters: docs/reference/instruments.md."""
    return instrument("compressor", at, name, **params)


def Conversion(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Energy and photon number conversion in an active element. Kind ``conversion``; parameters: docs/reference/instruments.md."""
    return instrument("conversion", at, name, **params)


def PhaseMatchingCurve(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Phase-matching efficiency versus angle, wavelength or temperature. Kind ``phaseMatchingCurve``; parameters: docs/reference/instruments.md."""
    return instrument("phaseMatchingCurve", at, name, **params)


def DelayScan(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Conversion versus delay between pump and signal. Kind ``delayScan``; parameters: docs/reference/instruments.md."""
    return instrument("delayScan", at, name, **params)


def BeamProfile(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Fluence versus position after an element with a transverse model. Kind ``beamProfile``; parameters: docs/reference/instruments.md."""
    return instrument("beamProfile", at, name, **params)


def CrossCorrelator(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Third-order cross-correlator (contrast). Kind ``crossCorrelator``; parameters: docs/reference/instruments.md."""
    return instrument("crossCorrelator", at, name, **params)


def Camera(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Near-field or far-field camera with a described sensor. Kind ``camera``; parameters: docs/reference/instruments.md."""
    return instrument("camera", at, name, **params)


def WavefrontSensor(at: str = "end", name: Optional[str] = None, **params: Any) -> dict:
    """Shack-Hartmann-like wavefront map and Zernike coefficients (Noll, rms waves). Kind ``wavefrontSensor``; parameters: docs/reference/instruments.md."""
    return instrument("wavefrontSensor", at, name, **params)
