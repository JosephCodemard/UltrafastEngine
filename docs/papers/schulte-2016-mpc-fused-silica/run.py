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
from ultrafast import cells  # noqa: E402

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


def helper_mode(p_peak_w, air_len=D_MIRROR):
    """Kerr-matched launch beam from the engine's cellMode helper (ultrafast.cells.cell_mode).

    Mapping of our bounce [air][26 mm glass][mirror] on the helper's cell: the 26 mm plate sits against mirror 2 (1 um air gap, so the
    mirror stays in air as in the radial model), mirror 1 has none; one traversal then equals one bounce (air, glass, mirror), launch
    plane = start of the first air gap. Mirror separation = air + glass. 36 traversals. peak_power = peak power of the pulse.
    NOT equivalent to the radial model's period-1 bounce in the nonlinear case: the helper cell has glass at mirror 2 only, so its radii on the two
    mirrors differ (540 / 465 um at 375 W) while every bounce of the radial model is the same. In the linear case (peak_power = 0) the mapping is exact:
    the radial model launched with this mode keeps 476.8 um in the glass on every bounce (numbers: helperLinearSpreadUm)."""
    d = air_len + 2 * L_GLASS + 1e-6
    r = cells.cell_mode(LAM, mirror_radius=R_EFF, separation=d, peak_power=p_peak_w, passes=NBOUNCE,
                        plates=[{"thicknessM": 2 * L_GLASS, "positionM": d - L_GLASS - 1e-6, "material": "FusedSilica"}])
    return r


def mean_peak_power(r):
    """Mean over the 36 bounces of the peak power entering each air gap (the engine reports it per element)."""
    pp = [e["pulse"]["peakPowerW"] for e in r.summary["elements"] if e["id"].startswith("air")]
    return float(np.mean(pp)), float(pp[0])


def glass_radii(r):
    return np.array([e["beam"]["radiusXM"] for e in r.summary["elements"] if e["id"].startswith("gl")])


def system(nb=NBOUNCE, gdd=GDD_MIRROR, air_nonlinear=True, air_len=D_MIRROR, points=96, radius=1.5e-3, start=None, spectral_samples=None):
    """Radial model of the cell. start = (w0, zWaist) of the Gaussian at the first air gap (default: the empty cell's mode)."""
    w0, zw = start or (W_WAIST, air_len / 2)
    co = {"hr": {"kind": "gdd", "R": T_TOTAL ** (1.0 / NBOUNCE), "gddS2": gdd}}
    tr = {"model": "radial", "points": points, "radiusM": radius}
    if spectral_samples:
        tr["spectralSamples"] = spectral_samples
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


SNAP_CARRIER = False      # carrier exactly at the requested 1030 nm (the engine default snaps it onto the frequency lattice: 1.4 % in the bandwidth, README)


