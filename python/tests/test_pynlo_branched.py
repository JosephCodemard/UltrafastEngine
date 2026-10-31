"""Branched systems against pyNLO: pyNLO has no splitters, so each arm is its own NLSE calculation at that arm's energy. That is a
reference knowing nothing of the graph: the split before a fibre, the split after it, and two equal arms recombining."""
import math

import numpy as np
import pytest

from conftest import import_pynlo, requires_engine

import ultrafast as uf
from ultrafast import build as b

pytestmark = requires_engine

C = 299792458.0
T0, E, LAM, FIBER, GAMMA, B2, B3 = 28.4e-15, 50e-12, 1550e-9, 0.008, 1.0, -120e-27, 0.005e-39


def _pynlo_arm(energy):
    pynlo = import_pynlo()
    pulse = pynlo.light.DerivedPulses.SechPulse(power=1, T0_ps=T0 * 1e12, center_wavelength_nm=LAM * 1e9, time_window_ps=10,
                                                GDD=0, TOD=0, NPTS=2 ** 12, frep_MHz=100, power_is_avg=False)
    pulse.set_epp(energy)
    fiber = pynlo.media.fibers.fiber.FiberInstance()
    fiber.generate_fiber(FIBER, center_wl_nm=LAM * 1e9, betas=(B2 * 1e27, B3 * 1e39), gamma_W_m=GAMMA, gvd_units="ps^n/km", gain=0)
    evol = pynlo.interactions.FourWaveMixing.SSFM.SSFM(local_error=0.0005, USE_SIMPLE_RAMAN=True, disable_Raman=True, disable_self_steepening=False, suppress_iteration=True)
    return evol.propagate(pulse_in=pulse, fiber=fiber, n_steps=10)[3]


def _fibre():
    return b.fiber("f", FIBER, gamma=GAMMA, betas_ps_km=[B2 * 1e27, B3 * 1e39], raman="none")


def _run(system):
    grid = {"n": 4096, "windowS": 10e-12, "auto": False, "snapCarrier": False}
    spec = b.run_spec(b.pulse(LAM, 1.7627 * T0, energy=E, shape="sech2"), grid=grid, physics={"selfSteepening": True, "raman": False}, stepper={"tolerance": 1e-8})
    return uf.run(system, spec)


def _same_shape(res, pout, scale, tol):
    t, p = res["output_time_s"], res["output_power_w"]
    o = np.argsort(pout.T_ps)
    ref = scale * np.interp(t, pout.T_ps[o] * 1e-12, (np.abs(pout.AT) ** 2)[o])
    assert np.abs(ref - p).sum() / p.sum() < tol
    assert res.summary["graph"]["energyOutJ"] == pytest.approx(scale * pout.calc_epp(), rel=tol)


def test_a_fibre_after_a_splitter_sees_the_energy_of_its_arm():
    # 25 % reflected away: the higher-order soliton at 3/4 of the energy is a different pulse, not a scaled copy
    _same_shape(_run(b.system("arm", b.Branched([b.link(b.beam_splitter("bs", 0.25), {"c": "f", "d": "virtual"}), b.link(_fibre(), "end")]))), _pynlo_arm(0.75 * E), 1.0, 0.005)


def test_a_splitter_after_a_fibre_halves_power_and_keeps_the_shape():
    _same_shape(_run(b.system("out", b.Branched([b.link(_fibre(), "bs"), b.link(b.beam_splitter("bs", 0.5), {"c": "end", "d": "virtual"})]))), _pynlo_arm(E), 0.5, 0.005)


def test_a_mach_zehnder_with_fibres_in_both_arms_recombines_the_output():
    # equal arms: the same nonlinear pulse in each, at half the energy, and all of it leaves by port d: pyNLO at E / 2, doubled
    s = b.system("mz", b.Branched([
        b.link(b.beam_splitter("bs1"), {"c": "a", "d": "b_"}),
        b.link(dict(_fibre(), id="a"), "bs2:1"), b.link(dict(_fibre(), id="b_"), "bs2:2"),
        b.link(b.beam_splitter("bs2"), {"c": "virtual", "d": "end"})]))
    _same_shape(_run(s), _pynlo_arm(0.5 * E), 2.0, 0.005)
