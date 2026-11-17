"""ultrafast.calc: pulse shapes, beam optics (Siegman), nonlinear estimates (Marburger 1975), renamed keywords, the no-engine path.

The pure-Python calculators (``beam``, ``nonlinear``, ``pulse.tbp / shapes``) are tested against closed forms and, for mode matching, by
propagating every returned solution through independent ABCD code. The engine-backed ones are tested for parity and error handling.
"""
import math
import os
import subprocess
import sys
from pathlib import Path

import numpy as np
import pytest

from conftest import requires_engine
from ultrafast import calc
from ultrafast.calc import beam, nonlinear, pulse

LAM = 1030e-9
SRC = Path(__file__).resolve().parents[1] / "src"


# ---- pulse -------------------------------------------------------------------------------------------------------------------------

def test_tbp_table_matches_diels_rudolph():
    assert pulse.tbp("gaussian") == pytest.approx(0.4413, abs=1e-4)
    assert pulse.tbp("sech2") == pytest.approx(0.3148, abs=1e-4)
    assert pulse.tbp("lorentzian") == pytest.approx(0.142, abs=1e-3)
    assert pulse.tbp("asymmetric-exponential") == pytest.approx(0.110, abs=1e-3)
    assert pulse.tbp("rectangular") == pytest.approx(0.8859, abs=1e-4)
    assert pulse.tbp("asym-exp") == pulse.tbp("asymmetric-exponential") and pulse.tbp("lorentz") == pulse.tbp("lorentzian")
    with pytest.raises(ValueError, match="unknown pulse shape"):
        pulse.tbp("triangle")


@requires_engine
def test_python_tbp_table_equals_the_engine_table():
    rows = pulse._call({"op": "shapes"})["shapes"]
    assert [r["shape"] for r in rows] == [r["shape"] for r in pulse.shapes()]
    for a, b in zip(rows, pulse.shapes()):
        assert a["timeBandwidthProduct"] == pytest.approx(b["timeBandwidthProduct"], rel=1e-9)


@requires_engine
def test_engine_errors_are_value_errors():
    with pytest.raises(ValueError, match="unknown pulse shape"):
        pulse.limit("triangle", duration_s=100e-15)
    with pytest.raises(ValueError):
        pulse.limit("gaussian")                                     # neither duration nor bandwidth
    with pytest.raises(ValueError):
        calc.crystal.phase_match("NoSuchCrystal", "shg", fundamental_m=LAM)


@requires_engine
def test_old_keyword_names_work_with_a_deprecation_warning():
    with pytest.warns(DeprecationWarning, match="duration_s"):
        r = calc.limit("gaussian", duration=100e-15)
    assert r["bandwidthFwhmHz"] == pytest.approx(pulse.tbp("gaussian") / 100e-15, rel=1e-9)
    with pytest.warns(DeprecationWarning, match="fundamental_m"):
        old = calc.crystal.phase_match("BBO", "shg", fundamental=800e-9)
    new = calc.crystal.phase_match("BBO", "shg", fundamental_m=800e-9)
    assert old["best"]["thetaDeg"] == new["best"]["thetaDeg"]
    with pytest.raises(TypeError):
        calc.limit("gaussian", duration=1e-13, duration_s=1e-13)


@requires_engine
def test_old_module_paths_still_import():
    from ultrafast import cells, crystal
    assert crystal.phase_match is calc.crystal.phase_match and cells.cell_mode is calc.cell.cell_mode


@requires_engine
def test_sfg_noncollinear_angle_is_accepted_and_zero_is_collinear():
    kw = dict(a_m=800e-9, b_m=400e-9, length_m=1e-3)
    base = calc.crystal.phase_match("BBO", "sfg", "I", **kw)["best"]
    zero = calc.crystal.phase_match("BBO", "sfg", "I", noncollinear=0.0, **kw)["best"]
    tilted = calc.crystal.phase_match("BBO", "sfg", "I", noncollinear=3.0, **kw)["best"]
    assert zero["thetaDeg"] == pytest.approx(base["thetaDeg"], abs=1e-9)
    assert tilted["noncollinearDeg"] == pytest.approx(3.0) and abs(tilted["thetaDeg"] - base["thetaDeg"]) > 0.05


@requires_engine
def test_cell_mode_empty_cell_matches_kogelnik_li():
    r = calc.cell.cell_mode(LAM, mirror_radius_m=0.35, separation_m=0.54)["emptyCell"]
    # symmetric cell: w0^2 = (lambda / 2 pi) sqrt(d (2R - d))
    assert r["waistRadiusM"] == pytest.approx(math.sqrt(LAM / (2 * math.pi) * math.sqrt(0.54 * (2 * 0.35 - 0.54))), rel=1e-6)


