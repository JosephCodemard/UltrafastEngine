"""Phase 2.3 from Python: the Oscillator builder, saturable absorbers, the oscillator figure, and time responses
that see the position of the grid point."""
import math

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b
from ultrafast.laser import Oscillator


def _haus():
    return (Oscillator("laser", model="haus", rep_rate_hz=80e6, output_coupler=0.05)
            .simple_gain(1.5, saturation_energy_j=5e-9, bandwidth_m=100e-9, shape="gaussian")
            .loss(0.02).dispersion(gdd_fs2=-500).spm(1.5e-6)
            .absorber("kerrLens", modulation_depth=0.03, kappa_per_w=3e-8)
            .start("pulse").round_trips(max=8000))


def test_haus_oscillator_from_python_is_a_soliton(tmp_path):
    pytest.importorskip("matplotlib")
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast import plotting
    spec = b.run_spec(b.pulse(800e-9, 60e-15, energy=1e-9), grid={"auto": False, "n": 4096, "windowS": 8e-12})
    res = uf.run(_haus().system(), spec)
    v = res.elements()[0]["values"]
    assert v["converged"] == 1 and v["pulsesInCavity"] == 1
    tsol = Oscillator.soliton_fwhm_s(-500e-30, 1.5e-6, v["intracavityEnergyJ"])
    assert v["outputFwhmS"] == pytest.approx(tsol, rel=0.12)
    assert "el_laser_evolutionPower" in res.elements()[0]["arrays"]
    written = plotting.report(res, tmp_path, pdf=False)
    assert "oscillator" in written


def test_saturable_absorber_builder_and_energy_budget():
    sysd = b.system("sa", [b.saturable_absorber("sesam", "slow", modulation_depth=0.01, saturation_energy_j=1e-9, recovery_time_s=1e-9)])
    res = uf.run(sysd, b.run_spec(b.pulse(1030e-9, 200e-15, energy=1e-9)))
    S = 1.0
    expect = 0.01 * (1 - math.exp(-S)) / S
    assert 1 - res.summary["totals"]["energyTransmission"] == pytest.approx(expect, rel=0.02)
    assert "saturable absorption" in res.summary["energyBudget"]


def test_cavity_builder_writes_nested_elements():
    osc = (Oscillator("osc", cavity_length_m=1.875, output_coupler=0.1)
           .gain_medium("Yb:YAG", 2e-3, b.pump("cw", 969e-9, 70e-6, power_w=8, passes=2), mode_radius_m=60e-6)
           .add(b.chirped_mirror("cm", -1500e-30, bounces=12))
           .absorber("slow", modulation_depth=0.01, saturation_fluence_j_per_m2=0.5, spot_radius_m=80e-6, recovery_time_s=1e-12))
    d = osc.element()
    assert d["model"] == "cavity" and [e["type"] for e in d["elements"]] == ["chirpedMirror", "saturableAbsorber"]
    assert d["elements"][1]["saturationFluenceJPerM2"] == 0.5 and d["cavityLengthM"] == 1.875


def test_time_response_callback_sees_position():
    seen = []

    def dn(ctx):
        seen.append((ctx.r, ctx.spatial))
        return 2.5e-20 * ctx.intensity

    sysd = b.system("p", [b.bulk("p", "FusedSilica", 2e-3, beam=b.gaussian_beam(0.3e-3), timeResponse="@kerr2", replaceKerr=True,
                                 transverse=b.transverse("radial", points=16, radius_m=1.2e-3))])
    res = uf.run(sysd, b.run_spec(b.pulse(1030e-9, 300e-15, energy=1e-5)), callbacks={"kerr2": dn})
    rs = sorted(set(round(r, 9) for r, s in seen))
    assert all(s for _, s in seen) and len(rs) == 16 and max(rs) < 1.2e-3
    assert res.elements()[0]["bIntegralRad"] > 0.05
