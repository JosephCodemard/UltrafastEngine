"""Analytic references for the spatial component checks (spatial.py), written independently of the engine.

What it does: Gaussian-beam optics (w(z), ABCD on the complex beam parameter, Kogelnik-Li resonator modes), and direct
quadratures of the Fresnel diffraction integral -- radial (Hankel, rotationally symmetric fields) and one-dimensional
(separable x / y fields). Nothing here imports the engine.

Main functions:
  gauss_w(w0, z, lam, m2=1) -> w(z)                      w0 sqrt(1 + (M2 z / zR)^2), zR = pi w0^2 / lam
  q_after(q, M) -> q'                                    (A q + B) / (C q + D)
  w_of_q(q, lam) -> w                                    1/q = 1/R - i lam / (pi w^2)
  fresnel_radial(u0, rho, r, z, lam) -> u(r, z)          u = (k / i z) e^{i k r^2 / 2z} int u0 e^{i k rho^2 / 2z} J0(k rho r / z) rho drho (J0 from scipy, else the A&S polynomial)
  fresnel_1d(u0, x0, x, z, lam) -> u(x, z)               u = sqrt(k / 2 pi i z) int u0(x0) e^{i k (x - x0)^2 / 2z} dx0
  linear_resonator_mode(R1, R2, L, lam) -> (w1, w2)      two-mirror cavity (Kogelnik & Li 1966, Eq. 18)
  ring_mode_from_matrix(A, B, C, D, lam) -> (w, stability)  eigenmode of a round-trip matrix
"""
from __future__ import annotations

import math

import numpy as np


def gauss_w(w0, z, lam, m2=1.0):
    zr = math.pi * w0 ** 2 / lam
    return w0 * np.sqrt(1 + (m2 * np.asarray(z, float) / zr) ** 2)


def q_after(q, M):
    (A, B), (C, D) = M
    return (A * q + B) / (C * q + D)


def w_of_q(q, lam):
    inv = 1 / q
    return math.sqrt(-lam / (math.pi * inv.imag))


def _j0(x):
    try:
        from scipy.special import j0
        return j0(x)
    except Exception:  # noqa: BLE001  NumPy only: Abramowitz & Stegun 9.4.1 (|x| <= 3) and 9.4.3 (|x| > 3), absolute error < 2e-8 (J0 amplitude form)
        x = np.abs(np.asarray(x, float))
        out = np.empty_like(x)
        m = x <= 3
        t = (x[m] / 3) ** 2
        out[m] = 1 - 2.2499997 * t + 1.2656208 * t ** 2 - 0.3163866 * t ** 3 + 0.0444479 * t ** 4 - 0.0039444 * t ** 5 + 0.0002100 * t ** 6
        y = 3 / x[~m]
        f0 = 0.79788456 - 0.00000077 * y - 0.00552740 * y ** 2 - 0.00009512 * y ** 3 + 0.00137237 * y ** 4 - 0.00072805 * y ** 5 + 0.00014476 * y ** 6
        th = (x[~m] - 0.78539816 - 0.04166397 * y - 0.00003954 * y ** 2 + 0.00262573 * y ** 3 - 0.00054125 * y ** 4
              - 0.00029333 * y ** 5 + 0.00013558 * y ** 6)
        out[~m] = f0 * np.cos(th) / np.sqrt(x[~m])
        return out


def fresnel_radial(u0, rho, r, z, lam):
    """Field at radii `r` after z of free space, from the field u0(rho) sampled on the (fine, uniform) grid `rho`."""
    k = 2 * math.pi / lam
    rho, r = np.asarray(rho, float), np.asarray(r, float)
    drho = rho[1] - rho[0]
    kern = u0 * np.exp(1j * k * rho ** 2 / (2 * z)) * rho * drho
    out = np.empty(len(r), complex)
    for i in range(0, len(r), 32):
        rr = r[i:i + 32]
        J = _j0(k * np.outer(rr, rho) / z)
        out[i:i + 32] = J @ kern
    return (k / (1j * z)) * np.exp(1j * k * r ** 2 / (2 * z)) * out


def fresnel_1d(u0, x0, x, z, lam):
    k = 2 * math.pi / lam
    x0, x = np.asarray(x0, float), np.asarray(x, float)
    dx = x0[1] - x0[0]
    out = np.empty(len(x), complex)
    for i in range(0, len(x), 64):
        xx = x[i:i + 64]
        out[i:i + 64] = (np.exp(1j * k * (xx[:, None] - x0[None, :]) ** 2 / (2 * z)) * u0[None, :]).sum(1) * dx
    return np.sqrt(k / (2j * math.pi * z)) * out


def linear_resonator_mode(R1, R2, L, lam):
    """Spot radii on mirrors 1 and 2 of a stable two-mirror cavity: g_i = 1 - L / R_i, w1^2 = (lam L / pi) sqrt(g2 / (g1 (1 - g1 g2)))."""
    g1, g2 = 1 - L / R1, 1 - L / R2
    w1 = math.sqrt(lam * L / math.pi * math.sqrt(g2 / (g1 * (1 - g1 * g2))))
    w2 = math.sqrt(lam * L / math.pi * math.sqrt(g1 / (g2 * (1 - g1 * g2))))
    return w1, w2


def ring_mode_from_matrix(A, B, C, D, lam):
    """Eigenmode of a unit-determinant round-trip matrix: stability m = (A + D) / 2, w^2 = lam |B| / (pi sqrt(1 - m^2))."""
    m = (A + D) / 2
    return math.sqrt(lam * abs(B) / (math.pi * math.sqrt(1 - m * m))), m
