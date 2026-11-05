#!/usr/bin/env python3
"""Escoto et al. (2022): temporal quality of post-compressed pulses at large compression factors.

    python run.py --tier fast            # B = 0.5 ... 40 rad and the chirped-input case, ~1 min (CI)
    python run.py --tier full --figures  # B up to 200 rad (compression factor ~100), committed figures

The paper's model (arXiv:2201.05836v1, Eq. 1): pure self-phase modulation of a Gaussian, no dispersion during the
broadening, then a linear compressor -- either the GDD that maximises the peak power, or the transform limit (FTL).
Here the engine does the broadening: a 1 mm Kerr plate with a constant index (no dispersion), no Raman, no
self-steepening, a constant beam area, the energy chosen for the B-integral. The compression and the paper's metrics
are computed from the engine's output field with the paper's definitions (Sec. 2.2):

* compression factor C = FWHM_in / FWHM_out (intensity FWHM of the main peak);
* energy ratio (Eq. 3) = energy within t0 +- FWHM_out / total energy;
* intensity ratio = highest secondary peak / main peak (a secondary peak is a local maximum outside the main one);
* peak power boost = P_out / P_in;
* GDD-only: the GDD that maximises the peak power (golden section after a coarse scan).

The same metrics of Eq. 1 evaluated directly with numpy are drawn on the same axes, with C = 0.59 B + 1 (Eq. 2). The
engine's ideal compressor instrument (optimizeTod: false) is a cross-check of the GDD-only duration.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM = 1030e-9
TAU = 1e-12            # input intensity FWHM, s
N0, N2, LEN = 1.45, 2.5e-20, 1e-3
AREA = 1e-8            # constant beam area, m^2
GRID = b.grid(n=8192, window_s=12e-12, auto=False)
GRID_CHIRPED = b.grid(n=16384, window_s=30e-12, auto=False)     # inputs stretched to ~3 x
GRID_EQ4 = b.grid(n=8192, window_s=100e-12, auto=False)        # 14 x


# ------------------------------------------------------------------ metrics (the paper's definitions)
def fwhm(t, p):
    """FWHM of the main peak: the half-maximum crossings next to the maximum (linear interpolation)."""
    i = int(np.argmax(p))
    h = p[i] / 2
    lo = i
    while lo > 0 and p[lo] > h:
        lo -= 1
    hi = i
    while hi < len(p) - 1 and p[hi] > h:
        hi += 1
    tl = t[lo] + (h - p[lo]) * (t[lo + 1] - t[lo]) / (p[lo + 1] - p[lo])
    th = t[hi - 1] + (h - p[hi - 1]) * (t[hi] - t[hi - 1]) / (p[hi] - p[hi - 1])
    return th - tl


def energy_ratio(t, p):
    f = fwhm(t, p)
    t0 = t[int(np.argmax(p))]
    m = np.abs(t - t0) <= f
    return p[m].sum() / p.sum()


def intensity_ratio(p, floor=1e-5):
    """Highest local maximum other than the main peak, relative to it (0 if there is none above floor x peak)."""
    i0 = int(np.argmax(p))
    loc = np.where((p[1:-1] > p[:-2]) & (p[1:-1] >= p[2:]))[0] + 1
    loc = loc[(loc != i0) & (p[loc] > floor * p[i0])]
    return float(p[loc].max() / p[i0]) if len(loc) else 0.0


def centred(a):
    """Time-domain field with its peak moved to the middle of the window."""
    return np.roll(a, len(a) // 2 - int(np.argmax(np.abs(a))))


def ftl(a):
    return centred(np.fft.ifft(np.abs(np.fft.fft(a))))


def with_gdd(a, gdd, dw):
    return np.fft.ifft(np.fft.fft(a) * np.exp(0.5j * gdd * dw * dw))


def gdd_optimum(a, dw, scale):
    """The GDD (s^2) that maximises the peak power: coarse scan, then golden section."""
    peak = lambda g: float(np.max(np.abs(with_gdd(a, g, dw)) ** 2))
    # the optimum grows as ~ (tau_in / C)^2 x C / 2.4: +-80 TL^2 holds it up to C ~ 120 (steps of 0.1 TL^2)
    gs = np.linspace(-80 * scale, 80 * scale, 1601)
    v = [peak(g) for g in gs]
    k = int(np.argmax(v))
    lo, hi = gs[max(k - 1, 0)], gs[min(k + 1, len(gs) - 1)]
    r = (math.sqrt(5) - 1) / 2
    c, d = hi - r * (hi - lo), lo + r * (hi - lo)
    fc, fd = peak(c), peak(d)
    for _ in range(50):
        if fc > fd:
            hi, d, fd = d, c, fc
            c = hi - r * (hi - lo)
            fc = peak(c)
        else:
            lo, c, fc = c, d, fd
            d = lo + r * (hi - lo)
            fd = peak(d)
    return 0.5 * (lo + hi)


def metrics(t, a_in, a_out):
    """The paper's quality measures of one broadened field, for FTL and GDD-only compression."""
    dt = t[1] - t[0]
    dw = 2 * np.pi * np.fft.fftfreq(len(t), dt)
    p_in = np.abs(a_in) ** 2
    f_in = fwhm(t, p_in)
    out = {}
    a_ftl = ftl(a_out)
    tl = fwhm(t, np.abs(a_ftl) ** 2)
    a_gdd = centred(with_gdd(a_out, gdd_optimum(a_out, dw, tl * tl), dw))
    for name, a in (("ftl", a_ftl), ("gdd", a_gdd)):
        p = np.abs(a) ** 2
        out[name] = {"C": f_in / fwhm(t, p), "energyRatio": energy_ratio(t, p), "intensityRatio": intensity_ratio(p),
                     "peakPowerBoost": p.max() / p_in.max()}
    return out


