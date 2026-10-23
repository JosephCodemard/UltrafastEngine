"""Python side of the UltrafastEngine **fitting** plugin (plugins/fitting).

The engine side (C#) adds the ``transform`` element, the ``"measured"`` instrument key and the ``"fit"`` and
``"backward"`` run modes. This package adds:

* builders: :func:`transform` with its operators (:func:`spectral_phase`, :func:`spectral_amplitude`, :func:`energy`,
  :func:`delay`, :func:`jones`, :func:`replica`, :func:`phase_noise`, :func:`pedestal`, :func:`wavefront`),
  :func:`fit_value`, :func:`fit`, :func:`fit_parameter`, :func:`backward`, :func:`measured`;
* the ``measurements`` and ``fit`` figures of ``ultrafast-plot`` / :func:`ultrafast.plotting.report`;
* :func:`comparisons`, :func:`fit_report`, :func:`fitted_system` and :func:`reconstructed_pulse`: typed access to a
  result's plugin sections.

It is imported automatically by :mod:`ultrafast` when the plugin is installed (``ultrafast.plugins.loaded()``).
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional

from .build import (backward, delay, energy, fit, fit_parameter, fit_value, jones, measured, pedestal, phase_noise, replica,
                    spectral_amplitude, spectral_phase, transform, wavefront)

__all__ = ["transform", "spectral_phase", "spectral_amplitude", "energy", "delay", "jones", "replica", "phase_noise", "pedestal",
           "wavefront", "fit_value", "fit", "fit_parameter", "backward", "measured", "comparisons", "fit_report",
           "fitted_system", "reconstructed_pulse", "register"]


def comparisons(result) -> List[Dict[str, Any]]:
    """The instruments compared with recorded data (``summary["comparisons"]``), or []."""
    return list(result.summary.get("comparisons") or [])


def fit_report(result) -> Optional[Dict[str, Any]]:
    """The calibration report (``summary["fit"]``, same content as fit.json), or None."""
    return result.summary.get("fit")


def fitted_system(result) -> Optional[Dict[str, Any]]:
    """The system with the fitted values in place (as written to fitted-system.ufs): run it on new inputs."""
    f = result.summary.get("fit")
    if not f:
        return None
    return _read_written(result, "fitted-system.ufs")


def reconstructed_pulse(result) -> Optional[Dict[str, Any]]:
    """The field found by backward propagation as a pulse object (as written to reconstructed-pulse.json)."""
    if not result.summary.get("backward"):
        return None
    return _read_written(result, "reconstructed-pulse.json")


def _read_written(result, name: str) -> Optional[Dict[str, Any]]:
    import json
    import os
    import tempfile
    d = getattr(result, "directory", None)
    if not d or not os.path.exists(os.path.join(d, name)):
        d = tempfile.mkdtemp(prefix="uf-fit-")
        result.write(d)
    p = os.path.join(d, name)
    if not os.path.exists(p):
        return None
    with open(p, encoding="utf-8") as fh:
        return json.load(fh)


def register(host) -> None:
    """Called by :func:`ultrafast.plugins.load_python_plugins`: registers the figures."""
    from . import figures
    host.register_figure("measurements", figures.measurements, after="instruments")
    host.register_figure("fit", figures.fit, after="measurements")
