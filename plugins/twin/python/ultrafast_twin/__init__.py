"""Python side of the UltrafastEngine **twin** plugin (plugins/twin): a digital twin of a lab.

A twin project is a folder (``twin.json``, ``data/``, ``fits/``, ``reports/``). The fit is the fitting plugin's multi-condition fit
(one set of shared parameters, parametric or smooth dependence on the condition, held-out conditions). Here:

* :class:`TwinProject` -- load, :func:`init` a project (a synthetic lab included), ``fit()``, ``twin()``;
* :class:`Twin` -- the fitted twin: ``predict`` any condition with credible bands, ``validate``, ``design`` the next experiment,
  ``whatif`` comparisons, ``track`` drift with an ensemble Kalman filter;
* :mod:`ultrafast_twin.mcp` -- a workspace-scoped MCP server (stdio) for agents; :mod:`ultrafast_twin.goals` -- goal files.

Command line: ``ultrafast twin <init|fit|validate|predict|design|track|check|serve>`` or ``python -m ultrafast_twin``.
Docs: docs/plugins/twin.md.
"""
from __future__ import annotations

from .dependence import DependenceModel
from .project import TwinProject, init
from .twin import Twin

__all__ = ["TwinProject", "Twin", "DependenceModel", "init", "register"]


def register(host) -> None:
    """Called by :func:`ultrafast.plugins.load_python_plugins`; the twin adds no figure."""
    return None
