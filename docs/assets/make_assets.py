#!/usr/bin/env python3
"""Regenerate the synthetic measurement assets of docs/assets/ (and their answer key) with the engine.

    python docs/assets/make_assets.py            # needs the hosted engine: python python/tools/build_engine.py

Each asset is what a lab instrument would record from a pulse whose parameters are known (they are in
answer_key.json, not in the data files), with realistic noise. Use them to try retrieval, fitting and contrast
analysis, and to test your own code: the answer key says what a correct analysis must find.
"""
import csv
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

rng = np.random.default_rng(2108)
key = {"format": "ultrafast-assets-answer-key", "engine": uf.version(), "assets": {}}


def noisy(trace, rel=0.01, floor=1e-3):
    t = np.asarray(trace, float)
    t = t / t.max()
    t = t * (1 + rel * rng.standard_normal(t.shape)) + floor * rng.standard_normal(t.shape)
    return np.clip(t, 0, None)


def write_csv(path, header, cols):
    with open(path, "w", newline="") as f:
        w = csv.writer(f)
        f.write("# synthetic data written by docs/assets/make_assets.py\n")
        w.writerow(header)
        for row in zip(*cols):
            w.writerow([f"{v:.6g}" for v in row])


# 1. SHG-FROG of a chirped 1030 nm pulse --------------------------------------------------------------------
gdd, tod = 3000e-30, 20000e-45
sysd = b.system("frog-asset", [b.dispersion("chirp", gdd_s2=gdd, tod_s3=tod)])
res = uf.run(sysd, b.run_spec(b.pulse(1.03e-6, 150e-15, energy=1e-6),
                              instruments=[b.instrument("frog", name="frog", type="shg", points=256), b.instrument("pulseSummary")]))
np.savez(os.path.join(HERE, "frog_shg_1030nm.npz"), delay_s=res["frog_delay_s"], omega_rad_s=res["frog_omega_rad_s"],
         trace=noisy(res["frog_trace"]))
key["assets"]["frog_shg_1030nm.npz"] = {
    "what": "SHG-FROG trace (rows: delay_s, columns: omega_rad_s of the SH signal), 1 % multiplicative + 1e-3 additive noise",
    "truth": {"centerWavelengthM": 1.03e-6, "transformLimitFwhmS": 150e-15, "gddFs2": gdd * 1e30, "todFs3": tod * 1e45,
              "fwhmS": res.summary["output"]["fwhmS"]},
    "expect": "epie (300 iterations) or pcgpa (--iterations 1500 at this noise level): FROG error < 1e-3, FWHM within 5 %, "
              "|GDD| of the retrieved spectral phase within 5 % (its sign is ambiguous in SHG-FROG; the weak TOD is not recovered)",
}

# 2. glass-insertion d-scan of an 800 nm pulse with residual GDD ------------------------------------------
gdd2 = -150e-30
sysd = b.system("dscan-asset", [b.dispersion("residual", gdd_s2=gdd2)])
res = uf.run(sysd, b.run_spec(b.pulse(800e-9, 25e-15, energy=1e-6),
                              instruments=[b.instrument("dscan", name="dscan", material="FusedSilica", insertionM=[-2e-3, 4e-3], steps=61),
                                           b.instrument("spectrometer", name="spec")]))
names = res.names()
row = next(n for n in names if n.startswith("dscan_") and n.endswith(("insertion_m",)))
col = next(n for n in names if n.startswith("dscan_") and (n.endswith("omega_rad_s") or n.endswith("wavelength_m")))
np.savez(os.path.join(HERE, "dscan_800nm.npz"), insertion_m=res[row], **{col.replace("dscan_", ""): res[col]},
         trace=noisy(res["dscan_trace"]))
