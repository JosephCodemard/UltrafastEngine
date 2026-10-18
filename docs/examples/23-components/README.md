# 23 -- Optical components and beam tracking

One beam line with the optical-component models ([Optical components](../../reference/components.md)). No
element has a `beam` of its own: the run's `inputBeam` (w0 = 1 mm at the input) is tracked through every element.

```bash
ultrafast run components.ufs run.json -o results
```

What to look at (`results/summary.json`):

* `elements[].values.trackedBeamRadiusXM` / `YM`: the two R = 2 m mirrors at 8° focus x and y differently
  (f_t = 0.990 m, f_s = 1.010 m, see their notes), so the beam leaves the telescope slightly astigmatic
  (1.156 / 1.134 mm); the lens then focuses it to ~0.14 mm in the plate, whose note says it used the tracked beam.
* `hwp`: a 10th-order quartz half-wave plate -- 10.5 waves at the carrier, and 38 fs between the group delays of its
  axes, which depolarises a 120 fs pulse (`pol_hwp`: degree of polarisation 0.97, azimuth 44°).
* `tfp`: the HfO2/SiO2 stack at 56°: Tp = 0.970, Ts = 0.001 (`energy_tfp`: 46 % of the energy passes, because the
  half-wave plate turned x to 45°).
* `fr`: 45° Faraday rotation in TGG (`pol_fr`: azimuth 44° after the rotator).
* `wfe`: 0.1 wave rms of astigmatism over 3 mm becomes 18 nm rms over the 1.14 mm beam, Strehl ~0.988 (1-D beam:
  reported, not applied; give the element a cartesian transverse model to see the far field).
* `dm`: four bounces on dispersive mirrors with ±40 fs² ripple in complementary pairs: the ripple cancels.
* The B-integral is almost all in the plate at the focus (0.38 of 0.40 rad).

Try: remove `"inputBeam"` from `run.json` -- tracking switches off and the plate is propagated linearly (no beam),
with a warning; or set `"aoiDeg": 30` on the mirrors and watch the astigmatism grow.
