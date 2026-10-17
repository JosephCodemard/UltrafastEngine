"""Phase 2 from Python: the phase-matching calculator, crystal elements built with ``ultrafast.build``, aux beams
from saved results, user elements, spatial models, and the plotting of active-element results."""
import math

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b


def test_phase_match_calculator_literature_values():
    assert "BBO" in uf.crystal.crystals()
    bbo = uf.crystal.phase_match("BBO", "shg", fundamental=800e-9)
    assert bbo["phaseMatched"]
    assert bbo["best"]["thetaDeg"] == pytest.approx(29.2, abs=0.15)
    lbo = uf.crystal.phase_match("LBO", "shg", fundamental=1030e-9)
    assert lbo["best"]["plane"] == "XY" and lbo["best"]["phiDeg"] == pytest.approx(13.8, abs=0.15)
    nc = uf.crystal.phase_match("BBO", "opa", pump=515e-9, signal=800e-9, noncollinear="magic")
    assert nc["best"]["noncollinearDeg"] == pytest.approx(2.55, abs=0.15)
    ppln = uf.crystal.phase_match("MgO:LiNbO3", "shg", fundamental=1550e-9, qpm=True)   # type 0 by default
    assert ppln["best"]["qpmPeriodM"] == pytest.approx(19.4e-6, rel=0.01)
    assert all(w["polarization"] == "e" for w in ppln["best"]["waves"])


def _shg_system(**extra):
    return b.system("shg", [b.crystal("bbo", "BBO", 1e-3, process="shg", beam=b.gaussian_beam(200e-6), output="sh",
                                      **extra)])


def test_crystal_shg_carrier_change_and_byproduct():
    res = uf.run(_shg_system(), b.run_spec(b.pulse(1030e-9, 300e-15, energy=2e-6)))
    el = res.elements()[0]
    v = el["values"]
    assert 0.01 < v["conversionEfficiency"] < 0.9
    assert el["carrierWavelengthM"] == pytest.approx(515e-9, rel=2e-3)
    assert "fundamental" in el["byproducts"]
    assert "probe_bbo_fundamental_spectrum" in res.names()
    # energy budget closes with the by-product as a "to fundamental" entry
    eb = res.summary["energyBudget"]
    assert sum(x["energyJ"] for x in eb.values()) == pytest.approx(2e-6 - res.summary["output"]["energyJ"], rel=1e-6)
    assert res.summary["outputGrid"]["omega0"] == pytest.approx(2 * math.pi * 299792458.0 / 515e-9, rel=2e-3)


def test_aux_beam_from_saved_results(tmp_path):
    shg = uf.run(_shg_system(), b.run_spec(b.pulse(1030e-9, 500e-15, energy=50e-6)))
    shg.write(tmp_path / "pump")
    pump_e = shg.summary["output"]["energyJ"]
    opa = b.system("opa", [b.crystal("opa", "BBO", 2e-3, process="opa", overlap="planeWave",
                                     aux=[b.aux_beam("pump", from_results=str(tmp_path / "pump"), radius_m=150e-6)],
                                     beam=b.gaussian_beam(150e-6), output="signal")])
    res = uf.run(opa, b.run_spec(b.pulse(800e-9, 500e-15, energy=1e-9)))
    assert -res.summary["energyBudget"]["from pump (aux)"]["energyJ"] == pytest.approx(pump_e, rel=1e-6)
    assert res.summary["output"]["energyJ"] > 2e-9


def test_user_element_python_shaper():
    gdd = 1500e-30

    def shaper(ctx: uf.ElementContext):
        assert ctx.params["gdd"] == gdd and ctx.element_id == "dz"
        w = ctx.omega - ctx.omega0
        return ctx.ax * np.exp(-0.5j * ctx.params["gdd"] * w ** 2)

    sysd = b.system("s", [b.user("dz", "shaper", params={"gdd": gdd}), b.gap("air", 0.01)])
    spec = b.run_spec(b.pulse(800e-9, 20e-15, energy=1e-9), instruments=[b.instrument("compressor")])
    res = uf.run(sysd, spec, elements={"shaper": shaper})
    # the shaper's phase -(GDD/2) w^2 is compensated by +GDD (plus the 1 cm of air)
    assert res.instrument("compressor")["compressorGddS2"] == pytest.approx(gdd, rel=0.05)
    assert res.summary["output"]["energyJ"] == pytest.approx(1e-9, rel=1e-9)


def test_spatial_bulk_reports_beam_and_plots(tmp_path):
    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting
    sysd = b.system("free", [b.gap("g", 0.05, medium="Vacuum", beam=b.gaussian_beam(200e-6),
                                   transverse=b.transverse("radial", points=48, radius_m=1e-3))])
    spec = b.run_spec(b.pulse(1030e-9, 300e-15, energy=1e-9), instruments=[b.instrument("beamProfile", at="g")])
    res = uf.run(sysd, spec)
    zr = math.pi * (200e-6) ** 2 / 1030e-9
    assert res.elements()[0]["beam"]["radiusXM"] == pytest.approx(200e-6 * math.sqrt(1 + (0.05 / zr) ** 2), rel=0.01)
    written = plotting.report(res, tmp_path, pdf=False)
    assert "instruments" in written and "overview" in written


def test_plot_byproducts_and_signed_budget(tmp_path):
    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting
    pump = b.aux_beam("pump", b.pulse(515e-9, 400e-15, energy=20e-6), radius_m=200e-6, delay_s=0.0)
    sysd = b.system("opa", [b.crystal("opa", "BBO", 2e-3, process="opa", aux=[pump], output="signal",
                                      beam=b.gaussian_beam(200e-6))])
    spec = b.run_spec(b.pulse(800e-9, 400e-15, energy=1e-9),
                      instruments=[b.instrument("delayScan", at="opa", fromS=-0.5e-12, toS=0.5e-12, points=5),
                                   b.instrument("phaseMatchingCurve", at="opa", scan="wavelength", rangeM=100e-9)])
    res = uf.run(sysd, spec)
    assert res.summary["energyBudget"]["from pump (aux)"]["energyJ"] < 0
    written = plotting.report(res, tmp_path, pdf=True)
    assert "byproducts" in written and "instruments" in written and "report.pdf" in written


def test_system_diagram_numbers_instruments_in_system_order(tmp_path):
    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import diagram, plotting
    sysd = b.system("line", [b.bulk("glass", "FusedSilica", 5e-3), b.probe("mid"), b.gap("air", 0.1)])
    spec = b.run_spec(b.pulse(800e-9, 30e-15, energy=1e-9),
                      instruments=[b.instrument("spectrometer", at="end"), b.instrument("autocorrelator", at="input"),
                                   b.instrument("wigner", at="mid"), b.instrument("nearFieldCamera", at="glass", beam=b.gaussian_beam(1e-3))])
    res = uf.run(sysd, spec)
    locs = diagram.instrument_locations(res.summary)
    assert [d["kind"] for d in locs] == ["autocorrelator", "camera", "wigner", "spectrometer"]
    assert [d["number"] for d in locs] == [1, 2, 3, 4]
    assert locs[2]["virtual"] and not locs[0]["virtual"]
    assert locs[3]["where"].startswith("end")
    written = plotting.report(res, tmp_path, pdf=False)
    assert "system" in written
    assert diagram.wavelength_color(532e-9) != diagram.wavelength_color(1030e-9)
