"""chi(2) crystals (SHG, SFG, DFG, OPA).

Each CapWords name is an alias of a snake_case builder in :mod:`ultrafast.build` (the same function: signature, typed
return value and docstring are unchanged, so autocompletion is the same).
"""
from ..build import (crystal)

Crystal = crystal

__all__ = ["Crystal", "crystal"]
