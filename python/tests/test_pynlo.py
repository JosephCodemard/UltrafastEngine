"""Cross-check against pyNLO (independent split-step NLSE implementation).

pyNLO is optional: install it, or point PYNLO_SRC at a source checkout's ``src`` folder
(``python tools/get_pynlo.py`` fetches the pinned commit). The compatibility shims below make the
(unmaintained) Python-3 branch run on current NumPy/SciPy.
"""
import math
import os
import sys

import numpy as np
import pytest

from conftest import import_pynlo, requires_engine

pytestmark = requires_engine


def _import_pynlo():
    return import_pynlo()


import ultrafast as uf
from ultrafast import build as b

C = 299792458.0
T0, E, LAM, L, GAMMA, B2, B3 = 28.4e-15, 50e-12, 1550e-9, 0.008, 1.0, -120e-27, 0.005e-39  # N ~ 2.4 soliton, ~1.2 L_D


def _run_both(raman: bool):
    pynlo = _import_pynlo()
    pulse = pynlo.light.DerivedPulses.SechPulse(power=1, T0_ps=T0 * 1e12, center_wavelength_nm=LAM * 1e9, time_window_ps=10,
                                                GDD=0, TOD=0, NPTS=2 ** 12, frep_MHz=100, power_is_avg=False)
    pulse.set_epp(E)
    fiber = pynlo.media.fibers.fiber.FiberInstance()
    fiber.generate_fiber(L, center_wl_nm=LAM * 1e9, betas=(B2 * 1e27, B3 * 1e39), gamma_W_m=GAMMA, gvd_units="ps^n/km", gain=0)
    evol = pynlo.interactions.FourWaveMixing.SSFM.SSFM(local_error=0.0005, USE_SIMPLE_RAMAN=True, disable_Raman=not raman,
                                                       disable_self_steepening=False, suppress_iteration=True)
    _, _, _, pout = evol.propagate(pulse_in=pulse, fiber=fiber, n_steps=10)

    # the same physics in UltrafastEngine: a bulk medium with a Taylor-series index and gamma = n2 w0 / (c Aeff)
    w0, area = 2 * math.pi * C / LAM, 1e-11
    mat = {"index": {"kind": "expression", "expr": "c*(k0 + k1*(omega-w0) + 0.5*b2*(omega-w0)^2 + b3*(omega-w0)^3/6)/omega",
                     "params": {"k0": 1.45 * w0 / C, "k1": 1.45 / C, "b2": B2, "b3": B3, "w0": w0}},
           "n2": GAMMA * C * area / w0}
    if raman:
        mat["raman"] = {"kind": "silica"}  # Blow-Wood damped oscillator, fR = 0.18 (pyNLO's "simple" Raman)
    sysd = b.system("fiber", [b.bulk("f", "fib", L, beam=b.constant_beam(area_m2=area))], materials={"fib": mat})
    spec = b.run_spec(b.pulse(LAM, 1.7627 * T0, energy=E, shape="sech2"),
                      grid={"n": 4096, "windowS": 10e-12, "auto": False, "snapCarrier": False},
                      physics={"selfSteepening": True, "raman": raman}, stepper={"tolerance": 1e-8})
    return pout, uf.run(sysd, spec)


def _l1(x_ref, y_ref, x, y):
    o = np.argsort(x_ref)
    yi = np.interp(x, x_ref[o], y_ref[o])
    return np.abs(yi / yi.max() - y / y.max()).sum() / (y / y.max()).sum()


@pytest.mark.parametrize("raman,tol", [(False, 0.005), (True, 0.02)])
def test_higher_order_soliton_matches_pynlo(raman, tol):
    pout, res = _run_both(raman)
    t, p = res["output_time_s"], res["output_power_w"]
    assert _l1(pout.T_ps * 1e-12, np.abs(pout.AT) ** 2, t, p) < tol, "temporal profile"
    assert _l1(pout.F_THz * 1e12, np.abs(pout.AW) ** 2, res["output_omega_rad_s"] / (2 * math.pi), res["output_spectrum"]) < tol, "spectrum"
    assert res.summary["output"]["energyJ"] == pytest.approx(pout.calc_epp(), rel=tol / 5)
    assert p.max() == pytest.approx((np.abs(pout.AT) ** 2).max(), rel=tol)
