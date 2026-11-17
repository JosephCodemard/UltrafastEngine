"""Gaussian-beam calculator (pure Python, no engine): q parameter, waist, Rayleigh range, ABCD matrices, mode matching, telescope, M^2.

>>> from ultrafast.calc import beam
>>> b = beam.gaussian_beam(1030e-9, waist_radius_m=0.5e-3)            # Rayleigh range, divergence, radius and curvature at z
>>> q = beam.q_at(1030e-9, waist_radius_m=0.5e-3, z_m=2.0)            # complex q at 2 m behind the waist
>>> beam.single_lens_match(waist_in_m=0.3e-3, waist_out_m=0.1e-3, wavelength_m=1030e-9, focal_length_m=0.5)
>>> beam.two_lens_match(0.3e-3, 0.1e-3, 1030e-9, focal_length_1_m=0.3, focal_length_2_m=0.2, separation_m=0.6)
>>> beam.telescope(magnification=3.0, focal_length_1_m=0.1)
>>> beam.m_squared_from_widths(z_m, d4sigma_m, wavelength_m=1030e-9)  # ISO 11146 hyperbola fit

Conventions (Siegman, *Lasers*, ch. 17 and 20): the beam radius ``w`` is the 1/e^2 intensity radius; a beam with beam-quality factor
``m2`` (M^2) has the Rayleigh range ``zR = pi w0^2 / (m2 lambda)`` and the divergence half-angle ``m2 lambda / (pi w0)``, i.e. every
formula of the ideal beam holds with ``lambda -> m2 lambda``. The complex beam parameter is ``q = z + i zR`` (``z`` from the waist,
positive downstream), ``1/q = 1/R - i m2 lambda / (pi w^2)``. A ray matrix ``[[A, B], [C, D]]`` maps ``q -> (A q + B) / (C q + D)``; lengths in
metres, in the medium the beam is in (``wavelength_m`` is the wavelength in that medium). Bad arguments raise ``ValueError``.

Main functions: gaussian_beam, rayleigh_range_m, beam_radius_m, curvature_radius_m, q_at, q_to_beam, free_space, thin_lens, curved_mirror,
interface, system_matrix, propagate_q, single_lens_match, two_lens_match, telescope, m_squared_from_widths, m_squared_from_waist_divergence.
"""
from __future__ import annotations

import math
from typing import List, Optional, Sequence, Tuple, TypedDict

import numpy as np

Matrix = Tuple[Tuple[float, float], Tuple[float, float]]


class GaussianBeam(TypedDict):
    """Result of :func:`gaussian_beam` and :func:`q_to_beam`."""
    wavelengthM: float
    m2: float
    waistRadiusM: float
    rayleighRangeM: float
    divergenceHalfAngleRad: float
    confocalParameterM: float
    zM: float
    radiusM: float
    curvatureRadiusM: float            # math.inf at the waist
    gouyPhaseRad: float
    qM: complex


class LensMatchSolution(TypedDict):
    """One placement found by :func:`single_lens_match` / :func:`two_lens_match`."""
    distanceInputM: float              # input waist to the (first) lens
    distanceOutputM: float             # (last) lens to the output waist; negative: the output waist is virtual (upstream of the lens)


class SingleLensMatch(TypedDict):
    minimumFocalLengthM: float         # f0 = pi w1 w2 / (m2 lambda): no solution for a shorter lens
    focalLengthM: float
    solutions: List[LensMatchSolution]


class TwoLensMatch(TypedDict):
    solutions: List[LensMatchSolution]


class TelescopeDesign(TypedDict):
    kind: str
    magnification: float
    focalLength1M: float
    focalLength2M: float
    separationM: float
    divergenceReduction: float


class MSquaredFit(TypedDict):
    m2: float
    waistDiameterM: float              # 4-sigma diameter at the waist
    waistPositionM: float
    divergenceFullAngleRad: float
    rayleighRangeM: float


def _pos(value: float, name: str) -> float:
    v = float(value)
    if not math.isfinite(v) or v <= 0:
        raise ValueError(f"{name} must be a positive finite number (got {value!r})")
    return v


def _m2(m2: float) -> float:
    v = float(m2)
    if not math.isfinite(v) or v < 1 - 1e-9:
        raise ValueError(f"m2 must be >= 1 (got {m2!r})")
    return v


def rayleigh_range_m(waist_radius_m: float, wavelength_m: float, m2: float = 1.0) -> float:
    """Rayleigh range ``zR = pi w0^2 / (m2 lambda)`` (m)."""
    return math.pi * _pos(waist_radius_m, "waist_radius_m") ** 2 / (_m2(m2) * _pos(wavelength_m, "wavelength_m"))


