# 29 -- Small-scale self-focusing in a 2-D plate (P / P_cr ≈ 100)

A 300 fs, 130 µJ pulse crosses 10 mm of fused silica in a flat-top beam of radius 0.3 mm: 3.3·10⁸ W, about 100 times the critical power for
self-focusing. A 1 % random amplitude noise with a 40 µm correlation length seeds the break-up. The beam is propagated in 2-D (x, y) on a
128 x 128 grid over 4 beam radii ([Small-scale self-focusing](../../reference/transverse.md#small-scale-self-focusing)).

```bash
cd docs/examples/29-small-scale-self-focusing
ultrafast run plate.ufs run.json -o results            # about 1 minute on a laptop CPU
```

```jsonc
"transverse": { "model": "cartesian", "nx": 128, "ny": 128, "halfWidthM": 0.6e-3,
  "initialProfile": { "kind": "flatTop", "noise": { "relativeAmplitudeRms": 0.01, "correlationLengthM": 40e-6, "seed": 1 } } }
```

Expected output (a fresh run, Apple-silicon laptop, 57 s):

```
  Grid auto-sized: N = 512, window = 12.81 ps, dt = 25.02 fs.
  energy 0.00013 J (T = 100.00 %), FWHM 381.6 fs (TL 111 fs), peak 3.263E+08 W, B = 4.27 rad
```

`summary.json -> elements[0].values` (the small-scale diagnostics):

| key | value | meaning |
|---|---|---|
| `peakPowerOverCritical` | 96.5 | peak power / P_cr, `P_cr = 3.77 lambda^2 / (8 pi n0 n2)` |
| `fluenceContrastIn` | 0.0148 | rms / mean of the fluence over the beam core (about twice the 1 % amplitude noise; the core holds only a few correlation cells) |
| `fluenceContrastOut` | 0.0465 | the same after the plate |
| `smallScaleGain` | 3.13 | their ratio: the noise has grown 3.1 x in 10 mm (B = 4.3 rad) and is not yet saturated |

The arrays `el_plate_x_m`, `el_plate_fluence_in_x` and `el_plate_fluence_out_x` in `results/result.npz` hold the fluence along y = 0.

## Things to try

* **The textbook seed.** Replace `noise` by `"ripple": {"periodM": 86e-6, "relativeAmplitude": 0.01}`: one ripple at the most unstable period
  `2 pi / sqrt(2 k Delta)` (`Delta = k0 n2 I`) grows as `cosh(Delta L)` -- the Bespalov-Talanov test checks this growth law to 6 %.
* **Resolution.** The grid must resolve the most unstable ripple (86 µm here; 128 points over 1.2 mm give 9.4 µm steps, 9 samples per period) and the
  correlation length of the seed. Run 64 x 64 and 256 x 256 for your beam and check that `smallScaleGain` has converged; 256 x 256 needs 4 x the memory
  and about 4 x the time of 128 x 128 (not run here).
* **Longer pulses and the GPU.** `"compute": {"backend": "auto"}` runs the 2-D steps on a CUDA / OpenCL device ([GPU plugin](../../plugins/gpu.md)); the time
  window of 12.8 ps (N = 512) is what the pulse needs, not 2^13 points.
* **More plates.** Chain several `bulk` elements: the profile travels with the beam ([Transverse](../../reference/transverse.md#the-profile-travels-with-the-beam)).
