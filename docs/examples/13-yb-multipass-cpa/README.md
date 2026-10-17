# 13 -- Yb:YAG multipass CPA amplifier

A 250 fs, 1 µJ seed at 10 kHz is stretched by a grating pair to ≈ 430 ps. It is amplified in 8 passes through a
6 mm Yb:YAG rod, pumped from both ends with 150 W of cw diode light at 940 nm, and recompressed by the matching
grating pair.

```bash
ultrafast run amp.ufs run.json -o results
ultrafast-plot results          # results/plots/gain.png: gain spectrum, inversion, build-up, pulse train
```

What the amplifier element does (docs/gain.md):

* iterates the pulse train with the rate equations. It converges after 28 pulses: the inversion is pumped up
  during the 100 µs between pulses and pulled down by each pulse;
* propagates the last pulse with the full model: per pass, 16 slices of saturated spectral gain with time-ordered
  extraction (16 arrival groups, because the pulse is stretched), YAG dispersion and SPM;
* applies 3 % loss and 3 mm of fused silica between passes;
* reports the thermal lens of the rod.

| quantity | value |
|---|---|
| absorbed pump | 120 W (of 150 W) |
| on-axis inversion before the pulse | 0.315 (small-signal gain 2.9 per pass) |
| output after the amplifier | 1.46 mJ (14.6 W), gain 2000 over 8 passes |
| extraction (of the energy stored above transparency) | 5.4 % |
| gain narrowing | 4.46 nm → 2.51 nm FWHM |
| compressed pulse | 535 fs, transform limited (Strehl 0.999) |
| B-integral | 0.07 rad (well stretched) |
| thermal lens | 10.5 W of heat, ΔT(0) = 57 K, f_th = 0.14 m |

Things to try:

* `"passes": 12`, or raise the seed to watch saturation flatten the build-up (`energyPerPass`);
* `"repRateHz": 1000` in run.json: more energy per pulse and less average power;
* replace the stretcher with a shorter one (`separationM: -0.05`) and watch the B-integral grow.
