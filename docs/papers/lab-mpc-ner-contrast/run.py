#!/usr/bin/env python3
"""Lab case (not a paper): Pharos laser -> multi-pass cell (fused-silica plate, 50 passes) -> SEQUOIA contrast measurement.

    python run.py --tier fast            # reduced cell (5 blocks of 10 passes), ~3-5 min
    python run.py --tier full --figures  # 25 blocks of 2 passes (closer to 50 passes), committed figures

Recorded data (data/, the owner's own recordings): seed spectrum, MPC-output spectra at four Pharos levels, the
SEQUOIA trace of the seed, the SEQUOIA trace with the cell bypassed, and SEQUOIA traces behind the cell at several
levels (Varying_Pharos_Pulse_Energies/).

Steps (the fitting plugin does both fits):
1. seed: a `transform` (super-Gaussian spectral amplitude) turns a short transform-limited pulse into the recorded seed
   spectrum; its GDD is fixed so that the duration is 200 fs.
2. cell: 1-D plane-wave model, fused-silica Kerr plates (constant beam, radius 0.55 mm) with a lumped GDD of the
   dispersive mirrors; the energy scale (B) and the GDD are fitted to the 20 % output spectrum; the other levels are
   predicted.
3. contrast: the measured SEQUOIA trace of the seed is the input pedestal (`pedestal.kind = "measured"`); the engine's
   crossCorrelator is compared with the no-cell record and with the record behind the cell.
"""
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "data")
RADIUS = 0.55e-3
PLATE = 5e-3
PASSES = 50
# pulse energies at the cell entrance, from the power meter readings in the file names of the recorded SEQUOIA traces (1 kHz)
ENERGY = {10: 10.1e-6, 20: 18.5e-6, 30: 21.2e-6, 39: 26.7e-6}
SPECTRUM = {10: "Spectrum_Pharos10%.csv", 20: "Spectrum_Pharos20%.csv", 30: "Spectrum_Pharos30%.csv", 39: "Spectrum_Pharos39%.csv"}
MPC_TRACE = "Varying_Pharos_Pulse_Energies/20261019-pharos20%-P_in18_5mW-ND0.dat"


# ------------------------------------------------------------------ data
def read_pairs(path):
    out = []
    for line in open(path, encoding="utf-8-sig").read().splitlines():
        try:
            out.append([float(v) for v in line.replace(",", " ").split()[:2]])
        except ValueError:
            pass
    return np.array(out)


def prepare(work):
    """Clean copies in results/work: spectra (wavelength_nm, intensity) and the pedestal (time_s, relativeIntensity)."""
    os.makedirs(work, exist_ok=True)
    paths = {}
    for k, f in list(SPECTRUM.items()) + [("seed", "Spectrum_Pharos_Output.csv")]:
        d = read_pairs(os.path.join(DATA, f))
        paths[k] = os.path.join(work, "spec_%s.csv" % k)
        np.savetxt(paths[k], d, delimiter=",", header="wavelength_nm,intensity", comments="")
    nb = read_pairs(os.path.join(DATA, "Contrast_Pharos_NarrowbandNF.dat"))
    y = np.abs(nb[:, 1]) / nb[:, 1].max()
    paths["ped"] = os.path.join(work, "pedestal.csv")
    np.savetxt(paths["ped"], np.c_[nb[:, 0], np.maximum(y, 1e-12)], delimiter=",", header="time_s,relativeIntensity", comments="", fmt="%.6e")
    return paths


def spectrometer(path, lo=900e-9, hi=1160e-9, sigma=0.03):
    return {"kind": "spectrometer", "name": "spectrum", "at": "end", "axis": {"from": lo, "to": hi, "step": 0.5e-9},
            "measured": {"file": path, "background": "auto", "sigma": sigma}}


# ------------------------------------------------------------------ models
def seed_ops(s, gdd):
    return {"id": "seed", "type": "transform", "operators": [
        {"kind": "spectralAmplitude", "shape": "superGaussian", "order": s["order"], "fwhmNm": s["fwhm"], "centerWavelengthNm": s["center"]},
        {"kind": "spectralPhase", "gddFs2": gdd}]}


