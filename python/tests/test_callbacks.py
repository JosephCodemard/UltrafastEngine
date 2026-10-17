import math

import numpy as np
import pytest

from conftest import requires_engine

pytestmark = requires_engine

import ultrafast as uf
from ultrafast import build as b

C = 299792458.0
LAM, N2, AREA, L = 1.03e-6, 2.5e-20, 1e-8, 0.01
GRID = {"n": 2048, "windowS": 4e-12, "auto": False}


def system(n2):
    return uf.System.load(b.system("flat", [b.bulk("nl", "flat", L, beam=b.constant_beam(area_m2=AREA))],
                                   materials={"flat": {"index": 1.45, "n2": n2}}))


def spec(**kw):
    gamma = N2 * 2 * math.pi * C / LAM / (C * AREA)
    return b.run_spec(b.pulse(LAM, 100e-15, peak_power_w=3.0 / (gamma * L)), grid=GRID, physics={"selfSteepening": False}, **kw)


def test_python_time_response_equals_builtin_kerr():
    ref = uf.run(system(N2), spec())
    run = uf.Run(system(0.0), spec(overrides=[b.override("element:nl", "timeResponse", {"kind": "callback", "name": "kerr"}, mode="add")]))
    calls = []

    def kerr(ctx):
        calls.append(ctx.n)
        return N2 * ctx.intensity          # returning dn_re

    run.time_response("kerr", kerr)
    res = run.execute()
    assert calls and calls[0] == 2048
    a, c = ref["output_ax_re"] + 1j * ref["output_ax_im"], res["output_ax_re"] + 1j * res["output_ax_im"]
    assert np.max(np.abs(a - c)) / np.max(np.abs(a)) < 1e-9


def test_lab_time_index_modulation_writes_in_place():
    # Delta n(t_lab) imposes phase modulation -> sidebands; ctx outputs written in place
    run = uf.Run(system(0.0), b.run_spec(b.pulse(LAM, 3e-12, energy=1e-9), grid={"windowS": 40e-12, "n": 8192},
                                          overrides=[b.override("element:nl", "timeResponse", "@pm", mode="add")]))
    k0 = 2 * math.pi / LAM

    def pm(ctx):
        ctx.dn_re[:] = 0.5 / (k0 * L) * np.sin(2 * math.pi * 3e12 * (ctx.t_lab - ctx.t_arrival))

    run.time_response("pm", pm)
    res = run.execute()
    w, s = res["output_omega_rad_s"], res["output_spectrum"]
    w0 = res.summary["grid"]["omega0"]
    band = lambda c: s[np.abs(w - c) < 0.4 * 2 * math.pi * 3e12].sum()
    assert band(w0 + 2 * math.pi * 3e12) / band(w0) == pytest.approx((0.24227 / 0.93847) ** 2, rel=0.02)


def test_index_and_filter_callbacks():
    sysd = b.system("idx", [b.bulk("g", "pyglass", 1e-3), b.filter_element("f", {"kind": "callback", "name": "bandpass"})],
                    materials={"pyglass": {"index": "@myindex"}})
    run = uf.Run(uf.System.load(sysd), b.run_spec(b.pulse(800e-9, 30e-15, energy=1e-9)))
    w0 = 2 * math.pi * C / 800e-9
    run.index("myindex", lambda w: 1.45 + 0.5 * 1e-31 * (w - w0) ** 2 * C / w * 0 + 0.004 * (w / w0 - 1))
    run.filter("bandpass", lambda w: (np.exp(-((w - w0) / 5e13) ** 2), np.zeros_like(w)))
    res = run.execute()
    losses = res.summary["lossBudget"]
    assert "filter" in losses and 0 < losses["filter"]["fractionOfInput"] < 1


def test_progress_cancel_and_callback_exceptions():
    run = uf.Run(system(N2), spec())
    with pytest.raises(uf.UltrafastError) as e:
        run.execute(progress=lambda eid, i, n, f: f > 0.2)
    assert e.value.code == -7  # cancelled

    run2 = uf.Run(system(0.0), spec(overrides=[b.override("element:nl", "timeResponse", "@boom", mode="add")]))

    def boom(ctx):
        raise ValueError("model failed")

    run2.time_response("boom", boom)
    with pytest.raises(ValueError, match="model failed"):
        run2.execute()
