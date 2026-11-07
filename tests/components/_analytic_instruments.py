"""Analytic references for the instrument checks (instruments_more.py), written independently of the engine.

What it does: closed-form and directly integrated answers for a transform-limited or linearly chirped Gaussian pulse
as seen by instruments. Nothing here imports the engine.

Main functions:
  gauss_spectrum_omega(tau, w0) -> (sigma_w)             rms width of the intensity spectrum of a Gaussian pulse of FWHM tau
  gauss_fraction(w, w0, sigma) -> fraction below w       normal CDF
  chirped_gaussian_fwhm(tau, gdd) -> FWHM                tau sqrt(1 + (4 ln2 gdd / tau^2)^2)
  iac_trace(delay, tau, w0) -> S(delay) / S(inf)         interferometric autocorrelation of a real Gaussian envelope
  shg_total_of_phase(w, amp, phase) -> int |A(t)|^4 dt   SHG d-scan signal for a spectral amplitude and phase
  wigner_ridge_slope(tau, gdd) -> d omega_inst / dt      gdd / (T0^4 + gdd^2), T0 = tau / (2 sqrt(ln2))
"""
from __future__ import annotations

import math

import numpy as np

LN2 = math.log(2.0)


def gauss_sigma_omega(tau_fwhm: float) -> float:
    """rms width of the intensity spectrum |A(w)|^2 of a transform-limited Gaussian pulse: FWHM_w = 4 ln2 / tau."""
    return (4 * LN2 / tau_fwhm) / (2 * math.sqrt(2 * LN2))


def norm_cdf(x):
    from math import erf
    return 0.5 * (1 + np.vectorize(erf)(np.asarray(x, float) / math.sqrt(2)))


def chirped_gaussian_fwhm(tau: float, gdd: float) -> float:
    return tau * math.sqrt(1 + (4 * LN2 * gdd / tau ** 2) ** 2)


def iac_trace(delay, tau_fwhm: float, w0: float, n: int = 8001):
    """S(d) = int |a(t) + a(t - d) e^{i w0 d}|^4 dt, a = exp(-2 ln2 t^2 / tau^2) (intensity FWHM tau), over S(inf) = 2 int a^4."""
    t = np.linspace(-6 * tau_fwhm, 6 * tau_fwhm, n)
    dt = t[1] - t[0]
    a = lambda x: np.exp(-2 * LN2 * (x / tau_fwhm) ** 2)
    out = []
    for d in np.atleast_1d(delay):
        b = a(t) + a(t - d) * np.exp(1j * w0 * d)
        out.append(np.sum(np.abs(b) ** 4) * dt)
    return np.array(out) / (2 * np.sum(a(t) ** 4) * dt)


def shg_total_of_phase(w, amp, phase):
    """int |A(t)|^4 dt (arbitrary units) of the field with spectral amplitude amp(w) and spectral phase on the uniform grid w."""
    n = 1 << 14
    spec = np.zeros(n, complex)
    k = len(w)
    spec[:k] = amp * np.exp(1j * phase)
    at = np.fft.ifft(spec)
    return float(np.sum(np.abs(at) ** 4))


def wigner_ridge_slope(tau_fwhm: float, gdd: float) -> float:
    """Chirped Gaussian, A(w) = exp(-T0^2 w^2/2 + i gdd w^2/2), E = Re{A(t) e^{-i w0 t}}: w_inst = w0 + gdd t / (T0^4 + gdd^2)."""
    T0 = tau_fwhm / (2 * math.sqrt(LN2))
    return gdd / (T0 ** 4 + gdd ** 2)
