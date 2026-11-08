#!/usr/bin/env python3
"""Maker, Terhune, Nisenoff, Savage (1962): effects of dispersion and focusing on the production of optical harmonics.

    python run.py --tier fast            # ~3 min (CI)
    python run.py --tier full --figures  # finer scans, committed figures

(1) Quartz (p. 21, Fig. 1): the spacing of the intensity maxima against thickness = 2 pi / dk, dk = k_SH - 2 k_F, from the engine's
    SHG in a user quartz crystal (ordinary-wave indices of Ghosh 1999, the same as the engine's Quartz material), against the
    paper's 13.9 um (theory) and 14 um (experiment). A tilted plate is modelled by the path d / cos(theta_r) (the engine has no
    oblique incidence); the exact oblique formula dk_z = (4 pi / lambda)(sqrt(n_b^2 - s^2) - sqrt(n_r^2 - s^2)) is shown next to it.
(2) KDP (Fig. 2): phase-matching angle theta_0 = 52 +- 2 deg, phi_0 = 45 deg, deff ~ sin 2 phi, angular width.
    (2026-11: the engine's d_eff with an explicit cut now follows sin 2 phi; the old erratic behaviour was fixed, repro_1.py deleted.)
(3) Focusing (p. 22): conversion of a focused vs a collimated beam in 1.5 mm KDP (cartesian model, walk-off included).
    The beam sizes are ASSUMPTIONS (the paper gives none): the 'ratio of photon numbers' is compared with the 1/area law only.
"""
import math, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

LAM = 694.3e-9            # ruby: ASSUMPTION (p. 21 says 'ruby laser')
D_PLATE = 0.0308 * 25.4e-3  # m, Fig. 1 caption
QUARTZ = {"crystal": {"name": "quartz (Ghosh 1999)",
                      "no": {"kind": "sellmeier", "A": 1.28604141, "B": [1.07044083, 1.10202242], "C": [1.00585997e-2, 100.0]},
                      "ne": {"kind": "sellmeier", "A": 1.28851804, "B": [1.09509924, 1.15662475], "C": [1.02101864e-2, 100.0]},
                      "pointGroup": "general", "deffPmPerV": 0.3}}


def no_q(l):
    x = (l * 1e6) ** 2
    return math.sqrt(1.28604141 + 1.07044083 * x / (x - 1.00585997e-2) + 1.10202242 * x / (x - 100))


def ne_q(l):
    x = (l * 1e6) ** 2
    return math.sqrt(1.28851804 + 1.09509924 * x / (x - 1.02101864e-2) + 1.15662475 * x / (x - 100))


def kdp_no(l):
    """KDP ordinary index, Zernike 1964 form used by the engine (refractiveindex.info formula 4), re-implemented here as an independent reference."""
    x = (l * 1e6) ** 2
    return math.sqrt(2.259276 + 13.00522 * x / (x - 400) + 0.01008956 / (x - 0.0129426))


def kdp_ne(l):
    x = (l * 1e6) ** 2
    return math.sqrt(2.132668 + 3.2279924 * x / (x - 400) + 0.008637494 / (x - 0.0122810))


def kdp_walkoff_rad(theta_deg):
    """Walk-off of the extraordinary SH wave: tan(rho) = (n_e(theta)^2 / 2) (1/n_e^2 - 1/n_o^2) sin 2 theta, negative uniaxial crystal."""
    th = math.radians(theta_deg)
    no, ne = kdp_no(LAM / 2), kdp_ne(LAM / 2)
    nth = 1 / math.sqrt(math.cos(th) ** 2 / no ** 2 + math.sin(th) ** 2 / ne ** 2)
    return math.atan(0.5 * nth ** 2 * (1 / ne ** 2 - 1 / no ** 2) * math.sin(2 * th))


def vals(res):
    return res.summary["elements"][0]["values"]


def quartz(L):
    el = b.crystal("q", "quartz", float(L), process="shg", type="0", cut={"thetaDeg": 90, "phiDeg": 0},
                   overlap={"model": "planeWave", "radiusM": 1e-3}, chi3=False, deff_pm_per_v=0.3)
    # default stepper tolerance: since 2026-11 the step is capped by the phase mismatch (<= 0.5 rad per step), so a thick plate
    # (delta k L = 370 rad) is resolved; before, tolerance 1e-11 was needed (old repro_2.py, deleted)
    r = uf.run(b.system("q", [el], materials={"quartz": QUARTZ}), b.run_spec(b.pulse(LAM, 5e-12, energy=1e-6)))
    return vals(r)


