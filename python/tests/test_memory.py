"""Memory safety (docs/reference/memory.md): estimate_memory, the scan / fit pre-flight, close() and the context manager."""
import warnings

import pytest

import ultrafast as uf
from conftest import requires_engine

pytestmark = requires_engine


def _system(n_elements=3, transverse=None):
    els = []
    for i in range(n_elements):
        e = {"id": f"g{i}", "type": "gap", "lengthM": 0.01, "material": "FusedSilica"}
        if transverse:
            e["transverse"] = transverse
        els.append(e)
    return {"format": "ultrafast-system", "version": 1, "name": "mem", "elements": els}


def _run(**extra):
    r = {"format": "ultrafast-run", "pulse": {"shape": "gaussian", "centerWavelengthM": 1030e-9, "durationFwhmS": 100e-15, "energyJ": 1e-6},
         "grid": {"auto": False, "n": 4096, "windowS": 20e-12}}
    r.update(extra)
    return r


def test_estimate_matches_the_hand_count():
    est = uf.estimate_memory(_system(3), _run())
    assert est["estimate"]["pulseBytes"] == 4 * 2 * 4096 * 16
    assert est["totalBytes"] == est["estimate"]["pulseBytes"] + est["estimate"]["workspaceBytes"]
    assert est["exceedsLimit"] is False and est["limitBytes"] == 0
    assert "Estimated memory" in est["summary"]


def test_estimate_reports_a_limit_without_refusing():
    est = uf.estimate_memory(_system(3), _run(compute={"memoryLimitBytes": 1e5}))
    assert est["exceedsLimit"] is True and "memoryLimitBytes" in est["warning"]


def test_scan_raises_above_the_limit_and_warns_above_free_memory():
    axes = {"pulse/energyJ": [1e-6, 2e-6]}
    sc = uf.scan(_system(3), _run(compute={"memoryLimitBytes": 1e5}), axes, ["pulse:end/energyJ"])
    with pytest.raises(MemoryError, match="memoryLimitBytes"):
        sc.compute()
    # unlimited: no warning on a small run
    with warnings.catch_warnings():
        warnings.simplefilter("error", uf.MemoryWarning)
        uf.scan(_system(2), _run(), axes, ["pulse:end/energyJ"]).compute()
    # many parallel copies of a run above 80 % of the free memory warn (a huge parallel count stands in for a big run)
    est = uf.estimate_memory(_system(2), _run())
    if est["availableBytes"] > 0:
        par = int(0.9 * est["availableBytes"] / est["totalBytes"]) + 1
        with pytest.warns(uf.MemoryWarning, match="runs at once"):
            uf.core._preflight(_system(2), _run(), "warn", par)
    with pytest.raises(ValueError):
        uf.core._preflight(_system(2), _run(), "loud")


def test_close_releases_the_engine_object_and_works_as_a_context_manager():
    with uf.System.load(_system(2)) as s:
        assert not s.closed
    assert s.closed
    s.close()  # twice is fine
    r = uf.run(_system(2), _run())
    assert not r.closed
    r.close()
    assert r.closed
