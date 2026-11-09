"""Moved to :mod:`ultrafast.plotting.diagrams`. This module keeps the old import path working and warns when it is imported."""
import warnings as _warnings

from .plotting import diagrams as _diagrams
from .plotting.diagrams import *  # noqa: F401,F403

globals().update({k: v for k, v in vars(_diagrams).items() if not (k.startswith("__") and k.endswith("__"))})

_warnings.warn("ultrafast.diagram moved to ultrafast.plotting.diagrams; the old name will keep working but is deprecated",
               DeprecationWarning, stacklevel=2)