def maxima(x, y):
    i = [k for k in range(1, len(y) - 1) if y[k] >= y[k - 1] and y[k] > y[k + 1]]
    out = []
    for k in i:
        a, c, d = y[k - 1], y[k], y[k + 1]
        den = a - 2 * c + d
        out.append(x[k] + (0.5 * (a - d) / den if den else 0) * (x[1] - x[0]))
    return np.array(out)


def kdp(L, theta, phi, E=1e-9):
    el = b.crystal("k", "KDP", L, process="shg", type="I", cut={"thetaDeg": theta, "phiDeg": phi}, overlap={"model": "planeWave", "radiusM": 1e-3}, chi3=False)
    r = uf.run(b.system("k", [el]), b.run_spec(b.pulse(LAM, 5e-12, energy=E)))
    return vals(r)


def kdp_detune(L, det, E=1e-9):
    el = b.crystal("k", "KDP", L, process="shg", type="I", cut={"detuneDeg": det}, overlap={"model": "planeWave", "radiusM": 1e-3}, chi3=False)
    return vals(uf.run(b.system("k", [el]), b.run_spec(b.pulse(LAM, 5e-12, energy=E))))["conversionEfficiency"]


def kdp_focus(w0, det, pts, hw, E=1e-10, L=1.5e-3):
    el = b.crystal("k", "KDP", L, process="shg", type="I", cut={"detuneDeg": det}, beam={"kind": "gaussian", "w0M": w0},
                   transverse={"model": "cartesian", "points": pts, "halfWidthM": hw}, chi3=False)
    r = uf.run(b.system("k", [el]), b.run_spec(b.pulse(LAM, 5e-12, energy=E)))
    return vals(r)["conversionEfficiency"], r.summary.get("warnings")


