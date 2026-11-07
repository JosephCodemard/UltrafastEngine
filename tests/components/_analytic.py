"""Analytic references for the component verification scripts, written independently of the engine.

What it does: refractive indices (Sellmeier from the published coefficients, Ciddor 1996 dry air), the group delay,
GDD and TOD of a length of a medium by exact differentiation of k(ω) L, Treacy's grating pair, Fresnel and Jones
helpers. Nothing here imports the engine.

Main functions:
  sellmeier(lam_m, B, C_um2) -> n               n² = 1 + Σ B λ² / (λ² − C), λ in µm
  ciddor_air(lam_m, t_c=20, p_pa=101325, co2=450) -> n   dry air
  GLASSES[name](lam_m) -> n                     absolute index (relative Sellmeier × n_air, as catalogue data are)
  phase_derivatives(n_of_lam, L_m, w0) -> (τ_g [s], GDD [s²], TOD [s³])    of φ(ω) = ω n(ω) L / c
  derivatives(phi, w0) -> (φ', φ'', φ''')     of any excess phase φ(ω)
  tilted_plate_phase(n_of_lam, d, aoi) -> φ(ω)  plane-wave insertion phase of a tilted plate
  treacy(lam_m, lines_per_m, aoi_rad, sep_m, order, passes) -> (GDD [s²], TOD [s³])
"""
from __future__ import annotations

import math

import numpy as np

C = 299792458.0


def sellmeier(lam_m, B, C_um2):
    l2 = (np.asarray(lam_m, float) * 1e6) ** 2
    n2 = 1.0 + sum(b * l2 / (l2 - c) for b, c in zip(B, C_um2))
    return np.sqrt(n2)


def ciddor_air(lam_m, t_c: float = 20.0, p_pa: float = 101325.0, co2_ppm: float = 450.0):
    """Ciddor, Appl. Opt. 35, 1566 (1996), dry air (x_w = 0)."""
    s2 = (1.0 / (np.asarray(lam_m, float) * 1e6)) ** 2
    k0, k1, k2, k3 = 238.0185, 5792105.0, 57.362, 167917.0
    n_as = 1 + 1e-8 * (k1 / (k0 - s2) + k3 / (k2 - s2))
    n_axs = 1 + (n_as - 1) * (1 + 0.534e-6 * (co2_ppm - 450))
    R = 8.314510
    Ma = 1e-3 * (28.9635 + 12.011e-6 * (co2_ppm - 400))

    def Z(p, T):
        t = T - 273.15
        a0, a1, a2 = 1.58123e-6, -2.9331e-8, 1.1043e-10
        d = 1.83e-11
        return 1 - (p / T) * (a0 + a1 * t + a2 * t * t) + (p / T) ** 2 * d

    T = t_c + 273.15
    rho_axs = 101325.0 * Ma / (Z(101325.0, 288.15) * R * 288.15)
    rho_a = p_pa * Ma / (Z(p_pa, T) * R * T)
    return 1 + (rho_a / rho_axs) * (n_axs - 1)


_SELL = {
    "FusedSilica": ([0.6961663, 0.4079426, 0.8974794], [0.0684043 ** 2, 0.1162414 ** 2, 9.896161 ** 2]),       # Malitson 1965
    "N-BK7": ([1.03961212, 0.231792344, 1.01046945], [0.00600069867, 0.0200179144, 103.560653]),                # Schott
    "CaF2": ([0.5675888, 0.4710914, 3.8484723], [0.050263605 ** 2, 0.1003909 ** 2, 34.649040 ** 2]),             # Malitson 1963
    "Sapphire": ([1.4313493, 0.65054713, 5.3414021], [0.0726631 ** 2, 0.1193242 ** 2, 18.028251 ** 2]),          # Malitson & Dodge 1972 (o)
    "N-SF11": ([1.73759695, 0.313747346, 1.89878101], [0.013188707, 0.0623068142, 155.23629]),                  # Schott
}


def glass(name: str):
    B, Cc = _SELL[name]
    return lambda lam: sellmeier(lam, B, Cc) * ciddor_air(lam)


GLASSES = {k: glass(k) for k in _SELL}
GLASSES["Air"] = ciddor_air
GLASSES["Vacuum"] = lambda lam: np.ones_like(np.asarray(lam, float))


def phi_of_omega(n_of_lam, L_m, w):
    """φ(ω) = ω n(ω) L / c (rad)."""
    w = np.asarray(w, float)
    return w * n_of_lam(2 * math.pi * C / w) * L_m / C


