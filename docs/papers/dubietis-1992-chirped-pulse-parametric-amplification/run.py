#!/usr/bin/env python3
"""Dubietis, Jonusauskas, Piskarskas (1992): chirped and stretched pulse parametric amplification (OPCPA) in BBO.

    python run.py --tier fast            # ~1 min (CI)
    python run.py --tier full --figures  # finer scans, committed figures

Chain of the paper (pp. 437-439): 1.7 ps Nd:glass pulse (8 nJ, 1.055 um) -> 4.5 m fibre (SPM + GVD: ~155 cm^-1 sweep over ~5 ps)
-> OPA in 8 mm BBO (type I, pumped at 527.5 nm by 8 ps, crossed ~2 deg, pump waist 0.3 mm diameter) -> grating compressor
(600 lines/mm, near Littrow, single pass, 170 mm) -> 70 fs.
Stages run here:
  A  the fibre as the engine models it (FusedSilica dispersion, n2 = 2.6e-20, A_eff = 4.5 kW / 9 GW/cm^2 = 50 um^2, silica Raman).
  B  the seed *as the paper describes it* (sech^2 pulse whose spectrum is 155 cm^-1 wide, linear chirp of 30 cm^-1/ps), used for
     the OPA and compressor: the paper states these numbers, the fibre model is a separate check (A), see the README.
  C  OPA: pump energy scan (the energy at the crystal is not given), pump-duration scan (the paper's matching argument).
  D  grating compressor scan; analytic GDD from Treacy.
"""
import math, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

C0 = 299792458.0
LS, LP = 1.055e-6, 0.5275e-6           # signal, pump (SH of the Nd:glass fundamental)
SPEC_CM = 155.0                          # cm^-1, p. 438 / Fig. 2b
SWEEP_CM_PER_PS = 30.0                   # p. 438
T_SEED_TL = 0.315 / (SPEC_CM * 100 * C0)  # sech^2: tau * dnu = 0.315  -> the paper's "predicted 67 fs"
W_SIG, W_PUMP = 64e-6, 150e-6            # radii: fibre core 8 um x 16 = 128 um diameter; pump waist diameter 0.3 mm (p. 438-439)
L_BBO = 8e-3
NC_DEG = 2.0                             # ASSUMPTION: 'crossed at ~2 deg' taken as the angle inside the crystal
LINES, D_G = 600.0, 1e-3 / 600.0


def cm_width(pulse):
    lam = pulse["centroidWavelengthM"]
    return pulse["spectralFwhmM"] / lam ** 2 / 100.0


def spec_width_cm(res, prefix="output"):
    """FWHM (outermost half-maximum crossings, cm^-1) of the spectrum array of a result."""
    w = np.asarray(res[prefix + "_omega_rad_s"]); sp = np.asarray(res[prefix + "_spectrum"])
    sp = sp.sum(axis=tuple(range(1, sp.ndim))) if sp.ndim > 1 else sp
    k = np.where(sp >= sp.max() / 2)[0]
    return (w[k.max()] - w[k.min()]) / (2 * math.pi * C0) / 100.0


def gdd_seed():
    # chirp rate d(omega)/dt = 2 pi c * 100 * 30 / 1e-12 [rad/s^2]; GDD = 1 / chirp rate
    return 1.0 / (2 * math.pi * C0 * 100 * SWEEP_CM_PER_PS / 1e-12)


def seed_pulse(energy=5e-9):
    return b.pulse(LS, T_SEED_TL, energy=energy, shape="sech2", gdd_s2=gdd_seed())


def opa_el(pump_e, pump_tau, delay="auto", length=L_BBO, nc=NC_DEG):
    aux = b.aux_beam("pump", b.pulse(LP, pump_tau, energy=pump_e), radius_m=W_PUMP, delay_s=delay)
    return b.crystal("opa", "BBO", length, process="opa", type="I", cut="auto", noncollinear=nc, aux=[aux], output="signal",
                     record=["idler", "pump"], beam={"kind": "gaussian", "w0M": W_SIG}, chi3=False, spectralMargin=4)


def run_chain(pump_e, pump_tau, g=None, aoi=None, seed_e=5e-9, **kw):
    els = [opa_el(pump_e, pump_tau, **kw)]
    if g is not None:
        els.append(b.grating_pair("comp", LINES, aoi, g, order=-1, passes=1))
    return uf.run(b.system("opcpa", els), b.run_spec(seed_pulse(seed_e)))


def pulse_of(res, i=-1):
    return res.summary["elements"][i]["pulse"]


def treacy_gdd(g, aoi_deg, lam=LS):
    gam = math.radians(aoi_deg)
    x = lam / D_G - math.sin(gam)
    cth = math.sqrt(1 - x * x)
    bslant = g / cth
    return -lam ** 3 * bslant / (2 * math.pi * C0 ** 2 * D_G ** 2 * (1 - x * x))


