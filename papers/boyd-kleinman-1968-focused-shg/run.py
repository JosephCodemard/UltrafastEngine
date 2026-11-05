#!/usr/bin/env python3
"""Boyd & Kleinman (1968): second-harmonic generation with a focused Gaussian beam, no walk-off.

    python run.py --tier fast            # 6 focusing parameters, ~30 s (CI, through build/paper_check.py)
    python run.py --tier full --figures  # 13 focusing parameters, the h(sigma) scan at xi = 2.84, committed figures

The crystal element on a radial (Hankel) grid takes a Gaussian beam focused into the middle of a 10 mm crystal
(waist w0 = sqrt(L / (xi k1)), so xi = L / b, b = k1 w0^2) and a weak (1 pJ), long (2 ps) pulse: low conversion,
no group-velocity effects. The crystal is "ideal": both waves see the same Sellmeier index n^2 = A + B lambda^2 /
(lambda^2 - C) with a tiny B, so the phase mismatch Delta k = k(2w) - 2 k(w) is set by B alone and there is no
walk-off. The focusing function is measured as

    h_engine(sigma, xi) = xi * eta_radial(sigma, xi) / eta_1D(xi)

where eta_1D is the 1-D model of the same crystal with a collimated beam of radius w0 and Delta k = 0 (the near-field
limit, in which h = xi exactly). BK's sigma = b (2 k1 - k2) / 2 = -b Delta k_engine / 2.

The reference is BK's integral, evaluated here with numpy:

    h(sigma, xi) = 1 / (4 xi) |int_{-xi}^{xi} exp(i sigma tau) / (1 + i tau) d tau|^2

maximised over sigma for each xi.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM = 1030e-9          # fundamental wavelength, m
L = 10e-3              # crystal length, m
N1 = 1.8               # index of the ideal crystal
C_UM2 = 0.01           # Sellmeier pole (um^2) that carries the small dispersion setting Delta k
D_PM_V = 2.0           # deff, pm/V (low conversion: only ratios matter)


# ------------------------------------------------------------------ the reference: BK's integral
def h_bk(sigma, xi, n=6001):
    """BK focusing function without walk-off or absorption (Gauss-Legendre is not needed: the integrand is smooth)."""
    tau = np.linspace(-xi, xi, n)
    f = np.exp(1j * sigma * tau) / (1 + 1j * tau)
    return abs(np.trapezoid(f, tau)) ** 2 / (4 * xi)


def golden_max(f, lo, hi, it=60):
    g = (math.sqrt(5) - 1) / 2
    c, d = hi - g * (hi - lo), lo + g * (hi - lo)
    fc, fd = f(c), f(d)
    for _ in range(it):
        if fc > fd:
            hi, d, fd = d, c, fc
            c = hi - g * (hi - lo)
            fc = f(c)
        else:
            lo, c, fc = c, d, fd
            d = lo + g * (hi - lo)
            fd = f(d)
    x = 0.5 * (lo + hi)
    return x, f(x)


def bk_optimum(xi):
    """(sigma_m, h_m) of the BK integral at xi."""
    return golden_max(lambda s: h_bk(s, xi), -0.5, 2.0)


# ------------------------------------------------------------------ the engine
def index(bc, lam_m):
    lu = lam_m * 1e6
    return math.sqrt(N1 ** 2 - bc + bc * lu ** 2 / (lu ** 2 - C_UM2))


def k1():
    return 2 * math.pi * index(0.0, LAM) / LAM


def delta_k(bc):
    """Engine phase mismatch k(2w) - 2 k(w) of the ideal crystal with Sellmeier B = bc (1/m)."""
    return 4 * math.pi / LAM * (index(bc, LAM / 2) - index(bc, LAM))


def bc_for_sigma(sigma, xi):
    """Sellmeier B that gives BK's sigma = -b Delta k / 2 at xi (Delta k is linear in B to 1e-6)."""
    w0 = math.sqrt(L / (xi * k1()))
    bb = k1() * w0 * w0
    slope = delta_k(1e-3) / 1e-3
    return -2 * sigma / (bb * slope)


