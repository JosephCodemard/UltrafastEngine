"""Phase 2.5 from Python: sampling, measured-data comparison, the calibration fit and their figures."""
import json

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b

fitting = uf.plugins.module("fitting")
if fitting is None:
    pytest.skip("fitting plugin not installed", allow_module_level=True)
measured, fit, fit_parameter = fitting.measured, fitting.fit, fitting.fit_parameter


def _plate():
    return b.system("plate", [b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(w0_m=60e-6, z_waist_m=2.5e-3)),
                              b.gap("air", 0.05)])


def _pulse(energy, gdd_fs2=0.0):
    return b.pulse(800e-9, 40e-15, energy=energy, gdd_s2=gdd_fs2 * 1e-30, rep_rate_hz=1e3)


@pytest.fixture(scope="module")
def recordings(tmp_path_factory):
    """Spectrum (CSV, nm), autocorrelation (CSV, ps) and a power reading (JSON) simulated at 260 nJ, +120 fs^2."""
    d = tmp_path_factory.mktemp("data")
    truth = uf.run(_plate(), b.run_spec(_pulse(260e-9, 120), instruments=[
        b.instrument("spectrometer", name="s"), b.instrument("autocorrelator", name="ac")]))
    lam, s = truth["s_wavelength_m"], truth["s_spectrum"]
    m = (lam > 740e-9) & (lam < 860e-9)
    np.savetxt(d / "spectrum.csv", np.c_[lam[m] * 1e9, 1000 * s[m] / s.max() + 20], delimiter=",",
               header="wavelength_nm,counts", comments="")
    t, a = truth["ac_delay_s"], truth["ac_signal"]
    m = np.abs(t) < 0.3e-12
    np.savetxt(d / "ac.csv", np.c_[t[m] * 1e12, a[m]], delimiter=",", header="delay_ps,signal", comments="")
    (d / "power.json").write_text(json.dumps({"energyJ": truth.summary["output"]["energyJ"]}))
    return d, truth


def _instruments(d):
    return [b.instrument("spectrometer", name="s", measured=measured(str(d / "spectrum.csv"), background=20, sigma=0.005)),
            b.instrument("autocorrelator", name="ac", measured=measured(str(d / "ac.csv"), sigma=0.01)),
            b.instrument("powerMeter", name="pm", measured=measured(str(d / "power.json"), value_sigma=0.03)),
            b.instrument("pulseSummary", name="summary")]


def test_sampling_helper_and_transmission():
    spec = b.run_spec(_pulse(1e-9), instruments=[
        b.instrument("spectrometer", at="plate", name="whole"),
        b.instrument("spectrometer", at="plate", name="fibre", sampling=b.sampling("far", "fibre", radius_m=10e-6, focal_length_m=0.1))])
    res = uf.run(_plate(), spec)
    fib = res.instrument("fibre")
    assert 0 < fib["samplingTransmission"] < 1
    assert "far field" in fib["sampling"]
    assert fib["centroidWavelengthM"] < res.instrument("whole")["centroidWavelengthM"]


def test_comparison_reports_chi2_and_arrays(recordings):
    d, _ = recordings
    res = uf.run(_plate(), b.run_spec(_pulse(200e-9), instruments=_instruments(d)))
    comps = {c["instrument"]: c for c in res.summary["comparisons"]}
    assert set(comps) == {"s", "ac", "pm"}
    assert comps["s"]["mode"] == "curve" and comps["pm"]["mode"] == "values"
    assert comps["pm"]["values"]["energyJ"]["deviation"] < 0          # 200 nJ simulated vs ~260 nJ measured
    assert res.summary["measuredChi2"] == pytest.approx(sum(c["chi2"] for c in comps.values()))
    x, y, sim = res["s_measured_x"], res["s_measured_y"], res["s_simulated_on_measured"]
    assert x.shape == y.shape == sim.shape and x.min() > 700e-9   # converted from nm to m


def test_fit_recovers_the_recorded_values(recordings, tmp_path):
    d, truth = recordings
    spec = b.run_spec(_pulse(200e-9), instruments=_instruments(d), fit=fit([
        fit_parameter("pulse/energyJ", 50e-9, 500e-9, name="energy", unit="J"),
        fit_parameter("pulse/chirp/gddS2", -400e-30, 400e-30, name="chirp", initial=0.0)]))
    res = uf.run(_plate(), spec)
    fitd = res.summary["fit"]
    p = {q["name"]: q for q in fitd["parameters"]}
    assert fitd["converged"]
    assert p["energy"]["value"] == pytest.approx(260e-9, rel=0.01)
    assert p["chirp"]["value"] == pytest.approx(120e-30, abs=4e-30)
    assert p["energy"]["sigma"] > 0
    assert "summary.fwhmS" in fitd["predictions"]
    assert res.instrument("summary")["fwhmSSigma"] > 0
    assert fitd["chi2Final"] < 0.01 * fitd["chi2Initial"]
    out = res.write(tmp_path / "fitres")
    assert (out / "fit.json").exists()

    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting as up
    loaded = up.load(out)
    assert up.measurements(loaded) is not None
    assert up._EXTRA_FIGURES["fit"](loaded) is not None
    files = up.report(loaded, tmp_path / "plots", formats=("png",))
    assert "measurements" in files and "fit" in files


def test_figures_skip_runs_without_data():
    pytest.importorskip("matplotlib")
    from ultrafast import plotting as up
    res = uf.run(_plate(), b.run_spec(_pulse(1e-9), instruments=[b.instrument("spectrometer")]))
    assert up.measurements(res) is None
    assert up._EXTRA_FIGURES["fit"](res) is None
