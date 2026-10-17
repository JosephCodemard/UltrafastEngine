"""Stage 1.2-1.7 features through the Python package: components, materials, studies, retrieval."""
import math

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b


def test_materials_library():
    av = uf.materials.available()
    assert "main/SiO2/Malitson" in av["refractiveIndexInfoBundled"]
    assert "N2" in av["molecularGases"] and "Quartz" in av["birefringent"]
    fs = uf.materials.show("FusedSilica")
    row = [r for r in fs["table"] if abs(r["wavelengthM"] - 800e-9) < 1e-12][0]
    assert row["gvdFs2PerMm"] == pytest.approx(36.2, rel=0.02)      # fused silica GVD at 800 nm
    n2 = uf.materials.show("N2")
    assert n2["rotationalAdiabaticN2"] > 2 * n2["n2"]


def test_plate_with_ghost_and_beam_tracking():
    sys = b.system("plate", [{"id": "w", "type": "plate", "material": "FusedSilica", "thicknessM": 3e-3, "ghosts": True},
                             {"id": "g", "type": "gap", "lengthM": 0.5}, {"id": "lens", "type": "lens", "focalLengthM": 0.25},
                             {"id": "g2", "type": "gap", "lengthM": 0.25}])
    spec = b.run_spec(b.pulse(1.03e-6, 100e-15, energy=1e-6))
    spec["inputBeam"] = {"kind": "gaussian", "w0M": 1e-3}
    res = uf.run(uf.System.load(sys), spec)
    els = res.summary["elements"]
    w = els[-1]["values"]["trackedBeamRadiusXM"]
    assert w == pytest.approx(1.03e-6 * 0.25 / (math.pi * 1e-3), rel=0.03)


def test_study_scan_and_optimize():
    sys = b.system("k", [b.bulk("nl", "flat", 0.01, beam=b.constant_beam(area_m2=1e-8))], materials={"flat": {"index": 1.45, "n2": 2.5e-20}})
    spec = b.run_spec(b.pulse(1.03e-6, 200e-15, peak_power_w=2e6), physics={"selfSteepening": False, "raman": False})
    r = uf.study(sys, spec, [{"path": "element:nl/lengthM", "min": 0.005, "max": 0.02, "points": 4}], ["record:nl/BIntegralRad"])
    b_vals = [row["record:nl/BIntegralRad"] for row in r["rows"]]
    lengths = [row["element:nl/lengthM"] for row in r["rows"]]
    ratio = np.array(b_vals) / np.array(lengths)
    assert np.allclose(ratio, ratio[0], rtol=1e-3)                    # B proportional to L


def test_retrieve_simulated_frog():
    sys = b.system("p", [{"id": "x", "type": "probe"}])
    spec = b.run_spec(b.pulse(0.8e-6, 25e-15, energy=1e-9, gdd_s2=200e-30))
    spec["instruments"] = [{"kind": "frog", "name": "frog", "points": 128}]
    res = uf.run(uf.System.load(sys), spec)
    trace = {"delay_s": res["frog_delay_s"], "omega_rad_s": res["frog_omega_rad_s"], "trace": res["frog_trace"]}
    out = uf.retrieve("pcgpa", trace)
    assert out["error"] < 5e-3
    assert out["fwhmS"] == pytest.approx(res.summary["output"]["fwhmS"], rel=0.05)
    assert out["pulse"]["shape"] == "spectrum"
