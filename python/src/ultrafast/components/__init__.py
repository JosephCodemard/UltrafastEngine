"""Optical components by kind. ``components.BeamSplitter`` is :func:`ultrafast.build.beam_splitter`: same function, same typed
return value, a CapWords name (the snake_case names work here too).

    from ultrafast.components import BeamSplitter, Mirror, Waveplate

Groups: optics, splitters, polarisation, crystals, fibres, amplifiers, media.
"""
from .optics import *  # noqa: F401,F403
from .splitters import *  # noqa: F401,F403
from .polarisation import *  # noqa: F401,F403
from .crystals import *  # noqa: F401,F403
from .fibres import *  # noqa: F401,F403
from .amplifiers import *  # noqa: F401,F403
from .media import *  # noqa: F401,F403
from . import optics, splitters, polarisation, crystals, fibres, amplifiers, media  # noqa: F401

__all__ = [n for m in (optics, splitters, polarisation, crystals, fibres, amplifiers, media) for n in m.__all__]