# ------------------------------------------------------------------ the engine
def energy_for_b(bint):
    p0 = bint * AREA / (2 * math.pi / LAM * N2 * LEN)
    return p0 * TAU / (2 * math.sqrt(math.log(2) / math.pi))        # Gaussian: P0 = E / (tau sqrt(pi / (4 ln 2)))


def broaden(energy_j, gdd_s2=0.0, compressor=False, grid=GRID):
    mats = {"kerr": b.material(index={"kind": "constant", "value": N0}, n2=N2)}
    sysd = b.system("escoto-2022", [b.bulk("plate", "kerr", LEN, beam=b.constant_beam(area_m2=AREA))], materials=mats)
    inst = [b.instrument("compressor", optimizeTod=False)] if compressor else None
    spec = b.run_spec(b.pulse(LAM, TAU, energy_j, gdd_s2=gdd_s2), grid=grid, instruments=inst,
                      physics=b.physics(raman=False, selfSteepening=False))
    r = uf.run(sysd, spec)
    # 1-D on purpose (the paper's model): the self-focusing warning of the large B values is expected
    warn = [w for w in r.summary.get("warnings", []) if "critical power" not in w]
    if warn:
        print("  warnings:", warn)
    return r


def field(r, which):
    """(t, A(t)) from the engine's spectral envelope (result arrays <which>_ax_re/_im, in grid order, with the absolute
    frequencies <which>_omega_rad_s): the bins are put in FFT order and transformed with E = Re{A e^{-i w0 t}}, i.e.
    A(t) = sum_k A_k exp(-i dw_k t). The metrics use |A(t)|^2 only, so the time origin does not matter."""
    g = r.summary["grid"]
    spec = r[which + "_ax_re"] + 1j * r[which + "_ax_im"]
    dw = r[which + "_omega_rad_s"] - g["omega0"]
    n = len(spec)
    k = np.rint(dw / g["domega"]).astype(int) % n
    s = np.zeros(n, complex)
    s[k] = spec
    t = (np.arange(n) - n // 2) * g["dtS"]
    return t, centred(np.fft.fft(s))


def eq1(t, bint):
    """Eq. 1 of the paper with numpy: a Gaussian times exp(i B |E|^2) (the engine's sign: SPM is +i gamma |A|^2 A)."""
    e = np.exp(-2 * math.log(2) * (t / TAU) ** 2)          # |E|^2 = exp(-4 ln 2 t^2 / tau^2): intensity FWHM tau
    return e * np.exp(1j * bint * e * e)


def interp_at(cs, ys, c0):
    order = np.argsort(cs)
    return float(np.interp(c0, np.asarray(cs)[order], np.asarray(ys)[order]))


def main():
    p = Paper(__file__)
    bs = [0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 7.5, 10, 15, 20, 25, 30, 35, 40]
    if not p.fast:
        bs += [50, 60, 80, 100, 130, 170, 200]
    eng, ref, b_rep, comp_c = [], [], [], {}
    for bint in bs:
        r = broaden(energy_for_b(bint), compressor=bint in (5, 20, 40))
        t, a_in = field(r, "input")
        _, a_out = field(r, "output")
        b_rep.append(r.summary["elements"][0]["bIntegralRad"])
        eng.append(metrics(t, a_in, a_out))
        ref.append(metrics(t, eq1(t, 0.0), eq1(t, bint)))
        if bint in (5, 20, 40):
            v = next(i for i in r.summary["instruments"] if i.get("kind") == "compressor")["values"]
            comp_c[bint] = TAU / v["compressedFwhmS"]
        e = eng[-1]
        print("B %5.1f (reported %6.2f)  C ftl %6.2f gdd %6.2f  Eq.2 %6.2f  ER ftl %.3f gdd %.3f  IR ftl %.3f gdd %.3f" % (
            bint, b_rep[-1], e["ftl"]["C"], e["gdd"]["C"], 0.59 * bint + 1, e["ftl"]["energyRatio"], e["gdd"]["energyRatio"],
            e["ftl"]["intensityRatio"], e["gdd"]["intensityRatio"]))

    def col(rows, case, key):
        return [x[case][key] for x in rows]

    bs_a = np.array(bs, float)
    eq2 = 0.59 * bs_a + 1
    upto40 = bs_a <= 40
    err = {c: np.array(col(eng, c, "C")) / eq2 - 1 for c in ("ftl", "gdd")}
    numbers = {
        "bIntegralMaxRelErr": float(np.max(np.abs(np.array(b_rep) / bs_a - 1))),
        "eq2MaxAbsRelErrFtlUpTo40": float(np.max(np.abs(err["ftl"][upto40]))),
        "eq2MaxAbsRelErrGddUpTo40": float(np.max(np.abs(err["gdd"][upto40]))),
        "engineVsEq1MaxRelDiffC": float(max(abs(e[c]["C"] / q[c]["C"] - 1) for e, q in zip(eng, ref) for c in ("ftl", "gdd"))),
        "compressorInstrumentVsNumpyMaxRelDiff": float(max(abs(comp_c[k] / eng[bs.index(k)]["gdd"]["C"] - 1) for k in comp_c)),
        "gaussianEnergyRatio": energy_ratio(*(lambda t: (t, np.abs(eq1(t, 0)) ** 2))(np.linspace(-6, 6, 20001) * TAU)),
        "energyRatioAtB20": {"ftl": eng[bs.index(20)]["ftl"]["energyRatio"], "gdd": eng[bs.index(20)]["gdd"]["energyRatio"]},
        "intensityRatioAtB40": {"ftl": eng[bs.index(40)]["ftl"]["intensityRatio"], "gdd": eng[bs.index(40)]["gdd"]["intensityRatio"]},
        "C": {c: {"x": bs, "y": col(eng, c, "C")} for c in ("ftl", "gdd")},
        "intensityRatioFtlVsC": {"x": col(eng, "ftl", "C"), "y": col(eng, "ftl", "intensityRatio")},
        "rows": [dict(B=bb, ftl=e["ftl"], gdd=e["gdd"], eq1=q) for bb, e, q in zip(bs, eng, ref)],
    }
    # where secondary peaks appear (Sec. 4.2: "roughly when C > 2"): the first C with an intensity ratio above 1e-3
    for c in ("ftl", "gdd"):
        numbers["sidePeakOnsetC" + c.capitalize()] = next((e[c]["C"] for e in eng if e[c]["intensityRatio"] > 1e-3), float("nan"))
    numbers["eq2MaxAbsRelErrGddFrom4To40"] = float(np.max(np.abs(err["gdd"][(bs_a >= 4) & upto40])))
    if not p.fast:
        big = bs_a <= 100
        numbers["eq2MaxAbsRelErrFtlUpTo100"] = float(np.max(np.abs(err["ftl"][big])))
        numbers["eq2MaxAbsRelErrGddUpTo100"] = float(np.max(np.abs(err["gdd"][big])))
        numbers["eq2RelErrFtlAt100"] = float(err["ftl"][bs.index(100)])
        numbers["eq2AbsRelErrGddAt100"] = float(abs(err["gdd"][bs.index(100)]))
        numbers["energyRatioFtlAtC100"] = interp_at(col(eng, "ftl", "C"), col(eng, "ftl", "energyRatio"), 100.0)
        numbers["intensityRatioFtlAtLargestC"] = eng[-1]["ftl"]["intensityRatio"]

    # Eq. 4: a chirped Gaussian is sqrt(1 + (4 ln 2 gamma / tau^2)^2) longer; "almost 14 times" at gamma / tau^2 = 5
    r5 = broaden(1e-15, gdd_s2=5 * TAU ** 2, grid=GRID_EQ4)
    numbers["stretchAtGddOverTau2Of5"] = r5.summary["input"]["fwhmS"] / TAU
    # chirped input (Sec. 3.3, Fig. 6): energy ratio of the FTL pulse at C = 10, input GDD = -tau^2, 0, +tau^2
    stretch1 = math.sqrt(1 + (4 * math.log(2)) ** 2)
    chirp = {}
    for sign, label in ((1, "plus"), (-1, "minus")):
        cs, ers = [], []
        for bnom in ([8, 12, 16, 20, 25, 30, 40, 50] if p.fast else [6, 8, 10, 12, 14, 16, 18, 20, 25, 30, 35, 40, 50, 60]):
            r = broaden(energy_for_b(bnom * stretch1), gdd_s2=sign * TAU ** 2, grid=GRID_CHIRPED)
            t, _ = field(r, "input")
            _, a_out = field(r, "output")
            pf = np.abs(ftl(a_out)) ** 2
            cs.append(TAU / fwhm(t, pf))          # C against the transform-limited input duration (as in the paper)
            ers.append(energy_ratio(t, pf))
        chirp[label] = {"x": cs, "y": ers}
        numbers["energyRatioFtlAtC10Chirp" + label.capitalize()] = interp_at(cs, ers, 10.0)
    numbers["energyRatioFtlAtC10Unchirped"] = interp_at(col(eng, "ftl", "C"), col(eng, "ftl", "energyRatio"), 10.0)
    numbers["chirped"] = chirp
    p.write_numbers(numbers)

    # ---------------------------------------------------------- figures
    bb = np.linspace(0, max(bs), 300)
    fig, ax = p.figure()
    ax.plot(bb, 0.59 * bb + 1, color=REF, lw=1.5, label="Eq. 2: $C = 0.59\\,B + 1$")
    ax.fill_between(bb, 0.95 * (0.59 * bb + 1), 1.05 * (0.59 * bb + 1), color=REF2, alpha=0.15, lw=0, label="Eq. 2 $\\pm$ 5 %")
    ax.plot(bs, col(ref, "ftl", "C"), color=REF2, lw=1, ls="--", label="Eq. 1 with numpy (FTL and GDD)")
    ax.plot(bs, col(ref, "gdd", "C"), color=REF2, lw=1, ls="--")
    ax.plot(bs, col(eng, "ftl", "C"), "o", color=BLUE, ms=6, label="engine, transform limit")
    ax.plot(bs, col(eng, "gdd", "C"), "s", color=ORANGE, ms=5, label="engine, GDD only")
    ax.set_xlabel("B-integral (rad)")
    ax.set_ylabel("compression factor $C$")
    ax.legend(loc="upper left")
    p.save(fig, "compression-factor")

    fig, ax = p.figure(height_in=5.2)
    ax.semilogx(col(eng, "ftl", "C"), col(eng, "ftl", "energyRatio"), "o-", color=BLUE, ms=5, lw=1.2, label="energy ratio, FTL")
    ax.semilogx(col(eng, "gdd", "C"), col(eng, "gdd", "energyRatio"), "s-", color=ORANGE, ms=4, lw=1.2, label="energy ratio, GDD only")
    ax.semilogx(col(eng, "ftl", "C"), col(eng, "ftl", "intensityRatio"), "o--", color=BLUE, ms=4, lw=1, mfc="white", label="intensity ratio, FTL")
    ax.semilogx(col(eng, "gdd", "C"), col(eng, "gdd", "intensityRatio"), "s--", color=ORANGE, ms=4, lw=1, mfc="white", label="intensity ratio, GDD only")
    ax.axhline(0.98, color=REF2, lw=0.8, ls=":")
    ax.text(4.0, 0.995, "Gaussian 98 % (Sec. 2.2)", color=REF2, fontsize=8)
    if not p.fast:
        ax.plot([100], [0.70], "D", color=REF, ms=7, label="paper: ~70 % at C = 100 (Sec. 3.1)")
        ax.axhline(0.10, color=REF, lw=0.8, ls=":")
        ax.text(1.05, 0.11, "paper: FTL intensity ratio -> 10 % (Sec. 3.1)", color=REF, fontsize=8)
    ax.axvline(2, color=REF2, lw=0.8, ls="-.")
    ax.text(2.07, 0.22, "side peaks from C ~ 2 (Sec. 4.2)", color=REF2, fontsize=8, rotation=90)
    ax.set_xlabel("compression factor $C$")
    ax.set_ylabel("ratio")
    ax.set_ylim(0, 1.05)
    ax.legend(loc="upper center", bbox_to_anchor=(0.5, -0.16), ncol=2, fontsize=8)
    p.save(fig, "temporal-quality")

    fig, ax = p.figure()
    ax.plot(col(eng, "ftl", "C"), col(eng, "ftl", "energyRatio"), "o-", color=BLUE, ms=5, lw=1.2, label="unchirped input")
    ax.plot(chirp["plus"]["x"], chirp["plus"]["y"], "s-", color=ORANGE, ms=5, lw=1.2, label="input GDD = $+\\tau^2$")
    ax.plot(chirp["minus"]["x"], chirp["minus"]["y"], "^-", color=AQUA, ms=5, lw=1.2, label="input GDD = $-\\tau^2$")
    ax.set_xlim(1, 25)
    ax.set_xlabel("compression factor $C$ (against the transform-limited input)")
    ax.set_ylabel("energy ratio of the FTL pulse")
    ax.legend(loc="lower left")
    p.save(fig, "chirped-input")

    # example pulses at B = 20 (cf. the paper's Fig. 2)
    r = broaden(energy_for_b(20.0))
    t, a_out = field(r, "output")
    dw = 2 * np.pi * np.fft.fftfreq(len(t), t[1] - t[0])
    a_f = ftl(a_out)
    tl = fwhm(t, np.abs(a_f) ** 2)
    a_g = centred(with_gdd(a_out, gdd_optimum(a_out, dw, tl * tl), dw))
    fig, ax = p.figure()
    tt = (t - t[len(t) // 2]) / TAU
    ax.plot(tt, np.abs(a_f) ** 2 / np.max(np.abs(a_f) ** 2), color=BLUE, lw=1.5, label="engine, transform limit")
    ax.plot(tt, np.abs(a_g) ** 2 / np.max(np.abs(a_f) ** 2), color=ORANGE, lw=1.5, label="engine, GDD only")
    ax.set_xlim(-0.6, 0.6)
    ax.set_xlabel("time / input FWHM")
    ax.set_ylabel("power / FTL peak")
    ax.set_title("B = 20 rad", fontsize=10)
    ax.legend(loc="upper right")
    p.save(fig, "pulses-b20")


if __name__ == "__main__":
    main()
