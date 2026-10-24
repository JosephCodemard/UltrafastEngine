"""UltrafastEngine -- nonlinear ultrafast pulse propagation through complete optical systems.

Quick start::

    import ultrafast as uf
    from ultrafast import build as b

    system = uf.System.load(b.system("plate", [
        b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(w0_m=100e-6, z_waist_m=2.5e-3)),
    ]))
    result = uf.run(system, b.run_spec(b.pulse(1.03e-6, 200e-15, energy=20e-6)))
    print(result.summary["output"]["fwhmS"], result["output_spectrum"].shape)

Docs: docs/reference/python.md. The engine itself is the C# UltrafastEngine.Core, reached through its C API
(``include/ultrafast.h``) -- see :mod:`ultrafast._native` for how the library is found.
"""
from .core import (ElementContext, Result, ResponseContext, Run, System, UltrafastError, capabilities, coherence, command, retrieve, run,
                   study, version)
from . import build
from . import crystal
from . import laser
from . import plugins
from . import materials
from . import measured
from . import types

__version__ = "2.1.8b0"
__all__ = ["System", "Run", "Result", "ResponseContext", "ElementContext", "UltrafastError", "run", "version", "capabilities",
           "command", "study", "coherence", "retrieve", "build", "crystal", "laser", "plugins", "materials", "measured", "types", "plot", "load"]


def plot(result, what: str = "overview", ax=None):
    """Quick matplotlib plots of a :class:`Result` or a CLI results folder ("overview", "measurements", "spectrum",
    "temporal", "phase", "losses", "elements"). Full reports: :func:`ultrafast.plotting.report`."""
    from .plotting import plot as _plot
    return _plot(result, what, ax)


def load(directory):
    """Open a results folder written by the CLI (``summary.json`` + ``result.npz``) -- no engine needed."""
    from .plotting import load as _load
    return _load(directory)