def cell_system(s, gdd_seed, gdd_mid, nblocks, ghost=None, fit_gdd=False):
    """Seed transform, then nblocks blocks of (PASSES / nblocks) passes through the 5 mm plate: one plate with its
    ghosts (reflectance per face `ghost`, scaled by the passes per block so that the coherent ghost amplitude of the
    block equals that of its passes) plus the remaining silica as a ghost-free Kerr element; mirrors' GDD lumped mid-cell."""
    k = PASSES // nblocks
    el = [seed_ops(s, gdd_seed)]
    gd = {"value": gdd_mid, "fit": True, "min": -3e4, "max": 3e4, "scale": 3000} if fit_gdd else gdd_mid
    for i in range(nblocks):
        plate = {"id": "fs%d" % i, "type": "bulk", "material": "FusedSilica", "lengthM": PLATE, "beam": {"kind": "constant", "radiusM": RADIUS}}
        if ghost:
            plate["ghosts"] = {"frontReflectance": min(ghost * k, 0.5), "backReflectance": min(ghost * k, 0.5)}
        el.append(plate)
        if k > 1:
            el.append({"id": "fsk%d" % i, "type": "bulk", "material": "FusedSilica", "lengthM": PLATE * (k - 1),
                       "beam": {"kind": "constant", "radiusM": RADIUS}})
        if i == nblocks // 2 - 1:
            el.append({"id": "mirrors", "type": "transform", "operators": [{"kind": "spectralPhase", "gddFs2": gd}]})
    return {"format": "ultrafast-system", "version": 2, "name": "lab-mpc", "elements": el}


def run_spec(energy, grid, instruments, fit=None, pedestal=None):
    s = {"format": "ultrafast-run", "version": 2, "name": "lab-mpc",
         "pulse": {"shape": "gaussian", "centerWavelengthNm": 1030, "durationFwhmFs": 60, "energyJ": energy},
         "grid": dict({"auto": False}, **grid), "physics": {"selfSteepening": True}, "instruments": instruments}
    if fit is not None:
        s["fit"] = fit
    if pedestal:
        s["pulse"]["pedestal"] = pedestal
    return s


def seed_energy_factor(s, gdd):
    """Energy transmission of the seed transform (the pulse energy is set before the filter)."""
    r = uf.run({"format": "ultrafast-system", "version": 2, "name": "t", "elements": [seed_ops(s, gdd)]},
               run_spec(1e-6, {"n": 8192, "windowS": 4e-12}, []))
    return r.summary["totals"]["energyTransmission"]


def param(fit, key):
    for p in fit["parameters"]:
        if p["name"].endswith(key):
            return p
    raise KeyError(key)


def to_db(y, floor=1e-12):
    return 10 * np.log10(np.maximum(y, floor))


def hitran_air_case(p, system, energy):
    """Delayed light from H2O lines in 67.5 m of lab air (45 % humidity) behind the cell: the engine's tail ledger."""
    hdir = os.path.join(p.results, "hitran")
    try:
        sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "plugins", "hitran", "python"))
        import ultrafast_hitran as hit
        if not os.path.exists(os.path.join(hdir, "H2O.csv")):
            hit.download(["H2O"], numin=9300, numax=10300, directory=hdir)
        os.environ["ULTRAFAST_HITRAN_DIR"] = hdir
    except Exception as e:     # offline or no hapi: say so, do not fail
        return {"available": False, "reason": str(e)[:200]}
    sysa = json.loads(json.dumps(system))
    sysa["elements"].append({"id": "air", "type": "gap", "lengthM": PASSES * 1.35})
    sysa["environment"] = {"air": {"kind": "hitran", "dataDir": hdir, "applyToGaps": True, "temperatureC": 22, "pressurePa": 101325, "humidity": 0.45, "species": {"H2O": True}}}
    sp = run_spec(energy, {"n": 8192, "windowS": 4e-12}, [{"kind": "contrast", "name": "c", "at": "end"}])
    sp["grid"]["tail"] = {"windowS": 2e-9}
    r = uf.run(sysa, sp)
    t = r.summary.get("tail") or {}
    return {"available": True, "tailEnergyFraction": t.get("fractionOfOutput"), "tailWindowNs": 2.0,
            "meanContrastDbEstimate": 10 * math.log10(max(t.get("fractionOfOutput", 1e-30), 1e-30) * 0.3e-12 / 1e-9)}


