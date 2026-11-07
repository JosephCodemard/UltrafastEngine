#!/usr/bin/env python3
"""Treacy (1969): the grating pair as a dispersive delay line and a pulse compressor.

    python run.py --tier fast            # engine GDD / TOD / delay at one separation + a 40-point compression scan, ~30 s (CI)
    python run.py --tier full --figures  # finer scan, committed figures

Setup (p. 456): gratings of 1200 lines/mm, lambda = 1.06 um, angle of incidence gamma = 60 degrees, first order, the light
passing the pair once (engine `gratingPair`, passes = 1, order -1; `separationM` is the *perpendicular* distance G, and
the paper's slant separation is b = G sec(gamma - theta), so G = b cos(theta_d) with theta_d the diffraction angle).
The reference is the paper's own equations, evaluated here:

    group delay        tau_0 = b (1 + cos theta) / c                                   (7)
    chirp rate         1/mu = 4 pi^2 c b / (omega^3 d^2 [1 - (lambda/d - sin gamma)^2])  (8): phi'' = -1/mu
    delay for d lambda  (5a):  b (lambda/d) d lambda / (c d [1 - (lambda/d - sin gamma)^2])
    cubic term         sigma = (2 omega_0 mu)^-1 (1 + (lambda/d) sin gamma + sin^2 gamma)  (17) as printed, phi''' = 6 sigma
and, for the exact phase, the numerical derivative of the group delay of eq. (4)/(7) at fixed G (b varies with wavelength).

The compression run (pp. 456-457, 'a pulse with a linearly increasing frequency sweep over a 100 A band, pulsewidth 5.8 ps,
would be compressed to its minimum length if the slant separation b were set at 10 cm'): a Gaussian pulse with 10 nm
(FWHM of the intensity spectrum, ASSUMPTION for '100 A band'), i.e. a 165 fs transform limit, carries the group-delay dispersion that
makes it 5.8 ps long; b is scanned and the output duration measured. The Fresnel-integral analogue of eq. (14) for a Gaussian is the
closed form  T(b) = T0 sqrt(1 + (4 ln2 (phi_in - |phi''(b)|) / T0^2)^2).
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

C0 = 299792458.0
LINES_PER_MM, GAMMA_DEG, LAM = 1200.0, 60.0, 1.06e-6
D = 1e-3 / LINES_PER_MM
GAMMA = math.radians(GAMMA_DEG)
B_PAPER = 0.10                  # m, p. 456
T_PAPER = 5.8e-12               # s, p. 456
SPEC_FWHM_M = 10e-9             # ASSUMPTION: the "100 A band" is the intensity FWHM


class Pair:
    """Treacy's equations at a carrier wavelength lam (m)."""

    def __init__(self, lam):
        self.lam = lam
        self.w0 = 2 * math.pi * C0 / lam
        self.x = lam / D - math.sin(GAMMA)                 # = sin(theta_d)
        self.td = math.asin(self.x)
        self.theta = GAMMA - self.td                         # angle between incident and diffracted rays, eq. (3)

    def g_of_b(self, bm):
        return bm * math.cos(self.td)                       # b = G sec(gamma - theta)

    def tau0(self, bm):                                      # eq. (7)
        return bm / C0 * (1 + math.cos(self.theta))

    def inv_mu(self, bm):                                    # eq. (8)
        return 4 * math.pi ** 2 * C0 * bm / (self.w0 ** 3 * D ** 2 * (1 - self.x ** 2))

    def dtau_dlam(self, bm):                                 # eq. (5a), per unit wavelength
        return bm * (self.lam / D) / (C0 * D * (1 - self.x ** 2))

    def sigma_printed(self, bm):                             # eq. (17) as printed
        return self.inv_mu(bm) / (2 * self.w0) * (1 + self.lam / D * math.sin(GAMMA) + math.sin(GAMMA) ** 2)

    def group_delay(self, w, g):                             # exact, fixed perpendicular separation g
        lam = 2 * math.pi * C0 / w
        td = math.asin(lam / D - math.sin(GAMMA))
        return g / C0 * (1 + math.cos(GAMMA - td)) / math.cos(td)

    def exact_gdd_tod(self, bm):
        g = self.g_of_b(bm)
        h = self.w0 * 1e-4
        t = [self.group_delay(self.w0 + k * h, g) for k in (-1, 0, 1)]
        return (t[2] - t[0]) / (2 * h), (t[2] - 2 * t[1] + t[0]) / h ** 2

    def tod_closed_form(self, bm):
        """Exact third-order term in closed form: |phi''| / omega * 3 (1 + (lambda/d) sin gamma - sin^2 gamma) / cos^2 theta_d."""
        return self.inv_mu(bm) / self.w0 * 3 * (1 + self.lam / D * math.sin(GAMMA) - math.sin(GAMMA) ** 2) / (1 - self.x ** 2)


