"""Repro: pure SPM of a 10 fs, 2 um Gaussian in a dispersion-free fibre (B = 3) changes |A(t)|^2 and loses energy.

Expected: A_out = A_in exp(i gamma |A_in|^2 L): |A|^2 unchanged, energy unchanged. Observed (auto grid, n = 512):
the power profile changes by 6 % of the peak and the energy drops by ~6e-7 (B = 1: 0.5 %, B = 0.3: 0.05 %, so ~ B^2).
1030 nm and 515 nm at the same 10 fs change the profile by 5e-4 / 2e-6 only. Guess: the auto grid is centred on the carrier and
spans omega < 0 at 2 um (omega0 = 0.94e15 rad/s, half-span 3.8e15), so the SPM-broadened spectrum / nonlinear products meet the omega = 0 edge.
Run: python tests/components/repro/fiber_spm_2um_10fs.py
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import b, np, uf  # noqa: E402

for lam in (1030e-9, 2e-6):
    tau, p0, L = 10e-15, 1e3, 1.0
    e = p0 * tau / (2 * math.sqrt(math.log(2) / math.pi))
    r = uf.run(b.system("s", [b.fiber("f", L, betas=[0.0], gamma=3e-3, raman="none")]),
               b.run_spec(b.pulse(lam, tau, energy=e), physics={"selfSteepening": False}))
    pin, pout = np.asarray(r["input_power_w"]), np.asarray(r["output_power_w"])
    el = r.summary["elements"][0]
    print(f"{lam * 1e9:.0f} nm: max|P_out - P_in|/P_peak = {np.max(np.abs(pout - pin)) / pin.max():.2e}, "
          f"energy out/in - 1 = {el['energyOutJ'] / el['energyInJ'] - 1:.2e}, grid n = {len(pin)}")
