"""Cross-check of the chi(2) crystal element against pyNLO's three-wave-mixing (DFG/OPA) solver.

pyNLO (``pynlo.interactions.ThreeWaveMixing.dfg_problem``) integrates Boyd's coupled-amplitude equations for
pump, signal and idler pulses in a periodically poled crystal, with the sign of d resolved domain by domain and every
field coupled at the on-axis intensity of the pump waist (plane-wave coupling). The equivalent UltrafastEngine set-up:

* crystal ``MgO:LiNbO3`` at 24.5 C (pyNLO's PPLN uses the same Gayer et al. 5 % MgO Sellmeier; at 24.5 C the
  temperature term vanishes, so the indices are identical),
* ``deffPmPerV`` = 14.9 (pyNLO's value; first-order QPM then gives 2/pi of it),
* ``qpm`` = {"periodM": pyNLO's period, "model": "domains", "phaseDeg": 90} -- the same domain pattern
  (a full positive domain first),
* ``overlap`` = {"model": "planeWave", "radiusM": pump waist}, ``chi3`` off (pyNLO: ``disable_SPM=True``).

pyNLO designs the period with a deliberate detuning (Delta k L = 3.2), so the comparison also tests the mismatch
handling. Agreement: 0.1-0.7 % in the energies in the small-signal regime and ~0.5 % at a gain of ~50 with 35 % pump
depletion (where back-conversion broadens the spectra: the engine is given spectralMargin 5), and the idler spectra
overlay.
"""
import numpy as np
import pytest

from conftest import import_pynlo, requires_engine, slow

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b

PUMP_NM, SIGNAL_NM, WAIST = 1064.0, 1550.0, 100e-6
SIGNAL_EPP = 10e-9
FWHM = 1e-12 * np.sqrt(4 * np.log(2) / 2.77)  # pyNLO's GaussianPulse uses exp(-2.77 t^2 / T0^2) for |A|^2


def _pynlo(length, pump_epp, npts=512, window_ps=20.0, tol=1e-8):
    pynlo = import_pynlo()
    from pynlo.interactions.ThreeWaveMixing import dfg_problem
    from pynlo.light.DerivedPulses import GaussianPulse
    from pynlo.media.crystals.XTAL_PPLN import PPLN
    from pynlo.util import ode_solve
    from pynlo.util.ode_solve import dopr853
    import contextlib, io  # noqa: E401
    with contextlib.redirect_stdout(io.StringIO()):  # pyNLO prints progress
        crystal = PPLN(24.5, length=length)
        frep = 1.0  # MHz: pulse energy = average power / 1e6
        pump = GaussianPulse(power=pump_epp * 1e6, T0_ps=1.0, center_wavelength_nm=PUMP_NM, time_window_ps=window_ps,
                             NPTS=npts, frep_MHz=frep, power_is_avg=True)
        sig = GaussianPulse(power=SIGNAL_EPP * 1e6, T0_ps=1.0, center_wavelength_nm=SIGNAL_NM, time_window_ps=window_ps,
                            NPTS=npts, frep_MHz=frep, power_is_avg=True)
        period = crystal.calculate_poling_period(PUMP_NM, SIGNAL_NM, None)
        crystal.set_pp(period)
        problem = dfg_problem(pump, sig, crystal, disable_SPM=True, pump_waist=WAIST)
        out = ode_solve.Output(2)
        solver = ode_solve.ODEint(problem.ystart, 0.0, length, tol, tol, 1e-6, 0.0, out, dopr853.StepperDopr853, problem)
        solver.integrate()
        res = problem.process_stepper_output(solver.out)
    last = solver.out.count - 1  # Output(n) saves n + 1 points, z = 0 ... L
    idler = res.get_idlr(last)
    spec = dict(wl=np.array(idler.wl_nm) * 1e-9, s=np.abs(np.array(idler.AW)) ** 2)
    return dict(period=float(period[0] if isinstance(period, tuple) else period), idler=idler.calc_epp(), signal=res.get_sgnl(last).calc_epp(),
                pump=res.get_pump(last).calc_epp(), idler_spectrum=spec)


def _engine(length, pump_epp, period):
    pump = b.pulse(PUMP_NM * 1e-9, FWHM, energy=pump_epp, shape="gaussian")
    el = b.crystal("ppln", "MgO:LiNbO3", length, process="opa", temperature_c=24.5, deff_pm_per_v=14.9,
                   qpm={"periodM": period, "model": "domains", "phaseDeg": 90},
                   aux=[b.aux_beam("pump", pump, radius_m=WAIST, delay_s=0.0)], output="signal", record=["idler", "pump"],
                   overlap={"model": "planeWave", "radiusM": WAIST}, chi3=False, spectralMargin=5)
    res = uf.run(b.system("dfg", [el]), b.run_spec(b.pulse(SIGNAL_NM * 1e-9, FWHM, energy=SIGNAL_EPP, shape="gaussian")))
    rec = next(e for e in res.elements() if e["id"] == "ppln")
    return res, rec["values"]


def _spectrum_l1(ref, wl, s):
    o = np.argsort(ref["wl"])
    r = np.interp(wl, ref["wl"][o], ref["s"][o], left=0, right=0)
    return np.abs(r / r.max() - s / s.max()).sum() / (s / s.max()).sum()


def test_dfg_small_signal_matches_pynlo():
    ref = _pynlo(1e-3, 0.05e-6)
    res, v = _engine(1e-3, 0.05e-6, ref["period"])
    assert v["energy_idler_J"] == pytest.approx(ref["idler"], rel=0.015)
    assert v["energy_signal_J"] - SIGNAL_EPP == pytest.approx(ref["signal"] - SIGNAL_EPP, rel=0.03), "signal gain"
    assert v["energy_pump_J"] == pytest.approx(ref["pump"], rel=1e-3)
    assert abs(v["manleyRoweResidual"]) < 1e-5
    assert _spectrum_l1(ref["idler_spectrum"], res["probe_ppln_idler_wavelength_m"], res["probe_ppln_idler_spectrum"]) < 0.05


@slow
def test_dfg_high_gain_matches_pynlo():
    """Signal gain ~50, pump depletion ~35 %."""
    ref = _pynlo(5e-3, 2e-6)
    res, v = _engine(5e-3, 2e-6, ref["period"])
    assert ref["signal"] / SIGNAL_EPP > 40
    assert v["energy_idler_J"] == pytest.approx(ref["idler"], rel=0.02)
    assert v["energy_signal_J"] == pytest.approx(ref["signal"], rel=0.02)
    assert v["energy_pump_J"] == pytest.approx(ref["pump"], rel=0.01)
    # back-conversion with group-velocity mismatch structures the spectra (+-3 THz side lobes at 1e-2..0.7 of
    # the peak in both codes); the idler spectra agree in shape
    l1 = _spectrum_l1(ref["idler_spectrum"], res["probe_ppln_idler_wavelength_m"], res["probe_ppln_idler_spectrum"])
    assert l1 < 0.2, l1