wl = res["spec_wavelength_m"]; sp = res["spec_spectrum"]
m = sp > 1e-4 * sp.max()
write_csv(os.path.join(HERE, "dscan_800nm_fundamental.csv"), ["wavelength_nm", "intensity"], [wl[m] * 1e9, sp[m] / sp.max()])
key["assets"]["dscan_800nm.npz"] = {
    "what": "SHG d-scan vs fused-silica insertion (+ dscan_800nm_fundamental.csv, the fundamental spectrum)",
    "truth": {"centerWavelengthM": 800e-9, "transformLimitFwhmS": 25e-15, "gddFs2": gdd2 * 1e30},
    "expect": "dscan: FWHM 30 fs (the chirped input), GDD of the retrieved phase -150 fs^2 within 10 %, with its sign",
}

# 3. third-order cross-correlation with a pre-pulse, a post-pulse and a pedestal ---------------------------
ped = [{"kind": "prePulse", "delayS": 20e-12, "relativeIntensity": 3e-7},
       {"kind": "postPulse", "delayS": 35e-12, "relativeIntensity": 1e-4},
       {"kind": "exponential", "level": 1e-5, "riseS": 1.5e-12, "fallS": 2e-12}]
sysd = b.system("xc-asset", [b.gap("air", 0.1)])
res = uf.run(sysd, b.run_spec(b.pulse(1.03e-6, 300e-15, energy=1e-3, pedestal=ped),
                              instruments=[b.instrument("crossCorrelator", name="xc", rangeS=[-50e-12, 50e-12], stepS=0.1e-12,
                                                        fineStepS=20e-15, fineRangeS=2e-12, dynamicRangeDb=100)]))
write_csv(os.path.join(HERE, "crosscorrelation_1030nm.csv"), ["delay_ps", "signal"], [res["xc_delay_s"] * 1e12, res["xc_signal"]])
key["assets"]["crosscorrelation_1030nm.csv"] = {
    "what": "third-order cross-correlation (normalised signal vs delay), 100 dB dynamic range with detector noise",
    "truth": {"prePulse": {"delayPs": -20, "relativeIntensity": 3e-7}, "postPulse": {"delayPs": 35, "relativeIntensity": 1e-4},
              "exponentialPedestal": {"level": 1e-5, "riseS": 1.5e-12, "fallS": 2e-12}},
    "expect": "pre-pulse near -65 dB at -20 ps, post-pulse near -40 dB at +35 ps, and the correlator's artefact of the "
              "post-pulse (~ -80 dB) at -35 ps; exponential wings of the pedestal",
}

# 4. a spectrum and an autocorrelation after SPM, for fitting the energy -----------------------------------
energy = 6e-6
sysd = b.system("spm-asset", [b.bulk("plate", "FusedSilica", 6.35e-3, beam=b.gaussian_beam(w0_m=120e-6, z_waist_m=3.175e-3))])
res = uf.run(sysd, b.run_spec(b.pulse(1.03e-6, 250e-15, energy=energy),
                              instruments=[b.instrument("spectrometer", name="spec", resolutionM=0.1e-9),
                                           b.instrument("autocorrelator", name="ac")]))
wl = res["spec_wavelength_m"]; sp = res["spec_spectrum"]; m = sp > 1e-4 * sp.max()
write_csv(os.path.join(HERE, "spm_spectrum.csv"), ["wavelength_nm", "counts"], [wl[m] * 1e9, noisy(sp[m], 0.02, 2e-3) * 4.0e4])
d = res["ac_delay_s"]; a = res["ac_signal"]
write_csv(os.path.join(HERE, "spm_autocorrelation.csv"), ["delay_ps", "signal"], [d * 1e12, noisy(a, 0.01, 2e-3)])
key["assets"]["spm_spectrum.csv"] = {
    "what": "spectrum after a 6.35 mm fused-silica plate (w0 = 120 um at its centre), 250 fs TL input at 1030 nm; "
            "with spm_autocorrelation.csv. The pulse energy is the unknown.",
    "truth": {"energyJ": energy, "BIntegralRad": res.summary["totals"]["bIntegralRad"]},
    "expect": "a fit of pulse/energyJ (fitting plugin, docs/plugins/fitting.md) finds 6 uJ within ~5 %",
}

with open(os.path.join(HERE, "answer_key.json"), "w") as f:
    json.dump(key, f, indent=2)
print("wrote", ", ".join(sorted(key["assets"])), "and answer_key.json")
