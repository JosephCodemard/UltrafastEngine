"""Beam splitters, dichroics, leaky mirrors and delays: elements of branched systems.

Each CapWords name is an alias of a snake_case builder in :mod:`ultrafast.build` (the same function: signature, typed
return value and docstring are unchanged, so autocompletion is the same).
"""
from ..build import (beam_splitter, polarizing_splitter, dichroic, leak_mirror, delay)

BeamSplitter = beam_splitter
PolarizingSplitter = polarizing_splitter
Dichroic = dichroic
LeakyMirror = leak_mirror
Delay = delay

__all__ = ["BeamSplitter", "PolarizingSplitter", "Dichroic", "LeakyMirror", "Delay", "beam_splitter", "polarizing_splitter", "dichroic", "leak_mirror", "delay"]
