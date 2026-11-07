"""Observation: the input pulse's spectral centre is the carrier snapped onto the frequency grid, up to half a grid step from the requested one.

docs/concepts.md says the engine snaps the carrier onto the frequency grid before building the pulse; this prints how far that moves the spectrum
(requested wavelength vs spectral centroid) on the default grid. Short pulses and long wavelengths: up to ~1 nm. Not a bug if intended,
but any reference that uses the requested carrier (energyMeter band, IAC fringes) is off by this much.

Run: .venv/bin/python tests/components/repro/pulse_carrier_snapped.py
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import C, b, centroid, math, np, uf  # noqa: E402

for lam in (515e-9, 1030e-9, 2e-6):
    for tau in (100e-15, 30e-15, 10e-15):
        r = uf.run(b.system("free", [b.probe("p")]), b.run_spec(b.pulse(lam, tau, energy=1e-9)))
        w, s = np.asarray(r["input_omega_rad_s"]), np.asarray(r["input_spectrum"])
        lc = 2 * math.pi * C / centroid(w, s)
        print(f"{lam * 1e9:5.0f} nm {tau * 1e15:4.0f} fs: spectral centre {lc * 1e9:9.3f} nm  (shift {(lc - lam) * 1e9:+.3f} nm, grid step {(w[1] - w[0]) / (2 * math.pi * C / lam) * lam * 1e9:.3f} nm)")