def main():
    p = Paper(__file__)
    work = os.path.join(p.results, "work")
    paths = prepare(work)
    nblocks = 5 if p.fast else 25
    N = {"n": 8192, "windowS": 4e-12}
    numbers = {"nBlocks": nblocks, "passesPerBlock": PASSES // nblocks}

    # ---- 1. seed fit
    seed_sys = {"format": "ultrafast-system", "version": 2, "name": "seed", "elements": [{"id": "seed", "type": "transform", "operators": [
        {"kind": "spectralAmplitude", "shape": "superGaussian", "order": {"value": 1.5, "fit": True, "min": 1, "max": 6},
         "fwhmNm": {"value": 8, "fit": True, "min": 3, "max": 20}, "centerWavelengthNm": {"value": 1030, "fit": True, "min": 1020, "max": 1040}}]}]}
    rs = uf.run(seed_sys, run_spec(1e-6, N, [spectrometer(paths["seed"], 1000e-9, 1070e-9, 0.02)], fit={"maxIterations": 20}))
    fit = rs.summary["fit"]
    seed = {"order": param(fit, "order")["value"], "fwhm": param(fit, "fwhmNm")["value"], "center": param(fit, "centerWavelengthNm")["value"]}
    numbers["seedFit"] = dict(seed, rmsError=fit["instruments"]["spectrum"]["rmsErrorAfter"],
                              sigmaFwhmNm=param(fit, "fwhmNm")["sigma"])
    # GDD that makes the Gaussian-equivalent duration 200 fs: tau^2 = tau_TL^2 + (4 ln2 GDD / tau_TL)^2
    rp = uf.run({"format": "ultrafast-system", "version": 2, "name": "t", "elements": [seed_ops(seed, 0.0)]},
                run_spec(1e-6, N, [{"kind": "pulseSummary", "name": "ps", "at": "end"}]))
    tl = rp.summary["output"]["fwhmS"] if "output" in rp.summary else 150e-15
    tau = 200e-15
    gdd_seed = math.sqrt(max(tau ** 2 - tl ** 2, 0)) * tl / (4 * math.log(2)) * 1e30     # fs^2
    numbers["seedTransformLimitFs"] = tl * 1e15
    numbers["seedGddFs2"] = gdd_seed
    fac = seed_energy_factor(seed, gdd_seed)

    def e_in(level):          # pulse energy before the filter such that ENERGY[level] enters the cell
        return ENERGY[level] / fac

    # ---- 2. cell fit at 20 %, prediction at the other levels
    grid = {"n": 8192, "windowS": 4e-12}
    sysf = cell_system(seed, gdd_seed, 0.0, nblocks, fit_gdd=True)
    spec20 = run_spec(e_in(20), grid, [spectrometer(paths[20])],
                      fit={"maxIterations": 12, "parameters": [
                          {"path": "pulse/energyJ", "name": "energyScale", "min": 0.3 * e_in(20), "max": 8 * e_in(20), "scale": 0.3 * e_in(20)}]})
    rc = uf.run(sysf, spec20)
    cf = rc.summary["fit"]
    gdd_mid = param(cf, "gddFs2")["value"]
    e_scale = param(cf, "energyScale")["value"] / e_in(20)
    numbers["cellFit"] = {"gddLumpedFs2": gdd_mid, "gddSigmaFs2": param(cf, "gddFs2")["sigma"], "energyScale": e_scale,
                          "energyScaleSigma": param(cf, "energyScale")["sigma"] / e_in(20),
                          "rmsError20": cf["instruments"]["spectrum"]["rmsErrorAfter"], "rmsErrorStart20": cf["instruments"]["spectrum"]["rmsErrorBefore"]}
    spectra = {}
    system = cell_system(seed, gdd_seed, gdd_mid, nblocks)
    for lvl in (10, 20, 30, 39):
        r = uf.run(system, run_spec(e_in(lvl) * e_scale, grid, [spectrometer(paths[lvl])]))
        c = r.comparison("spectrum")
        sp = r.instrument("spectrum")
        spectra[lvl] = {"lam": np.array(sp.get("wavelength_m", sp.get("x", []))) * 1e9, "sim": np.array(sp.get("spectrum", sp.get("y", []))),
                        "rms": c["rmsError"], "b": r.summary["totals"]["bIntegralRad"]}
        numbers["spectrumRms%d" % lvl] = c["rmsError"]
        numbers["bIntegral%d" % lvl] = r.summary["totals"]["bIntegralRad"]

    # ---- 3. contrast
    ped = [{"kind": "measured", "csv": paths["ped"], "excludeWithinS": 1.5e-12, "seed": 1}]
    cgrid = {"n": 32768, "windowS": 160e-12}
    xc = {"kind": "crossCorrelator", "name": "xc", "at": "end", "rangeS": [-60e-12, 60e-12], "stepS": 100e-15, "dynamicRangeDb": 110, "noise": False}

    def sequoia(sys_, energy):
        r = uf.run(sys_, run_spec(energy, cgrid, [xc], pedestal=ped))
        x = r.instrument("xc")
        return np.array(x["delay_s"]) * 1e12, np.array(x["signal_db"]), r

    bypass = {"format": "ultrafast-system", "version": 2, "name": "bypass", "elements": [seed_ops(seed, gdd_seed)]}
    t_by, db_by, _ = sequoia(bypass, e_in(20))
    t_mpc, db_mpc, rmpc = sequoia(system, e_in(20) * e_scale)
    ghost_cases = {}
    for name, g in (("plate with 0.2 % AR coating", 0.002), ("plate uncoated (3.4 % per face)", 0.034)):
        t_g, db_g, _ = sequoia(cell_system(seed, gdd_seed, gdd_mid, nblocks, ghost=g), e_in(20) * e_scale)
        ghost_cases[name] = db_g

    rec_by = read_pairs(os.path.join(DATA, "Contrast_Pharos20%_No_MPC_Ep_34_6uJ.dat"))
    rec_by_t, rec_by_db = rec_by[:, 0] * 1e12, to_db(np.abs(rec_by[:, 1]) / rec_by[:, 1].max())
    rec_m = read_pairs(os.path.join(DATA, MPC_TRACE))
    rec_m_t, rec_m_db = rec_m[:, 0] * 1e12, to_db(np.abs(rec_m[:, 1]) / rec_m[:, 1].max())

    def level(t, db, a, b):
        m = (t >= a) & (t <= b)
        return float(np.median(db[m])) if m.any() else float("nan")

    def peak(t, db, a, b):
        m = (t >= a) & (t <= b)
        return float(db[m].max()) if m.any() else float("nan")

    for name, (t, db) in {"bypassSim": (t_by, db_by), "bypassRec": (rec_by_t, rec_by_db), "mpcSim": (t_mpc, db_mpc), "mpcRec": (rec_m_t, rec_m_db)}.items():
        numbers[name] = {"dbAt4to10ps": level(t, db, 4, 10), "dbAt10to30ps": level(t, db, 10, 30), "dbAtM20toM8ps": level(t, db, -20, -8),
                         "peakDb40to55ps": peak(t, db, 40, 55)}
    for name, db in ghost_cases.items():
        numbers["ghost" + ("Ar" if "AR" in name else "Uncoated")] = {"peakDb40to55ps": peak(t_mpc, db, 40, 55)}
    # the recorded trace's ghost-like peak near 44-50 ps and the plate's double pass delay
    ng = 1.4677
    numbers["plateDoublePassDelayPs"] = 2 * ng * PLATE / 299792458.0 * 1e12

    # ---- 4. air: HITRAN water lines over the 67 m path (needs the line file: downloaded once, skipped offline)
    air = hitran_air_case(p, system, e_in(20) * e_scale)
    numbers["airLines"] = air

    p.write_numbers(numbers)
    plots(p, spectra, paths, seed, (t_by, db_by, rec_by_t, rec_by_db), (t_mpc, db_mpc, rec_m_t, rec_m_db, ghost_cases), rs, numbers)


def plots(p, spectra, paths, seed, by, mpc, rs, numbers):
    # seed spectrum + fitted transform
    fig, ax = p.figure(2, 2, 11, 7.2)
    ax = ax.ravel()
    d = read_pairs(paths["seed"])
    y = d[:, 1] - np.percentile(d[:, 1], 5)
    ax[0].plot(d[:, 0], y / y.max(), color=REF, label="recorded seed")
    sp = rs.instrument("spectrum")
    xs = np.array(sp.get("wavelength_m", sp.get("x", []))) * 1e9
    ys = np.array(sp.get("spectrum", sp.get("y", [])))
    ax[0].plot(xs, ys / max(ys.max(), 1e-30), color=BLUE, label="fitted transform")
    ax[0].set_xlim(1000, 1060)
    ax[0].set_xlabel("wavelength (nm)")
    ax[0].set_ylabel("spectrum (peak = 1)")
    ax[0].legend()
    ax[0].set_title("seed: transform fit")
    cols = {10: BLUE, 20: ORANGE, 30: AQUA, 39: "#7a3fbf"}
    for lvl, s in spectra.items():
        d = read_pairs(paths[lvl])
        yy = d[:, 1] - np.percentile(d[:, 1], 5)
        off = {10: 0, 20: 1.2, 30: 2.4, 39: 3.6}[lvl]
        ax[1].plot(d[:, 0], off + yy / yy.max(), color=REF2, lw=1)
        if len(s["lam"]):
            ax[1].plot(s["lam"], off + s["sim"] / max(s["sim"].max(), 1e-30), color=cols[lvl], lw=1.4, label="%d %% (B = %.1f rad)" % (lvl, s["b"]))
    ax[1].set_xlim(930, 1130)
    ax[1].set_xlabel("wavelength (nm)")
    ax[1].set_ylabel("spectrum, offset by level")
    ax[1].legend(fontsize=7)
    ax[1].set_title("cell output: grey recorded, colour model (fit at 20 %)")
    t_by, db_by, rt, rdb = by
    ax[2].plot(rt, rdb, color=REF2, lw=0.7, label="recorded, cell bypassed")
    ax[2].plot(t_by, db_by, color=BLUE, lw=1.0, label="model, seed + measured pedestal")
    ax[2].set_xlim(-45, 66)
    ax[2].set_ylim(-110, 3)
    ax[2].set_xlabel("delay (ps)")
    ax[2].set_ylabel("contrast (dB)")
    ax[2].legend(fontsize=7)
    ax[2].set_title("SEQUOIA, no cell")
    t_m, db_m, rt2, rdb2, ghosts = mpc
    ax[3].plot(rt2, rdb2, color=REF2, lw=0.7, label="recorded, 20 % behind the cell")
    ax[3].plot(t_m, db_m, color=BLUE, lw=1.0, label="model, no plate ghosts")
    for (name, db), c in zip(ghosts.items(), (ORANGE, AQUA)):
        ax[3].plot(t_m, db, color=c, lw=0.9, label="model, " + name)
    ax[3].set_xlim(-30, 60)
    ax[3].set_ylim(-110, 3)
    ax[3].set_xlabel("delay (ps)")
    ax[3].set_ylabel("contrast (dB)")
    ax[3].legend(fontsize=6.5)
    ax[3].set_title("SEQUOIA behind the cell")
    p.save(fig, "overview")


if __name__ == "__main__":
    main()
