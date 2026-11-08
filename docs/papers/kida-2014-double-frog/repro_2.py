#!/usr/bin/env python3
"""Minimal engine case 2: `retrieve_multi` with only the reference SHG trace and the XFROG trace (no test SHG trace), noise-free.

Finding (not an engine bug as far as we can tell, but not the paper's behaviour either). The truth (reference +195 fs^2, test +580 fs^2) has
G = 0, yet all six seeds stop in a wrong-direction solution (both GDD negative; G 1.3e-3 .. 3.7e-3 on the XFROG trace, 3e-3 .. 4e-3 on the
SHG trace). Raising the iterations from 600 to 3000 changes nothing (stagnation, not slow convergence). So the two-trace subset has a deep
local minimum in the wrong-direction basin that multiFrog's random start falls into 6 of 6 times, whereas the paper says the twin appears at random.
With the third trace (the SHG of the test pulse) the same six starts all find the truth (docs/papers/kida-2014-double-frog/run.py, tests
MultiTraceRetrievalTests). Run: python repro_2.py
"""
import numpy as np, ultrafast as uf
from ultrafast import build as b
C0=299792458.0
ref=b.pulse(800e-9,30e-15,energy=1e-9,gdd_s2=195e-30)
test=b.pulse(1200e-9,40e-15,energy=1e-9,gdd_s2=580e-30)
P=uf.System.load(b.system("p",[{"id":"x","type":"probe"}]))
rr=uf.run(P,b.run_spec(ref,instruments=[{"kind":"frog","name":"fr","type":"shg","points":128}]))
rx=uf.run(P,b.run_spec(test,instruments=[{"kind":"xfrog","name":"fx","points":128,"reference":{k:v for k,v in ref.items() if k!="energyJ"}}]))
mk=lambda r,n,k,**kw: dict(trace={"delay_s":r[n+"_delay_s"],"omega_rad_s":r[n+"_omega_rad_s"],"trace":np.asarray(r[n+"_trace"])/np.max(r[n+"_trace"])},kind=k,**kw)
def gdd(pulse,thr=0.05):
    sp=pulse["spectrum"]; lam,I,ph=[np.array(sp[k]) for k in("wavelengthsM","intensity","phaseRad")]
    w=2*np.pi*C0/lam;o=np.argsort(w);w,I,ph=w[o],I[o],np.unwrap(ph[o]);wm=(w*I).sum()/I.sum();m=I>thr*I.max()
    return 2*np.polyfit(w[m]-wm,ph[m],2,w=np.sqrt(I[m]))[0]*1e30
for its in (600,3000):
  for s in range(1,7):
    o=uf.retrieve_multi([mk(rr,"fr","shg-frog",pulse="ref"),mk(rx,"fx","xfrog",pulse="test",gate="ref")],maxIterations=its,seed=s)
    print(its,s,[f"{e:.5f}" for e in o["traceErrors"]],round(gdd(o["pulses"]["ref"]["pulse"])),round(gdd(o["pulses"]["test"]["pulse"])),round(o["pulses"]["ref"]["fwhmS"]*1e15,1),round(o["pulses"]["test"]["fwhmS"]*1e15,1),o["iterations"])
