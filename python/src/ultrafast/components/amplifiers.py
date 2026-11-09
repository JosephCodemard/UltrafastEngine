"""Gain media, pumps, amplifiers, saturable absorbers, thermal lenses and resonators.

Each CapWords name is an alias of a snake_case builder in :mod:`ultrafast.build` (the same function: signature, typed
return value and docstring are unchanged, so autocompletion is the same).
"""
from ..build import (gain, pump, multipass_amplifier, regenerative_amplifier, saturable_absorber, thermal_lens, resonator)

Gain = gain
Pump = pump
MultipassAmplifier = multipass_amplifier
RegenerativeAmplifier = regenerative_amplifier
SaturableAbsorber = saturable_absorber
ThermalLens = thermal_lens
Resonator = resonator

__all__ = ["Gain", "Pump", "MultipassAmplifier", "RegenerativeAmplifier", "SaturableAbsorber", "ThermalLens", "Resonator", "gain", "pump", "multipass_amplifier", "regenerative_amplifier", "saturable_absorber", "thermal_lens", "resonator"]