# ---- beam (Siegman) ----------------------------------------------------------------------------------------------------------------

def test_gaussian_beam_closed_forms():
    w0, zr = 0.5e-3, math.pi * 0.5e-3 ** 2 / LAM
    b = beam.gaussian_beam(LAM, waist_radius_m=w0, z_m=zr)
    assert b["rayleighRangeM"] == pytest.approx(zr, rel=1e-12)
    assert b["radiusM"] == pytest.approx(w0 * math.sqrt(2), rel=1e-12)           # w(zR) = sqrt(2) w0
    assert b["curvatureRadiusM"] == pytest.approx(2 * zr, rel=1e-12)             # R(zR) = 2 zR (the minimum)
    assert b["gouyPhaseRad"] == pytest.approx(math.pi / 4, rel=1e-12)
    assert b["divergenceHalfAngleRad"] == pytest.approx(LAM / (math.pi * w0), rel=1e-12)
    assert beam.gaussian_beam(LAM, waist_radius_m=w0, m2=2.0)["rayleighRangeM"] == pytest.approx(zr / 2, rel=1e-12)
    assert beam.gaussian_beam(LAM, waist_radius_m=w0)["curvatureRadiusM"] == math.inf


def test_q_round_trip_and_radius_curvature():
    q = beam.q_at(LAM, waist_radius_m=0.4e-3, z_m=1.7)
    b = beam.q_to_beam(q, LAM)
    assert b["waistRadiusM"] == pytest.approx(0.4e-3, rel=1e-12) and b["zM"] == pytest.approx(1.7)
    q2 = beam.q_from_radius_curvature(b["radiusM"], b["curvatureRadiusM"], LAM)
    assert q2 == pytest.approx(q, rel=1e-9)


def test_abcd_propagation_equals_the_analytic_beam():
    q0 = beam.q_at(LAM, waist_radius_m=0.3e-3)
    q = beam.propagate_q(q0, beam.free_space(2.5))
    assert q == pytest.approx(beam.q_at(LAM, waist_radius_m=0.3e-3, z_m=2.5))
    d, f = 200.0, 0.3                                              # far-field lens (w >> w0, f << zR there): focal spot w' = lambda f / (pi w)
    w_lens = beam.beam_radius_m(d, 0.3e-3, LAM)
    q1 = beam.propagate_q(q0, beam.system_matrix(beam.free_space(d), beam.thin_lens(f)))
    assert beam.q_to_beam(q1, LAM)["waistRadiusM"] == pytest.approx(LAM * f / (math.pi * w_lens), rel=1e-2)
    assert beam.curved_mirror(0.5)[1][0] == pytest.approx(-1 / 0.25)                # f = R / 2
    with pytest.raises(ValueError):
        beam.thin_lens(0.0)


def _output_waist(w_in, d1, f, lam, m2=1.0):
    q = beam.propagate_q(complex(d1, beam.rayleigh_range_m(w_in, lam, m2)), beam.system_matrix(beam.thin_lens(f)))
    b = beam.q_to_beam(q, lam, m2)
    return b["waistRadiusM"], -q.real


@pytest.mark.parametrize("m2", [1.0, 1.7])
def test_single_lens_match_closed_loop(m2):
    w1, w2, f = 0.3e-3, 0.1e-3, 0.5
    r = beam.single_lens_match(waist_in_m=w1, waist_out_m=w2, wavelength_m=LAM, focal_length_m=f, m2=m2)
    assert r["minimumFocalLengthM"] == pytest.approx(math.pi * w1 * w2 / (m2 * LAM))
    assert len(r["solutions"]) == 2
    for s in r["solutions"]:                                      # propagate through independent ABCD code
        w, d2 = _output_waist(w1, s["distanceInputM"], f, LAM, m2)
        assert w == pytest.approx(w2, rel=1e-9) and d2 == pytest.approx(s["distanceOutputM"], rel=1e-9, abs=1e-12)


def test_single_lens_match_edge_cases():
    f0 = math.pi * 0.3e-3 * 0.1e-3 / LAM
    assert len(beam.single_lens_match(waist_in_m=0.3e-3, waist_out_m=0.1e-3, wavelength_m=LAM, focal_length_m=f0)["solutions"]) == 1
    with pytest.raises(ValueError, match="shorter than the minimum"):
        beam.single_lens_match(waist_in_m=0.3e-3, waist_out_m=0.1e-3, wavelength_m=LAM, focal_length_m=0.5 * f0)
    with pytest.raises(ValueError):
        beam.single_lens_match(waist_in_m=-1, waist_out_m=0.1e-3, wavelength_m=LAM, focal_length_m=1)