def sigma_of(bc, xi):
    w0 = math.sqrt(L / (xi * k1()))
    return -k1() * w0 * w0 * delta_k(bc) / 2


def crystal_def(bc):
    s = {"kind": "sellmeier", "A": N1 ** 2 - bc, "B": [bc], "C": [C_UM2]}
    return {"name": "ideal", "no": s, "ne": s, "deffPmPerV": D_PM_V, "n2": 0.0}


def eta(xi, bc, radial=True):
    """Conversion efficiency of the crystal element (low conversion)."""
    w0 = math.sqrt(L / (xi * k1()))
    if radial:
        edge = w0 * math.sqrt(1 + xi * xi)                   # beam radius at the crystal faces
        pts = int(max(96, 28 * math.sqrt(1 + xi * xi)))      # >= 4 samples per waist radius
        beam = b.gaussian_beam(w0, z_waist_m=L / 2)
        tv = b.transverse("radial", points=pts, radius_m=max(6 * edge, 4 * w0))
    else:
        beam, tv = b.constant_beam(radius_m=w0), None
    el = b.crystal("x", crystal_def(bc), L, "shg", "0", cut={"thetaDeg": 90, "phiDeg": 0}, beam=beam, transverse=tv,
                   chi3=False, output="sh")
    spec = b.run_spec(b.pulse(LAM, 2e-12, 1e-12), physics=b.physics(selfSteepening=False))
    r = uf.run(b.system("boyd-kleinman", [el]), spec)
    if r.summary.get("warnings"):
        print("  warnings:", r.summary["warnings"])
    return r.summary["elements"][0]["values"]["conversionEfficiency"]


def h_engine(sigma, xi, eta1d):
    return xi * eta(xi, bc_for_sigma(sigma, xi)) / eta1d


def engine_optimum(xi, eta1d, sigma_ref, ds=0.15):
    """sigma and h at the engine's maximum: a parabola through three runs around the reference optimum."""
    s = np.array([sigma_ref - ds, sigma_ref, sigma_ref + ds])
    h = np.array([h_engine(x, xi, eta1d) for x in s])
    a, bq, c = np.polyfit(s, h, 2)
    sm = -bq / (2 * a)
    return sm, c - bq * bq / (4 * a), list(zip(s, h))


