"""Calculators: closed-form and engine-backed helpers that answer a design question in one call.

>>> from ultrafast import calc
>>> calc.tbp("sech2"); calc.limit("gaussian", duration_s=100e-15)       # pulse: time-bandwidth product, transform limit, chirp
>>> calc.crystal.phase_match("BBO", "shg", fundamental_m=800e-9)        # crystal: phase matching (engine)
>>> calc.cell.cell_mode(1030e-9, mirror_radius_m=0.35, separation_m=0.54)  # cell: Kerr-matched multi-pass cell mode (engine)
>>> calc.beam.single_lens_match(waist_in_m=0.3e-3, waist_out_m=0.1e-3, wavelength_m=1030e-9, focal_length_m=0.5)   # beam: pure Python
>>> calc.nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23, n0=1.0003)                                       # nonlinear: pure Python

Submodules: :mod:`pulse` (tbp, shapes, limit, analyze, measure), :mod:`crystal` (phase_match, crystals), :mod:`cell` (cell_mode),
:mod:`beam` (Gaussian beam, ABCD, mode matching, telescope, M^2), :mod:`nonlinear` (critical power, collapse distance, B-integral,
intensity, fluence). ``beam``, ``nonlinear`` and ``pulse.shapes / tbp`` need only NumPy (no engine, decision E7). The pulse functions
are also available here (``ultrafast.calc.tbp`` ...) under their old names. Arguments are SI with the unit in the name; bad input
raises ``ValueError``; results are typed dicts (autocompletion).
"""
from . import beam, cell, crystal, nonlinear, pulse
from .pulse import analyze, limit, measure, shapes, tbp

__all__ = ["beam", "cell", "crystal", "nonlinear", "pulse", "analyze", "limit", "measure", "shapes", "tbp"]
