"""Repro: a binaryPhase zone plate (pure pi phase mask) reports transmission < 1 (energy booked as clip) because the cell-averaged
complex transmission has |avg| < 1 at the pi edges; docs/reference/transverse.md says pure phase masks keep unit magnitude.
The loss falls with finer sampling. Expected: 1.0."""
import sys
sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parents[1]))
from _common import b, uf

for pts in (512, 1024, 2048):
    grid = dict(points=pts, radius_m=3e-3, spectral_samples=1)
    els = [b.gap("src", 0.0, medium="Vacuum", transverse=b.transverse("radial", **grid, initial_profile=b.initial_profile("gaussian", radius_m=1e-3))),
           b.mask("zp", "zonePlate", focal_length_m=0.2, binary_phase=True), b.gap("g", 0.0, medium="Vacuum", transverse=b.transverse("radial", **grid))]
    r = uf.run(b.system("zp", els), b.run_spec(b.cw(1030e-9, 1.0)))
    print(pts, r.summary["elements"][1]["values"]["transmission"])
