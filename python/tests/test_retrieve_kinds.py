"""Retrieval kinds through the Python API: a PG trace without a kind is refused, XFROG with its reference, the double FROG."""
import pytest

import ultrafast as uf
from ultrafast import build as b


def test_retrieve_kinds_pg_xfrog_and_multi_trace():
    sysd = b.system("p", [{"id": "x", "type": "probe"}])
    ref = b.pulse(0.8e-6, 30e-15, energy=1e-9, gdd_s2=195e-30)
    test = b.pulse(1.2e-6, 40e-15, energy=1e-9, gdd_s2=580e-30)
    res = uf.run(uf.System.load(sysd), b.run_spec(test, instruments=[b.instrument("frog", name="pg", type="pg", points=128)]))
    pg = {"delay_s": res["pg_delay_s"], "omega_rad_s": res["pg_omega_rad_s"], "trace": res["pg_trace"]}
    with pytest.raises(Exception, match="not symmetric"):
        uf.retrieve("pcgpa", pg)
    assert uf.retrieve("pcgpa", pg, kind="pg-frog", maxIterations=200)["error"] < 8e-3
    ref_spec = {"shape": "gaussian", "centerWavelengthM": 0.8e-6, "durationFwhmS": 30e-15, "chirp": {"gddS2": 195e-30}}
    rx = uf.run(uf.System.load(sysd), b.run_spec(test, instruments=[b.instrument("xfrog", name="xf", points=128, reference=ref_spec)]))
    names = ("delay_s", "omega_rad_s", "trace", "reference_time_s", "reference_field_re", "reference_field_im", "reference_omega_rad_s")
    xf = {k: rx["xf_" + k] for k in names}
    out = uf.retrieve("pcgpa", xf, kind="xfrog")
    assert out["error"] < 8e-3 and out["fwhmS"] == pytest.approx(rx.summary["output"]["fwhmS"], rel=0.03)
    rr = uf.run(uf.System.load(sysd), b.run_spec(ref, instruments=[b.instrument("frog", name="fr", points=128)]))
    rt = uf.run(uf.System.load(sysd), b.run_spec(test, instruments=[b.instrument("frog", name="ft", points=128)]))
    shg = lambda r, n: {"delay_s": r[n + "_delay_s"], "omega_rad_s": r[n + "_omega_rad_s"], "trace": r[n + "_trace"]}
    m = uf.retrieve_multi([{"trace": shg(rr, "fr"), "kind": "shg-frog", "pulse": "ref"},
                           {"trace": shg(rt, "ft"), "kind": "shg-frog", "pulse": "test"},
                           {"trace": xf, "kind": "xfrog", "pulse": "test", "gate": "ref"}], maxIterations=400)
    assert max(m["traceErrors"]) < 1.5e-2 and set(m["pulses"]) == {"ref", "test"}
    assert m["pulses"]["test"]["fwhmS"] == pytest.approx(rx.summary["output"]["fwhmS"], rel=0.06)
