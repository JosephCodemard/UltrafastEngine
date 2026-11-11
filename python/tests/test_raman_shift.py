"""Raman soliton self-frequency shift (WP7): the engine against pyNLO and against the exact-gain rate of a fixed-width soliton.

Gordon's -8 T_R |beta2| / (15 T0^4) uses the first moment of the Raman response and is 15-40 % low for 40-65 fs solitons; the rate computed with the
full Blow-Wood response (same physics, no first-moment approximation) is what the engine must reproduce. See docs/examples/Fibres/raman-soliton-shift.
"""
import numpy as np
import pytest

from conftest import import_pynlo, requires_engine, slow

import ultrafast as uf
from ultrafast import build as b

LAM, B2, GAMMA = 1550e-9, -20e-27, 1.3e-3
TAU1, TAU2, FR = 12.2e-15, 32e-15, 0.18
T0 = 50e-15


def _exact_rate(t0):
    """-gamma f_R / E * integral (h_R * P) P' dt, rad/s per m (see raman_shift.exact_rate)."""
    dt = 0.25e-15
    t = np.arange(-4e-12, 4e-12, dt)
    ts = np.arange(0, 2e-12, dt)
    h = (TAU1 ** 2 + TAU2 ** 2) / (TAU1 * TAU2 ** 2) * np.exp(-ts / TAU2) * np.sin(ts / TAU1)
    p = abs(B2) / (GAMMA * t0 ** 2) / np.cosh(t / t0) ** 2
    f = np.convolve(p, h * dt)[:len(p)]
    return -GAMMA * FR * abs(np.trapezoid(f * np.gradient(p, dt), t)) / np.trapezoid(p, t)


def test_exact_rate_reduces_to_gordon_for_a_narrow_response():
    """The same response with tau1 and tau2 scaled by 1/20 (T_R / 20, a memory far shorter than the soliton): the exact rate equals Gordon's formula to 1 %."""
    dt = 0.1e-15
    t = np.arange(-1.5e-12, 1.5e-12, dt)
    ts = np.arange(0, 0.2e-12, dt)
    t1, t2 = TAU1 / 20, TAU2 / 20
    h = (t1 ** 2 + t2 ** 2) / (t1 * t2 ** 2) * np.exp(-ts / t2) * np.sin(ts / t1)
    tr = FR * np.trapezoid(ts * h, ts) / np.trapezoid(h, ts)
    p = abs(B2) / (GAMMA * T0 ** 2) / np.cosh(t / T0) ** 2
    f = np.convolve(p, h * dt / np.trapezoid(h, ts))[:len(p)]
    rate = GAMMA * FR * abs(np.trapezoid(f * np.gradient(p, dt), t)) / np.trapezoid(p, t)
    assert rate == pytest.approx(8 * tr * abs(B2) / (15 * T0 ** 4), rel=0.01)


def _engine_centroid(length):
    fib = {"id": "fibre", "type": "fiber", "lengthM": length, "betas": [B2], "gamma": GAMMA, "raman": "silica", "referenceWavelengthM": LAM}
    pulse = b.pulse(LAM, 1.763 * T0, peak_power_w=abs(B2) / (GAMMA * T0 ** 2), shape="sech2")
    r = uf.run(b.system("raman", [fib]), b.run_spec(pulse, physics={"selfSteepening": False, "raman": True}, grid={"auto": False, "n": 8192, "windowS": 20e-12}))
    w, s = r["output_omega_rad_s"], r["output_spectrum"]
    return float(np.sum(w * s) / np.sum(s))


def _pynlo_centroid(pynlo, length):
    p0 = abs(B2) / (GAMMA * T0 ** 2)
    pulse = pynlo.light.DerivedPulses.SechPulse(power=1, T0_ps=T0 * 1e12, center_wavelength_nm=LAM * 1e9, time_window_ps=20, GDD=0, TOD=0, NPTS=2 ** 14, frep_MHz=100, power_is_avg=False)
    pulse.set_epp(2 * p0 * T0)
    fiber = pynlo.media.fibers.fiber.FiberInstance()
    fiber.generate_fiber(length, center_wl_nm=LAM * 1e9, betas=(B2 * 1e27,), gamma_W_m=GAMMA, gvd_units="ps^n/km", gain=0)
    evol = pynlo.interactions.FourWaveMixing.SSFM.SSFM(local_error=0.0005, USE_SIMPLE_RAMAN=True, disable_Raman=False, disable_self_steepening=True, suppress_iteration=True)
    _, _, _, pout = evol.propagate(pulse_in=pulse, fiber=fiber, n_steps=60)
    f, s = pout.F_THz * 1e12, np.abs(pout.AW) ** 2
    return 2 * np.pi * float(np.sum(f * s) / np.sum(s))


@requires_engine
@slow
def test_raman_soliton_shift_matches_pynlo_and_the_exact_gain_rate():
    """T0 = 50 fs, N = 1, 3 to 6 m: engine rate = pyNLO rate (1 %) = exact-gain rate of a fixed-width soliton (3 %), and Gordon's first-moment rate is 20-30 % low."""
    pynlo = import_pynlo()
    eng = (_engine_centroid(6.0) - _engine_centroid(3.0)) / 3.0
    ref = (_pynlo_centroid(pynlo, 6.0) - _pynlo_centroid(pynlo, 3.0)) / 3.0
    exact = _exact_rate(T0)
    assert eng == pytest.approx(ref, rel=0.01)
    assert eng == pytest.approx(exact, rel=0.03)
    tr = 1.462e-15
    assert eng / (-8 * tr * abs(B2) / (15 * T0 ** 4)) > 1.2