def waist_radius_m(rayleigh_range_m: float, wavelength_m: float, m2: float = 1.0) -> float:  # noqa: D417
    """Waist radius for a given Rayleigh range: ``w0 = sqrt(m2 lambda zR / pi)`` (m)."""
    return math.sqrt(_m2(m2) * _pos(wavelength_m, "wavelength_m") * _pos(rayleigh_range_m, "rayleigh_range_m") / math.pi)


def beam_radius_m(z_m: float, waist_radius_m: float, wavelength_m: float, m2: float = 1.0) -> float:
    """1/e^2 intensity radius ``w(z) = w0 sqrt(1 + (z / zR)^2)`` (m), ``z`` from the waist."""
    zr = rayleigh_range_m(waist_radius_m, wavelength_m, m2)
    return waist_radius_m * math.sqrt(1 + (z_m / zr) ** 2)


def curvature_radius_m(z_m: float, waist_radius_m: float, wavelength_m: float, m2: float = 1.0) -> float:
    """Wavefront radius of curvature ``R(z) = z (1 + (zR / z)^2)`` (m); ``inf`` at the waist."""
    zr = rayleigh_range_m(waist_radius_m, wavelength_m, m2)
    return math.inf if z_m == 0 else z_m * (1 + (zr / z_m) ** 2)


def q_at(wavelength_m: float, *, waist_radius_m: float, z_m: float = 0.0, m2: float = 1.0) -> complex:
    """Complex beam parameter ``q = z + i zR`` at distance ``z_m`` from the waist."""
    return complex(z_m, rayleigh_range_m(waist_radius_m, wavelength_m, m2))


def q_from_radius_curvature(radius_m: float, curvature_radius_m: float, wavelength_m: float, m2: float = 1.0) -> complex:
    """``q`` of a beam of 1/e^2 radius ``radius_m`` and wavefront radius ``curvature_radius_m`` (``math.inf`` = flat): ``1/q = 1/R - i m2 lambda / (pi w^2)``."""
    inv_r = 0.0 if math.isinf(curvature_radius_m) else 1.0 / float(curvature_radius_m)
    return 1.0 / complex(inv_r, -_m2(m2) * _pos(wavelength_m, "wavelength_m") / (math.pi * _pos(radius_m, "radius_m") ** 2))


def q_to_beam(q: complex, wavelength_m: float, m2: float = 1.0) -> GaussianBeam:
    """The beam described by ``q`` at a plane: radius and curvature there, and the waist (``zM`` is the plane's distance downstream of the waist)."""
    q = complex(q)
    if q.imag <= 0:
        raise ValueError("q must have a positive imaginary part (the Rayleigh range)")
    zr, z = q.imag, q.real
    w0 = waist_radius_m(zr, wavelength_m, m2)
    return _describe(w0, wavelength_m, m2, z)


def _describe(w0: float, wavelength_m: float, m2: float, z: float) -> GaussianBeam:
    zr = rayleigh_range_m(w0, wavelength_m, m2)
    return {"wavelengthM": wavelength_m, "m2": m2, "waistRadiusM": w0, "rayleighRangeM": zr,
            "divergenceHalfAngleRad": m2 * wavelength_m / (math.pi * w0), "confocalParameterM": 2 * zr, "zM": z,
            "radiusM": w0 * math.sqrt(1 + (z / zr) ** 2), "curvatureRadiusM": math.inf if z == 0 else z * (1 + (zr / z) ** 2),
            "gouyPhaseRad": math.atan2(z, zr), "qM": complex(z, zr)}


def gaussian_beam(wavelength_m: float, *, waist_radius_m: float, m2: float = 1.0, z_m: float = 0.0) -> GaussianBeam:
    """Parameters of a Gaussian beam of waist ``waist_radius_m`` (1/e^2 radius) at the distance ``z_m`` from the waist."""
    return _describe(_pos(waist_radius_m, "waist_radius_m"), _pos(wavelength_m, "wavelength_m"), _m2(m2), float(z_m))


# ---- ray (ABCD) matrices -----------------------------------------------------------------------------------------------------------

def free_space(length_m: float) -> Matrix:
    """Propagation over ``length_m`` in the medium of the beam."""
    return ((1.0, float(length_m)), (0.0, 1.0))


def thin_lens(focal_length_m: float) -> Matrix:
    """Thin lens (``f > 0`` converging)."""
    if focal_length_m == 0:
        raise ValueError("focal_length_m must not be zero")
    return ((1.0, 0.0), (-1.0 / float(focal_length_m), 1.0))


