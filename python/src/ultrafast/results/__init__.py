"""Results and simple metrics: the live :class:`Result`, a saved results folder (:class:`SavedResult`, :func:`load`) and a few
curve metrics that need only NumPy.

    from ultrafast.results import fwhm
    fwhm(t, intensity)
"""
from __future__ import annotations

from typing import Sequence

import numpy as np

from ..core import Result
from ..plotting.data import SavedResult, load

__all__ = ["Result", "SavedResult", "load", "fwhm", "rms_width", "strehl_marechal"]


def fwhm(x: Sequence[float], y: Sequence[float]) -> float:
    """Full width at half maximum of a single-peaked curve, with linear interpolation at the two half-maximum crossings."""
    x = np.asarray(x, float)
    y = np.asarray(y, float)
    half = y.max() / 2.0
    above = np.flatnonzero(y >= half)
    i0, i1 = above[0], above[-1]

    def cross(i_in, i_out):
        if i_out < 0 or i_out >= len(y):
            return x[i_in]
        return x[i_out] + (half - y[i_out]) * (x[i_in] - x[i_out]) / (y[i_in] - y[i_out])

    return float(cross(i1, i1 + 1) - cross(i0, i0 - 1))


def rms_width(x: Sequence[float], y: Sequence[float]) -> float:
    """sqrt of the second central moment of the weights ``y`` along ``x``."""
    x = np.asarray(x, float)
    w = np.asarray(y, float)
    m = np.sum(w * x) / np.sum(w)
    return float(np.sqrt(np.sum(w * (x - m) ** 2) / np.sum(w)))


def strehl_marechal(rms_waves: float) -> float:
    """Marechal estimate of the Strehl ratio from the rms wavefront error in waves."""
    from ..algorithms.zernike import strehl_marechal as _s
    return _s(rms_waves)
