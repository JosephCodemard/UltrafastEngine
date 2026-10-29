"""Delayed light: the tail grid, ghosts / echoes / scatter / ripple builders, window planning, and the hitran plugin from Python."""
import csv
import math
import sys
from pathlib import Path

import pytest

from conftest import requires_engine

REPO = Path(__file__).resolve().parents[2]
HITRAN_PY = REPO / "plugins" / "hitran" / "python"
if str(HITRAN_PY) not in sys.path:
    sys.path.insert(0, str(HITRAN_PY))

from ultrafast import build as b  # noqa: E402  (pure Python, no engine needed)


def test_builders_write_the_engine_keys():
    sub = b.substrate("FusedSilica", 12.7e-3, back_reflectance=1e-3, wedge_deg=0.1)
    assert sub == {"material": "FusedSilica", "thicknessM": 12.7e-3, "backReflectance": 1e-3, "wedgeDeg": 0.1}
    m = b.mirror("m", coating={"kind": "ideal", "R": 0.99, "T": 0.01}, substrate="FS-6.35mm", ripple=b.coating_ripple(0.02),
                 delayed_scatter=b.delayed_scatter(1e-4, b.delay_uniform(40e-12), 4.0))
    assert m["substrate"] == "FS-6.35mm" and m["ripple"]["phaseRmsRad"] == 0.02 and m["delayedScatter"]["delay"]["maxS"] == 40e-12
    g = b.grid(tail=b.tail_grid(2e-9), extend_for_satellites=True, strict=False)
    assert g == {"tail": {"windowS": 2e-9}, "extendForSatellites": True, "strict": False}
    env = b.environment(air=b.air(humidity=0.5, temperature_c=22.0, apply_to_gaps=True))
    assert env["air"]["applyToGaps"] is True and env["air"]["humidity"] == 0.5
    assert b.gap("g", 1.0, air=b.air(humidity=0.2))["air"] == {"humidity": 0.2}
    s = b.system("s", [m], substrates={"mine": sub})
    assert s["substrates"]["mine"]["thicknessM"] == 12.7e-3


def test_hitran_builders_and_downloader_with_a_fake_api(tmp_path):
    import ultrafast_hitran as hit

    a = hit.air(humidity=0.45, species=hit.species(h2o=True, co2="CO2.csv"))
    assert a["kind"] == "hitran" and a["applyToGaps"] is True and a["species"] == {"H2O": True, "CO2": "CO2.csv"}
    assert a["rangeCm1"] == [6500.0, 14000.0] and a["cutoffCm1"] == 25.0

    class FakeApi:
        def __init__(self):
            self.tables = {}

        def db_begin(self, d):
            self.dir = d

        def fetch(self, table, molecule, iso, numin, numax):
            nu = [9000.0 + 10 * iso, 9100.0 + 10 * iso]
            self.tables[table] = {"nu": nu, "sw": [1e-22, 2e-22], "a": [1.0, 2.0], "gamma_air": [0.09, 0.08], "gamma_self": [0.4, 0.4],
                                  "elower": [100.0, 200.0], "n_air": [0.7, 0.7], "delta_air": [-0.001, -0.002]}

        def getColumn(self, table, name):
            return self.tables[table][name]

        def partitionSum(self, molecule, iso, temps):
            return [0.5 * t * iso for t in temps]               # a fake TIPS: Q = iso T / 2

    files = hit.download(["H2O"], 6500, 14000, str(tmp_path), api=FakeApi())
    rows = list(csv.reader(open(files["H2O"])))
    assert rows[0] == hit.download.COLUMNS if hasattr(hit.download, "COLUMNS") else rows[0][:3] == ["molec_id", "local_iso_id", "nu"]
    assert len(rows) == 1 + 2 * 4                                   # four main isotopologues, two lines each
    part = list(csv.reader(open(tmp_path / "H2O.partition.csv")))
    assert part[0] == ["iso", "T", "Q"] and len(part) == 1 + 4 * 401 and part[1] == ["1", "100.0", "50.0"]
    assert [float(r[2]) for r in rows[1:]] == sorted(float(r[2]) for r in rows[1:])
    with pytest.raises(ValueError):
        hit.download(["N2"], api=FakeApi(), directory=str(tmp_path))


def _par(nu, s, g_air=0.1, g_self=0.4, e=100.0, n_air=0.7, d=0.0, mol=1, iso=1):
    def fw(v, dec, w):
        t = f"{v:.{dec}f}"
        if len(t) > w:
            t = t.replace("0.", ".")
        return t.rjust(w)
    return (f"{mol:02d}{iso}" + f"{nu:.6f}".rjust(12) + f"{s:.3E}".rjust(10) + f"{0.0:.3E}".rjust(10) + fw(g_air, 4, 5) + fw(g_self, 3, 5)
            + fw(e, 4, 10) + fw(n_air, 2, 4) + fw(d, 6, 8)).ljust(160)


@requires_engine
def test_ghost_plate_tail_grid_from_python():
    import ultrafast as uf
    sys_ = b.system("plate", [b.plate("w", "FusedSilica", 5e-3, ghosts=True, coating={"kind": "ar", "R": 0.01})])
    spec = b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-6), grid=b.grid(tail=True))
    res = uf.run(uf.System.load(sys_), spec)
    tail = res.summary["tail"]
    assert tail["energyJ"] > 0 and tail["factor"] >= 2 and tail["windowS"] > 2 * res.summary["grid"]["windowS"]
    ghost = 0.01 * 0.01
    assert tail["fractionOfOutput"] == pytest.approx(ghost, rel=0.1)       # R_front R_back of the 1 % AR coatings


