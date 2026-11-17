"""Old import path of :mod:`ultrafast.calc.crystal` (kept; the code moved into the ``calc`` package)."""
from .calc.crystal import crystals, phase_match  # noqa: F401
from .core import _b, _check, _lib, _take_string, capabilities  # noqa: F401  (names of the 2.2.0 module, kept for old imports)