def exact_phase_width(pr, g, t0, phi_in, n=1 << 13):
    """FWHM of the output of a Gaussian pulse (transform-limited FWHM t0, group-delay dispersion phi_in) through the pair at perpendicular
    separation g, with the exact phase: the integral of the exact group delay of eq. (4)/(7) over frequency, all orders."""
    sig = 2 * math.pi * 0.4413 / t0                         # FWHM of the intensity spectrum, rad/s
    dw = np.linspace(-8 * sig, 8 * sig, n)
    step = dw[1] - dw[0]
    tau = np.array([pr.group_delay(pr.w0 + x, g) for x in dw])
    phase = np.concatenate([[0], np.cumsum(0.5 * (tau[1:] + tau[:-1]) * step)])
    phase -= phase[n // 2]                                  # phase at the carrier is irrelevant
    phase -= tau[n // 2] * dw                               # remove the plain delay
    amp = np.exp(-(dw / sig) ** 2 * 2 * math.log(2))      # field amplitude: the intensity spectrum has FWHM sig
    spec = amp * np.exp(1j * (phase + 0.5 * phi_in * dw ** 2))
    field = np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(spec)))
    t = np.fft.fftshift(np.fft.fftfreq(n, d=step / (2 * math.pi)))
    k = int(np.argmax(np.abs(field) ** 2))
    half = np.abs(field[k]) ** 2 / 2
    width = float(np.sum(np.abs(field) ** 2 > half)) * (t[1] - t[0])           # coarse width, to place a fine time grid
    tf = t[k] + np.linspace(-1.2 * width, 1.2 * width, 1201)                     # the FFT's time step is too coarse for a 180 fs FWHM
    i = np.abs(np.array([np.sum(spec * np.exp(-1j * dw * x)) for x in tf])) ** 2
    k = int(np.argmax(i))
    half = i[k] / 2
    lo = k
    while i[lo] > half:
        lo -= 1
    hi = k
    while i[hi] > half:
        hi += 1
    tl_ = tf[lo] + (half - i[lo]) * (tf[lo + 1] - tf[lo]) / (i[lo + 1] - i[lo])
    th_ = tf[hi - 1] + (half - i[hi - 1]) * (tf[hi] - tf[hi - 1]) / (i[hi] - i[hi - 1])
    return th_ - tl_


def engine_pair(bm, pair, pulse, grid=None):
    system = b.system("treacy", b.Sequential([b.grating_pair("gp", LINES_PER_MM, GAMMA_DEG, pair.g_of_b(bm), order=-1, passes=1)]))
    return uf.run(system, b.run_spec(pulse, grid=grid))


def fwhm_gaussian(t0, phi):
    return t0 * math.sqrt(1 + (4 * math.log(2) * phi / t0 ** 2) ** 2)


