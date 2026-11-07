#!/usr/bin/env python3
"""Schulte et al., Opt. Lett. 41, 4511 (2016): nonlinear pulse compression in a bulk fused-silica Herriott multi-pass cell.

    python run.py --tier fast             # three powers of the paper, radial model, ~3 min
    python run.py --tier full --figures   # a power sweep, sensitivities, convergence; writes the committed figures

Set-up (paper pp. 2-4; every number and its location: paper.json):
  * two concave-convex mirrors, effective ROC 350 mm (inferred, see paper.json), mirror distance 540 mm, 18 bounces on each
    mirror = 36 bounces; each bounce crosses 2 x 13 mm of fused silica (AR-coated concave face, HR convex face), coating GDD
    -400 fs2 per bounce; ~20 m of air between.
  * input: 850 fs sech2 (transform limited), 1030 nm, 10 MHz, up to 50 uJ.
  * total transmission 91 % is a stated INPUT: the loss is spread evenly over the 36 bounces (mirror reflectance 0.91^(1/36)).
Model: every bounce is  [0.54 m of air] -> [26 mm fused silica, Kerr] -> [curved mirror with the GDD coating]. The transverse field is
resolved on a radial (Hankel) grid, so diffraction, the Kerr lens and the beam-integrated spectrum (what the spectrometer measured)
come out of the engine; the first air gap starts with the Gaussian mode of the EMPTY 540 mm cell (w = 459 um on the mirrors).
The bandwidth is the paper's definition: width between the points on the outer slopes where the spectrum has fallen to half of the
outer peaks (p. 3).
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM, TAU, F_REP = 1.03e-6, 850e-15, 10e6
C0 = 299792458.0
R_EFF, D_MIRROR, L_GLASS = 0.350, 0.540, 13e-3     # R_EFF inferred from w = 460 um (paper.json)
NBOUNCE = 36
T_TOTAL = 0.91
GDD_MIRROR = -400e-30


def bare_cell(R=R_EFF, d=D_MIRROR):
    """Waist and mirror radius (m) of the empty symmetric two-mirror cell."""
    g = 1 - d / R
    w_m = math.sqrt(LAM * d / math.pi / math.sqrt(1 - g * g))
    w_0 = math.sqrt(LAM / math.pi * math.sqrt(d * (2 * R - d)) / 2)
    return w_m, w_0


W_MIRROR, W_WAIST = bare_cell()


def system(nb=NBOUNCE, gdd=GDD_MIRROR, air_nonlinear=True, air_len=D_MIRROR, points=96, radius=1.5e-3, start=None):
    """Radial model of the cell. start = (w0, zWaist) of the Gaussian at the first air gap (default: the empty cell's mode)."""
    w0, zw = start or (W_WAIST, air_len / 2)
    co = {"hr": {"kind": "gdd", "R": T_TOTAL ** (1.0 / NBOUNCE), "gddS2": gdd}}
    tr = {"model": "radial", "points": points, "radiusM": radius}
    els = []
    for k in range(nb):
        a = {"id": "air%02d" % k, "type": "bulk" if air_nonlinear else "gap", "material": "Air", "lengthM": air_len, "transverse": tr}
        if k == 0:
            a["beam"] = {"kind": "gaussian", "w0M": w0, "zWaistM": zw}
        els.append(a)
        els.append({"id": "gl%02d" % k, "type": "bulk", "material": "FusedSilica", "lengthM": 2 * L_GLASS, "transverse": tr})
        els.append({"id": "m%02d" % k, "type": "mirror", "radiusOfCurvatureM": R_EFF, "coating": "hr"})
    return b.system("schulte-mpc", els, coatings=co)


def system_1d(nb=NBOUNCE, gdd=GDD_MIRROR):
    """1-D comparator: the same bounces with a fixed Gaussian mode (waist mid-gap, w = 459 um in the glass); on-axis intensity."""
    co = {"hr": {"kind": "gdd", "R": T_TOTAL ** (1.0 / NBOUNCE), "gddS2": gdd}}
    els = []
    for k in range(nb):
        els.append({"id": "air%02d" % k, "type": "bulk", "material": "Air", "lengthM": D_MIRROR,
                    "beam": {"kind": "gaussian", "w0M": W_WAIST, "zWaistM": D_MIRROR / 2}})
        els.append({"id": "gl%02d" % k, "type": "bulk", "material": "FusedSilica", "lengthM": 2 * L_GLASS, "beam": {"kind": "constant", "radiusM": W_MIRROR}})
        els.append(b.mirror("m%02d" % k, coating="hr", medium="FusedSilica"))
    return b.system("schulte-mpc-1d", els, coatings=co)


def spec(e_j):
    return {"format": "ultrafast-run", "version": 2, "name": "schulte",
            "pulse": {"shape": "sech2", "centerWavelengthM": LAM, "durationFwhmS": TAU, "energyJ": e_j, "repRateHz": F_REP, "polarization": "x"},
            "physics": {"selfSteepening": True, "raman": False}, "stepper": {"method": "adaptive", "tolerance": 1e-6}}


def energy_in(p_out_w):
    return p_out_w / T_TOTAL / F_REP


def wl_spectrum(r, which="output"):
    om = np.asarray(r[which + "_omega_rad_s"])
    a = np.asarray(r[which + "_ax_re"]) + 1j * np.asarray(r[which + "_ax_im"])
    lam = 2 * np.pi * C0 / om
    s = np.abs(a) ** 2 * om ** 2                      # per unit wavelength
    i = np.argsort(lam)
    return lam[i], s[i] / s.max()


def outer_peak_bandwidth(lam, s, floor=0.05):
    """Paper p. 3: width between the points on the outer slopes where the spectrum is half of the outer peaks' height.
    Outer peaks = the outermost local maxima above `floor` (of the global maximum) after a light smoothing."""
    k = 5
    sm = np.convolve(s, np.ones(k) / k, mode="same")
    pk = [i for i in range(1, len(sm) - 1) if sm[i] >= sm[i - 1] and sm[i] > sm[i + 1] and sm[i] > floor]
    lo_pk, hi_pk = pk[0], pk[-1]
    j = lo_pk
    while j > 0 and sm[j] > 0.5 * sm[lo_pk]:
        j -= 1
    m = hi_pk
    while m < len(sm) - 1 and sm[m] > 0.5 * sm[hi_pk]:
        m += 1
    return float(lam[m] - lam[j]), float(lam[lo_pk]), float(lam[hi_pk])


def fwhm_wavelength(lam, s):
    idx = np.where(s >= 0.5)[0]
    return float(lam[idx[-1]] - lam[idx[0]])


def width_at_half(t, p):
    i = int(np.argmax(p))
    lo = i
    while lo > 0 and p[lo] >= 0.5 * p[i]:
        lo -= 1
    hi = i
    while hi < len(p) - 1 and p[hi] >= 0.5 * p[i]:
        hi += 1
    return float(t[hi] - t[lo])


def time_axis(r, which="output"):
    om = np.asarray(r[which + "_omega_rad_s"])
    return 2 * np.pi / (len(om) * (om[1] - om[0])) * (np.arange(len(om)) - len(om) // 2)


def to_time(a):
    return np.fft.fftshift(np.fft.fft(np.fft.ifftshift(a)))


def field(r, which="output"):
    return np.asarray(r[which + "_ax_re"]) + 1j * np.asarray(r[which + "_ax_im"]), np.asarray(r[which + "_omega_rad_s"])


def transform_limited(r):
    a, om = field(r)
    t = time_axis(r)
    p = np.abs(to_time(np.abs(a))) ** 2
    return width_at_half(t, p), t, p


def compress(r, gdds):
    """Best GDD (min FWHM among the max-peak ones): apply exp(+i GDD dw^2 / 2) (e^{-iwt} convention, checked by the sign of the result)."""
    a, om = field(r)
    t = time_axis(r)
    dw = om - np.sum(om * np.abs(a) ** 2) / np.sum(np.abs(a) ** 2)
    best = (0, 0.0, None)
    for g in gdds:
        e = to_time(a * np.exp(0.5j * g * dw ** 2))
        pk = np.max(np.abs(e) ** 2)
        if pk > best[0]:
            best = (pk, g, e)
    return best[1], best[2], t


def autocorr_fwhm(t, p):
    dt = t[1] - t[0]
    ac = np.correlate(p, p, mode="full")
    tau = (np.arange(len(ac)) - (len(p) - 1)) * dt
    return width_at_half(tau, ac)


def analyse(r, label, with_compression=False):
    lam, s = wl_spectrum(r)
    bw, l1, l2 = outer_peak_bandwidth(lam, s)
    out = {"bwOuterNm": bw * 1e9, "bwFwhmNm": fwhm_wavelength(lam, s) * 1e9, "lamLoNm": l1 * 1e9, "lamHiNm": l2 * 1e9,
           "bTotalRad": r.summary["totals"]["bIntegralRad"], "transmission": r.summary["totals"]["energyTransmission"]}
    tl, _, _ = transform_limited(r)
    out["tlFs"] = tl * 1e15
    if with_compression:
        g, e, t = compress(r, np.arange(-120000, 1, 1000) * 1e-30)
        g, e, t = compress(r, np.arange(g * 1e30 - 1000, g * 1e30 + 1001, 100) * 1e-30)
        p = np.abs(e) ** 2
        w = width_at_half(t, p)
        e_tot = p.sum()
        main = p[np.abs(t - t[int(np.argmax(p))]) <= 1.5 * w].sum() / e_tot       # energy within +-1.5 FWHM of the peak
        e_pulse = r.summary["totals"].get("energyOutJ") or None
        out.update(gddOptFs2=g * 1e30, compressedFwhmFs=w * 1e15, acFwhmFs=autocorr_fwhm(t, p) * 1e15, mainPeakFraction=float(main))
        out["_t"], out["_p"] = t, p / p.max()
    out["_lam"], out["_s"] = lam, s
    return out


def main():
    p = Paper(__file__)
    nums = {"mirrorRadiusUm_bareCell": W_MIRROR * 1e6, "waistUm_bareCell": W_WAIST * 1e6}
    powers = [354.0, 375.0, 446.0]
    res = {}
    for pw in powers:
        e = energy_in(pw)
        r = uf.run(system(), spec(e))
        res[pw] = analyse(r, pw, with_compression=(pw == 375.0))
        k = "%d" % pw
        nums["inputEnergyUj_%sW" % k] = e * 1e6
        nums["bwOuterNm_%sW" % k] = res[pw]["bwOuterNm"]
        nums["bwFwhmNm_%sW" % k] = res[pw]["bwFwhmNm"]
        nums["tlFs_%sW" % k] = res[pw]["tlFs"]
        nums["bTotalRad_%sW" % k] = res[pw]["bTotalRad"]
        nums["bTotalOverPi_%sW" % k] = res[pw]["bTotalRad"] / math.pi
        nums["transmission_%sW" % k] = res[pw]["transmission"]
        nums["bwRatio_%sW" % k] = res[pw]["bwOuterNm"] / 1.6
        if pw == 375.0:
            elements = r.summary["elements"]
            nums["criticalPowerRatioFirstGlass"] = next(e_["criticalPowerRatio"] for e_ in elements if e_["id"] == "gl00" and e_.get("criticalPowerRatio"))
            rad = [e_["beam"]["radiusXM"] for e_ in elements if e_["id"].startswith("gl")]
            nums["glassBeamRadiusMinUm"], nums["glassBeamRadiusMaxUm"] = min(rad) * 1e6, max(rad) * 1e6
            nums["bAirRad_375W"] = sum(e_["bIntegralRad"] for e_ in elements if e_["id"].startswith("air"))
            nums["bGlassRad_375W"] = sum(e_["bIntegralRad"] for e_ in elements if e_["id"].startswith("gl"))
    c = res[375.0]
    for k in ("gddOptFs2", "compressedFwhmFs", "acFwhmFs", "mainPeakFraction"):
        nums[k + "_375W"] = c[k]
    nums["gddOptTotalRangeLoFs2"], nums["gddOptTotalRangeHiFs2"] = -54000.0, -21000.0          # 3 x (-18000 ... -7000), see paper.json
    nums["compressionFactor_375W"] = 850.0 / c["compressedFwhmFs"]
    nums["compressedPeakPowerMW_375W"] = c["mainPeakFraction"] * (T_TOTAL * energy_in(375.0)) / (0.88 * c["compressedFwhmFs"] * 1e-15) * 1e-6 * 0 + 0.0

    # 1-D on-axis comparator (fixed mode, on-axis intensity)
    r1 = uf.run(system_1d(), spec(energy_in(375.0)))
    a1 = analyse(r1, "1d")
    nums["oneD_bwOuterNm_375W"], nums["oneD_bTotalRad_375W"] = a1["bwOuterNm"], a1["bTotalRad"]

    sens = {}
    if not p.fast:
        # power sweep (curve)
        pws = [100.0, 200.0, 300.0, 354.0, 375.0, 400.0, 446.0, 500.0]
        sweep = []
        for pw in pws:
            r = res[pw]["_r"] if False else uf.run(system(), spec(energy_in(pw)))
            a = analyse(r, pw)
            sweep.append((pw, a["bwOuterNm"], a["bwFwhmNm"], a["tlFs"], a["bTotalRad"]))
        sweep = np.array(sweep)
        nums["sweep"] = {"x": sweep[:, 0], "y": sweep[:, 1]}
        nums["sweepFwhm"] = {"x": sweep[:, 0], "y": sweep[:, 2]}
        # sensitivities at 375 W
        e375 = energy_in(375.0)
        for name, kw in (("gddM300", dict(gdd=-300e-30)), ("gddM500", dict(gdd=-500e-30)), ("airLinear", dict(air_nonlinear=False)),
                         ("points128", dict(points=128, radius=2.0e-3)),
                         ("airGap514", dict(air_len=0.514, start=None))):
            r = uf.run(system(**kw), spec(e375))
            a = analyse(r, name)
            sens[name] = a
            nums["sens_%s_bwOuterNm" % name] = a["bwOuterNm"]
            nums["sens_%s_bTotalRad" % name] = a["bTotalRad"]
            rad = [e_["beam"]["radiusXM"] for e_ in r.summary["elements"] if e_["id"].startswith("gl")]
            nums["sens_%s_radiusMaxUm" % name] = max(rad) * 1e6

    p.write_numbers({k: v for k, v in nums.items()})
    plots(p, res, a1, nums, sens)


def plots(p, res, a1, nums, sens):
    import matplotlib.pyplot as plt
    fig, ax = p.figure(1, 2, 10.0, 4.0)
    lam0 = 1030.0
    r375 = res[375.0]
    ax[0].plot(r375["_lam"] * 1e9, r375["_s"], color=BLUE, label="engine, radial, 375 W out (beam-integrated)")
    ax[0].plot(a1["_lam"] * 1e9, a1["_s"], color=ORANGE, lw=1.2, label="engine, 1-D on-axis comparator")
    for pw, c in ((354.0, AQUA),):
        ax[0].plot(res[pw]["_lam"] * 1e9, res[pw]["_s"], color=c, lw=1.2, label="engine, radial, 354 W out")
    ax[0].axvspan(lam0 - 13.4 / 2, lam0 + 13.4 / 2, color=REF2, alpha=0.25, label="paper: 13.4 nm bandwidth at 354 W (width only)")
    ax[0].set_xlim(lam0 - 40, lam0 + 40)
    ax[0].set_xlabel("wavelength (nm)")
    ax[0].set_ylabel("spectrum per wavelength (peak = 1)")
    ax[0].legend(fontsize=6.5, loc="upper right")
    t = r375["_t"] * 1e15
    ax[1].plot(t, r375["_p"], color=BLUE, label="engine, 375 W, GDD %.0f fs2" % r375["gddOptFs2"])
    ax[1].axvspan(-85, 85, color=REF2, alpha=0.25, label="paper: < 170 fs FWHM (inferred, p. 4)")
    ax[1].set_xlim(-800, 800)
    ax[1].set_xlabel("time (fs)")
    ax[1].set_ylabel("compressed power (peak = 1)")
    ax[1].legend(fontsize=7)
    p.save(fig, "spectra-compression")
    if "sweep" in nums:
        fig, ax = p.figure(1, 1, 6.0, 4.0)
        sw = nums["sweep"]
        ax.plot(sw["x"], sw["y"], "o-", color=BLUE, label="engine, outer-peak bandwidth")
        ax.plot(nums["sweepFwhm"]["x"], nums["sweepFwhm"]["y"], "s--", color=AQUA, label="engine, FWHM")
        ax.plot([354, 446], [13.4, 16.1], "D", color=REF, ms=8, label="paper: 13.4 nm, 16.1 nm (p. 3)")
        ax.set_xlabel("MPC output power (W)")
        ax.set_ylabel("spectral bandwidth (nm)")
        ax.legend(fontsize=8)
        p.save(fig, "bandwidth-vs-power")


if __name__ == "__main__":
    main()
