"""Phase-only holograms: the Gerchberg-Saxton family, complex-to-phase-only encoding and grey-level mapping (NumPy only).

The SLM plane and the focal plane are related by a Fourier transform (a lens in the 2f layout). Fields are complex arrays [x][y];
``focal_field`` and the iterations use ``numpy.fft`` (orthonormal), so energy is conserved between the planes.

* :func:`gerchberg_saxton`  GS (Gerchberg and Saxton 1972): alternate the amplitude constraints of the two planes. The Fourier-plane
  error of the iteration does not increase (Fienup, Appl. Opt. 21, 2758 (1982)): ``result.error`` is that error per iteration.
* :func:`weighted_gs`       WGS for spot arrays (Di Leonardo, Ianni and Ruocco, Opt. Express 15, 1913 (2007)): the target weights are
  corrected each iteration to equalise the spot intensities.
* :func:`mraf`              mixed-region amplitude freedom (Pasienski and DeMarco, Opt. Express 16, 2176 (2008)): the amplitude is held
  only in a signal region; a noise region absorbs the error.
* :func:`double_phase_encode`, :func:`bolduc_encode`  a complex field A exp(i phi) with A <= 1 written on a phase-only device.
* :func:`grey_levels`, :func:`quantise_phase`  map a phase to the nearest grey level of a (possibly non-uniform) voltage-to-phase curve.

Every function documents the reference value its test uses (python/tests/test_ao_holography.py).
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Callable, List, Optional, Sequence

import numpy as np


def focal_field(phase: np.ndarray, amplitude: Optional[np.ndarray] = None) -> np.ndarray:
    """Far field (focal plane of a lens, orthonormal FFT, zero frequency at the array centre) of ``amplitude * exp(i phase)``."""
    a = np.ones_like(phase) if amplitude is None else amplitude
    return np.fft.fftshift(np.fft.fft2(a * np.exp(1j * phase), norm="ortho"))


def quantise_phase(phase: np.ndarray, levels: int, wrap: float = 2 * np.pi) -> np.ndarray:
    """Phase rounded to ``levels`` uniform steps over ``wrap`` (the top half-step rounds to level 0, as the engine does)."""
    step = wrap / levels
    p = np.mod(phase, wrap)
    return step * (np.rint(p / step) % levels)


def grey_levels(phase: np.ndarray, lut: Sequence[float], wrap: float = 2 * np.pi) -> np.ndarray:
    """Grey level whose phase (``lut[k]``, non-decreasing) is nearest to ``phase`` modulo ``wrap``: the inverse of the voltage-to-phase curve.
    Same rule as the engine's ``slm`` element, so a phase target and these grey levels give the same device."""
    lut = np.asarray(lut, dtype=float)
    t = np.mod(phase, wrap)
    best = np.zeros(t.shape, int); bd = np.full(t.shape, np.inf)
    k = -1
    while k * wrap <= lut[-1] + wrap:                  # the curve may span more than one wrap: test every image of the target inside its range
        tt = t + k * wrap
        j = np.clip(np.searchsorted(lut, tt), 1, len(lut) - 1)
        for c in (j - 1, j):
            d = np.abs(lut[c] - tt)
            better = d < bd
            best, bd = np.where(better, c, best), np.where(better, d, bd)
        k += 1
    return best.astype(int)


@dataclass
class HologramResult:
    """``phase`` is the SLM-plane phase (rad); ``error[k]`` the Fourier-plane amplitude error before iteration k's constraint (rms of
    |G| - T, both unit-energy); ``efficiency`` the energy fraction in the signal region; ``field`` the final focal field; ``weights`` WGS weights."""
    phase: np.ndarray
    error: List[float] = field(default_factory=list)
    efficiency: float = 0.0
    field: Optional[np.ndarray] = None
    weights: Optional[np.ndarray] = None
    uniformity: float = float("nan")


def _unit_energy(a: np.ndarray) -> np.ndarray:
    return a / np.sqrt(np.sum(np.abs(a) ** 2))


