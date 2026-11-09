"""Optical fibres.

Each CapWords name is an alias of a snake_case builder in :mod:`ultrafast.build` (the same function: signature, typed
return value and docstring are unchanged, so autocompletion is the same).
"""
from ..build import (fiber)

Fiber = fiber

__all__ = ["Fiber", "fiber"]
