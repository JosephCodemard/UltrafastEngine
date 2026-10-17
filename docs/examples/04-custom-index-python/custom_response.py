#!/usr/bin/env python3
"""Example 04 -- your own n(t, I): a Python time-domain response.

The built-in Kerr effect is Delta n = n2 I. Here we ADD a toy free-carrier (plasma) response with memory and the
matching multiphoton absorption, written in NumPy and called by the engine once per solver stage:

    rho(t)       = integral sigma_K I(t')^K dt'          (carriers created by K-photon ionisation)
    Delta n_re  += -rho(t) / (2 n0 rho_c)                (free carriers lower the index -> blue shift)
    Delta n_im  +=  K hbar sigma_K I^(K-1) c / 2         (each ionisation absorbs K photons: dI/dz = -K hbar w0 sigma_K I^K)

The coefficients are ILLUSTRATIVE (chosen so the effect is visible), not material data. At these intensities a
real plate would also self-focus; spatial effects are outside model level L0.
Run from the repository root after building the engine (python python/tools/build_engine.py):

    python docs/examples/04-custom-index-python/custom_response.py
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "python", "src"))
import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

C = 299792458.0
K, SIGMA_K, N0 = 4, 2e-28, 1.45     # illustrative (SI units): ~1e23 m^-3 carriers
LAM = 800e-9
RHO_C = 8.854e-12 * 9.109e-31 * (2 * math.pi * C / LAM) ** 2 / 1.602e-19 ** 2   # critical density (1/m^3)


def plasma(ctx: uf.ResponseContext):
    I = ctx.intensity
    dt = ctx.t[1] - ctx.t[0]
    rho = np.cumsum(SIGMA_K * I ** K) * dt                 # causal: carriers accumulate during the pulse
    ctx.dn_re[:] = -rho / (2 * N0 * RHO_C)
    ctx.dn_im[:] = K * 1.054571817e-34 * SIGMA_K * I ** (K - 1) * C / 2


def main():
    sysd = b.system("plate-with-plasma", [
        b.bulk("plate", "FusedSilica", 2e-3, beam=b.gaussian_beam(w0_m=40e-6, z_waist_m=1e-3)),
    ])
    base = dict(pulse=b.pulse(LAM, 40e-15, energy=2e-6), physics={"raman": True},
                instruments=[b.instrument("spectrometer"), b.instrument("energyMeter")])

    kerr_only = uf.run(sysd, b.run_spec(**base))

    spec = b.run_spec(**base, overrides=[b.override("element:plate", "timeResponse", "@plasma", mode="add")])
    run = uf.Run(uf.System.load(sysd), spec)
    run.time_response("plasma", plasma)
    with_plasma = run.execute()

    for name, r in (("Kerr only", kerr_only), ("Kerr + plasma", with_plasma)):
        s = r.summary
        print(f"{name:14s}: T = {s['totals']['energyTransmission']:.4f}, spectral centroid "
              f"{s['output']['centroidWavelengthM'] * 1e9:.2f} nm, rms width {s['output']['spectralRmsWidthM'] * 1e9:.2f} nm, "
              f"B = {s['totals']['bIntegralRad']:.2f} rad")
    print("provenance:", [p for p in with_plasma.provenance if "timeResponse" in p["property"]])
    try:
        import matplotlib.pyplot as plt
        for name, r in (("Kerr only", kerr_only), ("Kerr + plasma", with_plasma)):
            lam, s = r["output_wavelength_m"], r["output_spectrum"]
            plt.plot(lam * 1e9, s / s.max(), label=name)
        plt.xlabel("wavelength (nm)"); plt.legend(); plt.title("plasma blue-shift"); plt.savefig("custom_response.png", dpi=120)
        print("saved custom_response.png")
    except ImportError:
        pass


if __name__ == "__main__":
    main()
