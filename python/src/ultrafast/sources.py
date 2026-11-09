"""Pulses, beams and measured spectra: everything that describes the light entering a system.

    from ultrafast.sources import Pulse, GaussianBeam, MeasuredSpectrum

CapWords aliases of the builders in :mod:`ultrafast.build` (same functions, typed returns, docstrings).
"""
from .build import (aux_beam, constant_beam, cw, gaussian_beam, initial_profile, measured_spectrum, measured_spectrum_csv, pedestal,
                    profile_noise, profile_ripple, pulse, sampled_beam)

Pulse = pulse
Cw = cw
MeasuredSpectrum = measured_spectrum
MeasuredSpectrumCsv = measured_spectrum_csv
Pedestal = pedestal
GaussianBeam = gaussian_beam
ConstantBeam = constant_beam
SampledBeam = sampled_beam
AuxBeam = aux_beam
InitialProfile = initial_profile
ProfileRipple = profile_ripple
ProfileNoise = profile_noise

__all__ = ["Pulse", "Cw", "MeasuredSpectrum", "MeasuredSpectrumCsv", "Pedestal", "GaussianBeam", "ConstantBeam", "SampledBeam", "AuxBeam",
           "InitialProfile", "ProfileRipple", "ProfileNoise", "pulse", "cw", "measured_spectrum", "measured_spectrum_csv", "pedestal",
           "gaussian_beam", "constant_beam", "sampled_beam", "aux_beam", "initial_profile", "profile_ripple", "profile_noise"]
