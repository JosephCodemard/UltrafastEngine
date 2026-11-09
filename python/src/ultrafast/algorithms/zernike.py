"""Zernike polynomials in Noll ordering and normalisation, in plain NumPy (no engine needed).

The same convention as the engine's ``Zernike`` class (``Optics/ComponentTerms.cs``) and the ``wavefrontSensor`` instrument: index
``j`` starts at 1, ``m < 0`` is the sine term, and ``Z_j`` is scaled so that its rms over the unit disc is 1. A coefficient is
therefore the rms contribution of that term, in the unit of the wavefront (waves, nm, ...). Piston is j = 1, tilt j = 2, 3,
defocus j = 4.

    from ultrafast.algorithms import zernike
    c = zernike.fit(w, x, y, radius=1e-3, max_noll=15)         # w: wavefront map [x][y]
    w2 = zernike.evaluate_map(c, x, y, radius=1e-3)
"""
from __future__ import annotations

import math
from typing import Dict, Iterable, Optional, Sequence, Tuple

import numpy as np

__all__ = ["noll_to_nm", "radial", "value", "basis", "evaluate_map", "fit", "rms", "strehl_marechal", "NAMES", "name", "radial_order_of"]

NAMES: Dict[int, str] = {1: "piston", 2: "tilt x", 3: "tilt y", 4: "defocus", 5: "astigmatism 45°", 6: "astigmatism 0°", 7: "coma y",
                         8: "coma x", 9: "trefoil y", 10: "trefoil x", 11: "spherical"}


def noll_to_nm(j: int) -> Tuple[int, int]:
    """Noll index (1-based) -> radial order n and azimuthal order m (m < 0: sine term)."""
    if j < 1:
        raise ValueError("Noll index starts at 1")
    n, j1 = 0, j - 1
    while j1 > n:
        n += 1
        j1 -= n
    m = (1 if j % 2 == 0 else -1) * ((n % 2) + 2 * ((j1 + ((n + 1) % 2)) // 2))
    return n, m


def name(j: int) -> str:
    """A name for the first 11 terms (piston ... spherical); ``Z<j>`` for the others."""
    return NAMES.get(j, f"Z{j}")


def radial_order_of(j: int) -> int:
    return noll_to_nm(j)[0]


def radial(n: int, m: int, rho: np.ndarray) -> np.ndarray:
    """Radial polynomial R_n^|m|(rho)."""
    m = abs(m)
    rho = np.asarray(rho, dtype=float)
    s = np.zeros_like(rho)
    for k in range((n - m) // 2 + 1):
        s = s + (-1) ** k * math.factorial(n - k) / (math.factorial(k) * math.factorial((n + m) // 2 - k) * math.factorial((n - m) // 2 - k)) * rho ** (n - 2 * k)
    return s


def value(j: int, rho: np.ndarray, theta: np.ndarray) -> np.ndarray:
    """Noll-normalised Z_j at polar coordinates (rho in units of the pupil radius)."""
    n, m = noll_to_nm(j)
    r = radial(n, m, rho)
    if m == 0:
        return math.sqrt(n + 1) * r
    return math.sqrt(2.0 * (n + 1)) * r * (np.cos(m * theta) if m > 0 else np.sin(-m * theta))


def _polar(x, y, radius, centre):
    X, Y = np.meshgrid(np.asarray(x, float) - centre[0], np.asarray(y, float) - centre[1], indexing="ij")
    return np.hypot(X, Y) / radius, np.arctan2(Y, X)


def basis(js: Iterable[int], x, y, radius: float, centre: Tuple[float, float] = (0.0, 0.0)) -> np.ndarray:
    """Z_j on a grid, shape ``(len(js), nx, ny)``; ``x``, ``y`` are the axes (the map is indexed ``[x][y]``)."""
    rho, th = _polar(x, y, radius, centre)
    return np.stack([value(j, rho, th) for j in js])


def evaluate_map(coefficients: Sequence[float], x, y, radius: float, centre: Tuple[float, float] = (0.0, 0.0),
                 first: int = 1, outside: float = np.nan) -> np.ndarray:
    """sum_j c_j Z_j on a grid; ``coefficients[0]`` belongs to Noll index ``first``. Points outside the pupil get ``outside``."""
    js = range(first, first + len(coefficients))
    b = basis(js, x, y, radius, centre)
    w = np.tensordot(np.asarray(coefficients, float), b, axes=1)
    rho, _ = _polar(x, y, radius, centre)
    return np.where(rho <= 1.0, w, outside)


def fit(wavefront: np.ndarray, x, y, radius: float, max_noll: int = 15, centre: Tuple[float, float] = (0.0, 0.0),
        mask: Optional[np.ndarray] = None, first: int = 1, return_residual: bool = False):
    """Least-squares Zernike coefficients ``c[0 .. max_noll - first]`` of ``wavefront[x][y]`` over the disc of ``radius``
    (points with a non-finite value or ``mask == False`` are skipped). Same unit as ``wavefront``.
    With ``return_residual=True`` also returns ``wavefront - fit`` on the pupil (NaN outside)."""
    w = np.asarray(wavefront, float)
    rho, _ = _polar(x, y, radius, centre)
    ok = (rho <= 1.0) & np.isfinite(w)
    if mask is not None:
        ok &= np.asarray(mask, bool)
    js = list(range(first, max_noll + 1))
    b = basis(js, x, y, radius, centre)
    A = b[:, ok].T
    c, *_ = np.linalg.lstsq(A, w[ok], rcond=None)
    if not return_residual:
        return c
    model = np.tensordot(c, b, axes=1)
    res = np.where(ok, w - model, np.nan)
    return c, res


def rms(coefficients: Sequence[float], first: int = 1, skip: Sequence[int] = (1,)) -> float:
    """rms of the wavefront from its coefficients (orthonormal basis): sqrt(sum c_j^2), leaving out the Noll indices in ``skip``
    (piston by default)."""
    c = np.asarray(coefficients, float)
    keep = [i for i in range(len(c)) if (first + i) not in skip]
    return float(np.sqrt(np.sum(c[keep] ** 2)))


def strehl_marechal(rms_waves: float) -> float:
    """Marechal estimate exp(-(2 pi sigma)^2) of the Strehl ratio for an rms wavefront error in waves (valid for sigma < ~0.1)."""
    return float(math.exp(-(2.0 * math.pi * rms_waves) ** 2))