def main():
    p = Paper(__file__)
    out = {}
    # ---------------------------------------------------------------- (1) quartz thickness scan
    Ls = np.linspace(0, 60e-6, 121 if p.fast else 301)
    eta, dk = [], None
    for L in Ls[1:]:
        v = quartz(L)
        eta.append(v["conversionEfficiency"]); dk = v["deltaK0PerM"]
    eta = np.array(eta); Ln = Ls[1:]
    period_dk = 2 * math.pi / abs(dk)
    mx = maxima(Ln * 1e6, eta)
    period_scan = float(np.mean(np.diff(mx))) if len(mx) > 2 else float("nan")
    shape = eta * dk ** 2 / (4 * np.sin(dk * Ln / 2) ** 2 + 1e-30)
    ok = np.abs(np.sin(dk * Ln / 2)) > 0.3
    out["quartzPeriod_dk_um"] = period_dk * 1e6
    out["quartzPeriod_scan_um"] = period_scan
    out["quartzShapeSpread_vs_sin2"] = float(np.std(shape[ok]) / np.mean(shape[ok]))
    dn_o = no_q(LAM / 2) - no_q(LAM); dn_e = ne_q(LAM / 2) - ne_q(LAM)
    out["quartzPeriod_analytic_o_um"] = LAM / (2 * dn_o) * 1e6
    out["quartzPeriod_analytic_e_um"] = LAM / (2 * dn_e) * 1e6
    out["paperTheory_um"], out["paperExperiment_um"] = 13.9, 14.0
    out["quartzPeriod_engine_over_paperTheory"] = period_dk * 1e6 / 13.9
    fig, ax = p.figure(width_in=6.6, height_in=3.8)
    anal = np.sin(dk * Ln / 2) ** 2
    ax.plot(Ln * 1e6, eta * dk ** 2 / 4 / ((eta * dk ** 2 / 4).max()), color=BLUE, label="engine SHG in quartz (ooo), SH energy / L$^2$")
    ax.plot(Ln * 1e6, anal, "--", color=REF, label="sin$^2$($\\Delta k L/2$)")
    for k in range(4):
        ax.axvline(mx[0] + 13.9 * k, color=ORANGE, lw=0.9, ls=":", label="maxima 13.9 um apart (paper theory)" if k == 0 else None)
    ax.axvline(0, color=REF2, lw=0.5)
    ax.set(xlabel="crystal thickness L (um)", ylabel="SH energy (norm.)")
    ax.legend(loc="lower right", fontsize=8)
    p.save(fig, "quartz-fringes")

    # tilted plate
    th = np.linspace(0, 40, 201 if p.fast else 401)
    nr, nb = no_q(LAM), no_q(LAM / 2)
    s = np.sin(np.radians(th))
    Lpath = D_PLATE / np.sqrt(1 - (s / nr) ** 2)
    eng_t = np.array([quartz(L)["conversionEfficiency"] for L in Lpath])
    dkz = 4 * math.pi / LAM * (np.sqrt(nb ** 2 - s ** 2) - np.sqrt(nr ** 2 - s ** 2))
    exact = np.sin(dkz * D_PLATE / 2) ** 2     # oblique exact: phase dk_z * d (thickness along the normal)
    nm_eng = len(maxima(th, eng_t)); nm_exact = len(maxima(th, exact))
    out["tiltMaxima_0to40_engine_pathModel"] = nm_eng
    out["tiltMaxima_0to40_exactOblique"] = nm_exact
    out["tiltPhase_exact_over_pathModel"] = float((dkz[-1] * D_PLATE - dkz[0] * D_PLATE) / (abs(dk) * (Lpath[-1] - Lpath[0])))
    fig, ax = p.figure(width_in=6.6, height_in=3.8)
    ax.plot(th, eng_t / eng_t.max(), color=BLUE, lw=1.4, label="engine, path d / cos$\\theta_r$ (fixed $\\Delta k$)")
    ax.plot(th, exact, color=REF, lw=1.0, ls="--", label="exact oblique $\\Delta k_z$ (analytic)")
    ax.set(xlabel="plate rotation from normal incidence (deg)", ylabel="blue intensity (norm.)", title="0.0308 in quartz plate, 694.3 nm (assumed)")
    ax.legend(fontsize=8, loc="upper right")
    p.save(fig, "quartz-rotation")

    # ---------------------------------------------------------------- (2) KDP
    r = uf.crystal.phase_match("KDP", "shg", fundamental=LAM, type="I")["best"]
    th0, ph0 = r["thetaDeg"], r["phiDeg"]
    out["kdpTheta0_deg"], out["kdpPhi0_deg"] = th0, ph0
    out["paperTheta0_deg"], out["paperPhi0_deg"] = 52.0, 45.0
    phis = np.linspace(0, 90, 19)
    ef = np.array([kdp(1.5e-3, th0, float(f))["conversionEfficiency"] for f in phis])
    ref = np.sin(2 * np.radians(phis)) ** 2
    refn = ef[np.argmin(abs(phis - 30))] / ref[np.argmin(abs(phis - 30))]      # normalise at 30 deg
    out["kdpPhiLaw_fractionOffBy1pct"] = float(np.mean(np.abs(ef / refn - ref) > 0.01 * ref.clip(1e-3)))
    # d_eff itself against the textbook d36 sin(theta) sin(2 phi) (type I, -42m, o o -> e); the engine projects the polarisation onto the
    # E vector of the extraordinary wave, which differs from the textbook angle theta by the walk-off angle (about 1.4 deg here)
    dv = np.array([abs(kdp(1.5e-3, th0, float(f))["deffPmPerV"]) for f in phis[1:-1]])
    d36 = 0.39                                                     # pm/V: the engine library value (Eckardt et al. 1990)
    form = d36 * math.sin(math.radians(th0)) * np.sin(2 * np.radians(phis[1:-1]))
    rr = dv / form
    out["kdpDeffOverTextbook_mean"] = float(rr.mean())
    out["kdpDeffOverTextbook_spreadAcrossPhi"] = float((rr.max() - rr.min()) / rr.mean())
    out["kdpDeffOverTextbook_walkoffProjection"] = float(rr.mean() - 1)
    rho = abs(kdp_walkoff_rad(th0))
    out["kdpWalkoffMrad_independent"] = rho * 1e3
    out["kdpWalkoffMrad_calculator"] = float(next(w["walkOffMrad"] for w in r["waves"] if w["polarization"] == "e"))
    out["kdpTheta0_check_nOfTheta_vs_no"] = kdp_no(LAM) - 1 / math.sqrt(math.cos(math.radians(th0)) ** 2 / kdp_no(LAM / 2) ** 2 + math.sin(math.radians(th0)) ** 2 / kdp_ne(LAM / 2) ** 2)
    # if the engine projects onto the E vector (angle theta + rho to the optic axis): d_eff / textbook = sin(theta + rho)/sin(theta)
    pred = math.sin(math.radians(th0) + rho) / math.sin(math.radians(th0))
    out["kdpDeffOverTextbook_predictedByWalkoff"] = pred
    out["kdpDeffOverTextbook_minus_predicted"] = float(rr.mean() - pred)
    out["kdpPhi45_explicitCut_over_auto"] = kdp(1.5e-3, th0, 45.0)["conversionEfficiency"] / kdp_detune(1.5e-3, 0.0)
    # angular width: engine scan, FWHM in internal theta
    dets = np.linspace(-0.5, 0.5, 41 if p.fast else 81)
    ed = np.array([kdp_detune(1.5e-3, float(d_)) for d_ in dets])
    ed /= ed.max()
    above = dets[ed >= 0.5]
    out["kdpAngularFwhm_1p5mm_deg"] = float(above.max() - above.min())
    out["kdpAcceptance_calculator_1p5mm_deg"] = math.degrees(r["acceptance"]["angularFwhmRad"]) / 1.5
    fig, (a1, a2) = p.figure(ncols=2, width_in=9, height_in=3.6)
    a1.plot(phis, ef / refn, "o", color=BLUE, label="engine (explicit cut)"); a1.plot(phis, ref, color=REF, label="sin$^2 2\\phi$")
    a1.axvline(45, color=REF2, ls=":"); a1.set(xlabel="azimuth $\\phi$ (deg)", ylabel="SH (norm.)", title="KDP, $\\theta$ fixed at $\\theta_0$"); a1.legend(fontsize=8)
    a2.plot(th0 + dets, ed, color=BLUE, label="engine, 1.5 mm, plane wave")
    a2.axvspan(50, 54, color=REF2, alpha=0.25, label="paper: $\\theta_0$ = 52 +- 2 deg")
    a2.set(xlabel="$\\theta$ to the optic axis (deg)", ylabel="SH (norm.)", title="angle tuning"); a2.legend(fontsize=8)
    p.save(fig, "kdp-angles")

    # ---------------------------------------------------------------- (3) focusing
    n = 9 if p.fast else 21
    dscan = np.linspace(-0.5, 0.5, n)
    cases = {"collimated w0 = 300 um": (300e-6, 64, 1.0e-3), "focused w0 = 20 um": (20e-6, 64, 150e-6)}
    res = {}
    for name, (w0, pts, hw) in cases.items():
        res[name] = np.array([kdp_focus(w0, float(d_), pts, hw)[0] for d_ in dscan])
    e0 = {k: float(v[np.argmin(abs(dscan))]) for k, v in res.items()}
    ratio = e0["focused w0 = 20 um"] / e0["collimated w0 = 300 um"]
    out["focusGain_efficiency_ratio_at_theta0"] = ratio
    out["focusGain_area_ratio"] = (300 / 20) ** 2
    out["focusGain_ratio_over_area"] = ratio / (300 / 20) ** 2
    for k, v in res.items():
        pk = dscan[int(np.argmax(v))]
        out["peakDetune_deg_" + k.split()[0]] = float(pk)
    c = res["collimated w0 = 300 um"]; f = res["focused w0 = 20 um"]
    def fwhm(v):
        a = dscan[v >= v.max() / 2]
        return float(a.max() - a.min())
    out["fwhm_collimated_deg"], out["fwhm_focused_deg"] = fwhm(c), fwhm(f)
    def skew(v, d=0.2):      # (f(+d) - f(-d)) / (f(+d) + f(-d)), interpolated
        fp, fm = np.interp(d, dscan, v), np.interp(-d, dscan, v)
        return float((fp - fm) / (fp + fm))
    out["asymmetry_focused"], out["asymmetry_collimated"] = skew(f), skew(c)
    fig, ax = p.figure(width_in=6.6, height_in=3.8)
    ax.plot(dscan, c / c.max(), "o-", color=BLUE, label="collimated, w0 = 300 um")
    ax.plot(dscan, f / f.max(), "s-", color=ORANGE, label="focused, w0 = 20 um")
    ax.set(xlabel="detuning of $\\theta$ from the plane-wave optimum (deg)", ylabel="conversion (each norm. to its peak)", title="KDP 1.5 mm, cartesian model")
    ax.legend(fontsize=8)
    p.save(fig, "kdp-focus")
    out["dscan_deg"] = dscan; out["collimated"] = c; out["focused"] = f
    p.write_numbers(out)


if __name__ == "__main__":
    main()
