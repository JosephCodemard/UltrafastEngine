"""Waveplates, polarisers, rotators.

Each CapWords name is an alias of a snake_case builder in :mod:`ultrafast.build` (the same function: signature, typed
return value and docstring are unchanged, so autocompletion is the same).
"""
from ..build import (waveplate, polarizer, thin_film_polarizer, faraday_rotator)

Waveplate = waveplate
Polarizer = polarizer
ThinFilmPolarizer = thin_film_polarizer
FaradayRotator = faraday_rotator

__all__ = ["Waveplate", "Polarizer", "ThinFilmPolarizer", "FaradayRotator", "waveplate", "polarizer", "thin_film_polarizer", "faraday_rotator"]