def test_two_lens_match_closed_loop():
    w1, w2, f1, f2, sep = 0.3e-3, 0.1e-3, 0.3, 0.2, 0.6
    r = beam.two_lens_match(w1, w2, LAM, focal_length_1_m=f1, focal_length_2_m=f2, separation_m=sep)
    assert r["solutions"]
    for s in r["solutions"]:
        q = beam.propagate_q(complex(s["distanceInputM"], beam.rayleigh_range_m(w1, LAM)),
                             beam.system_matrix(beam.thin_lens(f1), beam.free_space(sep), beam.thin_lens(f2)))
        out = beam.q_to_beam(q, LAM)
        assert out["waistRadiusM"] == pytest.approx(w2, rel=1e-9) and -q.real == pytest.approx(s["distanceOutputM"], rel=1e-9, abs=1e-12)


@pytest.mark.parametrize("kind,f1", [("keplerian", 0.1), ("galilean", -0.05)])
def test_telescope_magnifies_the_beam(kind, f1):
    t = beam.telescope(magnification=3.0, focal_length_1_m=f1, kind=kind)
    assert t["focalLength2M"] == pytest.approx(3.0 * abs(f1)) and t["separationM"] == pytest.approx(t["focalLength1M"] + t["focalLength2M"])
    m = beam.system_matrix(beam.thin_lens(t["focalLength1M"]), beam.free_space(t["separationM"]), beam.thin_lens(t["focalLength2M"]))
    assert m[1][0] == pytest.approx(0.0, abs=1e-9)                                   # afocal: C = 0
    assert abs(m[0][0]) == pytest.approx(3.0) and m[1][1] == pytest.approx(1 / m[0][0])   # |A| = M, D = 1/A
    assert (m[0][0] < 0) == (kind == "keplerian")                                    # the Keplerian image is inverted


def test_telescope_rejects_the_wrong_sign():
    with pytest.raises(ValueError):
        beam.telescope(magnification=2.0, focal_length_1_m=-0.1, kind="keplerian")


def test_m_squared_fit_recovers_a_noise_free_hyperbola():
    m2, w0, z0 = 1.8, 0.4e-3, 0.25
    z = np.linspace(-1.5, 2.0, 12)
    d = 2 * np.array([beam.beam_radius_m(zz - z0, w0, LAM, m2) for zz in z])
    fit = beam.m_squared_from_widths(z, d, LAM)
    assert fit["m2"] == pytest.approx(m2, rel=1e-9) and fit["waistDiameterM"] == pytest.approx(2 * w0, rel=1e-9) and fit["waistPositionM"] == pytest.approx(z0, rel=1e-9)
    assert beam.m_squared_from_waist_divergence(w0, m2 * LAM / (math.pi * w0), LAM) == pytest.approx(m2)
    with pytest.raises(ValueError):
        beam.m_squared_from_widths([0, 1], [1e-3, 1e-3], LAM)


# ---- nonlinear (Marburger 1975) ----------------------------------------------------------------------------------------------------

def test_critical_power_marburger_and_air():
    pcr = nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23, n0=1.0003)
    assert pcr == pytest.approx(3.77 * 800e-9 ** 2 / (8 * math.pi * 1.0003 * 3e-23), rel=1e-12)
    assert pcr == pytest.approx(3.2e9, rel=0.02)                                   # "a few GW in air"
    assert nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23, profile="townes") > nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23)
    with pytest.raises(ValueError):
        nonlinear.critical_power_w(800e-9, n2_m2_per_w=0.0)


def test_marburger_collapse_distance():
    kw = dict(wavelength_m=800e-9, n2_m2_per_w=3e-23, n0=1.0)
    pcr = nonlinear.critical_power_w(kw["wavelength_m"], n2_m2_per_w=3e-23)
    w0 = 1e-3
    zr = math.pi * w0 ** 2 / 800e-9
    z4 = nonlinear.collapse_distance_m(4 * pcr, waist_radius_m=w0, **kw)             # sqrt(4) = 2: 0.367 / sqrt((2 - 0.852)^2 - 0.0219)
    assert z4 == pytest.approx(0.367 * zr / math.sqrt((2 - 0.852) ** 2 - 0.0219), rel=1e-12)
    assert z4 == pytest.approx(0.3224 * zr, rel=1e-3)
    assert nonlinear.collapse_distance_m(100 * pcr, waist_radius_m=w0, **kw) < z4   # more power collapses sooner
    with pytest.raises(ValueError, match="no collapse"):
        nonlinear.collapse_distance_m(pcr, waist_radius_m=w0, **kw)


