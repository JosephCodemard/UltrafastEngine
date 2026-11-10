"""Python side of the UltrafastEngine **ao** plugin (plugins/ao): adaptive optics, beam shaping and pulse shaping.

Engine side (C#): the elements ``deformableMirror``, ``slm``, ``spectralShaper`` and ``aopdf``. Here:

* builders (:func:`DeformableMirror`, :func:`Slm`, :func:`SpectralShaper`, :func:`Aopdf`) returning the element dicts;
* NumPy twins of the devices (:mod:`ultrafast_ao.devices`);
* :class:`DmBench`, a simulated AO bench that drives the engine (calibrate, correct, closed loop, sensorless metrics);
* :func:`tiled_array` and :func:`combining_metrics` for coherent tiled-aperture arrays (the core tile source);
* the algorithms live in :mod:`ultrafast.algorithms` (``holography``, ``control``, ``sensorless``) and work without the plugin.

Docs: docs/plugins/ao.md.
"""
from __future__ import annotations

from . import devices
from .benches import DmBench
from .build import Aopdf, DeformableMirror, Slm, SpectralShaper
from .tiled import combining_metrics, tile_count, tiled_array

__all__ = ["DeformableMirror", "Slm", "SpectralShaper", "Aopdf", "DmBench", "tiled_array", "tile_count", "combining_metrics", "devices", "register"]


def register(host) -> None:
    """Called by :func:`ultrafast.plugins.load_python_plugins`; the plugin adds no figure (plots are in :mod:`ultrafast.plotting`)."""
    return None