def main():
    p = Paper(__file__)
    xis = [0.05, 0.5, 1.0, 2.0, 2.84, 4.0, 6.0] if p.fast else [0.05, 0.2, 0.5, 1.0, 1.5, 2.0, 2.5, 2.84, 3.2, 4.0, 6.0, 8.0, 10.0]
    rows, worst = [], 0.0
    for xi in xis:
        e1 = eta(xi, 0.0, radial=False)
        if xi < 0.1:
            # weak focusing: h -> xi at sigma = 0 (the near field); this is not circular: the radial run diffracts
            he, hr = h_engine(0.0, xi, e1), h_bk(0.0, xi)
            rows.append(dict(xi=xi, sigma_ref=0.0, h_ref=hr, sigma_eng=0.0, h_eng=he, samples=[(0.0, he)]))
        else:
            sr, hr = bk_optimum(xi)
            se, he, samples = engine_optimum(xi, e1, sr)
            rows.append(dict(xi=xi, sigma_ref=sr, h_ref=hr, sigma_eng=se, h_eng=he, samples=samples))
        for s, h in rows[-1]["samples"]:
            worst = max(worst, abs(h / h_bk(s, xi) - 1))
        r = rows[-1]
        print("xi %5.2f  sigma_m: BK %.3f engine %.3f   h_m: BK %.4f engine %.4f" % (xi, r["sigma_ref"], r["sigma_eng"], r["h_ref"], r["h_eng"]))

    # the optimum over xi: a parabola in ln(xi) through the three largest engine values
    focus = [r for r in rows if r["xi"] >= 0.5]
    i = int(np.argmax([r["h_eng"] for r in focus]))
    i = min(max(i, 1), len(focus) - 2)
    lx = np.log([focus[j]["xi"] for j in (i - 1, i, i + 1)])
    hv = [focus[j]["h_eng"] for j in (i - 1, i, i + 1)]
    a, bq, c = np.polyfit(lx, hv, 2)
    xi_opt = math.exp(-bq / (2 * a))
    h_at_284 = next(r for r in rows if abs(r["xi"] - 2.84) < 1e-9)
    xi_ref, h_ref_max = golden_max(lambda x: bk_optimum(x)[1], 1.0, 6.0, it=40)
    weak = rows[0]

    numbers = {
        "hmAt2p84Engine": h_at_284["h_eng"], "hmAt2p84Bk": h_at_284["h_ref"],
        "sigmaMAt2p84Engine": h_at_284["sigma_eng"], "sigmaMAt2p84Bk": h_at_284["sigma_ref"],
        "xiOptEngine": xi_opt, "xiOptBk": xi_ref, "hMaxBk": h_ref_max,
        "weakFocusHOverXiEngine": weak["h_eng"] / weak["xi"], "weakFocusHOverXiBk": weak["h_ref"] / weak["xi"],
        "maxRelDiffEngineVsBk": worst,
        "hmVsXi": {"x": [r["xi"] for r in focus], "y": [r["h_eng"] for r in focus]},
        "table": [{k: v for k, v in r.items() if k != "samples"} for r in rows],
    }

    # h(sigma) at xi = 2.84 (full tier: a scan; fast tier: the three samples)
    xi0 = 2.84
    e10 = eta(xi0, 0.0, radial=False)
    sig = np.linspace(-1.0, 2.5, 15) if not p.fast else np.array([s for s, _ in h_at_284["samples"]])
    h_sig = [h_engine(s, xi0, e10) for s in sig] if not p.fast else [h for _, h in h_at_284["samples"]]
    numbers["hVsSigmaAt2p84"] = {"x": list(sig), "y": list(h_sig)}
    p.write_numbers(numbers)

    # figures
    xs = np.geomspace(0.05, 20, 200)
    fig, ax = p.figure()
    ax.semilogx(xs, [bk_optimum(x)[1] for x in xs], color=REF, lw=1.5, label="Boyd-Kleinman integral, optimised $\\sigma$")
    ax.semilogx(xs, [h_bk(0.0, x) for x in xs], color=REF2, lw=1.2, ls="--", label="Boyd-Kleinman integral, $\\sigma = 0$")
    ax.semilogx([r["xi"] for r in rows], [r["h_eng"] for r in rows], "o", color=BLUE, ms=7, label="engine, radial crystal element")
    ax.axvline(2.84, color=REF2, lw=0.8, ls=":")
    ax.annotate("$h_m$ = %.3f at $\\xi$ = %.2f (engine)" % (h_at_284["h_eng"], xi_opt), (xi_opt, h_at_284["h_eng"]), xytext=(0.06, 1.12),
                arrowprops=dict(arrowstyle="-", color=REF2, lw=0.8), fontsize=9)
    ax.set_xlabel("focusing parameter $\\xi = L / b$")
    ax.set_ylabel("focusing function $h(\\sigma_m, \\xi)$")
    ax.set_ylim(0, 1.2)
    ax.legend(loc="lower right")
    p.save(fig, "h-vs-xi")

    fig, ax = p.figure()
    ss = np.linspace(-1.0, 2.5, 300)
    ax.plot(ss, [h_bk(s, xi0) for s in ss], color=REF, lw=1.5, label="Boyd-Kleinman integral")
    ax.plot(sig, h_sig, "o", color=BLUE, ms=7, label="engine, radial crystal element")
    ax.set_xlabel("phase-mismatch parameter $\\sigma = b\\,(2k_1 - k_2)/2$")
    ax.set_ylabel("$h(\\sigma, \\xi = 2.84)$")
    ax.legend(loc="lower center")
    p.save(fig, "h-vs-sigma")


if __name__ == "__main__":
    main()
