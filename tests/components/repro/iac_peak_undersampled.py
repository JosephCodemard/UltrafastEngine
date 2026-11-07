"""Repro: interferometric autocorrelator peakToBackground is 7.84, not 8, for a 30 fs pulse at 1030 nm.

The delay axis has an even number of points (840 for 30 fs at 1030 nm) and no sample at d = 0; the fringes are sampled at 16 points per period,
so the highest sample misses the true peak S(0) = 8 by up to 4 %. The 100 fs / 1030 nm and 30 fs / 515 nm cases have an odd count (a sample at
d = 0) and give 8.001. Expected: a delay grid that contains d = 0 (odd point count), or a peak found by interpolating the fringe.

Run: .venv/bin/python tests/components/repro/iac_peak_undersampled.py
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import b, np, uf  # noqa: E402

for lam, tau in ((1030e-9, 100e-15), (1030e-9, 30e-15), (2e-6, 100e-15), (515e-9, 30e-15)):
    r = uf.run(b.system("free", [b.probe("p")]), b.run_spec(b.pulse(lam, tau, energy=1e-9),
               instruments=[b.instrument("autocorrelator", type="interferometric", name="iac")]))
    d = np.asarray(r["iac_delay_s"])
    print(f"{lam * 1e9:6.0f} nm {tau * 1e15:4.0f} fs: peakToBackground = {r.instrument('iac')['peakToBackground']:.4f} (expected 8), "
          f"{len(d)} delay points, nearest sample to d = 0: {np.min(np.abs(d)) * 1e15:.3f} fs")
