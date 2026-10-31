#!/usr/bin/env python3
"""Tutorial 3: the CPA + SHG system of cpa.ufs, built in Python, then a scan of the pump power.

    python docs/tutorials/files/cpa.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402


def cpa_system(pump_w=150.0, lbo_length_m=1.5e-3):
    return b.system("tutorial-cpa", [
        b.grating_pair("stretcher", 1740, 60.0, -0.25, efficiency=0.92),
        b.probe("stretched"),
        b.multipass_amplifier("amplifier", "Yb:YAG", 0.006, passes=8, mode_radius_m=0.45e-3,
                              pump=b.pump("cw", 940e-9, 0.5e-3, power_w=pump_w, profile="superGaussian", order=4, passes=2),
                              between_passes={"loss": 0.03}, thermalLens={"rodRadiusM": 1.5e-3}),
        b.probe("amplified"),
        b.grating_pair("compressor", 1740, 60.0, 0.25, efficiency=0.92),
        b.crystal("shg", "LBO", lbo_length_m, process="shg", type="I", output="sh", record=["fundamental"],
                  beam=b.gaussian_beam(w0_m=2.5e-3)),
    ])


spec = b.run_spec(b.pulse(1.03e-6, 250e-15, energy=1e-6, shape="sech2", rep_rate_hz=10e3),
                  probes=["amplified"],
                  instruments=[b.instrument("powerMeter", at="amplified", name="P_amp"),
                               b.instrument("compressor", at="compressor", name="residual_ir"),
                               b.instrument("conversion", at="shg", name="shg_eff")])

# one run, saved like the CLI does (plot it with ultrafast-plot cpa_results)
res = uf.run(cpa_system(), spec)
res.write("cpa_results")
print(f"amplified: {res.instrument('P_amp')['averagePowerW']:.2f} W, "
      f"compressible to {res.instrument('residual_ir')['compressedFwhmS'] * 1e15:.0f} fs, "
      f"SHG efficiency {res.instrument('shg_eff')['efficiencyVsInput']:.1%}")

# a scan: the system is a plain dict, so loops and parameter studies are ordinary Python
print("pump (W)   IR after amp (W)   green (W)")
for p in (60, 100, 150):
    r = uf.run(cpa_system(pump_w=p), spec)
    green = r.summary["output"]["energyJ"] * 10e3
    print(f"{p:8.0f}   {r.instrument('P_amp')['averagePowerW']:16.2f}   {green:9.2f}")
