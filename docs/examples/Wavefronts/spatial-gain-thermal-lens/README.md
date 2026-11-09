# 15 -- Spatially resolved gain and the thermal lens

A 2 ps, 100 µJ pulse at 10 kHz crosses a 4 mm Yb:YAG rod once, end-pumped by 60 W at 940 nm (super-Gaussian,
0.6 mm, double pass). The rod is modelled on a radial grid, and then the beam travels 0.4 m in free space.

```bash
ultrafast run rod.ufs run.json -o results-60W
ultrafast run rod.ufs run-lowpump.json -o results-5W     # same system, 5 W pump (a run-spec override)
ultrafast-plot results-60W
```

On the grid:

* every transverse point has its own inversion, so the gain and depletion follow the pump and beam profiles;
* the beam diffracts inside the rod;
* the heat load (quantum defect × absorbed pump) gives ΔT(r, z) and Δn = dn/dT·ΔT, applied as a
  position-dependent index ([docs/spatial-properties.md](../../reference/spatial-properties.md)).

| | 60 W pump | 5 W pump |
|---|---|---|
| absorbed pump / heat | 44.3 W / 3.87 W | 4.05 W / 0.35 W |
| on-axis inversion | 0.20 | 0.02 |
| energy gain | 1.42 | 0.92 (reabsorption: Yb:YAG is quasi-three-level) |
| thermal lens f_th | 0.54 m | 5.9 m |
| wavefront after the rod (R) | −0.51 m (converging) | −3.7 m |
| beam radius after the rod / after 0.4 m | 471 µm / 319 µm | 497 µm / 524 µm |
| M² | 1.034 (aberrated lens: the pump is not Gaussian) | 1.002 |

`gain.png` shows the inversion across the beam (flat inside the pump, zero outside). The pulse train converges
in ≈ 70 pulses (τ = 0.95 ms ≫ 100 µs period). The far-field camera's Strehl ratio (0.41 at 60 W) is computed
against a flat-phase beam, so it includes the defocus of the thermal lens as well as its aberration.
