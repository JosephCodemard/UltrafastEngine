#!/usr/bin/env python3
"""Perot & Fabry (1899): multiple-beam fringes of an etalon, coincidences of two spectral lines, and resolving power.

    python run.py --tier fast            # a few thousand cw runs, about a minute (CI)
    python run.py --tier full --figures  # finer scans, committed figures

NOTE: the PDF supplied as "Michelson 1892" is this paper (A. Perot and C. Fabry, Astrophys. J. 9, 87 (1899); the header and
the author line say so). Nothing of Michelson's 1892 paper was available to read.

The etalon is two mirrors of power reflectance R joined by a loop (the setup of the engine's FabryPerot test, docs/examples/32):
beamSplitter m1 -> gap -> beamSplitter m2 (entered at its port b, so both mirrors reflect the inside wave alike) -> gap -> m1 port b,
with a single-frequency source. The path difference of the interfering beams is Delta = forth + back, the paper's 2 e cos i at
normal incidence. The mirrors are lossless (the paper's silver absorbs, p. 92: that is not modelled). Two spectral lines are
mutually incoherent, so each runs separately and the transmitted intensities add.

Checks: (1) the engine's transmission is the paper's I / I0 = 1 / (1 + (4 R / (1 - R)^2) sin^2(pi Delta / lambda)) with
the coefficient 80 for R = 0.8 (p. 91), (2) the fringe spacing of each line is lambda, so the coincidence period of two lines,
lambda lambda' / (lambda - lambda') (p. 97), comes out as the paper's 311.9, 14.56515 and 4.759901 Cd-green waves (pp. 102-103),
(3) two lines of relative separation eps/lambda are just separable (their fringes 1/5 of the spacing apart) at the order
p = lambda / (5 eps), e = 5 cm for eps / lambda = 1e-6 (p. 99).
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
# wavelengths in metres, as printed in the paper (pp. 102, 107)
CD_GREEN, CD_RED = 0.50858240e-6, 0.64384722e-6
HG_GREEN, HG_Y1, HG_Y2 = 0.54607424e-6, 0.57906593e-6, 0.57695984e-6
PAIRS = {  # name: (first line, second line, period printed in the paper in Cd-green waves, page)
    "hgYellow": (HG_Y1, HG_Y2, 311.9, 102),
    "cdGreenHgGreen": (CD_GREEN, HG_GREEN, 14.56515, 102),
    "cdGreenCdRed": (CD_GREEN, CD_RED, 4.759901, 103),
}


def etalon(r, forth):
    return b.system("etalon", b.Branched([
        b.link(b.beam_splitter("m1", r), {"c": "forth", "d": "virtual"}, loop=b.loop("ringDown", max_round_trips=2000, tolerance=1e-14)),
        b.link(b.gap("forth", forth, medium="Vacuum"), "m2:2"),
        b.link(b.beam_splitter("m2", r), {"c": "back", "d": "end"}),
        b.link(b.gap("back", forth, medium="Vacuum"), "m1:2"),
    ]))


class Etalon:
    """One line through one etalon: transmission for a list of 'back' gap lengths."""

    def __init__(self, lam, r, forth):
        self.r, self.forth = r, forth
        self.system = etalon(r, forth)
        self.run = b.run_spec(b.cw(lam, 1.0), grid=b.grid(window_s=1e-6))      # a 1 us window puts the carrier within 1e-9 of the request
        s = uf.run(self.system, self.run).summary
        self.omega0 = s["outputGrid"]["omega0"]
        self.lam = 2 * math.pi * C0 / self.omega0          # the carrier the grid actually uses
        self.e_in = s["graph"]["energyInJ"]

    def transmission(self, backs):
        s = uf.study(self.system, self.run, parameters=[{"path": "element:back/lengthM", "values": [float(v) for v in backs], "nominal": float(self.forth)}],
                     outputs=["energy:end"])
        return np.array([row["energy:end"] for row in s["rows"]]) / self.e_in

    def airy(self, delta):
        return (1 - self.r) ** 2 / (1 + self.r ** 2 - 2 * self.r * np.cos(self.omega0 * delta / C0))


def peaks(x, y, level=0.3):
    """Positions of the local maxima above level, refined by a parabola through the top three points."""
    i = np.where((y[1:-1] >= y[:-2]) & (y[1:-1] > y[2:]) & (y[1:-1] > level))[0] + 1
    d = y[i - 1] - 2 * y[i] + y[i + 1]
    return x[i] + 0.5 * (y[i - 1] - y[i + 1]) / d * (x[1] - x[0]), y[i]


def fwhm_fraction(x, y, lam):
    """FWHM of the highest peak of y(x) in units of lam (linear interpolation of the half-maximum crossings)."""
    k = int(np.argmax(y))
    half = y[k] / 2
    lo = k
    while y[lo] > half:
        lo -= 1
    hi = k
    while y[hi] > half:
        hi += 1
    xl = x[lo] + (half - y[lo]) * (x[lo + 1] - x[lo]) / (y[lo + 1] - y[lo])
    xh = x[hi - 1] + (half - y[hi - 1]) * (x[hi] - x[hi - 1]) / (y[hi] - y[hi - 1])
    return (xh - xl) / lam


def fringe_spacing(line, r, delta_a=2e-3, delta_b=4e-3, step=0.004):
    """Spacing of the transmission peaks of a line, from the position of one peak near delta_a and one near delta_b (N orders apart)."""
    forth = 5e-4
    et = Etalon(line, r, forth)
    pos = []
    for delta in (delta_a, delta_b):
        k = round(delta / line)
        centre = k * line - forth                         # peak expected at back = k lambda - forth
        offs = np.arange(-0.6 * line, 0.6 * line, step * line)
        t = et.transmission(centre + offs)
        px, py = peaks(centre + offs, t)
        pos.append((px[np.argmin(np.abs(px - centre))] + forth, k))
    (xa, ka), (xb, kb) = pos
    return (xb - xa) / (kb - ka), et


def main():
    p = Paper(__file__)
    out = {}

    # ---------------------------------------------------------------- 1. the Airy function (p. 91)
    lam = CD_GREEN
    curves = {}
    for r in (0.8, 0.9):
        et = Etalon(lam, r, 1e-3)
        x = np.linspace(-0.5, 0.5, 2001 if p.fast else 4001)          # Delta / lambda about an order
        k0 = round((2e-3) / et.lam)
        delta = (k0 + x) * et.lam
        t = et.transmission(delta - 1e-3)
        coeff = 4 * r / (1 - r) ** 2
        ref = 1 / (1 + coeff * np.sin(np.pi * x) ** 2)
        key = "R%02d" % round(r * 100)
        out["airyMaxError_" + key] = float(np.abs(t - ref).max())
        out["peakTransmission_" + key] = float(t.max())
        out["minTransmission_" + key] = float(t.min())
        out["fwhmOverSpacing_" + key] = fwhm_fraction(x, t, 1.0)
        out["fwhmOverSpacingAnalytic_" + key] = 2 * math.asin(1 / math.sqrt(coeff)) / math.pi
        out["coefficient_" + key] = coeff
        curves[key] = (x, t, ref)
    out["maxOverMin_R80"] = out["peakTransmission_R80"] / out["minTransmission_R80"]
    out["maxOverMinAnalytic_R80"] = 1 + out["coefficient_R80"]
    out["fwhmFractionLimit"] = 1 / 20
    # smallest R whose FWHM is 1/20 of the spacing: 4R/(1-R)^2 = 1 / sin^2(pi / 40) (derived from the formula, not a number of the paper)
    c20 = 1 / math.sin(math.pi / 40) ** 2
    lo, hi = 0.5, 0.99
    for _ in range(60):
        mid = (lo + hi) / 2
        if 4 * mid / (1 - mid) ** 2 < c20:
            lo = mid
        else:
            hi = mid
    out["R_for_1_over_20"] = lo

    # ---------------------------------------------------------------- 2. coincidence periods (pp. 97, 102, 103)
    r = 0.8
    lam_eff = {}
    for name, (l1, l2, period_paper, page) in PAIRS.items():
        s1, _ = fringe_spacing(l1, r)
        s2, _ = fringe_spacing(l2, r)
        period = s1 * s2 / abs(s1 - s2)
        lam_eff[name] = (s1, s2)
        out["period_%s_CdGreenWaves" % name] = period / CD_GREEN
        out["periodPaper_%s" % name] = period_paper
        out["period_%s_fromPrinted" % name] = l1 * l2 / abs(l1 - l2) / CD_GREEN
        out["spacingError_%s" % name] = max(abs(s1 / l1 - 1), abs(s2 / l2 - 1))      # fringe spacing against the requested wavelength
    # the Na D lines: "the period is long (about 1000)" (p. 98), in waves of the mean D wavelength
    out["period_NaD_waves"] = 589.592e-9 * 588.995e-9 / (589.592e-9 - 588.995e-9) / 589.3e-9

    # coincidences seen directly: the two lines of Cd green / Cd red, product of the two transmissions, from Delta = 0
    g, rd = CD_GREEN, CD_RED
    et1, et2 = Etalon(g, r, 1e-9), Etalon(rd, r, 1e-9)
    omega_period = et1.lam * et2.lam / abs(et1.lam - et2.lam)
    step = 0.004 if p.fast else 0.002
    delta = np.arange(0, 3.2 * omega_period, step * g)
    t1, t2 = et1.transmission(delta), et2.transmission(delta)
    out["twoLines_sumAtDelta0"] = float(t1[0] + t2[0])
    prod = t1 * t2
    win = int(round(g / (step * g)))
    env = np.convolve(prod, np.ones(win) / win, mode="same")
    cpos = []
    for m in (1, 2, 3):
        sel = np.abs(delta - m * omega_period) < omega_period / 3
        cpos.append(delta[sel][np.argmax(env[sel])])
    out["coincidenceDeviationOverPeriod"] = float(max(abs(cpos[m - 1] - m * omega_period) for m in (1, 2, 3)) / omega_period)
    # discordance (n + 1/2) period: the product is smallest there
    sel = np.abs(delta - 1.5 * omega_period) < omega_period / 3
    cd = delta[sel][np.argmin(env[sel])]
    out["discordanceDeviationOverPeriod"] = float(abs(cd - 1.5 * omega_period) / omega_period)

    # ---------------------------------------------------------------- 3. resolving power (p. 99): 1/5 of the spacing apart
    resolve = {}
    for tag, eps_rel, e in (("e5mm", 1e-5, 5e-3), ("e5cm", 1e-6, 5e-2)):
        la = 0.5e-6
        lb = la * (1 + eps_rel)
        ea, eb = Etalon(la, r, e), Etalon(lb, r, e)
        eps_eff = eb.lam - ea.lam
        delta0 = ea.lam ** 2 / (5 * eps_eff)               # the paper's order p = lambda / (5 eps), Delta = p lambda
        out["orderP_%s" % tag] = delta0 / ea.lam
        out["pPaper_%s" % tag] = 20000 if tag == "e5mm" else 200000
        out["eOverCm_%s" % tag] = delta0 / 2 * 100
        k0 = round(delta0 / ea.lam)
        centre = k0 * ea.lam
        back0 = centre - e
        offs = np.arange(-1.2 * ea.lam, 1.2 * ea.lam, 0.003 * ea.lam)
        ta = ea.transmission(back0 + offs)
        tb = eb.transmission(back0 + offs)
        pa, _ = peaks(offs, ta)
        pb, _ = peaks(offs, tb)
        i = np.argmin(np.abs(pa))
        d = pb - pa[i]
        d = d[np.argmin(np.abs(d))]
        out["peakOffsetOverSpacing_%s" % tag] = float(d / ea.lam)
        both = ta + tb
        # the two Airy functions in the same window, for the dip between the peaks
        ref = ea.airy(centre + offs) + eb.airy(centre + offs)
        out["sumVsAiryMaxError_%s" % tag] = float(np.abs(both - ref).max())
        seg = both[(offs >= min(pa[i], pa[i] + d) - 0.01 * ea.lam) & (offs <= max(pa[i], pa[i] + d) + 0.01 * ea.lam)]
        out["valleyOverPeak_%s" % tag] = float(seg.min() / seg.max())
        out["dipLeftVisible_%s" % tag] = float(1 - seg.min() / seg.max())
        resolve[tag] = (offs / ea.lam, ta, tb, both, ref)

    # ---------------------------------------------------------------- 4. resolving-power arithmetic of p. 99 (analytic)
    out["orderAt5cm_halfMicron"] = 2 * 0.05 / 0.5e-6
    # ---- derived from the paper's formula, in waves
    p.write_numbers(out)

    # ---------------------------------------------------------------- figures
    fig, (a1, a2) = p.figure(ncols=2, width_in=9.0, height_in=3.8)
    for key, color, lab in (("R80", BLUE, "R = 0.8"), ("R90", ORANGE, "R = 0.9")):
        x, t, ref = curves[key]
        a1.plot(x, ref, color=REF, lw=1.5, label="paper's formula" if key == "R80" else None)
        a1.plot(x[::40], t[::40], "o", color=color, ms=4.5, label="engine, " + lab)
        a2.semilogy(x, ref, color=REF, lw=1.5)
        a2.semilogy(x[::40], t[::40], "o", color=color, ms=4.5)
    a1.set(xlabel="(path difference - order x wavelength) / wavelength", ylabel="transmitted / incident", title="Airy fringe, linear")
    a1.legend(fontsize=8)
    a2.set(xlabel="(path difference - order x wavelength) / wavelength", title="log scale (max / min = 81 at R = 0.8)")
    p.save(fig, "airy-fringe")

    fig, (a1, a2) = p.figure(ncols=2, width_in=9.4, height_in=3.9)
    a1.plot(delta * 1e6, t1 + t2, color=REF2, lw=0.8, label="sum, Cd green + Cd red")
    a1.plot(delta * 1e6, t1, color=AQUA, lw=0.7, label="Cd green 508.6 nm")
    a1.plot(delta * 1e6, t2, color=ORANGE, lw=0.7, label="Cd red 643.8 nm")
    for m in range(0, 4):
        a1.axvline(m * omega_period * 1e6, color=REF, ls="--", lw=0.9, label="coincidences m x period (period 4.759901 waves, p. 103)" if m == 0 else None)
        if m < 3:
            a1.axvline((m + 0.5) * omega_period * 1e6, color=REF, ls=":", lw=0.9, label="discordances (m + 1/2) x period" if m == 0 else None)
    a1.set(xlabel="path difference 2e (um)", ylabel="transmitted / incident", title="two lines: fringe systems part and re-join")
    a1.legend(fontsize=7, loc="upper right", ncol=1)
    a1.set_ylim(0, 2.2)
    x, ta, tb, both, ref = resolve["e5mm"]
    a2.plot(x, ref, color=REF, lw=1.5, label="two Airy functions (formula)")
    a2.plot(x[::6], both[::6], "o", color=BLUE, ms=3.5, label="engine, two lines")
    a2.plot(x, ta, color=AQUA, lw=0.8)
    a2.plot(x, tb, color=ORANGE, lw=0.8)
    a2.set(xlabel="path difference - order x wavelength (wavelengths)", title="lines 1e-5 apart, e = 5 mm: just resolved")
    a2.legend(fontsize=8, loc="upper right")
    a2.set_xlim(-0.8, 0.8)
    p.save(fig, "two-lines-and-resolution")


if __name__ == "__main__":
    main()
