"""Repro: energyMeter rangeM band edges snap to whole spectral bins.

A 100 fs, 1030 nm Gaussian pulse; band 1030 nm +- 2 nm. Gaussian CDF gives 0.2370; the engine gives 0.2456 on the default grid
(0.2410 on a 40 ps / 65536-point grid): the meter sums whole bins inside the band, so each edge is wrong by up to half a bin of
spectral density. Expected: edge bins weighted by the fraction of the bin inside the band (or the cumulative spectrum
interpolated), so the error falls as the square of the bin width.

Run: .venv/bin/python tests/components/repro/energymeter_band_bins.py
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import C, b, np, uf  # noqa: E402

lam, tau, h = 1030e-9, 100e-15, 2e-9
w0 = 2 * math.pi * C / lam
sig = (4 * math.log(2) / tau) / (2 * math.sqrt(2 * math.log(2)))
cdf = lambda x: 0.5 * (1 + math.erf(x / math.sqrt(2)))
wl, wh = 2 * math.pi * C / (lam + h), 2 * math.pi * C / (lam - h)
want = cdf((wh - w0) / sig) - cdf((wl - w0) / sig)
for name, grid in (("default grid", None), ("n=2^14, 40 ps", b.grid(auto=False, n=2 ** 14, window_s=40e-12))):
    r = uf.run(b.system("free", [b.probe("p")]), b.run_spec(b.pulse(lam, tau, energy=1e-6), grid=grid,
               instruments=[b.instrument("energyMeter", name="em", rangeM=[lam - h, lam + h])]))
    got = r.instrument("em")["energyJ"] / 1e-6
    w = np.asarray(r["input_omega_rad_s"])
    print(f"{name}: engine {got:.4f}, Gaussian CDF {want:.4f}, bin = {(w[1] - w[0]) / sig:.3f} sigma")