def curved_mirror(radius_m: float, angle_rad: float = 0.0, plane: str = "sagittal") -> Matrix:
    """Mirror of radius ``radius_m`` (concave > 0) at incidence ``angle_rad``: ``f = R cos(a) / 2`` (sagittal) or ``R / (2 cos a)`` (tangential)."""
    if radius_m == 0:
        raise ValueError("radius_m must not be zero")
    if plane not in ("sagittal", "tangential"):
        raise ValueError("plane must be 'sagittal' or 'tangential'")
    c = math.cos(angle_rad)
    f = radius_m * c / 2 if plane == "sagittal" else radius_m / (2 * c)
    return thin_lens(f)


def interface(n1: float, n2: float, radius_m: float = math.inf) -> Matrix:
    """Refraction at a spherical interface of radius ``radius_m`` (``inf`` = flat) from index ``n1`` to ``n2`` (centre of curvature downstream > 0)."""
    _pos(n1, "n1"), _pos(n2, "n2")
    c = 0.0 if math.isinf(radius_m) else (n1 - n2) / (n2 * float(radius_m))
    return ((1.0, 0.0), (c, n1 / n2))


def system_matrix(*elements: Matrix) -> Matrix:
    """Product of the matrices in the order the light meets them (first element first)."""
    m = np.eye(2)
    for e in elements:
        m = np.array(e, dtype=float) @ m
    return ((float(m[0, 0]), float(m[0, 1])), (float(m[1, 0]), float(m[1, 1])))


def propagate_q(q: complex, matrix: Matrix) -> complex:
    """``q' = (A q + B) / (C q + D)``."""
    (a, b), (c, d) = matrix
    return (a * q + b) / (c * q + d)


# ---- mode matching -----------------------------------------------------------------------------------------------------------------

def single_lens_match(*, waist_in_m: float, waist_out_m: float, wavelength_m: float, focal_length_m: float, m2: float = 1.0) -> SingleLensMatch:
    """Where to put one thin lens of focal length ``f`` to turn a waist ``w1`` into a waist ``w2`` (Self 1983, Appl. Opt. 22, 658).

    With ``f0 = pi w1 w2 / (m2 lambda)`` there are two placements when ``f > f0`` (one when ``f == f0``, none for a shorter lens):
    ``d1 = f +- (w1 / w2) sqrt(f^2 - f0^2)``, ``d2 = f +- (w2 / w1) sqrt(f^2 - f0^2)`` (same sign), ``d1`` from the input waist to the lens
    and ``d2`` from the lens to the output waist. Raises ``ValueError`` when ``f`` is shorter than ``f0``."""
    w1, w2, lam, f, mm = _pos(waist_in_m, "waist_in_m"), _pos(waist_out_m, "waist_out_m"), _pos(wavelength_m, "wavelength_m"), _pos(focal_length_m, "focal_length_m"), _m2(m2)
    f0 = math.pi * w1 * w2 / (mm * lam)
    if f < f0 * (1 - 1e-12):
        raise ValueError(f"focal_length_m = {f:.6g} is shorter than the minimum {f0:.6g} m (pi w1 w2 / (m2 lambda)): no lens position matches these waists")
    root = math.sqrt(max(f * f - f0 * f0, 0.0))
    sols: List[LensMatchSolution] = [{"distanceInputM": f - (w1 / w2) * root, "distanceOutputM": f - (w2 / w1) * root},
                                     {"distanceInputM": f + (w1 / w2) * root, "distanceOutputM": f + (w2 / w1) * root}]
    if root == 0:
        sols = sols[:1]
    return {"minimumFocalLengthM": f0, "focalLengthM": f, "solutions": sols}


def _second_waist(d1: float, f1: float, f2: float, sep: float, zr1: float, mm: float, lam: float) -> Tuple[float, float]:
    """Output waist radius and its distance after lens 2 for an input waist at distance d1 before lens 1."""
    q = complex(d1, zr1)
    q = propagate_q(q, system_matrix(thin_lens(f1), free_space(sep), thin_lens(f2)))
    return waist_radius_m(q.imag, lam, mm), -q.real


