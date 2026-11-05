#!/usr/bin/env python3
"""Gordon (1986): the soliton self-frequency shift, its T0^-4 law and its rate for three silica Raman models.

    python run.py --tier fast            # 5 soliton durations x 3 Raman models, ~1 min (CI)
    python run.py --tier full --figures  # 9 durations, committed figures

A fundamental (N = 1) sech^2 soliton at 1550 nm in a fibre with only beta2 (D = 15 ps/(nm km), the dispersion of
Gordon's worked example) and gamma = 1.3 /(W km), Raman on, self-steepening off. The fibre is cut into four
segments with probes; the spectral centroid Omega(z) is fitted with a line, its slope is the shift rate. Each run is
long enough for a shift of 0.05 / T0 (a small perturbation: the soliton keeps its shape and T0).

The reference is the first-order perturbation (moment) result for a sech^2 soliton, P(t) = P0 sech^2(t / T0),
gamma P0 T0^2 = |beta2|, energy E = 2 P0 T0, with the model's own response h(t) (unit integral, fraction fR):

    dOmega/dz = -(gamma fR / E) int |P~(W)|^2 W Im H(W) dW / 2 pi,     |P~(W)|^2 = (pi P0 T0^2 W / sinh(pi W T0 / 2))^2,
    Im H(W) = int h(t) sin(W t) dt

-- Gordon's integral over the Raman gain. For T0 much longer than the Raman times Im H ~ W T_R / fR with
T_R = fR int t h(t) dt, and the rate becomes the textbook -8 |beta2| T_R / (15 T0^4) (Agrawal, Nonlinear Fiber
Optics, section on the Raman-induced frequency shift), the T0^-4 law.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM = 1550e-9
C0 = 299792458.0
D_PS_NM_KM = 15.0
BETA2 = -D_PS_NM_KM * 1e-6 * LAM ** 2 / (2 * math.pi * C0)        # s^2/m  (-19.1 ps^2/km)
GAMMA = 1.3e-3                                                    # 1/(W m)
SEGMENTS = 4
MODELS = {"silica": ("Blow-Wood (silica)", 0.18, BLUE), "linAgrawal": ("Lin-Agrawal", 0.245, ORANGE),
          "hollenbeck": ("Hollenbeck-Cantrell", 0.18, AQUA)}


# ------------------------------------------------------------------ the published response functions (as in the engine)
def h_blow_wood(t, t1=12.2e-15, t2=32e-15):
    return np.where(t >= 0, (t1 ** 2 + t2 ** 2) / (t1 * t2 ** 2) * np.exp(-t / t2) * np.sin(t / t1), 0.0)


def h_lin_agrawal(t, tb=96e-15, fa=0.75, fb=0.21, fc=0.04):
    hb = np.where(t >= 0, (2 * tb - t) / tb ** 2 * np.exp(-t / tb), 0.0)
    return (fa + fc) * h_blow_wood(t) + fb * hb


# Hollenbeck & Cantrell, JOSA B 19, 2886 (2002), Table 1: positions (cm^-1), amplitudes, Gaussian and Lorentzian FWHM (cm^-1)
HC = np.array([[56.25, 1.00, 52.10, 17.37], [100.00, 11.40, 110.42, 38.81], [231.25, 36.67, 175.00, 58.33], [362.50, 67.67, 162.50, 54.17],
               [463.00, 74.00, 135.33, 45.11], [497.00, 4.50, 24.50, 8.17], [611.50, 6.80, 41.50, 13.83], [691.67, 4.60, 155.00, 51.67],
               [793.67, 4.20, 59.50, 19.83], [835.50, 4.50, 64.30, 21.43], [930.00, 2.70, 150.00, 50.00], [1080.00, 3.10, 91.00, 30.33],
               [1215.00, 3.00, 160.00, 53.33]])


def h_hollenbeck(t):
    c_cm = C0 * 100
    s = np.zeros_like(t)
    for pos, amp, gfw, lfw in HC:
        s += amp * np.exp(-math.pi * c_cm * lfw * t) * np.exp(-(math.pi * c_cm * gfw) ** 2 * t * t / 4) * np.sin(2 * math.pi * c_cm * pos * t)
    return np.where(t >= 0, s, 0.0)


H = {"silica": h_blow_wood, "linAgrawal": h_lin_agrawal, "hollenbeck": h_hollenbeck}
T_H = np.linspace(0, 2e-12, 40001)                # 0.05 fs steps; the responses have decayed after 1 ps


def response(model):
    h = H[model](T_H)
    return h / np.trapezoid(h, T_H)


def raman_time(model):
    """T_R = fR int t h(t) dt."""
    return MODELS[model][1] * np.trapezoid(T_H * response(model), T_H)


def im_h(model, w):
    h = response(model)
    return np.array([np.trapezoid(h * np.sin(x * T_H), T_H) for x in w])


def gordon_rate(model, t0):
    """|dOmega/dz| (rad/s per m) of Gordon's integral with the model's response."""
    p0 = abs(BETA2) / (GAMMA * t0 * t0)
    w = np.linspace(1e9, 40 / t0, 400)
    p2 = (math.pi * p0 * t0 * t0 * w / np.sinh(math.pi * w * t0 / 2)) ** 2
    integ = 2 * np.trapezoid(p2 * w * im_h(model, w), w) / (2 * math.pi)        # the integrand is even in W
    return GAMMA * MODELS[model][1] / (2 * p0 * t0) * integ


def linear_rate(model, t0):
    return 8 * abs(BETA2) * raman_time(model) / (15 * t0 ** 4)


# ------------------------------------------------------------------ the engine
def centroid(r, probe):
    lam, s = r["probe_%s_wavelength_m" % probe], r["probe_%s_spectrum" % probe]
    w = 2 * math.pi * C0 / lam                       # grid bins: uniform in omega, density per unit omega
    return float(np.sum(w * s) / np.sum(s))


def engine_rate(model, t0):
    """(rate, Omega shifts, z) of the engine: Omega(z) at the probes, fitted with a line (rad/s per m)."""
    length = 0.05 * 15 * t0 ** 3 / (8 * abs(BETA2) * raman_time("silica"))     # a shift of ~0.05 / T0
    seg = length / SEGMENTS
    els = [b.probe("p0")]
    for k in range(1, SEGMENTS + 1):
        els += [b.fiber("f%d" % k, seg, betas=[BETA2], gamma=GAMMA, raman=model), b.probe("p%d" % k)]
    p0 = abs(BETA2) / (GAMMA * t0 * t0)
    spec = b.run_spec(b.pulse(LAM, 2 * math.acosh(math.sqrt(2)) * t0, shape="sech2", peak_power_w=p0),
                      physics=b.physics(selfSteepening=False))
    r = uf.run(b.system("gordon-1986", els), spec)
    if r.summary.get("warnings"):
        print("  warnings:", r.summary["warnings"])
    z = np.arange(SEGMENTS + 1) * seg
    om = np.array([centroid(r, "p%d" % k) for k in range(SEGMENTS + 1)])
    slope = np.polyfit(z, om - om[0], 1)[0]
    return slope, om - om[0], z


def main():
    p = Paper(__file__)
    t0s = np.array([70, 100, 140, 200]) * 1e-15 if p.fast else np.array([50, 70, 85, 100, 120, 140, 200, 280, 400]) * 1e-15
    numbers = {"beta2S2PerM": BETA2, "gammaPerWm": GAMMA}
    curves = {}
    for model, (label, fr, _) in MODELS.items():
        tr = raman_time(model)
        numbers["ramanTimeFs_" + model] = tr * 1e15
        rows = []
        for t0 in t0s:
            rate, _, _ = engine_rate(model, t0)
            g = gordon_rate(model, t0)
            rows.append(dict(t0Fs=t0 * 1e15, engine=rate, gordon=g, linear=linear_rate(model, t0)))
            print("%-11s T0 %5.0f fs  dOmega/dz: engine %+.4e  Gordon integral %.4e  linear %.4e  (engine/Gordon %.4f)" % (
                model, t0 * 1e15, rate, g, rows[-1]["linear"], -rate / g))
        curves[model] = rows
        eng = np.array([-r["engine"] for r in rows])
        numbers["redShift_" + model] = bool(all(r["engine"] < 0 for r in rows))
        numbers["minusRateOverGordonMaxRelErr_" + model] = float(np.max(np.abs(eng / np.array([r["gordon"] for r in rows]) - 1)))
        # the exponent: a power-law fit over the longest three durations (T0 >> the Raman times)
        k = np.polyfit(np.log(t0s[-3:]), np.log(eng[-3:]), 1)[0]
        numbers["exponentLongT0_" + model] = float(k)
        numbers["exponentLongT0Gordon_" + model] = float(np.polyfit(np.log(t0s[-3:]), np.log([r["gordon"] for r in rows[-3:]]), 1)[0])
        numbers["exponentDiffVsGordon_" + model] = abs(float(k) - numbers["exponentLongT0Gordon_" + model])
        numbers["exponentShortT0_" + model] = float(np.polyfit(np.log(t0s[:2]), np.log(eng[:2]), 1)[0])
        numbers["engineOverLinearAtLongestT0_" + model] = float(eng[-1] / rows[-1]["linear"])
        numbers["rows_" + model] = rows
        # Gordon's worked example (abstract, UNVERIFIED): a 250 fs soliton shifts by its own spectral width in ~100 m
        t0_ex = 250e-15 / (2 * math.acosh(math.sqrt(2)))
        rate_ex = math.exp(np.interp(math.log(t0_ex), np.log(t0s), np.log(eng)))        # log-log between the runs
        dnu = 0.315 / 250e-15                                  # sech^2 time-bandwidth product
        numbers["distanceToShiftOneWidthM_" + model] = 2 * math.pi * dnu / abs(rate_ex)
    numbers["ramanTimeFs"] = {m: numbers["ramanTimeFs_" + m] for m in MODELS}
    numbers["redShiftCount"] = sum(int(numbers["redShift_" + m]) for m in MODELS)
    numbers["exponentDiffVsGordonMax"] = max(numbers["exponentDiffVsGordon_" + m] for m in MODELS)
    p.write_numbers(numbers)

    # figures
    tt = np.geomspace(t0s[0], t0s[-1], 24)
    fig, ax = p.figure()
    for model, (label, fr, colr) in MODELS.items():
        rows = curves[model]
        ax.loglog([r["t0Fs"] for r in rows], [-r["engine"] * 1e-12 for r in rows], "o", color=colr, ms=7, label="engine, " + label)
        ax.loglog(tt * 1e15, [gordon_rate(model, x) * 1e-12 for x in tt], color=colr, lw=1.2)
        ax.loglog(tt * 1e15, [linear_rate(model, x) * 1e-12 for x in tt], color=colr, lw=0.8, ls="--")
    ax.plot([], [], color=REF2, lw=1.2, label="Gordon integral with the model's $h(t)$")
    ax.plot([], [], color=REF2, lw=0.8, ls="--", label="$8|\\beta_2| T_R / 15 T_0^4$")
    ax.set_xlabel("soliton duration $T_0$ (fs)")
    ax.set_ylabel("red shift rate $-d\\Omega/dz$ (rad ps$^{-1}$ m$^{-1}$)")
    ax.legend(loc="lower left", fontsize=8)
    p.save(fig, "rate-vs-t0")

    fig, ax = p.figure()
    for model, (label, fr, colr) in MODELS.items():
        rows = curves[model]
        ax.semilogx([r["t0Fs"] for r in rows], [-r["engine"] / r["gordon"] for r in rows], "o-", color=colr, ms=6, lw=1, label=label)
    ax.axhline(1.0, color=REF, lw=1)
    ax.set_xlabel("soliton duration $T_0$ (fs)")
    ax.set_ylabel("engine rate / Gordon integral")
    ax.set_ylim(0.9, 1.1)
    ax.legend(loc="upper right")
    p.save(fig, "rate-ratio")

    fig, ax = p.figure()
    w = np.linspace(0, 2 * math.pi * 45e12, 500)
    for model, (label, fr, colr) in MODELS.items():
        ax.plot(w / (2 * math.pi) * 1e-12, fr * im_h(model, w), color=colr, lw=1.5, label="%s, $T_R$ = %.2f fs" % (label, raman_time(model) * 1e15))
    ax.set_xlabel("frequency offset (THz)")
    ax.set_ylabel("$f_R\\,\\mathrm{Im}\\,\\tilde h$ (Raman gain shape)")
    ax.legend(loc="upper right", fontsize=8)
    p.save(fig, "raman-gain")


if __name__ == "__main__":
    main()