def test_peak_power_intensity_fluence():
    assert nonlinear.peak_power_w(1e-3, 100e-15, "gaussian") == pytest.approx(0.9394 * 1e-3 / 100e-15, rel=1e-4)
    assert nonlinear.peak_power_w(1e-3, 100e-15, "sech2") == pytest.approx(0.8814 * 1e-3 / 100e-15, rel=1e-4)
    assert nonlinear.peak_intensity_w_per_m2(1e9, 1e-3) == pytest.approx(2e9 / (math.pi * 1e-6))
    r = np.linspace(0, 6e-3, 200001)                               # P = integral of I(r) 2 pi r dr
    i0 = nonlinear.peak_intensity_w_per_m2(1e9, 1e-3)
    y = i0 * np.exp(-2 * r ** 2 / 1e-6) * 2 * np.pi * r
    assert float(np.sum(0.5 * (y[1:] + y[:-1]) * np.diff(r))) == pytest.approx(1e9, rel=1e-6)
    assert nonlinear.peak_fluence_j_per_m2(1e-3, 1e-3) == pytest.approx(2e-3 / (math.pi * 1e-6))


def test_b_integral_averages_gaussian_and_sech2():
    kw = dict(n2_m2_per_w=2.7e-20, length_m=5e-3, pulse_energy_j=1e-3, duration_fwhm_s=200e-15, beam_radius_m=1e-3)
    g = nonlinear.b_integral_estimate(LAM, **kw)
    assert g["bPeakRad"] == pytest.approx(2 * math.pi / LAM * 2.7e-20 * g["peakIntensityWPerM2"] * 5e-3, rel=1e-12)
    assert g["bPulseAveragedRad"] / g["bPeakRad"] == pytest.approx(1 / math.sqrt(2), rel=1e-12)
    assert g["bBeamAveragedRad"] / g["bPeakRad"] == pytest.approx(0.5, rel=1e-12)
    assert g["bPulseAndBeamAveragedRad"] / g["bPeakRad"] == pytest.approx(1 / (2 * math.sqrt(2)), rel=1e-12)
    s = nonlinear.b_integral_estimate(LAM, shape="sech2", **kw)
    assert s["bPulseAveragedRad"] / s["bPeakRad"] == pytest.approx(2 / 3, rel=1e-12)
    # the averages are the intensity-weighted means int I^2 / (I0 int I), checked numerically
    t = np.linspace(-6, 6, 200001)
    i = np.exp(-t * t)
    assert np.sum(i * i) / np.sum(i) == pytest.approx(1 / math.sqrt(2), rel=1e-6)
    j = 1 / np.cosh(np.linspace(-30, 30, 200001)) ** 2
    assert np.sum(j * j) / np.sum(j) == pytest.approx(2 / 3, rel=1e-6)
    assert math.isnan(g["powerOverCritical"]) and nonlinear.b_integral_estimate(LAM, n0=1.45, **kw)["powerOverCritical"] > 0


# ---- no engine ---------------------------------------------------------------------------------------------------------------------

def test_beam_nonlinear_and_tbp_work_without_the_engine():
    code = (
        "import ultrafast.core as core\n"
        "def no_engine(*a, **k):\n"
        "    raise RuntimeError('the engine is not available')\n"
        "core._lib = no_engine\n"
        "from ultrafast.calc import beam, nonlinear, pulse\n"
        "assert abs(pulse.tbp('sech2') - 0.3148) < 1e-4\n"
        "assert beam.gaussian_beam(1e-6, waist_radius_m=1e-3)['rayleighRangeM'] > 3\n"
        "assert nonlinear.critical_power_w(800e-9, n2_m2_per_w=3e-23) > 1e9\n"
        "try:\n"
        "    pulse.limit('gaussian', duration_s=1e-13)\n"
        "except RuntimeError as e:\n"
        "    print('engine missing ->', e)\n"
        "else:\n"
        "    raise SystemExit('the engine call should have failed')\n")
    env = dict(os.environ, PYTHONPATH=str(SRC))
    r = subprocess.run([sys.executable, "-c", code], env=env, capture_output=True, text=True, timeout=120)
    assert r.returncode == 0, r.stderr + r.stdout
    assert "engine missing" in r.stdout
