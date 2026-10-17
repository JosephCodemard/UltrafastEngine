"""Python side of the UltrafastEngine **fitting** plugin (plugins/fitting).

The engine side (C#) adds the ``"measured"`` instrument key and the ``"fit"`` run mode. This package adds:

* builders for those keys: :func:`measured`, :func:`fit`, :func:`fit_parameter`;
* the ``measurements`` and ``fit`` figures of ``ultrafast-plot`` / :func:`ultrafast.plotting.report`;
* :func:`comparisons` and :func:`fit_report`, typed access to a result's plugin sections.

It is imported automatically by :mod:`ultrafast` when the plugin is installed (``ultrafast.plugins.loaded()``).
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional

from .build import fit, fit_parameter, measured

__all__ = ["measured", "fit", "fit_parameter", "comparisons", "fit_report", "register"]


def comparisons(result) -> List[Dict[str, Any]]:
    """The instruments compared with recorded data (``summary["comparisons"]``), or []."""
    return list(result.summary.get("comparisons") or [])


def fit_report(result) -> Optional[Dict[str, Any]]:
    """The calibration report (``summary["fit"]``, same content as fit.json), or None."""
    return result.summary.get("fit")


def register(host) -> None:
    """Called by :func:`ultrafast.plugins.load_python_plugins`: registers the figures."""
    from . import figures
    host.register_figure("measurements", figures.measurements, after="instruments")
    host.register_figure("fit", figures.fit, after="measurements")