def main():
    p = Paper(__file__)
    out = {}

    # ---- 1. the pair itself: group delay, GDD, TOD at b = 10 cm
    probe = engine_pair(B_PAPER, Pair(LAM), b.pulse(LAM, 1e-12, energy=1e-9))
    lam_c = probe.summary["input"]["centroidWavelengthM"]            # the carrier the grid actually uses
    pr = Pair(lam_c)
    el = probe.summary["elements"][0]
    out["carrierWavelengthM"] = lam_c
    out["diffractionAngleDeg"] = math.degrees(pr.td)
    out["gdd_engine_s2"] = el["own"]["gddS2"]
    out["gdd_eq8_s2"] = -pr.inv_mu(B_PAPER)
    out["gddRelError"] = el["own"]["gddS2"] / out["gdd_eq8_s2"] - 1
    out["tau0_engine_s"] = el["own"]["groupDelayS"]
    out["tau0_eq7_s"] = pr.tau0(B_PAPER)
    out["tau0RelError"] = el["own"]["groupDelayS"] / pr.tau0(B_PAPER) - 1
    gdd_x, tod_x = pr.exact_gdd_tod(B_PAPER)
    out["gddRelError_vs_numericalDerivative"] = el["own"]["gddS2"] / gdd_x - 1
    out["tod_engine_s3"] = el["own"]["todS3"]
    out["tod_numericalDerivative_s3"] = tod_x
    out["tod_closedForm_s3"] = pr.tod_closed_form(B_PAPER)
    out["tod_eq17AsPrinted_s3"] = 6 * pr.sigma_printed(B_PAPER)
    out["todRelError_vs_numericalDerivative"] = el["own"]["todS3"] / tod_x - 1
    out["todRelError_closedForm_vs_numerical"] = pr.tod_closed_form(B_PAPER) / tod_x - 1
    out["tod_eq17_over_exact"] = 6 * pr.sigma_printed(B_PAPER) / tod_x

    # ---- 2. the worked example: delay between two wavelengths 100 A apart, per cm of slant separation (p. 456)
    dlam = 100e-10
    dw = 2 * math.pi * C0 * dlam / lam_c ** 2
    per_cm = 1e-2
    probe1 = engine_pair(per_cm, pr, b.pulse(LAM, 1e-12, energy=1e-9))
    out["delayPerCm_engine_psPerCm"] = abs(probe1.summary["elements"][0]["own"]["gddS2"]) * dw * 1e12
    out["delayPerCm_eq5a_psPerCm"] = pr.dtau_dlam(per_cm) * dlam * 1e12
    out["delayPerCm_paper_psPerCm"] = 0.58
    out["delay100A_at10cm_engine_ps"] = abs(el["own"]["gddS2"]) * dw * 1e12
    out["delay100A_at10cm_paper_ps"] = 5.8
    # the angular dispersion quoted on p. 457: "about 2e-2 rad / 100 A"
    out["angularDispersion_radPer100A"] = dlam / (D * math.cos(pr.td))

    # ---- 3. compression scan
    tl = 0.4413 * LAM ** 2 / (C0 * SPEC_FWHM_M)
    phi_in = tl ** 2 * math.sqrt((T_PAPER / tl) ** 2 - 1) / (4 * math.log(2))
    pulse = b.pulse(LAM, tl, energy=1e-9, gdd_s2=phi_in)
    grid = b.grid(n=16384, window_s=60e-12)
    pin = engine_pair(1e-3, pr, pulse, grid).summary["input"]
    out["input_fwhm_ps"] = pin["fwhmS"] * 1e12
    out["input_transformLimit_fs"] = pin["transformLimitFwhmS"] * 1e15
    out["input_spectralFwhm_nm"] = pin["spectralFwhmM"] * 1e9
    out["input_gdd_s2"] = phi_in
    t0 = pin["transformLimitFwhmS"]

    coarse = np.arange(3e-2, 17e-2 + 1e-9, 1e-2 if p.fast else 0.5e-2)
    b_opt_analytic = phi_in / (pr.inv_mu(1.0))
    fine = np.arange(b_opt_analytic - 0.4e-2, b_opt_analytic + 0.4e-2 + 1e-9, 0.04e-2 if p.fast else 0.02e-2)
    bs = np.unique(np.concatenate([coarse, fine]))
    widths = []
    for bm in bs:
        e = engine_pair(float(bm), pr, pulse, grid).summary["elements"][0]["pulse"]
        widths.append(e["fwhmS"])
    widths = np.array(widths)
    analytic = np.array([fwhm_gaussian(t0, phi_in - pr.inv_mu(bm)) for bm in bs])
    exact = np.array([exact_phase_width(pr, pr.g_of_b(float(bm)), t0, phi_in) for bm in bs])
    k = int(np.argmin(widths))
    # parabola through the minimum for the location
    sel = slice(max(k - 2, 0), k + 3)
    c2 = np.polyfit(bs[sel], widths[sel], 2)
    b_opt_engine = -c2[1] / (2 * c2[0]) if c2[0] > 0 else bs[k]
    out["bOpt_engine_cm"] = b_opt_engine * 100
    out["bOpt_analytic_cm"] = b_opt_analytic * 100
    out["bOpt_paper_cm"] = B_PAPER * 100
    out["minWidth_engine_fs"] = widths.min() * 1e15
    out["minWidth_analyticQuadratic_fs"] = t0 * 1e15
    out["compressionFactor_engine"] = T_PAPER / widths.min()
    out["widthMaxRelError"] = float(np.max(np.abs(widths / analytic - 1)[(bs < b_opt_analytic - 0.6e-2) | (bs > b_opt_analytic + 0.6e-2)]))
    out["widthMaxRelError_vs_exactPhase"] = float(np.max(np.abs(widths / exact - 1)))
    out["minWidth_exactPhaseReference_fs"] = exact.min() * 1e15
    out["bOpt_exactPhaseReference_cm"] = bs[int(np.argmin(exact))] * 100
    out["widthAtZeroPair_ps"] = out["input_fwhm_ps"]
    # the sign: the same pair with the opposite chirp makes the pulse longer
    wrong = engine_pair(B_PAPER, pr, b.pulse(LAM, tl, energy=1e-9, gdd_s2=-phi_in), grid).summary["elements"][0]["pulse"]["fwhmS"]
    out["oppositeChirpWidth_ps"] = wrong * 1e12
    out["curve_exact"] = {"x": bs * 100, "y": exact * 1e15}
    out["curve_width"] = {"x": bs * 100, "y": widths * 1e15}
    p.write_numbers(out)

    # ------------------------------------------------------------ figures
    fig, ax = p.figure(width_in=6.6, height_in=4.0)
    bb = np.linspace(3e-2, 17e-2, 800)
    ax.semilogy(bb * 100, [fwhm_gaussian(t0, phi_in - pr.inv_mu(x)) * 1e15 for x in bb], color=REF, lw=1.5, label="Gaussian through eq. (14): quadratic phase of eq. (8)")
    ax.semilogy(bs * 100, widths * 1e15, "o", color=BLUE, ms=4, label="engine gratingPair (exact phase)")
    ax.semilogy(bs * 100, exact * 1e15, color=AQUA, lw=1.2, label="reference with the exact phase (integral of the group delay)")
    ax.axvline(B_PAPER * 100, color=REF2, ls="--", lw=1.2, label="paper's b = 10 cm (p. 456)")
    ax.axvline(b_opt_analytic * 100, color=ORANGE, ls=":", lw=1.5, label="b = chirp / (1/mu per cm) = %.2f cm" % (b_opt_analytic * 100))
    ax.axhline(t0 * 1e15, color=REF2, lw=0.8)
    ax.set(xlabel="slant separation b (cm)", ylabel="output pulse duration, FWHM (fs)")
    ax.legend(fontsize=7.5, loc="upper center")
    p.save(fig, "compression-vs-separation")

    fig, ax = p.figure(width_in=6.4, height_in=3.8)
    names = ["engine", "numerical derivative\nof eq. (4)", "closed form\n(derived)", "eq. (17)\nas printed"]
    vals = np.array([out["tod_engine_s3"], out["tod_numericalDerivative_s3"], out["tod_closedForm_s3"], out["tod_eq17AsPrinted_s3"]]) * 1e39
    ax.bar(names, vals, color=[BLUE, REF2, REF2, ORANGE])
    for i, v in enumerate(vals):
        ax.text(i, v, "%.2f" % v, ha="center", va="bottom", fontsize=9)
    ax.set(ylabel="third-order phase derivative (1e-39 s^3)", title="TOD of the pair at b = 10 cm")
    ax.grid(False)
    p.save(fig, "tod-vs-eq17")


if __name__ == "__main__":
    main()
