"""Compute back-ends from Python: devices(), set_default_backend(), the compute() builder, and -- when a CUDA or
OpenCL device with double precision is present (on a machine without a GPU: pip install pocl-binary-distribution,
docs/testing.md) -- a run on the device that must match the CPU run to 1e-9."""
import numpy as np
import pytest

from conftest import requires_engine

import ultrafast as uf
from ultrafast import build as b


def _system():
    return uf.System.load(b.system("plate", [b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(w0_m=50e-6, z_waist_m=2.5e-3))]))


def _spec(compute):
    return b.run_spec(b.pulse(1.03e-6, 100e-15, energy=2e-6), grid={"auto": False, "n": 4096, "windowS": 2e-12}, compute=compute)


def test_compute_builder_is_typed_and_clean():
    c = b.compute("auto", threads=2, fft="managed", device=0, gpu_min_points=4096)
    assert c == {"backend": "auto", "threads": 2, "fft": "managed", "device": 0, "gpuMinPoints": 4096}
    assert "compute" not in b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-9))
    assert b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-9), compute=b.compute("cpu"))["compute"] == {"backend": "cpu"}


@requires_engine
def test_devices_lists_the_cpu_first():
    d = uf.devices()
    assert d["devices"][0]["backend"] == "cpu"
    names = {x["name"] for x in d["backends"]}
    assert {"cpu", "auto"} <= names
    assert d["fft"]


@requires_engine
def test_set_default_backend_round_trip():
    before = uf.devices()["defaultBackend"]
    try:
        assert uf.set_default_backend("auto")["backend"] == "auto"
        assert uf.devices()["defaultBackend"] == "auto"
        r = uf.run(_system(), _spec(None))      # no compute key: the default applies (falls back to the CPU when no device)
        assert r.summary["output"]["energyJ"] > 0
    finally:
        uf.set_default_backend(before)


@requires_engine
def test_run_on_a_device_matches_the_cpu():
    d = uf.devices()
    dev = next((x for x in d["devices"] if x["backend"] in ("cuda", "opencl") and x.get("usable", True)), None)
    if dev is None:
        pytest.skip("no CUDA / OpenCL device with double precision")
    cpu = uf.run(_system(), _spec(b.compute("cpu")))
    gpu = uf.run(_system(), _spec(b.compute(dev["backend"], device=dev["index"], gpu_min_points=1)))
    notes = [n for e in gpu.summary["elements"] for n in e.get("notes", [])]
    assert any(n.startswith("device: ") for n in notes), notes
    for name in ("output_spectrum",):
        x, y = np.asarray(gpu[name]), np.asarray(cpu[name])
        assert np.max(np.abs(x - y)) <= 1e-9 * np.max(np.abs(y))
    assert gpu.summary["totals"]["bIntegralRad"] == pytest.approx(cpu.summary["totals"]["bIntegralRad"], rel=1e-9)
