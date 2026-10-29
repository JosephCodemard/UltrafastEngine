"""Python side of the UltrafastEngine **hitran** plugin (plugins/hitran).

The engine side (C#) adds the index/attenuation model ``kind: "hitran"``: line-by-line Voigt absorption and
Kramers-Kronig-consistent dispersion of H2O, CO2 and O2, used as the lab air. The line data are *not* shipped (HITRAN has its
own licence and terms of use): download them once with :func:`download` (or ``python -m ultrafast_hitran download H2O``,
which needs the HITRAN API: ``pip install hitran-api``).

>>> import ultrafast as uf
>>> from ultrafast import build as b
>>> import ultrafast_hitran as hit
>>> env = b.environment(air=hit.air(humidity=0.45, temperature_c=22.0, pressure_pa=101325.0))
>>> sysd = b.system("cell", [b.gap("air", 10.0)], environment=env)
>>> run = b.run_spec(b.pulse(1.03e-6, 150e-15, energy=1e-6), grid=b.grid(tail=b.tail_grid(window_s=2e-9)))

It is imported automatically by :mod:`ultrafast` when the plugin is installed (``ultrafast.plugins.loaded()``).
"""
from __future__ import annotations

from .build import MOLECULES, air, species
from .download import data_dir, download, write_csv, write_partition

__all__ = ["air", "species", "download", "write_csv", "write_partition", "data_dir", "MOLECULES", "register"]


def register(host) -> None:
    """Called by :func:`ultrafast.plugins.load_python_plugins`. The plugin adds no figures; nothing to register."""
    return None