def derivatives(phi, w0: float, h_rel: float = 2e-3):
    """(φ', φ'', φ''') of a callable φ(ω) at ω0 by 7-point central differences. φ must be the *excess* phase
    (no large ω L / c term): a 3e4 rad phase quantises its third derivative to a count of ulps."""
    h = h_rel * w0
    f = np.array([float(phi(w0 + k * h)) for k in range(-3, 4)])
    d1 = (-f[0] + 9 * f[1] - 45 * f[2] + 45 * f[4] - 9 * f[5] + f[6]) / (60 * h)
    d2 = (2 * f[0] - 27 * f[1] + 270 * f[2] - 490 * f[3] + 270 * f[4] - 27 * f[5] + 2 * f[6]) / (180 * h * h)
    d3 = (f[0] - 8 * f[1] + 13 * f[2] - 13 * f[4] + 8 * f[5] - f[6]) / (8 * h ** 3)
    return float(d1), float(d2), float(d3)


def phase_derivatives(n_of_lam, L_m: float, w0: float, h_rel: float = 2e-3):
    """τ_g = dφ/dω, GDD = d²φ/dω², TOD = d³φ/dω³ of φ = ω n L / c at ω0. Differentiates the excess phase
    ω (n − 1) L / c and adds the vacuum delay L / c exactly."""
    d1, d2, d3 = derivatives(lambda w: w * (n_of_lam(2 * math.pi * C / w) - 1.0) * L_m / C, w0, h_rel)
    return d1 + L_m / C, d2, d3


def tilted_plate_phase(n_of_lam, d_m: float, aoi_rad: float, n_out_of_lam=None, w_carrier=None):
    """Plane-wave phase of a tilted plane-parallel plate on a fixed reference plane, k_z d:
    φ(ω) = (ω / c) d [√(n² − n_o² sin²θ) − 1] (n_o: the surrounding medium, which sets the internal angle). The engine's
    elements are absolute (a plate does not remove the air it replaces), so no n_o cos θ term; the − 1 is the vacuum
    phase ω d / c, which has no GDD and only keeps φ small for differencing.
    With `w_carrier`, the phase is read on the plane normal to the carrier ray through the carrier's exit point (the engine's
    reference plane): each colour crosses the extra air path n_o sin θ d tan θ_t(ω0) between its own exit point and that plane."""
    n_o = n_out_of_lam if n_out_of_lam is not None else (lambda lam: np.ones_like(np.asarray(lam, float)))
    s = math.sin(aoi_rad)
    tan_t0 = 0.0
    if w_carrier is not None:
        lam0 = 2 * math.pi * C / w_carrier
        st = float(n_o(lam0)) * s / float(n_of_lam(lam0))
        tan_t0 = st / math.sqrt(1 - st * st)

    def phi(w):
        lam = 2 * math.pi * C / w
        n, no = n_of_lam(lam), n_o(lam)
        return w / C * d_m * (np.sqrt(n * n - (no * s) ** 2) - 1.0) + w / C * no * s * d_m * tan_t0
    return phi


def treacy_gdd(lam_m: float, lines_per_m: float, aoi_rad: float, sep_m: float, order: int = -1, passes: int = 2) -> float:
    """GDD of a parallel grating pair (Treacy, IEEE JQE 5, 454 (1969)), perpendicular separation G, crossed `passes`
    times: sin θ_d = |m| λ / d − sin θ_i,  GDD = −passes · m² λ³ G / (2π c² d² cos³θ_d)
    (the textbook −m² λ³ b / (2π c² d² cos²θ_d) with the slant distance b = G / cos θ_d)."""
    d, m = 1.0 / lines_per_m, abs(order)
    sd = m * lam_m / d - math.sin(aoi_rad)
    cd = math.sqrt(1 - sd * sd)
    return -passes * m * m * lam_m ** 3 * sep_m / (2 * math.pi * C ** 2 * d ** 2 * cd ** 3)


def treacy(lam_m: float, lines_per_m: float, aoi_rad: float, sep_m: float, order: int = -1, passes: int = 2):
    """(GDD [s²], TOD [s³]) of the pair; TOD = dGDD/dω at fixed G (exact differentiation of the closed form)."""
    w0 = 2 * math.pi * C / lam_m
    h = 1e-4 * w0
    g = lambda w: treacy_gdd(2 * math.pi * C / w, lines_per_m, aoi_rad, sep_m, order, passes)
    tod = (-g(w0 + 2 * h) + 8 * g(w0 + h) - 8 * g(w0 - h) + g(w0 - 2 * h)) / (12 * h)
    return g(w0), tod


def fresnel_rs_rp(n1, n2, theta1):
    s1 = math.sin(theta1)
    st = n1 * s1 / n2
    ct = math.sqrt(1 - st * st)
    c1 = math.cos(theta1)
    rs = (n1 * c1 - n2 * ct) / (n1 * c1 + n2 * ct)
    rp = (n2 * c1 - n1 * ct) / (n2 * c1 + n1 * ct)
    return rs, rp
