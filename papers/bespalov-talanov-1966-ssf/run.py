#!/usr/bin/env python3
"""Bespalov & Talanov (1966): the small-scale self-focusing gain spectrum of a plane wave in a Kerr medium.

    python run.py --tier fast            # 14 ripple wavenumbers, ~10 s (CI, through build/paper_check.py)
    python run.py --tier full --figures  # 40 wavenumbers, two seed amplitudes, committed figures

A cw beam (one spectral bin, so the intensity is constant in time: no average over a pulse) with a flat-top
transverse profile crosses a Kerr plate on a cartesian grid (1024 x 1, the x cut of a 2-D grid). Its field carries
a weak amplitude ripple eps cos(K x). Linearising the envelope equation about the plane wave gives (docs/reference/
transverse.md, small-scale self-focusing)

    u' = q v,  v' = (2 Delta - q) u,   q = K^2 / (2 k),  k = n0 k0,  Delta = k0 n2 I

so the ripple of the fluence grows as 2 eps cosh(g z), g^2 = q (2 Delta - q) (a cosine when q > 2 Delta). The gain
g(K) peaks at q = Delta, i.e. K_opt = sqrt(2 k Delta) = k0 sqrt(2 n0 n2 I), with g_max = Delta = k0 n2 I, and is zero
above K = sqrt(2) K_opt. We measure the engine's growth from the fluence ripple of the core and compare.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM = 1030e-9      # m
N0 = 1.45
N2 = 2.7e-20       # m^2/W (a fused-silica-like value; constant index: no dispersion)
L = 3e-3           # plate length, m
HALF = 3.5e-3      # half width of the grid, m
NX = 2048
CORE = 0.4e-3      # the ripple is fitted over |x| < CORE (the flat core of the 1 mm flat-top)
DELTA = 1000.0     # target Delta = k0 n2 I, 1/m  (B = Delta L = 3 rad)
K0 = 2 * math.pi / LAM
K = N0 * K0


def plate(power_w, profile):
    mats = {"kerr": b.material(index={"kind": "constant", "value": N0}, n2=N2)}
    el = b.bulk("k", "kerr", L, beam=b.gaussian_beam(1e-3),
                transverse=b.transverse("cartesian", nx=NX, ny=1, half_width_m=HALF, initial_profile=profile))
    spec = b.run_spec(b.cw(LAM, power_w), physics=b.physics(raman=False, selfSteepening=False))
    r = uf.run(b.system("bespalov-talanov", [el], materials=mats), spec)
    if r.summary.get("warnings"):
        print("  warnings:", r.summary["warnings"])
    return r


def modulation(x, f, period):
    """Amplitude of the cos/sin(Kx) component of f relative to its mean over the core (least squares)."""
    m = np.abs(x) < CORE
    kx = 2 * math.pi / period * x[m]
    A = np.stack([np.ones(m.sum()), np.cos(kx), np.sin(kx)], axis=1)
    c, *_ = np.linalg.lstsq(A, f[m], rcond=None)
    return math.hypot(c[1], c[2]) / c[0]


def predicted(eps, kk, delta):
    q = kk * kk / (2 * K)
    g2 = q * (2 * delta - q)
    return 2 * eps * (math.cosh(math.sqrt(g2) * L) if g2 >= 0 else abs(math.cos(math.sqrt(-g2) * L)))


def growth_rate(m, eps):
    """The g that gives the measured fluence ripple m = 2 eps cosh(g L) (unstable side only)."""
    r = m / (2 * eps)
    return math.acosh(r) / L if r >= 1 else float("nan")


def main():
    p = Paper(__file__)
    eps_list = [1e-3] if p.fast else [1e-3, 3e-3]
    # calibrate: the intensity of the flat core per watt of the cw source (the fluence is intensity x the 1 ns window)
    r0 = plate(1e6, b.initial_profile("flatTop"))
    x, f0 = r0["el_k_x_m"], r0["el_k_fluence_in_x"]
    i_per_w = f0[np.abs(x) < CORE].mean() / r0.summary["grid"]["windowS"] / 1e6
    intensity = DELTA / (K0 * N2)
    power = intensity / i_per_w
    k_opt = math.sqrt(2 * K * DELTA)
    print("I = %.4g W/m^2, Delta = %.4g 1/m, K_opt = %.5g 1/m (period %.2f um)" % (intensity, DELTA, k_opt, 2e6 * math.pi / k_opt))

    # ripple wavenumbers: integer numbers of periods in the box, from 0.2 to 1.9 K_opt
    n_k = 14 if p.fast else 40
    ratios = np.linspace(0.2, 1.9, n_k)
    rows = []
    b_reported = None
    for eps in eps_list:
        for ratio in ratios:
            periods = max(1, round(2 * HALF * ratio * k_opt / (2 * math.pi)))
            period = 2 * HALF / periods
            kk = 2 * math.pi / period
            r = plate(power, b.initial_profile("flatTop", ripple=b.profile_ripple(period, eps)))
            b_reported = r.summary["elements"][0]["bIntegralRad"]
            m = modulation(r["el_k_x_m"], r["el_k_fluence_out_x"], period)
            mp = predicted(eps, kk, DELTA)
            q = kk * kk / (2 * K)
            g2 = q * (2 * DELTA - q)
            rows.append(dict(eps=eps, kOverKopt=kk / k_opt, modulation=m, predicted=mp, relErr=m / mp - 1,
                             gEngineOverDelta=growth_rate(m, eps) / DELTA, gBtOverDelta=(math.sqrt(g2) / DELTA if g2 > 0 else 0.0)))
            print("eps %.0e K/Kopt %.3f  ripple: engine %.5f  BT %.5f  (%+.2f %%)" % (eps, kk / k_opt, m, mp, 100 * (m / mp - 1)))

    main_rows = [r for r in rows if r["eps"] == eps_list[0]]
    unstable = [r for r in main_rows if r["kOverKopt"] < math.sqrt(2) - 0.05]
    # the peak of the measured g(K): a parabola through the three largest points
    gk = np.array([[r["kOverKopt"], r["gEngineOverDelta"]] for r in unstable])
    i = int(np.nanargmax(gk[:, 1]))
    i = min(max(i, 1), len(gk) - 2)
    a, bq, c = np.polyfit(gk[i - 1:i + 2, 0], gk[i - 1:i + 2, 1], 2)
    k_peak, g_peak = -bq / (2 * a), c - bq * bq / (4 * a)
    numbers = {
        "one": 1.0, "intensityWPerM2": intensity, "deltaPerM": DELTA, "kOptPerM": k_opt,
        "bIntegralReportedRad": b_reported, "bIntegralExpectedRad": DELTA * L,
        "gMaxOverDeltaEngine": g_peak, "kPeakOverKoptEngine": k_peak,
        "maxRelErrRipple": max(abs(r["relErr"]) for r in main_rows),
        "gainSpectrum": {"x": [r["kOverKopt"] for r in unstable], "y": [r["gEngineOverDelta"] for r in unstable]},
        "rows": rows,
    }
    stable = [r for r in main_rows if r["kOverKopt"] > math.sqrt(2) + 0.05]
    if stable:
        numbers["maxStableRippleOverSeed"] = max(r["modulation"] / (2 * r["eps"]) for r in stable)
    p.write_numbers(numbers)

    kx = np.linspace(0, 1.95, 400)
    q = (kx * k_opt) ** 2 / (2 * K)
    g_bt = np.sqrt(np.clip(q * (2 * DELTA - q), 0, None)) / DELTA
    fig, ax = p.figure()
    ax.plot(kx, g_bt, color=REF, lw=1.5, label="Bespalov-Talanov $g = \\sqrt{q(2\\Delta - q)}$")
    ax.plot([r["kOverKopt"] for r in unstable], [r["gEngineOverDelta"] for r in unstable], "o", color=BLUE, ms=7,
            label="engine, cartesian plate (from the ripple growth)")
    ax.axvline(1.0, color=REF2, lw=0.8, ls=":")
    ax.axvline(math.sqrt(2), color=REF2, lw=0.8, ls=":")
    ax.text(1.02, 0.05, "$K_{opt}$", color=REF2)
    ax.text(math.sqrt(2) + 0.02, 0.05, "$\\sqrt{2} K_{opt}$", color=REF2)
    ax.set_xlabel("ripple wavenumber $K / K_{opt}$,  $K_{opt} = k_0 \\sqrt{2 n_0 n_2 I}$")
    ax.set_ylabel("amplitude growth rate $g / \\Delta$,  $\\Delta = k_0 n_2 I$")
    ax.set_ylim(0, 1.35)
    ax.legend(loc="upper right")
    p.save(fig, "gain-spectrum")

    fig, ax = p.figure()
    for eps, col in zip(eps_list, (BLUE, ORANGE)):
        rr = [r for r in rows if r["eps"] == eps]
        ax.semilogy([r["kOverKopt"] for r in rr], [r["modulation"] / (2 * eps) for r in rr], "o", color=col, ms=6,
                    label="engine, seed $\\varepsilon$ = %.0e" % eps)
    ax.semilogy(kx[1:], [predicted(1.0, k * k_opt, DELTA) / 2 for k in kx[1:]], color=REF, lw=1.5,
                label="Bespalov-Talanov: cosh($gL$), |cos| above $\\sqrt{2} K_{opt}$")
    ax.set_xlabel("ripple wavenumber $K / K_{opt}$")
    ax.set_ylabel("fluence ripple after the plate / seed ripple $2\\varepsilon$")
    ax.set_title("$B = \\Delta L$ = %.1f rad" % (DELTA * L), fontsize=10)
    ax.legend(loc="lower left")
    p.save(fig, "ripple-growth")


if __name__ == "__main__":
    main()