def gerchberg_saxton(target_amplitude: np.ndarray, source_amplitude: Optional[np.ndarray] = None, iterations: int = 50, *, seed: int = 0,
                     initial_phase: Optional[np.ndarray] = None, levels: Optional[int] = None, lut: Optional[Sequence[float]] = None) -> HologramResult:
    """Phase hologram whose far field has the amplitude ``target_amplitude``.

    ``levels`` or ``lut`` apply the device's phase quantisation *inside* the loop (quantisation-aware refinement): the SLM-plane phase is
    rounded to the nearest level before every transform, so the iteration sees what the device will write. ``levels`` = uniform steps over
    2 pi; ``lut`` = the phase of each grey level (non-uniform curve)."""
    T = _unit_energy(np.asarray(target_amplitude, float))
    S = np.ones_like(T) if source_amplitude is None else np.asarray(source_amplitude, float)
    S = _unit_energy(S)
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, 2 * np.pi, T.shape) if initial_phase is None else np.array(initial_phase, float)
    res = HologramResult(phase)
    lut_arr = None if lut is None else np.asarray(lut, float)

    def device(p):
        if lut_arr is not None:
            return lut_arr[grey_levels(p, lut_arr)]
        if levels:
            return quantise_phase(p, levels)
        return p

    for _ in range(iterations):
        phase = device(phase)
        G = np.fft.fftshift(np.fft.fft2(S * np.exp(1j * phase), norm="ortho"))
        res.error.append(float(np.sqrt(np.mean((np.abs(G) - T) ** 2))))
        G = T * np.exp(1j * np.angle(G))
        phase = np.angle(np.fft.ifft2(np.fft.ifftshift(G), norm="ortho"))
    phase = device(phase)
    res.phase = phase
    res.field = np.fft.fftshift(np.fft.fft2(S * np.exp(1j * phase), norm="ortho"))
    sig = T > 0
    res.efficiency = float(np.sum(np.abs(res.field[sig]) ** 2) / np.sum(np.abs(res.field) ** 2))
    return res


def weighted_gs(spot_mask: np.ndarray, iterations: int = 30, *, gain: float = 1.0, seed: int = 0, source_amplitude: Optional[np.ndarray] = None) -> HologramResult:
    """Hologram for an array of spots (``spot_mask`` > 0 at the spot pixels) with equal intensities (WGS). The weight of spot j is multiplied
    each iteration by (mean intensity / intensity_j)^``gain``. ``uniformity`` = 1 - (I_max - I_min) / (I_max + I_min) over the spots."""
    mask = np.asarray(spot_mask) > 0
    S = np.ones(mask.shape) if source_amplitude is None else np.asarray(source_amplitude, float)
    S = _unit_energy(S)
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, 2 * np.pi, mask.shape)
    w = mask.astype(float)
    res = HologramResult(phase)
    for _ in range(iterations):
        G = np.fft.fftshift(np.fft.fft2(S * np.exp(1j * phase), norm="ortho"))
        inten = np.abs(G) ** 2
        if mask.any():
            mean = inten[mask].mean()
            w[mask] = w[mask] * (mean / np.maximum(inten[mask], 1e-30)) ** gain
        T = np.sqrt(w) * mask
        T = _unit_energy(T)
        res.error.append(float(np.sqrt(np.mean((np.abs(G) - T) ** 2))))
        G = T * np.exp(1j * np.angle(G))
        phase = np.angle(np.fft.ifft2(np.fft.ifftshift(G), norm="ortho"))
    res.phase = phase
    res.field = np.fft.fftshift(np.fft.fft2(S * np.exp(1j * phase), norm="ortho"))
    inten = np.abs(res.field) ** 2
    s = inten[mask]
    res.efficiency = float(s.sum() / inten.sum())
    res.uniformity = float(1 - (s.max() - s.min()) / (s.max() + s.min()))
    res.weights = w
    return res