def spec(e_j, snap=None):
    return {"format": "ultrafast-run", "version": 2, "name": "schulte", "grid": {"snapCarrier": SNAP_CARRIER if snap is None else snap},
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


PAD = 64        # zero-padding of the spectrum (finer time sampling for the widths; the engine grid is coarse in time)


def padded(a, om):
    """Spectral field and angular-frequency axis extended with zeros by PAD x its length on each side (same step)."""
    n = len(a)
    d = om[1] - om[0]
    ap = np.concatenate([np.zeros(PAD * n // 2, complex), a, np.zeros(PAD * n // 2, complex)])
    omp = om[0] + d * (np.arange(len(ap)) - PAD * n // 2)
    return ap, omp


def to_time(a):
    return np.fft.fftshift(np.fft.fft(np.fft.ifftshift(a)))


def field(r, which="output"):
    return np.asarray(r[which + "_ax_re"]) + 1j * np.asarray(r[which + "_ax_im"]), np.asarray(r[which + "_omega_rad_s"])


def tgrid(om):
    return 2 * np.pi / (len(om) * (om[1] - om[0])) * (np.arange(len(om)) - len(om) // 2)


def transform_limited(r):
    a, om = field(r)
    ap, omp = padded(np.abs(a), om)
    t = tgrid(omp)
    p = np.abs(to_time(ap)) ** 2
    return width_at_half(t, p), t, p


def compress(r, gdds):
    """Best GDD (maximum peak power): apply exp(+i GDD dw^2 / 2) to the spectrum (e^{-iwt} convention; a negative optimum for a
    positively chirped SPM pulse confirms it)."""
    a, om = field(r)
    dw0 = om - np.sum(om * np.abs(a) ** 2) / np.sum(np.abs(a) ** 2)
    ap, omp = padded(a, om)
    dw = omp - np.sum(om * np.abs(a) ** 2) / np.sum(np.abs(a) ** 2)
    t = tgrid(omp)
    best = (0, 0.0, None)
    for g in gdds:
        e = to_time(ap * np.exp(0.5j * g * dw ** 2))
        pk = np.max(np.abs(e) ** 2)
        if pk > best[0]:
            best = (pk, g, e)
    return best[1], best[2], t


def autocorr_fwhm(t, p, half_window=3e-12):
    """FWHM of the intensity autocorrelation (FFT-based, the pulse cropped to +-half_window around its peak)."""
    i = int(np.argmax(p))
    m = np.abs(t - t[i]) <= half_window
    q = p[m]
    n = len(q)
    f = np.fft.rfft(q, 2 * n)
    ac = np.fft.fftshift(np.fft.irfft(f * np.conj(f), 2 * n))
    tau = (np.arange(2 * n) - n) * (t[1] - t[0])
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
    res, trial, rad_curves = {}, {}, {}
    for pw in powers:
        e = energy_in(pw)
        k = "%d" % pw
        # (1) the earlier launch beam: Gaussian mode of the empty AIR-ONLY 540 mm cell (a trial value, kept for comparison)
        r0 = uf.run(system(), spec(e))
        trial[pw] = analyse(r0, pw)
        p_mean, p_first = mean_peak_power(r0)
        if pw == 375.0:
            p_mean_375 = p_mean
        # (2) launch beam from the Kerr-matched cell mode at the mean peak power of the pulse along the cell (rule, not tuned)
        hm = helper_mode(p_mean)
        m = hm["mode"]
        r = uf.run(system(start=(m["waistRadiusM"], m["waistDistanceFromLaunchM"])), spec(e))
        res[pw] = analyse(r, pw, with_compression=(pw == 375.0))
        rad_curves[pw] = (glass_radii(r0), glass_radii(r))
        nums["inputEnergyUj_%sW" % k] = e * 1e6
        nums["bwOuterNm_%sW" % k] = res[pw]["bwOuterNm"]
        nums["bwFwhmNm_%sW" % k] = res[pw]["bwFwhmNm"]
        nums["tlFs_%sW" % k] = res[pw]["tlFs"]
        nums["bTotalRad_%sW" % k] = res[pw]["bTotalRad"]
        nums["bTotalOverPi_%sW" % k] = res[pw]["bTotalRad"] / math.pi
        nums["transmission_%sW" % k] = res[pw]["transmission"]
        nums["bwRatio_%sW" % k] = res[pw]["bwOuterNm"] / 1.6
        nums["trialStart_bwOuterNm_%sW" % k] = trial[pw]["bwOuterNm"]
        nums["trialStart_bTotalRad_%sW" % k] = trial[pw]["bTotalRad"]
        nums["trialStart_tlFs_%sW" % k] = trial[pw]["tlFs"]
        nums["helper_peakPowerUsedMW_%sW" % k] = p_mean / 1e6
        nums["helper_peakPowerFirstBounceMW_%sW" % k] = p_first / 1e6
        nums["helper_waistUm_%sW" % k] = m["waistRadiusM"] * 1e6
        nums["helper_waistDistanceMm_%sW" % k] = m["waistDistanceFromLaunchM"] * 1e3
        nums["helper_radiusMirror1Um_%sW" % k] = m["radiusAtMirror1M"] * 1e6
        nums["helper_radiusMirror2Um_%sW" % k] = m["radiusAtMirror2M"] * 1e6
        nums["helper_bTotalRad_%sW" % k] = hm["bIntegralTotalRad"]
        nums["helper_overlapWithEmptyMode_%sW" % k] = hm["overlapEmptyModeWithMatchedMode"]
        nums["helper_emptyCellMirrorRadiusUm"] = hm["emptyCell"]["radiusAtMirror1M"] * 1e6
        nums["helper_emptyCellWaistUm"] = hm["emptyCell"]["waistRadiusM"] * 1e6
        nums["glassRadiusMinUm_trial_%sW" % k], nums["glassRadiusMaxUm_trial_%sW" % k] = rad_curves[pw][0].min() * 1e6, rad_curves[pw][0].max() * 1e6
        nums["glassRadiusMinUm_%sW" % k], nums["glassRadiusMaxUm_%sW" % k] = rad_curves[pw][1].min() * 1e6, rad_curves[pw][1].max() * 1e6
        if pw == 375.0:
            elements = r.summary["elements"]
            nums["powerOverCriticalFirstGlass"] = next(e_["values"]["peakPowerOverCritical"] for e_ in elements if e_["id"] == "gl00")
            rad = glass_radii(r)
            nums["glassBeamRadiusMinUm"], nums["glassBeamRadiusMaxUm"] = rad.min() * 1e6, rad.max() * 1e6
            nums["bAirRad_375W"] = sum(e_["bIntegralRad"] for e_ in elements if e_["id"].startswith("air"))
            nums["bGlassRad_375W"] = sum(e_["bIntegralRad"] for e_ in elements if e_["id"].startswith("gl"))
    c = res[375.0]
    for k in ("gddOptFs2", "compressedFwhmFs", "acFwhmFs", "mainPeakFraction"):
        nums[k + "_375W"] = c[k]
    nums["limit170"] = 170.0
    nums["gddOptTotalRangeLoFs2"], nums["gddOptTotalRangeHiFs2"] = -54000.0, -21000.0          # 3 x (-18000 ... -7000), see paper.json
    nums["compressionFactor_375W"] = 850.0 / c["compressedFwhmFs"]

    # 1-D on-axis comparator (fixed mode, on-axis intensity)
    r1 = uf.run(system_1d(), spec(energy_in(375.0)))
    a1 = analyse(r1, "1d")
    nums["oneD_bwOuterNm_375W"], nums["oneD_bTotalRad_375W"] = a1["bwOuterNm"], a1["bTotalRad"]

    # linear check of the launch mapping: 1 nJ, the helper's empty-cell mode must keep its radius at every bounce
    hm0 = helper_mode(0.0)["mode"]
    rl = uf.run(system(nb=6, start=(hm0["waistRadiusM"], hm0["waistDistanceFromLaunchM"])), spec(1e-9))
    rad0 = glass_radii(rl) * 1e6
    nums["helperLinearRadiusUm"], nums["helperLinearSpreadUm"] = float(rad0.mean()), float(rad0.max() - rad0.min())
    sens = {}
    if not p.fast:
        # carrier snapping: the default grid moves the carrier onto the frequency lattice (README: this explains the shift against the earlier README numbers)
        for pwk in (354.0, 375.0):
            a_s = analyse(uf.run(system(), spec(energy_in(pwk), snap=True)), "snap")
            nums["snapDefault_bwOuterNm_%dW" % pwk], nums["snapDefault_tlFs_%dW" % pwk], nums["snapDefault_bTotalRad_%dW" % pwk] = a_s["bwOuterNm"], a_s["tlFs"], a_s["bTotalRad"]
        # diagnostic: the helper cell with 13 mm of glass against EACH mirror (1 um gaps) is periodic per traversal like the physical cell
        dd = D_MIRROR + 2 * L_GLASS + 2e-6
        hs = cells.cell_mode(LAM, mirror_radius=R_EFF, separation=dd, peak_power=p_mean_375, passes=NBOUNCE,
                             plates=[{"thicknessM": L_GLASS, "positionM": 1e-6 + L_GLASS / 2, "material": "FusedSilica"},
                                     {"thicknessM": L_GLASS, "positionM": dd - 1e-6 - L_GLASS / 2, "material": "FusedSilica"}])["mode"]
        n_glass = 1.4503
        rs = uf.run(system(start=(hs["waistRadiusM"], hs["waistDistanceFromLaunchM"] - L_GLASS / n_glass)), spec(energy_in(375.0)))
        radc = glass_radii(rs) * 1e6
        nums["symPlates_radiusMinUm"], nums["symPlates_radiusMaxUm"], nums["symPlates_mirrorRadiusUm"] = float(radc.min()), float(radc.max()), hs["radiusAtMirror1M"] * 1e6
        nums["symPlates_bwOuterNm"] = analyse(rs, "sym")["bwOuterNm"]
        # power sweep (curve)
        pws = [100.0, 200.0, 300.0, 354.0, 375.0, 400.0, 446.0, 500.0]
        sweep = []
        for pw in pws:
            r = uf.run(system(), spec(energy_in(pw)))
            mm = helper_mode(mean_peak_power(r)[0])["mode"]
            r = uf.run(system(start=(mm["waistRadiusM"], mm["waistDistanceFromLaunchM"])), spec(energy_in(pw)))
            a = analyse(r, pw)
            sweep.append((pw, a["bwOuterNm"], a["bwFwhmNm"], a["tlFs"], a["bTotalRad"]))
        sweep = np.array(sweep)
        nums["sweep"] = {"x": sweep[:, 0], "y": sweep[:, 1]}
        nums["sweepFwhm"] = {"x": sweep[:, 0], "y": sweep[:, 2]}
        # sensitivities at 375 W
        e375 = energy_in(375.0)
        for name, kw in (("gddM300", dict(gdd=-300e-30)), ("gddM500", dict(gdd=-500e-30)), ("airLinear", dict(air_nonlinear=False)),
                         ("points128", dict(points=128, radius=2.0e-3)), ("spectral128", dict(spectral_samples=128)),
                         ("airGap514", dict(air_len=0.514, start=None))):
            r = uf.run(system(**kw), spec(e375))
            a = analyse(r, name)
            sens[name] = a
            nums["sens_%s_bwOuterNm" % name] = a["bwOuterNm"]
            nums["sens_%s_bTotalRad" % name] = a["bTotalRad"]
            rad = [e_["beam"]["radiusXM"] for e_ in r.summary["elements"] if e_["id"].startswith("gl")]
            nums["sens_%s_radiusMaxUm" % name] = max(rad) * 1e6
        # the 514 mm air gap (glass inside the 540 mm) again, now launched with the helper's Kerr-matched mode
        hm514 = helper_mode(p_mean_375, air_len=0.514)     # peak power: the mean of the 540 mm reference run (the 514 mm run itself blows up)
        m514 = hm514["mode"]
        rh = uf.run(system(air_len=0.514, start=(m514["waistRadiusM"], m514["waistDistanceFromLaunchM"])), spec(e375))
        ah = analyse(rh, "airGap514helper")
        nums["sens_airGap514helper_bwOuterNm"], nums["sens_airGap514helper_bTotalRad"] = ah["bwOuterNm"], ah["bTotalRad"]
        nums["sens_airGap514helper_radiusMaxUm"] = glass_radii(rh).max() * 1e6
        nums["helper514_radiusMirror1Um"], nums["helper514_waistUm"] = m514["radiusAtMirror1M"] * 1e6, m514["waistRadiusM"] * 1e6
        nums["radiusCurves"] = {"bounce": list(range(1, NBOUNCE + 1)), "trial375": list(rad_curves[375.0][0] * 1e6), "helper375": list(rad_curves[375.0][1] * 1e6)}

    p.write_numbers({k: v for k, v in nums.items()})
    plots(p, res, a1, nums, sens)


def plots(p, res, a1, nums, sens):
    import matplotlib.pyplot as plt
    fig, ax = p.figure(2, 1, 11.0, 4.2)
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
    if "radiusCurves" in nums:
        fig, ax = p.figure(1, 1, 6.0, 4.0)
        rc = nums["radiusCurves"]
        ax.plot(rc["bounce"], rc["trial375"], "s--", color=AQUA, ms=3, label="launch: empty-cell mode (trial value)")
        ax.plot(rc["bounce"], rc["helper375"], "o-", color=BLUE, ms=3, label="launch: cellMode helper, mean peak power")
        ax.axhline(nums["helper_radiusMirror1Um_375W"], color=ORANGE, ls=":", label="helper: radius at mirror 1")
        ax.axhline(nums["helper_radiusMirror2Um_375W"], color=ORANGE, ls="-.", label="helper: radius at mirror 2")
        ax.set_xlabel("bounce number")
        ax.set_ylabel("beam radius in the glass (um)")
        ax.set_title("375 W: beam radius from the radial model", fontsize=9)
        ax.legend(fontsize=7)
        p.save(fig, "beam-radius")
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
