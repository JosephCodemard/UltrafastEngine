#!/usr/bin/env python3
"""The calculator system (ultrafast.calc): five design questions answered in one call each, then one answer checked against a full engine run."""
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402
from ultrafast import calc  # noqa: E402
from ultrafast import plotting as up  # noqa: E402

LAM, TAU, E_J = 1030e-9, 100e-15, 1e-6

# 1. pulse: transform limit of a 100 fs sech^2 pulse, and what a measured 20 nm / 100 fs pulse says about its chirp (no engine needed for tbp)
print("sech2 TBP %.4f   100 fs -> %.2f THz" % (calc.tbp("sech2"), calc.pulse.limit("sech2", duration_s=TAU)["bandwidthFwhmHz"] / 1e12))
chirp = calc.pulse.analyze(duration_s=TAU, bandwidth_m=20e-9, center_m=LAM)
print("100 fs with 20 nm at 1030 nm: %.2f x the Gaussian limit, |GDD| %.0f fs^2" % (chirp["ratioToLimit"], chirp["gddMagnitudeFs2"]))

# 2. beam: lens positions that turn a 0.2 mm waist into a 0.5 mm waist with f = 0.5 m, checked by propagating q through the ABCD matrices (NumPy only)
match = calc.beam.single_lens_match(waist_in_m=0.2e-3, waist_out_m=0.5e-3, wavelength_m=LAM, focal_length_m=0.5)
sol = match["solutions"][1]           # the second solution has a real output waist (the first one is virtual)
d1, d2 = sol["distanceInputM"], sol["distanceOutputM"]
q0 = calc.beam.q_at(LAM, waist_radius_m=0.2e-3, z_m=0.0)
q = calc.beam.propagate_q(q0, calc.beam.system_matrix(calc.beam.free_space(d1), calc.beam.thin_lens(0.5), calc.beam.free_space(d2)))
out = calc.beam.q_to_beam(q, LAM)
print("single lens: f_min %.3f m, lens %.3f m from the input waist, output waist %.3f m behind it" % (match["minimumFocalLengthM"], d1, d2))
print("ABCD check: radius at the output waist %.4f mm (asked 0.5 mm), flat wavefront: %s" % (out["radiusM"] * 1e3, abs(out["curvatureRadiusM"]) > 1e9))

# 3. nonlinear: critical power in fused silica and the B-integral of 5 mm of it (NumPy only)
N2, N0, LENGTH, W = 2.6e-20, 1.45, 5e-3, 0.5e-3     # n2 of the engine's FusedSilica entry (approximate, about 30 %)
pcr = calc.nonlinear.critical_power_w(LAM, n2_m2_per_w=N2, n0=N0)
est = calc.nonlinear.b_integral_estimate(LAM, n2_m2_per_w=N2, length_m=LENGTH, pulse_energy_j=E_J, duration_fwhm_s=TAU, beam_radius_m=W, shape="sech2", n0=N0)
print("fused silica: P_cr %.2f MW, peak power %.2f MW, B (peak) %.4f rad" % (pcr / 1e6, est["peakPowerW"] / 1e6, est["bPeakRad"]))

# 4. crystal: phase matching of type-I SHG in BBO at 1030 nm (the engine's Sellmeier equations)
pm = calc.crystal.phase_match("BBO", "shg", "I", fundamental_m=LAM, length_m=1e-3)
print("BBO type I SHG at 1030 nm: theta %.2f deg, d_eff %.2f pm/V" % (pm["best"]["thetaDeg"], pm["best"]["deffPmPerV"]))

# 5. check one answer against the full engine: a run through 5 mm of fused silica gives the B-integral of the estimate
plate = b.system("plate", [b.bulk("silica", "FusedSilica", LENGTH, beam=b.gaussian_beam(w0_m=W, z_waist_m=LENGTH / 2))])
res = uf.run(plate, b.run_spec(b.pulse(LAM, TAU, energy=E_J, shape="sech2")))
engine_b = res.summary["totals"]["bIntegralRad"]
print("engine B-integral %.4f rad, calculator %.4f rad, ratio %.2f" % (engine_b, est["bPeakRad"], engine_b / est["bPeakRad"]))

# figure: beam through the matching lens, and B against energy for both
os.makedirs(os.path.join(HERE, "figures"), exist_ok=True)
with up.use_style("ultrafast"):
    fig, (a, c) = plt.subplots(1, 2, figsize=(10, 3.6), constrained_layout=True)
    z = np.linspace(0, d1 + d2, 400)
    w = []
    for zi in z:
        mats = [calc.beam.free_space(min(zi, d1))]
        if zi > d1:
            mats += [calc.beam.thin_lens(0.5), calc.beam.free_space(zi - d1)]
        w.append(calc.beam.q_to_beam(calc.beam.propagate_q(q0, calc.beam.system_matrix(*mats)), LAM)["radiusM"])
    a.plot(z, np.array(w) * 1e3, lw=2)
    a.axvline(d1, color="gray", ls="--", lw=1)
    a.set(xlabel="distance from the input waist (m)", ylabel="beam radius (mm)", title="single-lens mode matching (f = 0.5 m)")
    energies = np.geomspace(0.1e-6, 10e-6, 12)
    a.text(d1, a.get_ylim()[1] * 0.9, " lens", fontsize=9)
    c.loglog(energies * 1e6, [calc.nonlinear.b_integral_estimate(LAM, n2_m2_per_w=N2, length_m=LENGTH, pulse_energy_j=e, duration_fwhm_s=TAU, beam_radius_m=W, shape="sech2", n0=N0)["bPeakRad"] for e in energies], lw=2, label="calculator")
    c.plot([E_J * 1e6], [engine_b], "o", ms=7, label="engine run")
    c.set(xlabel="pulse energy (uJ)", ylabel="B-integral (rad)", title="B-integral, 5 mm fused silica")
    c.legend(frameon=False)
    fig.savefig(os.path.join(HERE, "hero.png"), dpi=100)