def mraf(target_amplitude: np.ndarray, signal_region: np.ndarray, iterations: int = 60, *, mixing: float = 0.4, seed: int = 0,
         source_amplitude: Optional[np.ndarray] = None) -> HologramResult:
    """MRAF (Pasienski and DeMarco 2008): in the signal region the field is ``G' = m T e^{i arg G} + (1 - m) G`` with the target ``T`` scaled to the
    signal-region energy of ``G``; in the noise region the field is left free (``G' = G``), so the error is pushed out of the signal region.
    ``mixing`` m = 1 is plain GS on the signal region; smaller m trades efficiency for a cleaner signal region. ``error`` is the rms amplitude
    error inside the signal region (relative to the target peak)."""
    T = np.asarray(target_amplitude, float)
    sig = np.asarray(signal_region) > 0
    S = np.ones(T.shape) if source_amplitude is None else np.asarray(source_amplitude, float)
    S = _unit_energy(S)
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, 2 * np.pi, T.shape)
    res = HologramResult(phase)
    Tn = np.where(sig, T, 0.0)
    for _ in range(iterations):
        G = np.fft.fftshift(np.fft.fft2(S * np.exp(1j * phase), norm="ortho"))
        scale = np.sqrt(np.sum(np.abs(G[sig]) ** 2) / np.sum(Tn[sig] ** 2))
        Ts = Tn * scale
        res.error.append(float(np.sqrt(np.mean((np.abs(G[sig]) - Ts[sig]) ** 2)) / max(Ts.max(), 1e-30)))
        Gn = np.where(sig, mixing * Ts * np.exp(1j * np.angle(G)) + (1 - mixing) * G, G)
        phase = np.angle(np.fft.ifft2(np.fft.ifftshift(Gn), norm="ortho"))
    res.phase = phase
    res.field = np.fft.fftshift(np.fft.fft2(S * np.exp(1j * phase), norm="ortho"))
    res.efficiency = float(np.sum(np.abs(res.field[sig]) ** 2) / np.sum(np.abs(res.field) ** 2))
    return res


def double_phase_encode(field_complex: np.ndarray) -> np.ndarray:
    """Phase-only map for a complex field A exp(i phi) with A <= 1: A e^{i phi} = (e^{i(phi + a)} + e^{i(phi - a)}) / 2 with a = arccos A, the two
    phases on the two sublattices of a checkerboard. Any filter that averages neighbouring pixels (a low-pass in the Fourier plane) returns
    A e^{i phi} exactly for a field that is locally constant. Returns the phase map (rad)."""
    f = np.asarray(field_complex)
    A = np.minimum(np.abs(f), 1.0)
    phi, a = np.angle(f), np.arccos(A)
    ix = np.indices(f.shape).sum(axis=0)
    return np.where(ix % 2 == 0, phi + a, phi - a)


def _inverse_sinc(a: np.ndarray) -> np.ndarray:
    """u in [0, 1] with sin(pi u) / (pi u) = a (a in [0, 1]), by bisection (sinc falls monotonically from 1 to 0 on [0, 1])."""
    a = np.clip(np.asarray(a, float), 0.0, 1.0)
    lo, hi = np.zeros_like(a), np.ones_like(a)
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        s = np.where(mid > 0, np.sin(np.pi * mid) / (np.pi * np.maximum(mid, 1e-300)), 1.0)
        lo, hi = np.where(s > a, mid, lo), np.where(s > a, hi, mid)
    return 0.5 * (lo + hi)


def bolduc_encode(field_complex: np.ndarray, period_pixels: float = 8.0, axis: int = 0) -> np.ndarray:
    """Phase-only map of Bolduc et al. (Opt. Lett. 38, 3546 (2013)): a blazed grating of period ``period_pixels`` whose depth carries the amplitude,
    Psi = M mod(phi + 2 pi x / period, 2 pi) - pi (M - 1), M = 1 + u, sinc(pi u) = A. The first diffraction order is A exp(i phi) (here: its
    Fourier coefficient over one period is checked by the tests). The wanted light is in the first order, away from the unmodulated light."""
    f = np.asarray(field_complex)
    A = np.minimum(np.abs(f), 1.0)
    u = _inverse_sinc(A)
    M = 1 + u
    x = np.indices(f.shape)[axis]
    carrier = 2 * np.pi * x / period_pixels
    return M * np.mod(np.angle(f) + carrier, 2 * np.pi) - np.pi * u
