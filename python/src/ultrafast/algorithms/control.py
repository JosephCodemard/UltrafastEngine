"""Wavefront sensing and adaptive-optics control (NumPy only).

Sensing
* :func:`shack_hartmann`           a lenslet-array model: every subaperture of a complex field is focused (an FFT), the spot centroid gives the
                                   local slope, ``shift = f dW/dx`` (W the wavefront in metres). Noise optional.
* :func:`modal_reconstructor`      slopes -> Zernike coefficients (least squares on the derivative basis; exact for noiseless slopes of a mode within the basis).
* :func:`southwell_reconstructor`  slopes -> wavefront on the lenslet grid (Southwell 1980, least squares; the piston is the null space and is set to the minimum-norm value).

Control
* :func:`interaction_matrix`       poke each actuator (push-pull), record the sensor: D (measurements x actuators). For a linear device one poke is exact.
* :func:`control_matrix`           pseudo-inverse of D: Tikhonov or truncated SVD; returns the singular values so the null space and waffle modes are visible.
* :func:`correct`                  one-shot command = - G m.
* :func:`integrator_loop`          closed loop c(k+1) = c(k) - gain G m(k - delay) on a disturbance that may change in time.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Optional, Sequence, Tuple

import numpy as np

from . import zernike


# ----------------------------------------------------------------------------------------------------- sensing
@dataclass
class SlopeMap:
    """Slopes (rad, = dW/dx and dW/dy) on the lenslet grid ``[ix][iy]`` with the lenslet centres (m) and which lenslets are lit."""
    slope_x: np.ndarray
    slope_y: np.ndarray
    x_m: np.ndarray
    y_m: np.ndarray
    valid: np.ndarray


def shack_hartmann(field: np.ndarray, x_m: np.ndarray, y_m: np.ndarray, *, wavelength_m: float, lenslet_pitch_m: float, focal_length_m: float,
                   threshold: float = 0.0, window: float = 2.0, min_flux: float = 0.05, pad: int = 4, photons: Optional[float] = None, read_noise: float = 0.0,
                   seed: int = 0) -> SlopeMap:
    """Slopes measured by a Shack-Hartmann sensor on a complex field (x_m, y_m: the sample axes, uniform). Each lenslet is a square subaperture of
    ``lenslet_pitch_m``; its focal spot is the FFT of the zero-padded subaperture (``pad`` x), centroided (centre of gravity in a window of
    +-``window`` x lambda/D around the brightest pixel, pixels below ``threshold`` x peak removed: the window removes the bias of a centroid over the whole,
    truncated spot plane). The slope is ``centroid / focal_length_m``; the spot of a plane wave tilted by theta moves by f theta, so the slope
    equals dW/dx for W = optical path. ``photons`` (total per lit sub-aperture) adds shot noise, ``read_noise`` (counts rms) read noise."""
    dx, dy = float(x_m[1] - x_m[0]), float(y_m[1] - y_m[0])
    nsx, nsy = int(round(lenslet_pitch_m / dx)), int(round(lenslet_pitch_m / dy))
    nlx, nly = len(x_m) // nsx, len(y_m) // nsy
    sx = np.zeros((nlx, nly)); sy = np.zeros((nlx, nly)); valid = np.zeros((nlx, nly), bool)
    cx = np.zeros(nlx); cy = np.zeros(nly)
    rng = np.random.default_rng(seed)
    total = np.abs(field) ** 2
    ref_flux = total.max() * nsx * nsy
    for i in range(nlx):
        for j in range(nly):
            sub = field[i * nsx:(i + 1) * nsx, j * nsy:(j + 1) * nsy]
            cx[i] = x_m[i * nsx:(i + 1) * nsx].mean(); cy[j] = y_m[j * nsy:(j + 1) * nsy].mean()
            flux = np.sum(np.abs(sub) ** 2)
            if flux < min_flux * ref_flux:
                continue
            nfx, nfy = pad * nsx, pad * nsy
            spot = np.abs(np.fft.fftshift(np.fft.fft2(sub, s=(nfx, nfy)))) ** 2
            if photons is not None:
                spot = rng.poisson(spot / spot.sum() * photons).astype(float)
            if read_noise > 0:
                spot = spot + rng.normal(0, read_noise, spot.shape)
            spot = np.clip(spot - threshold * spot.max(), 0, None)
            if spot.sum() <= 0:
                continue
            if window > 0:
                ipk, jpk = np.unravel_index(np.argmax(spot), spot.shape)
                half = int(round(window * pad))
                win = np.zeros_like(spot, bool)
                win[max(0, ipk - half):ipk + half + 1, max(0, jpk - half):jpk + half + 1] = True
                spot = np.where(win, spot, 0.0)
            # angular coordinates of the FFT bins: theta = lambda * nu, nu the spatial frequency (cycles/m)
            ux = np.fft.fftshift(np.fft.fftfreq(nfx, dx)) * wavelength_m
            uy = np.fft.fftshift(np.fft.fftfreq(nfy, dy)) * wavelength_m
            gx = (spot.sum(axis=1) * ux).sum() / spot.sum()
            gy = (spot.sum(axis=0) * uy).sum() / spot.sum()
            sx[i, j], sy[i, j], valid[i, j] = gx, gy, True            # the spot shift / f = the direction of the local normal = dW/dx
    return SlopeMap(sx, sy, cx, cy, valid)


def _basis_points(js, x, y, radius):
    """Z_j at scattered points (x, y in metres), shape (points, modes)."""
    rho, th = np.hypot(x, y) / radius, np.arctan2(y, x)
    return np.stack([zernike.value(j, rho, th) for j in js], axis=1)


def modal_matrices(slopes: SlopeMap, radius_m: float, max_noll: int = 15):
    """(A, js): the derivative of the Zernike basis at the lit lenslets, ``A @ c = [slope_x; slope_y]`` for coefficients ``c`` in metres rms of
    wavefront (Noll 2 .. max_noll, piston has no slope). Derivatives by central differences of the analytic polynomials."""
    X, Y = np.meshgrid(slopes.x_m, slopes.y_m, indexing="ij")
    x, y = X[slopes.valid], Y[slopes.valid]
    js = list(range(2, max_noll + 1)); h = radius_m * 1e-6
    bx = (_basis_points(js, x + h, y, radius_m) - _basis_points(js, x - h, y, radius_m)) / (2 * h)
    by = (_basis_points(js, x, y + h, radius_m) - _basis_points(js, x, y - h, radius_m)) / (2 * h)
    return np.vstack([bx, by]), js


def modal_fit(slopes: SlopeMap, radius_m: float, max_noll: int = 15) -> Tuple[np.ndarray, list]:
    """Zernike coefficients (metres rms of wavefront, Noll 2 .. max_noll) from a :class:`SlopeMap` by least squares on the derivative basis; exact for
    noiseless slopes of a wavefront inside the basis. Returns (coefficients, Noll indices). Divide by the wavelength for waves."""
    A, js = modal_matrices(slopes, radius_m, max_noll)
    b = np.concatenate([slopes.slope_x[slopes.valid], slopes.slope_y[slopes.valid]])
    coef, *_ = np.linalg.lstsq(A, b, rcond=None)
    return coef, js


def southwell_reconstructor(slopes: SlopeMap) -> np.ndarray:
    """Wavefront (metres) at the lenslet centres from the slopes, Southwell geometry: (W[i+1] - W[i]) / h = (s[i] + s[i+1]) / 2 along both axes, for
    neighbouring lit lenslets; least squares (dense, fine to about 40 x 40 lenslets); the minimum-norm solution, i.e. zero mean over lit lenslets.
    Unlit lenslets are NaN."""
    nx, ny = slopes.valid.shape
    hx = float(slopes.x_m[1] - slopes.x_m[0]); hy = float(slopes.y_m[1] - slopes.y_m[0])
    idx = -np.ones((nx, ny), int); idx[slopes.valid] = np.arange(slopes.valid.sum())
    rows, rhs = [], []
    n = int(slopes.valid.sum())
    for i in range(nx):
        for j in range(ny):
            if not slopes.valid[i, j]:
                continue
            if i + 1 < nx and slopes.valid[i + 1, j]:
                r = np.zeros(n); r[idx[i + 1, j]] = 1 / hx; r[idx[i, j]] = -1 / hx; rows.append(r); rhs.append(0.5 * (slopes.slope_x[i, j] + slopes.slope_x[i + 1, j]))
            if j + 1 < ny and slopes.valid[i, j + 1]:
                r = np.zeros(n); r[idx[i, j + 1]] = 1 / hy; r[idx[i, j]] = -1 / hy; rows.append(r); rhs.append(0.5 * (slopes.slope_y[i, j] + slopes.slope_y[i, j + 1]))
    W, *_ = np.linalg.lstsq(np.array(rows), np.array(rhs), rcond=None)
    out = np.full((nx, ny), np.nan); out[slopes.valid] = W - W.mean()
    return out


# ----------------------------------------------------------------------------------------------------- control
def interaction_matrix(measure: Callable[[np.ndarray], np.ndarray], n_actuators: int, poke: float) -> np.ndarray:
    """D[:, k] = (m(+poke e_k) - m(-poke e_k)) / (2 poke): the sensor response per unit command of actuator k (push-pull cancels the even part,
    so a mildly nonlinear device is still calibrated about zero). ``measure(commands)`` returns the measurement vector."""
    cols = []
    for k in range(n_actuators):
        e = np.zeros(n_actuators); e[k] = poke
        cols.append((np.asarray(measure(e)) - np.asarray(measure(-e))) / (2 * poke))
    return np.array(cols).T


def control_matrix(D: np.ndarray, *, method: str = "tikhonov", regularisation: float = 1e-3, n_modes: Optional[int] = None) -> Tuple[np.ndarray, np.ndarray]:
    """Pseudo-inverse G of D (so that ``G @ D`` ~ identity on the controllable space). ``tikhonov``: singular value s -> s / (s^2 + (r s_max)^2);
    ``tsvd``: keep the ``n_modes`` largest singular values (default: those above ``regularisation`` x s_max). Returns (G, singular values)."""
    U, s, Vt = np.linalg.svd(D, full_matrices=False)
    if method == "tikhonov":
        f = s / (s ** 2 + (regularisation * s[0]) ** 2)
    elif method == "tsvd":
        keep = n_modes if n_modes is not None else int(np.sum(s > regularisation * s[0]))
        f = np.zeros_like(s); f[:keep] = 1 / s[:keep]
    else:
        raise ValueError("method must be 'tikhonov' or 'tsvd'")
    return (Vt.T * f) @ U.T, s


def correct(G: np.ndarray, measurement: np.ndarray) -> np.ndarray:
    """One-shot correction: the command that cancels the measured aberration, ``-G m``."""
    return -G @ np.asarray(measurement)


def integrator_loop(measure: Callable[[np.ndarray, int], np.ndarray], G: np.ndarray, n_actuators: int, *, gain: float = 0.5, delay: int = 0,
                    steps: int = 20) -> np.ndarray:
    """Closed-loop integrator: ``c[k+1] = c[k] - gain G m[k - delay]``, with ``m[k] = measure(c[k], k)`` the sensor reading with the correction applied
    at step k (so a changing disturbance is supported). Returns the measurement vectors, one row per step. Stable for 0 < gain < 2 without delay."""
    c = np.zeros(n_actuators); hist = []; pending = [np.zeros(G.shape[1])] * delay
    out = []
    for k in range(steps):
        m = np.asarray(measure(c, k)); out.append(m)
        pending.append(m)
        mm = pending.pop(0)
        c = c - gain * (G @ mm)
    return np.array(out)
