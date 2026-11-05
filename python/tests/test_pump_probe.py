"""Example 41: the cross-phase shift of a delayed probe in a Branched pump-probe system against the exact result (three delays)."""
import importlib.util
import pathlib

import numpy as np
import pytest

PATH = pathlib.Path(__file__).resolve().parents[2] / "docs" / "examples" / "41-pump-probe" / "pump_probe.py"


@pytest.fixture(scope="module")
def pp():
    spec = importlib.util.spec_from_file_location("pump_probe", PATH)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def test_probe_shift_matches_the_exact_cross_phase(pp):
    i_pump = 0.9394 * pp.E_IN * pp.SPLIT * 0.5 / pp.FWHM / pp.AREA
    peak = abs(pp.reference(0.6e-12, i_pump))
    for tau in (-0.6e-12, 0.0, 0.6e-12, 1.5e-12):
        res, c = pp.run(tau)
        shift = c - pp.run(tau, kerr=False)[1]
        assert abs(shift - pp.reference(tau, i_pump)) < 0.025 * peak, tau
        assert res.summary["graph"]["packetsAtEnd"] == 1


def test_the_pump_is_blocked_in_the_probe_channel(pp):
    res, _ = pp.run(0.0, kerr=False)
    assert res.summary["output"]["energyJ"] == pytest.approx(pp.E_IN * (1 - pp.SPLIT) * 0.5, rel=2e-3)
