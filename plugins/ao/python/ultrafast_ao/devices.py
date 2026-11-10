"""NumPy twins of the engine's device models, for calibration, control matrices and offline design (they use the same formulas as the C# elements; the tests
compare them with engine runs).

* :func:`dm_actuators`         actuator positions of a layout (square, hex, ring, explicit), as in ``DmGeometry.Layout``.
* :func:`dm_influence_matrix`  surface (m) at points (x, y) per unit command of each actuator: ``surface = M @ commands``.
* :func:`slm_lut`              phase (rad, at the design wavelength) of each grey level for the linear / tanh / table curves of the ``slm`` element.
* :func:`liquid_crystal_scale` s(lambda) = lambda_d dn(lambda) / (lambda dn(lambda_d)): the factor by which the 2 pi written at the design wavelength is scaled at lambda.
"""
from __future__ import annotations

from typing import Optional, Sequence, Tuple

import numpy as np

GENERIC_NEMATIC_CAUCHY = (0.1805, 1.6e-14, 0.0)     # dn = A + B / lambda^2 + C / lambda^4: a GENERIC nematic preset (0.227 at 589 nm, 0.195 at 1064 nm); not a datasheet value


def dm_actuators(layout: str = "square", *, pitch_m: float, actuators_x: int = 0, actuators_y: Optional[int] = None, ring_counts: Sequence[int] = (),
                 positions_x_m: Sequence[float] = (), positions_y_m: Sequence[float] = ()) -> np.ndarray:
    """Actuator positions, shape (N, 2), in the order the engine uses (square: x-major; hex: row by row, odd rows shifted by half a pitch, row spacing
    pitch sqrt(3)/2; ring k at radius k pitch)."""
    p = pitch_m
    ny = actuators_x if actuators_y is None else actuators_y
    pts = []
    if layout == "square":
        for i in range(actuators_x):
            for j in range(ny):
                pts.append(((i - (actuators_x - 1) / 2) * p, (j - (ny - 1) / 2) * p))
    elif layout == "hex":
        dy = p * np.sqrt(3) / 2
        for j in range(ny):
            for i in range(actuators_x):
                pts.append(((i - (actuators_x - 1) / 2 + (-0.25 if j % 2 == 0 else 0.25)) * p, (j - (ny - 1) / 2) * dy))
    elif layout == "ring":
        for k, n in enumerate(ring_counts):
            if k == 0:
                if n >= 1:
                    pts.append((0.0, 0.0))
                continue
            for m in range(int(n)):
                a = 2 * np.pi * m / n
                pts.append((k * p * np.cos(a), k * p * np.sin(a)))
    elif layout == "explicit":
        pts = list(zip(positions_x_m, positions_y_m))
    else:
        raise ValueError("layout must be square, hex, ring or explicit")
    return np.array(pts, float)


def dm_influence_matrix(actuators: np.ndarray, x_m: np.ndarray, y_m: np.ndarray, *, pitch_m: float, coupling: float = 0.15) -> np.ndarray:
    """Matrix (points, actuators): Gaussian influence ``exp(ln(c) (r / pitch)^2)`` of every actuator at every point; ``x_m``, ``y_m`` are point coordinates of the
    same shape (any shape; the first axis of the result is the flattened points)."""
    x = np.asarray(x_m, float).ravel()[:, None] - actuators[None, :, 0]
    y = np.asarray(y_m, float).ravel()[:, None] - actuators[None, :, 1]
    return np.exp(np.log(coupling) * (x * x + y * y) / pitch_m ** 2)


def slm_lut(levels: int = 256, kind: str = "linear", max_phase_rad: float = 2 * np.pi, steepness: float = 2.0, table: Optional[Sequence[float]] = None) -> np.ndarray:
    """Phase of each grey level: ``linear`` max k / levels; ``tanh`` the same range with an S-shaped response; ``table`` as given."""
    k = np.arange(levels)
    if kind == "linear":
        return max_phase_rad * k / levels
    if kind == "tanh":
        return max_phase_rad * (0.5 + 0.5 * np.tanh(steepness * (k / levels - 0.5)) / np.tanh(steepness / 2))
    if kind == "table":
        t = np.asarray(table, float)
        if t.size != levels:
            raise ValueError("a table curve needs one phase per grey level")
        return t
    raise ValueError("kind must be linear, tanh or table")


def liquid_crystal_scale(wavelength_m, design_wavelength_m: float, cauchy: Sequence[float] = GENERIC_NEMATIC_CAUCHY):
    """s(lambda) = lambda_d dn(lambda) / (lambda dn(lambda_d)) with dn = A + B / lambda^2 + C / lambda^4."""
    A, B, C = (list(cauchy) + [0.0, 0.0])[:3]
    dn = lambda l: A + B / l ** 2 + C / l ** 4
    lam = np.asarray(wavelength_m, float)
    return design_wavelength_m * dn(lam) / (lam * dn(design_wavelength_m))
