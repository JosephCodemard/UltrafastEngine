#!/usr/bin/env python3
"""Minimal engine case: crossCorrelator `resolutionS` and the dynamic-range floor / reported contrast.

A 50 fs pulse with ONE pre-pulse (1e-6 at -10 ps), no noise, `dynamicRangeDb` 105, a probe system with no elements.
Expected (resolution smears the main peak and the pre-pulse alike): the pre-pulse reads -60 dB and the floor -105 dB.
Observed with resolutionS = 1 ps: trace pre-pulse -60.0 dB but floor -92.9 dB (= -105 + 12.1 dB, the loss of peak height of a
50 fs correlation smoothed to 1 ps); the reported contrastAt_m10ps_db is -72.1 dB (= -60 - 12.1), which disagrees with the trace.
Without resolutionS the trace, the floor and the report are all right (-60.0 / -105.0 / -60.0).
"""
import math
import numpy as np
import ultrafast as uf
from ultrafast import build as b

def go(res):
    pu = b.pulse(800e-9, 50e-15, energy=1e-6)
    pu["pedestal"] = [{"kind": "prePulse", "delayS": -10e-12, "relativeIntensity": 1e-6}]
    kw = {"resolutionS": res} if res else {}
    inst = b.instrument("crossCorrelator", name="xc", rangeS=[-45e-12, 10e-12], stepS=0.5e-12, fineStepS=50e-15, fineRangeS=50e-12,
                        dynamicRangeDb=105, noise=False, probeDelaysS=[-10e-12], **kw)
    r = uf.run(uf.System.load(b.system("probe", [{"id": "x", "type": "probe"}])), b.run_spec(pu, grid={"windowS": 120e-12}, instruments=[inst]))
    d, s = np.asarray(r["xc_delay_s"]), np.asarray(r["xc_signal"])
    s = s / s.max()
    db = lambda t: 10 * math.log10(s[np.abs(d - t) < 0.4e-12].max())
    rep = [i for i in r.summary["instruments"] if i["name"] == "xc"][0]["values"]["contrastAt_m10ps_db"]
    print(f"resolutionS = {res:g} s: trace at -10 ps {db(-10e-12):.1f} dB, trace floor (-20 ps) {db(-20e-12):.1f} dB, reported contrastAt_m10ps_db {rep:.1f}")

go(0)
go(1e-12)
