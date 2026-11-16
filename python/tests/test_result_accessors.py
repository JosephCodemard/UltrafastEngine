"""Typed accessors on a Result: ``unverified_inputs`` and ``ase_pedestal`` (the ASE of one stage as the pedestal of the next)."""
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf


def _system(*elements):
    return {"format": "ultrafast-system", "version": 2, "name": "t", "elements": list(elements)}


def _amplifier(ase):
    el = {"id": "amp", "type": "gain", "medium": "Yb:YAG", "lengthM": 0.008, "slices": 16, "modeRadiusM": 0.4e-3 * 2 ** 0.5, "spm": False,
          "pump": {"kind": "cw", "powerW": 250, "wavelengthM": 940e-9, "radiusM": 0.4e-3, "profile": "tophat", "passes": 2},
          "thermalLens": False, "repRateHz": 1e5}
    if ase:
        el["aseModel"] = True
    return _system(el)


def _spec(pedestal=None):
    pulse = {"shape": "gaussian", "centerWavelengthM": 1030e-9, "durationFwhmS": 1e-12, "energyJ": 1e-8}
    if pedestal:
        pulse["pedestal"] = [pedestal]
    return {"format": "ultrafast-run", "pulse": pulse, "grid": {"auto": False, "n": 1024, "windowS": 40e-12}}


def test_ase_pedestal_carries_the_amplifier_ase_into_the_next_stage():
    r = uf.run(_amplifier(True), _spec())
    f = r.elements()[0]["values"]["asePedestalEnergyFraction"]
    ped = r.ase_pedestal("amp", duration_s=10e-6)
    assert ped == {"kind": "ase", "energyFraction": f, "durationS": 10e-6}
    assert f > 0
    # the component is a valid pulse.pedestal of the next stage (strict options reject unknown keys)
    nxt = uf.run(_system({"id": "g", "type": "gap", "lengthM": 0.01}), _spec(ped))
    assert nxt.summary["totals"]["energyTransmission"] > 0


def test_ase_pedestal_without_an_ase_model_says_so():
    r = uf.run(_amplifier(False), _spec())
    with pytest.raises(KeyError, match="aseModel"):
        r.ase_pedestal()


def test_unverified_inputs_is_a_list_of_typed_entries():
    clean = uf.run(_system({"id": "g", "type": "gap", "lengthM": 0.01}), _spec())
    assert clean.unverified_inputs == []
    cs2 = uf.run(_system({"id": "cell", "type": "bulk", "material": "CS2", "lengthM": 0.01, "beam": {"kind": "constant", "areaM2": 1e-8}}), _spec())
    items = cs2.unverified_inputs
    assert items and all({"quantity", "material", "property", "status", "source", "origin"} <= set(i) for i in items)
    assert any(i["quantity"].startswith("CS2") for i in items)
