"""Measured data in the core, from Python: measured input spectra (cleaning, background), instrument comparisons
(``b.measured``, ``Result.comparisons``, ``ultrafast.measured``), and the vector Kerr / Raman fixes for NER."""
import math

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b
from ultrafast import measured as um


def _probe():
    return b.system("probe", [b.probe("p")])


def _wl(lo, hi, n):
    return np.linspace(lo, hi, n) * 1e-9


def _gauss(wl, fwhm_nm, floor=0.0, center=1030.0):
    return np.exp(-4 * math.log(2) * ((wl * 1e9 - center) / fwhm_nm) ** 2) + floor


def test_builder_carries_the_cleaning_options():
    p = b.measured_spectrum([1e-6, 1.1e-6], [1, 1], background="auto", threshold=0.01)
    assert p["spectrum"]["background"] == "auto" and p["spectrum"]["threshold"] == 0.01
    q = b.measured_spectrum_csv("osa.csv", 1e-6, wavelength_unit="nm", background=0.5)
    assert q["spectrum"] == {"csv": "osa.csv", "wavelengthUnit": "nm", "background": 0.5}
    m = b.measured("osa.csv", background="auto", resolution_m=0.5e-9, match_resolution=False, x_unit="nm")
    assert m == {"file": "osa.csv", "background": "auto", "resolutionM": 0.5e-9, "matchResolution": False, "xUnit": "nm"}


def test_unsorted_spectrum_with_repeated_points_runs():
    wl = _wl(1000, 1060, 200); I = _gauss(wl, 6)
    clean = uf.run(_probe(), b.run_spec(b.measured_spectrum(wl, I, energy=1e-6)))
    w2, i2 = wl.copy(), I.copy()
    idx = np.arange(5, 180, 6)
    w2[idx], w2[idx + 1] = wl[idx + 1], wl[idx]
    i2[idx], i2[idx + 1] = I[idx + 1], I[idx]
    w2[190] = w2[191]
    res = uf.run(_probe(), b.run_spec(b.measured_spectrum(w2, i2, energy=1e-6)))
    assert math.isfinite(res.summary["output"]["energyJ"])
    assert res.summary["output"]["transformLimitFwhmS"] == pytest.approx(clean.summary["output"]["transformLimitFwhmS"], rel=3e-3)
    notes = " ".join(res.summary.get("notes", []))
    assert "out of wavelength order" in notes and "repeated a wavelength" in notes


def test_background_restores_the_transform_limit():
    wl = _wl(950, 1110, 1200)
    clean = uf.run(_probe(), b.run_spec(b.measured_spectrum(wl, _gauss(wl, 6.5), energy=1e-6)))
    floor = uf.run(_probe(), b.run_spec(b.measured_spectrum(wl, _gauss(wl, 6.5, 0.0066), energy=1e-6)))
    auto = uf.run(_probe(), b.run_spec(b.measured_spectrum(wl, _gauss(wl, 6.5, 0.0066), energy=1e-6, background="auto")))
    tl = lambda r: r.summary["output"]["transformLimitFwhmS"]
    assert tl(floor) < 0.6 * tl(clean)
    assert any("floor of" in w for w in floor.summary.get("warnings", []))
    assert tl(auto) == pytest.approx(tl(clean), rel=5e-3)


def test_comparison_accessors_and_partial_range(tmp_path):
    pulse = b.pulse(1.03e-6, 200e-15, energy=1e-9)
    ax = b.axis(start=1020e-9, stop=1040e-9, step=0.05e-9)
    ref = uf.run(_probe(), b.run_spec(pulse, instruments=[b.instrument("spectrometer", name="osa", axis=ax)]))
    lam, s = ref["osa_wavelength_m"], ref["osa_spectrum"]
    o = np.argsort(lam); lam, s = lam[o], s[o]
    x = np.arange(1000, 1060.001, 0.05)
    y = np.where((x >= 1020) & (x <= 1040), np.interp(x * 1e-9, lam, s, left=0, right=0), 0.0) * 5 + 0.3
    np.savetxt(tmp_path / "osa.csv", np.c_[x, y], delimiter=",", header="wavelength_nm,counts", comments="")
    res = uf.run(_probe(), b.run_spec(pulse, instruments=[
        b.instrument("spectrometer", name="osa", axis=ax, measured=b.measured(str(tmp_path / "osa.csv"), background="auto")),
        b.instrument("energyMeter", name="pm", measured=b.measured(values={"energyJ": [1.1e-9, 0.02e-9]}))]))
    assert {c["instrument"] for c in res.comparisons} == {"osa", "pm"}
    c = res.comparison("osa")
    assert c["mode"] == "curve" and c["rmsError"] < 2e-3
    assert any("outside the simulated instrument's range" in n for n in c["notes"])
    curve = um.curve(res, "osa")
    x, sim = curve["measured_x"], curve["simulated_on_measured"]
    assert x.shape == sim.shape and np.all(np.isnan(sim[x < 1019.99e-9])) and np.all(np.isfinite(sim[(x > 1020.01e-9) & (x < 1039.99e-9)]))
    assert res.comparison("pm")["values"]["energyJ"]["deviation"] < 0
    assert um.total_chi2(res) == pytest.approx(sum(cc["chi2"] for cc in res.comparisons))
    with pytest.raises(KeyError):
        res.comparison("nothing")
    # the same through a written results folder
    saved = uf.load(res.write(tmp_path / "out"))
    assert saved.comparison("osa")["points"] == c["points"]
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting
    assert plotting.measurements(saved) is not None
    assert "measurements" in plotting.FIGURES


def test_fitting_plugin_aliases_still_work():
    fx = uf.plugins.module("fitting")
    if fx is None:
        pytest.skip("fitting plugin not installed")
    assert fx.measured is b.measured
    assert fx.measured("a.csv", sigma=0.01) == {"file": "a.csv", "sigma": 0.01}


def _ner(chi, energy, raman=None, fr=0.0):
    mat = b.material(index={"kind": "constant", "value": 1.45}, n2=3e-20)
    if raman is not None:
        mat["raman"] = raman; mat["ramanFraction"] = fr
    sysd = b.system("ner", [b.bulk("plate", "Kerr", 5e-3, beam=b.constant_beam(radius_m=100e-6))], materials={"Kerr": mat})
    pulse = b.pulse(1.03e-6, 1e-12, energy=energy)
    pulse["polarization"] = {"angleDeg": 0, "ellipticity": math.tan(chi)}
    return uf.run(sysd, b.run_spec(pulse, physics={"selfSteepening": False}, grid={"auto": False, "n": 4096, "windowS": 12e-12}))


def test_circular_b_integral_is_two_thirds_of_linear():
    bl = _ner(0, 2e-6).summary["totals"]["bIntegralRad"]
    bc = _ner(math.pi / 4, 2e-6).summary["totals"]["bIntegralRad"]
    assert bc / bl == pytest.approx(2 / 3, rel=5e-3)
