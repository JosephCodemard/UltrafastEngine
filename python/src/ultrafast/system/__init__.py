"""Building and loading systems: :class:`Sequential` and :class:`Branched` element lists, :func:`link`, :func:`loop`,
:func:`virtual_input`, the :func:`system` builder and :class:`System`. The classes live in :mod:`ultrafast.build` (the low-level
builders); this package is where to look for them by what they are.

    from ultrafast.system import Sequential, Branched, link, system
"""
import json
from pathlib import Path
from typing import Any, Mapping, Union

from ..build import Branched, Sequential, link, loop, same_as, system, virtual_input
from ..core import System

__all__ = ["Sequential", "Branched", "link", "same_as", "loop", "virtual_input", "system", "System", "load", "save"]


def load(source: Any) -> System:
    """Open a system from a ``.ufs`` path, JSON text or a dict (same as :meth:`System.load`)."""
    return System.load(source)


def save(system_dict: Mapping[str, Any], path: Union[str, Path], indent: int = 2) -> Path:
    """Write a system dict (from :func:`system`) as a ``.ufs`` file and return the path."""
    p = Path(path)
    p.write_text(json.dumps(system_dict, indent=indent) + "\n", encoding="utf-8")
    return p