def two_lens_match(waist_in_m: float, waist_out_m: float, wavelength_m: float, *, focal_length_1_m: float, focal_length_2_m: float,
                   separation_m: float, m2: float = 1.0, max_distance_m: Optional[float] = None, samples: int = 20001) -> TwoLensMatch:
    """Input distance and output distance that turn a waist ``w1`` into a waist ``w2`` through two thin lenses a fixed ``separation_m`` apart.

    The first lens is placed ``d1`` after the input waist; the function scans ``d1`` in ``[0, max_distance_m]`` (default 20 times the sum of the
    focal lengths, the separation and the input Rayleigh range) and returns every root of ``w_out(d1) = waist_out_m`` with the output waist
    ``d2`` behind the second lens (negative: virtual). Each solution can be checked with :func:`propagate_q`."""
    w1, w2, lam, mm = _pos(waist_in_m, "waist_in_m"), _pos(waist_out_m, "waist_out_m"), _pos(wavelength_m, "wavelength_m"), _m2(m2)
    f1, f2, sep = float(focal_length_1_m), float(focal_length_2_m), float(separation_m)
    if f1 == 0 or f2 == 0:
        raise ValueError("focal lengths must not be zero")
    if sep < 0:
        raise ValueError("separation_m must be >= 0")
    zr1 = rayleigh_range_m(w1, lam, mm)
    dmax = float(max_distance_m) if max_distance_m is not None else 20 * (abs(f1) + abs(f2) + sep + zr1)
    ds = np.linspace(0.0, dmax, int(samples))

    def g(d: float) -> float:
        return _second_waist(d, f1, f2, sep, zr1, mm, lam)[0] - w2

    vals = np.array([g(d) for d in ds])
    sols: List[LensMatchSolution] = []
    for i in range(len(ds) - 1):
        a, b, fa, fb = ds[i], ds[i + 1], vals[i], vals[i + 1]
        if fa == 0 or fa * fb < 0:
            for _ in range(80):                                   # bisection
                m = 0.5 * (a + b)
                fm = g(m)
                if fa * fm <= 0:
                    b, fb = m, fm
                else:
                    a, fa = m, fm
            d = 0.5 * (a + b)
            sols.append({"distanceInputM": d, "distanceOutputM": _second_waist(d, f1, f2, sep, zr1, mm, lam)[1]})
    return {"solutions": sols}


def telescope(*, magnification: float, focal_length_1_m: float, kind: str = "keplerian") -> TelescopeDesign:
    """Two-lens beam expander: the beam radius grows by ``magnification`` (> 0; < 1 reduces) and the divergence shrinks by the same factor.

    ``keplerian``: ``f2 = M f1`` (both positive, an intermediate focus), separation ``f1 + f2``, the image is inverted.
    ``galilean``: ``f1 < 0`` and ``f2 = -M f1`` (no focus), separation ``f1 + f2``."""
    m = _pos(magnification, "magnification")
    f1 = float(focal_length_1_m)
    if kind == "keplerian":
        if f1 <= 0:
            raise ValueError("a keplerian telescope needs focal_length_1_m > 0")
        f2 = m * f1
    elif kind == "galilean":
        if f1 >= 0:
            raise ValueError("a galilean telescope needs focal_length_1_m < 0 (a diverging first lens)")
        f2 = -m * f1
    else:
        raise ValueError("kind must be 'keplerian' or 'galilean'")
    return {"kind": kind, "magnification": m, "focalLength1M": f1, "focalLength2M": f2, "separationM": f1 + f2, "divergenceReduction": m}


# ---- beam quality ------------------------------------------------------------------------------------------------------------------

def m_squared_from_waist_divergence(waist_radius_m: float, half_angle_rad: float, wavelength_m: float) -> float:
    """``M^2 = pi w0 theta / lambda`` from the waist radius and the far-field half angle (both second-moment or both 1/e^2)."""
    return math.pi * _pos(waist_radius_m, "waist_radius_m") * _pos(half_angle_rad, "half_angle_rad") / _pos(wavelength_m, "wavelength_m")


def m_squared_from_widths(z_m: Sequence[float], d4sigma_m: Sequence[float], wavelength_m: float) -> MSquaredFit:
    """ISO 11146 hyperbola fit of second-moment (4 sigma) diameters measured at positions ``z_m``: ``d^2(z) = A + B z + C z^2``,
    ``z0 = -B / 2C``, ``d0^2 = A - B^2 / 4C``, ``theta = sqrt(C)`` (full angle), ``M^2 = pi d0 theta / (4 lambda)``. Needs >= 3 positions
    (ISO asks for 10, half of them within one Rayleigh range of the waist and half beyond two)."""
    z, d = np.asarray(z_m, dtype=float), np.asarray(d4sigma_m, dtype=float)
    if z.shape != d.shape or z.size < 3:
        raise ValueError("z_m and d4sigma_m must have the same length, at least 3")
    if np.any(d <= 0):
        raise ValueError("d4sigma_m must be positive")
    c, b, a = np.polyfit(z, d * d, 2)
    if c <= 0 or a - b * b / (4 * c) <= 0:
        raise ValueError("the widths do not follow a hyperbola (the fit has no positive waist and divergence)")
    z0, d0, theta = -b / (2 * c), math.sqrt(a - b * b / (4 * c)), math.sqrt(c)
    m2 = math.pi * d0 * theta / (4 * _pos(wavelength_m, "wavelength_m"))
    return {"m2": m2, "waistDiameterM": d0, "waistPositionM": z0, "divergenceFullAngleRad": theta, "rayleighRangeM": d0 / theta}
