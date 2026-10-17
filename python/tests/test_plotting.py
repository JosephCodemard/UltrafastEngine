"""Plotting of live results and of CLI results folders, plus a regression test for the ideal compressor."""
import math

import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b

C = 299792458.0


def _spm_run(phi=2.0):
    lam, n2, area, L = 1.03e-6, 2.5e-20, 1e-8, 0.01
    gamma = n2 * 2 * math.pi * C / lam / (C * area)
    sysd = b.system("flat", [b.bulk("nl", "flat", L, beam=b.constant_beam(area_m2=area)), b.probe("mid"),
                             b.bulk("nl2", "flat", L, beam=b.constant_beam(area_m2=area))],
                    materials={"flat": {"index": 1.45, "n2": n2}})
    spec = b.run_spec(b.pulse(lam, 200e-15, peak_power_w=phi / (2 * gamma * L)),
                      instruments=[b.instrument("compressor"), b.instrument("spectrometer"),
                                   b.instrument("autocorrelator")])
    return uf.run(sysd, spec)


def test_compressor_never_worse_than_no_compressor():
    res = _spm_run()
    cp = res.instrument("compressor")
    out = res.summary["output"]
    assert cp["compressedPeakPowerW"] >= out["peakPowerW"] * 0.999
    assert cp["compressedFwhmS"] <= out["fwhmS"] * 1.001
    # SPM chirp is positive (up-chirp), so the matched compressor has negative GDD
    assert cp["compressorGddS2"] < 0


def test_report_from_live_result_and_saved_folder(tmp_path):
    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting as up

    res = _spm_run()
    figs = up.report(res, tmp_path / "live", close=True)
    assert (tmp_path / "live" / "overview.png").exists() and (tmp_path / "live" / "report.pdf").exists()
    assert "instruments" in figs

    folder = res.write(tmp_path / "run")
    saved = uf.load(folder)
    assert saved.summary["output"]["energyJ"] == pytest.approx(res.summary["output"]["energyJ"])
    from ultrafast.plot import main
    assert main([str(folder), "--format", "png,svg"]) == 0
    for name in ("overview.png", "overview.svg", "instruments.png", "probes.png", "elements.png", "report.pdf"):
        assert (folder / "plots" / name).exists(), name
    assert main([str(folder), str(folder), "--out", str(tmp_path / "cmp"), "--no-pdf"]) == 0
    assert (tmp_path / "cmp" / "compare.png").exists()