def main():
    p = Paper(__file__)
    out = {}
    # ------------------------------------------------------------------ analytic facts of the paper
    out["seedTL_sech2_fs"] = T_SEED_TL * 1e15
    out["paperPredicted_fs"] = 67.0
    out["tbp_70fs_155cm"] = 70e-15 * SPEC_CM * 100 * C0
    out["tbp_paper"] = 0.325
    out["ac_over_1p543_fs"] = 110.0 / 1.543
    out["compression_ratio_1p7ps_over_70fs"] = 1.7e-12 / 70e-15
    out["compressedPeakPower_GW"] = 65e-6 / 70e-15 / 1e9
    out["seedGdd_fs2"] = gdd_seed() * 1e30

    # ------------------------------------------------------------------ A: the fibre
    fib = b.fiber("fib", 4.5, material="FusedSilica", n2=2.6e-20, effectiveAreaM2=50e-12, raman="silica")
    rf = uf.run(b.system("fibre", [fib]), b.run_spec(b.pulse(LS, 1.7e-12, energy=8e-9)))
    pf = pulse_of(rf)
    out["fibre_spectralFwhm_cm"] = cm_width(pf)
    out["fibre_duration_ps"] = pf["fwhmS"] * 1e12
    out["fibre_sweepRate_cm_per_ps"] = cm_width(pf) / (pf["fwhmS"] * 1e12)
    out["fibre_Bintegral_rad"] = rf.summary["elements"][0]["bIntegralRad"]
    out["paper_fibre_spectrum_cm"], out["paper_fibre_duration_ps"], out["paper_fibre_sweep"] = 155.0, 5.0, 30.0

    # ------------------------------------------------------------------ gain formula: pump intensity for G = 2e4 (inferred)
    # high-gain OPA, degenerate: G ~ (1/4) exp(2 Gamma L), Gamma = sqrt(2 w_s w_i d^2 I / (eps0 c^3 n_s n_i n_p))
    rp = uf.crystal.phase_match("BBO", "opa", pump=LP, signal=LS, type="I")["best"]
    d_eff = abs(rp["deffPmPerV"]) * 1e-12
    no_s = rp["waves"][0]["n"] if isinstance(rp["waves"], list) and isinstance(rp["waves"][0], dict) and "n" in rp["waves"][0] else 1.65
    ws = 2 * math.pi * C0 / LS
    gam_needed = math.log(4 * 2e4) / (2 * L_BBO)
    i_need = gam_needed ** 2 * 8.854187817e-12 * C0 ** 3 * no_s ** 3 / (2 * ws * ws * d_eff ** 2)
    out["inferred_pumpIntensity_for_G2e4_GWcm2"] = i_need / 1e13
    out["bbo_theta_deg"] = rp["thetaDeg"]

    # ------------------------------------------------------------------ C: pump energy scan
    energies = np.array([20, 50, 100, 150, 200, 300, 500, 800, 1500]) * 1e-6 if p.fast else np.geomspace(10e-6, 2e-3, 25)
    rows = []
    for e in energies:
        r = run_chain(float(e), 8e-12)
        v = r.summary["elements"][0]["values"]
        pl = pulse_of(r)
        rows.append((e, v["energy_signal_J"], v["energy_idler_J"], v["energy_pump_J"], cm_width(pl), pl["fwhmS"] * 1e12))
    rows = np.array(rows)
    e_sig = rows[:, 1]
    # pump energy giving 100 uJ signal (interpolated, log-log)
    if e_sig.max() >= 100e-6 and e_sig.min() < 100e-6:
        e100 = float(np.exp(np.interp(math.log(100e-6), np.log(e_sig), np.log(energies))))
    else:
        e100 = float("nan")
    out["pumpEnergy_for_100uJ_signal_uJ"] = e100 * 1e6
    out["gain_at_100uJ"] = 100e-6 / 5e-9
    out["peakPumpIntensity_at_e100_GWcm2"] = (0.94 * e100 / 8e-12) * 2 / (math.pi * W_PUMP ** 2) / 1e13 if e100 == e100 else float("nan")
    out["opa_saturated_pumpEnergy_uJ"] = e100 * 1e6
    # unsaturated stage for the bandwidth and compression tests: pump peak intensity at which the small-signal exponent Gamma L = 4
    # (G ~ 3e2): the 100 uJ of the paper needs >= 200 uJ of pump, i.e. deep saturation, which a 1-D overlap model does not describe well
    i_lin = i_need * (4.0 / gam_needed / L_BBO) ** 2
    p_peak = i_lin * math.pi * W_PUMP ** 2 / 2
    ep = p_peak * 8e-12 / 0.94
    out["linearStage_pumpIntensity_GWcm2"] = i_lin / 1e13
    out["linearStage_pumpEnergy_8ps_uJ"] = ep * 1e6
    r = run_chain(ep, 8e-12)
    v = r.summary["elements"][0]["values"]
    out["linearStage_gain"] = v["energy_signal_J"] / 5e-9
    out["linearStage_pumpDepletion"] = 1 - v["energy_pump_J"] / ep
    out["linearStage_outputSpectrum_cm"] = spec_width_cm(r)
    out["seed_spectrum_cm"] = SPEC_CM
    # pump duration scan at constant peak pump power (energy ~ duration)
    taus = np.array([2.8, 4, 5, 6, 8, 10, 12, 15]) * 1e-12
    pw = []
    for t in taus:
        rr = run_chain(p_peak * float(t) / 0.94, float(t))
        pw.append((t, spec_width_cm(rr), rr.summary["elements"][0]["values"]["energy_signal_J"] / 5e-9))
    pw = np.array(pw)
    out["narrowing_2p8ps_pump_cm"] = float(pw[0, 1]); out["spectrum_8ps_pump_cm"] = float(pw[taus == 8e-12, 1][0])
    out["spectrum_15ps_pump_cm"] = float(pw[-1, 1])
    out["gain_vs_pumpDuration"] = pw[:, 2]

    # ------------------------------------------------------------------ gain bandwidth (calculator)
    gb = float(rp["acceptance"].get("fundamentalFwhmM", float("nan"))) if isinstance(rp.get("acceptance"), dict) else float("nan")
    out["calc_acceptance_fundamentalFwhm_m_1p0mm_note"] = gb

    # ------------------------------------------------------------------ D: compressor
    lit = math.degrees(math.asin(LS / (2 * D_G)))
    out["littrow_deg"] = lit
    gdd_need = gdd_seed()
    # perpendicular separation for |phi''| = chirp GDD, solved from the Treacy formula (analytic)
    g_need = gdd_need / abs(treacy_gdd(1.0, lit))
    out["compressorG_analytic_mm"] = g_need * 1e3
    out["compressorSlant_analytic_mm"] = g_need / math.cos(math.asin(LS / D_G - math.sin(math.radians(lit)))) * 1e3
    out["paper_compressor_mm"] = 170.0
    gs = np.linspace(0.6, 1.4, 21 if p.fast else 41) * g_need
    # compress the OPA output (pump at e100)
    wd = []
    for g in gs:
        rr = run_chain(ep, 8e-12, g=float(g), aoi=lit)
        wd.append(pulse_of(rr)["fwhmS"] * 1e15)
    wd = np.array(wd)
    i0 = int(np.argmin(wd))
    out["compressed_min_fs"] = float(wd[i0]); out["compressed_bestG_mm"] = float(gs[i0] * 1e3)
    out["paper_compressed_fs"] = 70.0
    # linear seed alone (no OPA) to separate the OPA's effect
    wl = []
    for g in gs:
        rr = uf.run(b.system("c", [b.grating_pair("comp", LINES, lit, float(g), order=-1, passes=1)]), b.run_spec(seed_pulse()))
        wl.append(pulse_of(rr)["fwhmS"] * 1e15)
    wl = np.array(wl)
    out["linearSeed_min_fs"] = float(wl.min()); out["linearSeed_bestG_mm"] = float(gs[int(np.argmin(wl))] * 1e3)
    # at the paper's 170 mm
    rr = run_chain(ep, 8e-12, g=0.170, aoi=lit)
    out["compressed_at_170mm_fs"] = pulse_of(rr)["fwhmS"] * 1e15
    # sensitivity to the angle (not given exactly)
    sens = {}
    for a in (lit - 4, lit, lit + 4):
        gneed = gdd_need / abs(treacy_gdd(1.0, a))
        sens["%.1f" % a] = gneed * 1e3
    out["compressorG_vs_angle_mm"] = sens

    # ------------------------------------------------------------------ figures
    fig, axs = p.figure(ncols=2, width_in=9.5, height_in=3.8)
    ax = axs[0]
    ax.loglog(energies * 1e6, e_sig * 1e6, "o-", color=BLUE, label="engine signal (seed 5 nJ, 8 ps pump)")
    ax.axhline(100, color=REF, ls="--", label="paper: 100 uJ")
    ax.set(xlabel="pump energy in the 0.3 mm spot (uJ)", ylabel="signal energy (uJ)"); ax.legend(fontsize=8)
    ax = axs[1]
    ax.plot(pw[:, 0] * 1e12, pw[:, 1], "o-", color=BLUE, label="amplified spectrum (FWHM), constant pump peak power")
    ax.axhline(SPEC_CM, color=REF, ls="--", label="seed 155 cm$^{-1}$ (paper)")
    ax.set(xlabel="pump duration (ps)", ylabel="spectral width (cm$^{-1}$)"); ax.legend(fontsize=8)
    p.save(fig, "opa-energy-bandwidth")

    fig, ax = p.figure(width_in=6.6, height_in=3.8)
    ax.plot(gs * 1e3, wd, "o-", color=BLUE, label="OPA output, compressed")
    ax.plot(gs * 1e3, wl, "-", color=ORANGE, label="seed alone, compressed")
    ax.axvline(170, color=REF, ls="--", label="paper: 170 mm")
    ax.axhline(70, color=REF2, ls=":", label="paper: 70 fs")
    ax.set(xlabel="grating separation G (mm, perpendicular; Littrow)", ylabel="pulse duration FWHM (fs)", yscale="log"); ax.legend(fontsize=8)
    p.save(fig, "compression-scan")
    out["energies_uJ"] = energies * 1e6; out["signal_uJ"] = e_sig * 1e6
    p.write_numbers(out)


if __name__ == "__main__":
    main()
