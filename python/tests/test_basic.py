import math

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b

C = 299792458.0


def flat_kerr_system(length=0.01, n2=2.5e-20, area=1e-8, extra_material=None):
    mats = {"flat": {"index": 1.45, "n2": n2}}
    return b.system("flat", [b.bulk("nl", "flat", length, beam=b.constant_beam(area_m2=area))], materials=mats)


def test_version_and_capabilities():
    assert uf.version().startswith("2.")
    caps = uf.capabilities()
    assert "sellmeier" in caps["models"]["index"]
    assert "spectrometer" in caps["instruments"]


def test_spm_broadening_matches_analytic():
    lam, n2, area, L, phi = 1.03e-6, 2.5e-20, 1e-8, 0.01, 5.0
    gamma = n2 * 2 * math.pi * C / lam / (C * area)
    spec = b.run_spec(b.pulse(lam, 100e-15, peak_power_w=phi / (gamma * L)), physics={"selfSteepening": False})
    res = uf.run(flat_kerr_system(L, n2, area), spec)

    def rms(prefix):
        w, s = res[prefix + "_omega_rad_s"], res[prefix + "_spectrum"]
        m = (w * s).sum() / s.sum()
        return math.sqrt(((w - m) ** 2 * s).sum() / s.sum())

    ratio = rms("output") / rms("input")
    assert ratio == pytest.approx(math.sqrt(1 + 4 / (3 * math.sqrt(3)) * phi ** 2), rel=0.01)
    assert res.summary["totals"]["bIntegralRad"] == pytest.approx(phi, rel=0.01)


def test_system_validation_reports_errors():
    bad = uf.System.load(b.system("bad", [{"id": "a", "type": "warpDrive"}]))
    rep = bad.validate()
    assert not rep["ok"] and any("warpDrive" in e for e in rep["errors"])


def test_engine_errors_raise():
    with pytest.raises(uf.UltrafastError) as e:
        uf.System.load('{"format": "ultrafast-system", "elements": [ ')
    assert e.value.code == -2


def test_result_write_and_arrays(tmp_path):
    res = uf.run(b.system("free", [b.probe("p")]), b.run_spec(b.pulse(800e-9, 30e-15, energy=1e-9), instruments=[b.instrument("autocorrelator")]))
    out = res.write(tmp_path / "out")
    assert (out / "summary.json").exists() and (out / "result.npz").exists()
    npz = np.load(out / "result.npz")
    assert np.allclose(npz["output_power_w"], res["output_power_w"])
    ac = res.instrument("autocorrelator")
    assert ac["acFwhmS"] == pytest.approx(30e-15 * math.sqrt(2), rel=0.02)