@requires_engine
def test_mirror_echo_and_window_warning_from_python():
    import ultrafast as uf
    sys_ = b.system("m", [b.mirror("m", coating={"kind": "ideal", "R": 0.99, "T": 0.01}, substrate="FS-6.35mm")])
    spec = b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-6), grid=b.grid(auto=False, n=4096, window_s=20e-12))
    res = uf.run(uf.System.load(sys_), spec)
    w = [x for x in res.summary["warnings"] if x.startswith("Window: element 'm'")]
    assert w and "wraps around" in w[0] and "+61.9" in w[0]


@requires_engine
def test_hitran_air_in_a_gap_from_python(tmp_path):
    import ultrafast as uf
    import ultrafast_hitran as hit
    if "hitran" not in [p.id for p in uf.plugins.installed()]:
        pytest.skip("hitran plugin not installed in this engine build (python tools/build_engine.py)")
    nu0 = 1e7 / 1030.0
    f = tmp_path / "H2O.par"
    f.write_text("\n".join([_par(nu0, 3e-21), _par(nu0 + 1.7, 1e-21), _par(nu0 - 2.3, 2e-21)]) + "\n")
    env = b.environment(air=hit.air(humidity=0.5, species={"H2O": str(f)}, range_cm1=(9000.0, 10500.0)))
    sys_ = b.system("cell", [b.gap("air", 10.0)], environment=env)
    spec = b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-6), grid=b.grid(tail=b.tail_grid(window_s=2e-9), snap_carrier=False))
    res = uf.run(uf.System.load(sys_), spec)
    assert any("environment.air is applied to every air gap" in n for n in res.summary["notes"])
    absorbed = 1 - res.summary["output"]["energyJ"] / res.summary["input"]["energyJ"]
    assert absorbed > 1e-5                                           # the lines absorb
    assert res.summary["tail"]["energyJ"] > 0                       # and ring behind the pulse
    assert res.summary["grid"]["windowS"] < 50e-12                  # while the nonlinear grid stays small


def test_small_scale_detector_and_scatter_builders():
    prof = b.initial_profile("flatTop", ripple=b.profile_ripple(67e-6, 0.01), noise=b.profile_noise(0.01, 40e-6, seed=3))
    assert prof == {"kind": "flatTop", "ripple": {"periodM": 67e-6, "relativeAmplitude": 0.01, "angleDeg": 0.0, "phaseRad": 0.0},
                    "noise": {"relativeAmplitudeRms": 0.01, "correlationLengthM": 40e-6, "seed": 3}}
    tr = b.transverse("cartesian", nx=128, ny=128, half_width_m=0.6e-3, initial_profile=prof)
    assert tr["nx"] == 128 and tr["initialProfile"]["kind"] == "flatTop"
    sc = b.delayed_scatter(model="surface", roughness_rms_m=2e-9, correlation_length_m=2e-4, beam_radius_m=1e-3, surfaces=2)
    assert sc == {"model": "surface", "roughnessRmsM": 2e-9, "correlationLengthM": 2e-4, "beamRadiusM": 1e-3, "surfaces": 2}
    assert b.tail_grid(2e-9, exact_steps=True, cross_phase=False) == {"windowS": 2e-9, "exactSteps": True, "crossPhase": False}
    det = b.detector(response=b.detector_response([900e-9, 1100e-9], [0.5, 0.9]), counting="photons", saturation_level=0.9)
    assert det["response"]["values"] == [0.5, 0.9] and det["counting"] == "photons"
    preset = b.detector_preset(instrument={"saturationLevel": 0.5, "segments": [b.detector_segment(-2e-12, 2e-12, 1e-3)]})
    spec = b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-6), detectors={"my": preset}, instruments=[b.instrument("crossCorrelator", detector="my")])
    assert spec["detectors"]["my"]["instrument"]["saturationLevel"] == 0.5 and spec["instruments"][0]["detector"] == "my"


@requires_engine
def test_small_scale_seed_and_diagnostics_from_python():
    import ultrafast as uf
    sys_ = b.system("p", [b.bulk("k", "FusedSilica", 10e-3, beam=b.gaussian_beam(0.3e-3),
                                 transverse=b.transverse("cartesian", nx=32, ny=32, half_width_m=0.6e-3,
                                                         initial_profile=b.initial_profile("flatTop", noise=b.profile_noise(0.01, 40e-6, seed=1))))])
    spec = b.run_spec(b.pulse(1.03e-6, 300e-15, energy=130e-6), grid=b.grid(n=64), physics={"raman": False, "selfSteepening": False})
    res = uf.run(uf.System.load(sys_), spec)
    v = res.summary["elements"][0]["values"]
    assert v["fluenceContrastIn"] > 0 and v["peakPowerOverCritical"] > 10 and "smallScaleGain" in v


@requires_engine
def test_detector_response_and_preset_from_python():
    import numpy as np
    import ultrafast as uf
    sysd = b.system("s", [b.dispersion("d", 0.0)])
    pulse = b.pulse(1.03e-6, 30e-15, energy=1e-6)
    plain = uf.run(uf.System.load(sysd), b.run_spec(pulse, instruments=[b.instrument("spectrometer", name="sp", normalize="none")]))
    qe = b.detector_preset(detector=b.detector(response=b.detector_response([900e-9, 1200e-9], [0.5, 0.5])))
    half = uf.run(uf.System.load(sysd), b.run_spec(pulse, detectors={"qe": qe}, instruments=[b.instrument("spectrometer", name="sp", normalize="none", detector="qe")]))
    a, h = plain.instrument("sp")["spectrum"], half.instrument("sp")["spectrum"]
    assert np.allclose(h, 0.5 * a, rtol=1e-9)                       # a flat 50 % quantum efficiency, by name from the run spec's presets
