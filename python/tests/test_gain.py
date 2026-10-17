"""Phase 2.2 from Python: position-dependent properties with Python field callbacks, gain media and amplifiers
built with ``ultrafast.build``, element arrays in the results, and the gain figure."""
import math

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b


def _absorber(profile):
    return b.system("abs", [b.bulk("a", "FusedSilica", 0.01, nonlinear=False, beam=b.gaussian_beam(0.5e-3),
                                   transverse=b.transverse("radial", points=64, radius_m=2.5e-3), absorptionProfile=profile)])


def test_field_callback_matches_expression_and_sees_positions():
    spec = b.run_spec(b.pulse(1030e-9, 300e-15, energy=1e-9))
    seen = {}

    def alpha(x, y, z):
        seen["z"] = (float(np.min(z)), float(np.max(z)))
        seen["n"] = len(x)
        return 100.0 * np.exp(-2 * (x * x + y * y) / (0.5e-3) ** 2)

    r_cb = uf.run(_absorber("@alpha"), spec, fields={"alpha": alpha})
    r_ex = uf.run(_absorber({"kind": "gaussian", "amplitude": 100.0, "radiusM": 0.5e-3}), spec)
    t_cb = r_cb.summary["totals"]["energyTransmission"]
    t_ex = r_ex.summary["totals"]["energyTransmission"]
    assert t_cb == pytest.approx(t_ex, rel=1e-9)
    assert t_cb == pytest.approx((1 - math.exp(-1.0)) / 1.0, rel=3e-3)   # alpha0 L = 1, same width as the beam
    assert seen["n"] == 64 and seen["z"][0] >= 0 and seen["z"][1] <= 0.01 + 1e-12


def test_tapered_grin_callback_focuses_at_twice_the_distance():
    lam, w0, L, n0, f = 1.03e-6, 0.5e-3, 5e-3, 1.45, 0.2
    g2 = 1 / (n0 * f * L)
    sysd = b.system("grin", [
        b.bulk("rod", "FusedSilica", L, nonlinear=False, beam=b.gaussian_beam(w0),
               transverse=b.transverse("radial", points=64, radius_m=2e-3), indexProfile="@taper"),
        b.gap("to_focus", 2 * f, medium="Vacuum", transverse=b.transverse("radial", points=96, radius_m=2.5e-3))])
    res = uf.run(sysd, b.run_spec(b.pulse(lam, 300e-15, energy=1e-9)),
                 fields={"taper": lambda x, y, z: -0.5 * n0 * g2 * (x * x + y * y) * (1 - z / L)})
    w = {e["id"]: e for e in res.elements()}["to_focus"]["beam"]["radiusXM"]
    assert w == pytest.approx(lam * 2 * f / (math.pi * w0), rel=0.03)


def test_regen_from_python_arrays_and_gain_figure(tmp_path):
    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting
    sysd = b.system("regen", [
        b.dispersion("stretcher", gdd_s2=5e-25),
        b.regenerative_amplifier("rg", "Ti:sapphire", 0.01, round_trips="auto", mode_radius_m=0.4e-3,
                                 pump=b.pump("pulsed", 532e-9, 0.45e-3, energy_j=5e-3, passes=2),
                                 cavity={"lossPerRoundTrip": 0.05})])
    p = b.pulse(800e-9, 60e-15, energy=1e-9)
    p["repRateHz"] = 1000.0
    res = uf.run(sysd, b.run_spec(p, probes=["rg"]))
    el = {e["id"]: e for e in res.elements()}["rg"]
    v = el["values"]
    assert v["roundTrips"] >= 5 and v["converged"] == 1
    assert 790e-9 < v["gainPeakWavelengthM"] < 800e-9
    hist = res["el_rg_energyPerRoundTrip"]
    assert hist[0] == pytest.approx(1e-9, rel=1e-6) and hist.max() == pytest.approx(el["energyOutJ"], rel=1e-9)
    assert "el_rg_gainSpectrum" in el["arrays"]
    written = plotting.report(res, tmp_path, pdf=False)
    assert "gain" in written and "system" in written


def test_build_helpers_write_the_engine_json():
    g = b.gain("g", {"base": "Yb:YAG", "dopingDensityM3": 1e26}, 4e-3, pump=b.pump(power_w=50, profile="gaussian"),
               transverse=b.transverse("radial", points=32), thermal_lens={"rodRadiusM": 2e-3})
    assert g["type"] == "gain" and g["pump"]["powerW"] == 50 and g["pump"]["profile"] == "gaussian"
    m = b.multipass_amplifier("m", "Yb:YAG", 5e-3, 6, between_passes={"loss": 0.02})
    assert m["geometry"] == "multipass" and m["betweenPasses"]["loss"] == 0.02
