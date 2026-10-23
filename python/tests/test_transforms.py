"""Stage 2 from Python: transform elements, fits of their parameters (estimators, priors, layers, influence map,
fitted system applied to a new input), backward propagation and the plugin figures."""
import json

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b

fx = uf.plugins.module("fitting")
if fx is None:
    pytest.skip("fitting plugin not installed", allow_module_level=True)


def _pulse(energy=1e-6):
    return b.pulse(1030e-9, 100e-15, energy=energy)


def _filter_system(fwhm, center):
    return b.system("filter", [fx.transform("filter", [fx.spectral_amplitude("gaussian", fwhm_m=fwhm, center_wavelength_m=center)])])


@pytest.fixture(scope="module")
def spectrum_file(tmp_path_factory):
    d = tmp_path_factory.mktemp("tr")
    truth = uf.run(_filter_system(8e-9, 1034e-9), b.run_spec(_pulse(), instruments=[b.instrument("spectrometer", name="spec")]))
    lam, s = truth["spec_wavelength_m"], truth["spec_spectrum"]
    np.savetxt(d / "spec.csv", np.c_[lam[::4] * 1e9, s[::4]], delimiter=",", header="wavelength_nm,intensity", comments="")
    return d


def test_builders_produce_engine_json():
    t = fx.transform("shaper", [fx.spectral_phase(gdd_fs2=fx.fit_value(0, -1000, 1000)), fx.replica(5e-12, 1e-4),
                                fx.wavefront({4: 0.1}), fx.jones(rotation_deg=3)])
    assert t["type"] == "transform" and t["operators"][0]["gddFs2"] == {"value": 0, "fit": True, "min": -1000, "max": 1000}
    assert t["operators"][2]["zernike"] == {"4": 0.1}
    assert fx.fit(estimator="mcmc", walkers=8)["walkers"] == 8
    assert fx.backward("end", "input", active="identity") == {"from": "end", "to": "input", "active": "identity"}
    assert b.axis(start=1e-6, stop=1.1e-6, step=1e-9, bin_width="pixel")["binWidth"] == "pixel"
    assert b.measured_spectrum([1e-6, 1.1e-6], [1, 1], resolution_m=1e-9)["spectrum"]["resolutionM"] == 1e-9


def test_transform_runs_and_validates():
    sysd = b.system("t", [fx.transform("t", [fx.spectral_phase(gdd_fs2=20000), fx.energy(0.5)])])
    res = uf.run(sysd, b.run_spec(_pulse()))
    assert res.summary["output"]["energyJ"] == pytest.approx(0.5e-6, rel=1e-6)
    assert res.summary["output"]["fwhmS"] == pytest.approx(4 * np.log(2) * 20000e-30 / 100e-15, rel=0.03)


def test_fit_marked_transform_parameters(spectrum_file, tmp_path):
    d = spectrum_file
    sysd = _filter_system(fx.fit_value(12e-9, 2e-9, 30e-9), fx.fit_value(1030e-9, 1020e-9, 1040e-9))
    spec = b.run_spec(_pulse(), instruments=[b.instrument("spectrometer", name="spec", measured=fx.measured(str(d / "spec.csv"), sigma=0.01))],
                      fit=fx.fit())
    res = uf.run(sysd, spec)
    f = fx.fit_report(res)
    vals = {p["name"].split(".")[-1]: p["value"] for p in f["parameters"]}
    assert vals["fwhmM"] == pytest.approx(8e-9, rel=0.01)
    assert vals["centerWavelengthM"] == pytest.approx(1034e-9, rel=1e-4)
    assert set(f["layers"]) == {"simulated", "inferred", "believed"}
    assert f["influence"][f["parameters"][0]["name"]]["spec"] == pytest.approx(1.0)
    fitted = fx.fitted_system(res)
    op = fitted["elements"][0]["operators"][0]
    assert isinstance(op["fwhmM"], float) and op["fwhmM"] == pytest.approx(8e-9, rel=0.01)
    new = uf.run(fitted, b.run_spec(b.pulse(1030e-9, 50e-15, energy=1e-6)))
    assert new.summary["output"]["energyJ"] < 0.6e-6

    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting as up
    out = res.write(tmp_path / "fit")
    loaded = up.load(out)
    assert up._EXTRA_FIGURES["fit"](loaded) is not None
    assert up._EXTRA_FIGURES["measurements"](loaded) is not None


def test_backward_recovers_the_input():
    sysd = b.system("line", [b.bulk("glass", "FusedSilica", 0.02, nonlinear=False),
                             fx.transform("shaper", [fx.spectral_phase(gdd_fs2=-3000, tod_fs3=2e4)])])
    fw = uf.run(sysd, b.run_spec(b.pulse(1030e-9, 60e-15, energy=1e-6), grid={"auto": False, "n": 4096, "windowS": 20e-12}))
    out_pulse = {"shape": "spectrum", "energyJ": fw.summary["output"]["energyJ"],
                 "spectrum": {"wavelengthsM": list(fw["output_wavelength_m"]), "intensity": list(fw["output_spectrum"] * fw["output_wavelength_m"] ** -2),
                              "phaseRad": list(fw["output_phase_x_rad"])}}
    bw = uf.run(sysd, b.run_spec(out_pulse, grid={"auto": False, "n": 4096, "windowS": 20e-12},
                                 instruments=[b.instrument("pulseSummary", at="input", name="inp")], backward=fx.backward()))
    assert bw.summary["backward"]["to"] == "input"
    assert bw.instrument("inp")["fwhmS"] == pytest.approx(60e-15, rel=0.03)
    rp = fx.reconstructed_pulse(bw)
    assert rp["shape"] == "spectrum" and rp["energyJ"] == pytest.approx(1e-6, rel=0.01)
