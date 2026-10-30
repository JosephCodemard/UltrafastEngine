"""Diffraction by apertures, the thermal lens on the tracked beam and spatio-temporal couplings through the Python package."""
import cmath
import math

import pytest

from conftest import requires_engine

import ultrafast as uf
from ultrafast import build as b

LAM = 1030e-9


def test_builders_write_the_new_keys():
    t = b.transverse("cartesian", nx=64, half_width_m=5e-3, absorbing_boundary=True, spectral_samples=4,
                     initial_profile=b.initial_profile("tophat", geometry="square", radius_x_m=1e-3, radius_y_m=2e-3))
    assert t["absorbingBoundary"] is True and t["spectralSamples"] == 4
    assert t["initialProfile"] == {"kind": "tophat", "geometry": "square", "radiusXM": 1e-3, "radiusYM": 2e-3}
    assert b.aperture("a", half_width_x_m=1e-3, half_width_y_m=2e-3) == {"id": "a", "type": "aperture", "halfWidthXM": 1e-3, "halfWidthYM": 2e-3}
    with pytest.raises(ValueError):
        b.aperture("a")
    assert b.grating("g", 1200, -45, order=1) == {"id": "g", "type": "grating", "linesPerMm": 1200, "aoiDeg": -45, "order": 1}
    assert b.thermal_lens(focal_length_m=0.5, track=True) == {"focalLengthM": 0.5, "track": True}
    assert b.physics(spatio_temporal="track") == {"spatioTemporal": "track"}


@requires_engine
def test_gaussian_through_an_iris_matches_the_fresnel_integral_on_the_axis():
    # Gaussian (w = 2 mm) cut at a = 1 mm, screen at N = a^2 / (lambda z) = 1: transmission 1 - exp(-2 a^2 / w^2) and the on-axis
    # Fresnel integral F0 |(k / 2z) / p|^2 |1 - exp(-a^2 p)|^2, p = 1 / w^2 - i k / 2z.
    w, a, e = 2e-3, 1e-3, 1e-3
    z = a * a / LAM
    grid = dict(points=512, radius_m=8e-3, absorbing_boundary=True)
    sys_ = b.system("iris", [
        b.gap("source", 0.0, medium="Vacuum", transverse=b.transverse("radial", **grid, initial_profile=b.initial_profile("gaussian", radius_m=w))),
        b.aperture("iris", radius_m=a),
        b.gap("screen", z, medium="Vacuum", transverse=b.transverse("radial", **grid)),
    ])
    res = uf.run(sys_, b.run_spec(b.pulse(LAM, 1e-12, energy=e), instruments=[b.instrument("beamProfile", at="screen", name="screen")]))
    t = next(x for x in res.elements() if x["id"] == "iris")["values"]["transmission"]
    assert t == pytest.approx(1 - math.exp(-2 * a * a / (w * w)), rel=0.005)
    k = 2 * math.pi / LAM
    p = 1 / w ** 2 - 1j * k / (2 * z)
    f_axis = 2 * e / (math.pi * w * w) * abs(k / (2 * z) / p) ** 2 * abs(1 - cmath.exp(-a * a * p)) ** 2
    assert res.instrument("screen")["fluence_j_m2"][0] == pytest.approx(f_axis, rel=0.01)


@requires_engine
def test_two_gratings_give_the_grating_pairs_gdd():
    # grating - slant gap - grating = Treacy (gratingPair, single pass) to second order
    beta = math.asin(LAM * 1.2e6 - math.sin(math.pi / 4))
    g = 0.1
    spec = b.run_spec(b.pulse(LAM, 100e-15, energy=1e-6), input_beam=b.gaussian_beam(2e-3))
    two = uf.run(b.system("two", [b.grating("g1", 1200, -45, order=1), b.gap("gap", g / math.cos(beta), medium="Vacuum"),
                                  b.grating("g2", 1200, math.degrees(beta), order=-1)]), spec)
    pair = uf.run(b.system("pair", [b.grating_pair("pair", 1200, 45, g, passes=1)]), spec)
    gdd = lambda r: r.summary["elements"][-1]["system"]["gddS2"]
    assert gdd(two) == pytest.approx(gdd(pair), rel=2e-3)
    v = two.summary["elements"][-1]["values"]
    assert v["trackedSpatialChirpXMPerM"] == pytest.approx(pair.summary["elements"][-1]["values"]["trackedSpatialChirpXMPerM"], rel=2e-3)


@requires_engine
def test_zone_plate_and_structured_source_through_the_builders():
    # a uniform beam on a 10-zone amplitude plate: 4 N_open^2 = 100 x the incident fluence at the focus
    f, zones, e = 0.1, 10, 1e-3
    a = math.sqrt(zones * LAM * f)
    grid = dict(points=1024, radius_m=5e-3, absorbing_boundary=True)
    sys_ = b.system("zp", [
        b.gap("source", 0.0, medium="Vacuum", transverse=b.transverse("radial", **grid, initial_profile=b.initial_profile("tophat", radius_m=a))),
        b.mask("zp", "zonePlate", focal_length_m=f),
        b.gap("focus", f, medium="Vacuum", transverse=b.transverse("radial", **grid)),
    ])
    res = uf.run(sys_, b.run_spec(b.pulse(LAM, 1e-12, energy=e)))
    v = next(x for x in res.elements() if x["id"] == "focus")["values"]
    assert v["onAxisFluenceOutJPerM2"] == pytest.approx(100 * e / (math.pi * a * a), rel=0.08)
    lg = b.initial_profile("laguerreGauss", mode_p=1, radius_m=1e-3)
    assert lg == {"kind": "laguerreGauss", "modeP": 1, "radiusM": 1e-3}


@requires_engine
def test_cw_resonator_and_coherence_through_the_builders():
    # a lens-guide eigenmode; a Gaussian-Schell beam of M^2 = 2 at one Rayleigh range; both as cw sources
    res = uf.run(b.system("cav", [b.resonator("c", [b.gap("g", 0.6, medium="Vacuum"), b.lens("l", 0.5, medium="Vacuum")])]), b.run_spec(b.cw(LAM, 1.0)))
    assert res.elements()[0]["values"]["stabilityX"] == pytest.approx(0.4, rel=1e-6)
    w0, zr = 0.5e-3, math.pi * (0.5e-3) ** 2 / LAM
    grid = dict(nx=256, ny=1, half_width_m=6e-3, spectral_samples=2)
    sys_ = b.system("m2", [b.gap("s", 0.0, medium="Vacuum", transverse=b.transverse("cartesian", **grid, initial_profile=b.initial_profile("gaussian", radius_m=w0))),
                           b.gap("z", zr, medium="Vacuum", transverse=b.transverse("cartesian", **grid))])
    res = uf.run(sys_, b.run_spec(b.cw(LAM, 1.0), coherence=b.coherence(2.0, 30)))
    assert res.elements()[-1]["beam"]["radiusXM"] == pytest.approx(w0 * math.sqrt(5), rel=0.03)
