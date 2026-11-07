"""Repro: trackedAngularDispersionXRadPerM of a grating (and a prism) is off by (lam_req / lam_snap)^2 - 1.
The diffraction angle (and the magnification cos t_out / cos t_in) use the requested carrier, the dtheta/domega -> dtheta/dlambda
conversion uses the grid-snapped one. Change the grid (n, window) and the error follows the snap. Expected: ratio = 1 on every grid.
Run: ULTRAFAST_LIBRARY=... PYTHONPATH=python/src .venv/bin/python tests/components/repro/grating_snapped_carrier.py"""
import math, sys
sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parents[1]))
from _common import b, uf, C

lam, lpm, aoi = 1030e-9, 1200.0, 30.0
for grid in (None, b.grid(n=4096, window_s=20e-12), b.grid(n=1024, window_s=7e-12)):
    s = b.system("g", [b.grating("G", lpm, aoi, order=-1)])
    spec = b.run_spec(b.pulse(lam, 100e-15, energy=1e-9), grid=grid, physics={"nonlinear": False})
    spec["inputBeam"] = {"kind": "gaussian", "w0M": 1e-3}
    r = uf.run(s, spec)
    so = math.sin(math.radians(aoi)) - lam * lpm * 1e3
    D = -lpm * 1e3 / math.sqrt(1 - so * so)
    snap = 2 * math.pi * C / r.summary["grid"]["omega0"]
    got = r.summary["elements"][0]["values"]["trackedAngularDispersionXRadPerM"]
    print(f"snapped carrier {snap * 1e9:.4f} nm  engine/analytic - 1 = {got / D - 1:+.3e}   (lam_req/lam_snap)^2 - 1 = {(lam / snap) ** 2 - 1:+.3e}")
