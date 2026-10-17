# 08 -- Second-harmonic generation in LBO (the pump line for example 09)

A 1 ps, 400 uJ Yb pulse at 1030 nm is frequency doubled in 2 mm of LBO (type I, XY plane). The crystal element
follows **one main beam**: it enters as the 1030 nm fundamental and leaves as the 515 nm second harmonic
(`"output": "sh"`). The unconverted fundamental is not lost: `"record": ["fundamental"]` keeps it as a by-product.

```bash
ultrafast run shg.ufs run.json -o results            # 1-D coupled waves, Gaussian-mode overlap (fast)
ultrafast run shg.ufs run-radial.json -o results-radial   # radial (Hankel) model across the beam
ultrafast-plot results
```

What to look at in `results/summary.json`:

* `elements[0].values` -- `phiDeg` (the cut found by `"cut": "auto"`: 13.8 deg), `deffPmPerV` (0.82 pm/V),
  `conversionEfficiency` (~52 %), `energy_sh_J`, `energy_fundamental_J`.
* `elements[0].carrierWavelengthM` and the note "carrier changed 1029.97 -> 514.95 nm": from here on the main
  beam is the SH, on its own (re-planned) grid. `outputGrid` describes it.
* `energyBudget` -- "to fundamental" is the energy carried off by the by-product; "numerical" is the solver's
  bookkeeping error (~1e-8 of the input).
* `instruments`: `conversion_shg` (energies and photon numbers of every field), `phaseMatchingCurve_shg`
  (angular acceptance, ~0.9 deg FWHM for 2 mm), `acceptance_wavelength` (spectral acceptance, ~11.5 nm).
* `results/result.npz` -> `probe_shg_fundamental_*`: the depleted fundamental (time and spectrum).

The radial run computes the conversion point by point across the beam: the centre converts more than the wings,
so the SH beam is narrower than a Gaussian-overlap estimate suggests, and the fundamental gets a depleted
centre. `results-radial/summary.json` -> `elements[0].beam` (radius, M^2) and the `beamProfile_shg` instrument
(fluence vs radius) show this. The 1-D model uses an overlap of Gaussian modes, which is exact at low
conversion and an estimate at high conversion (here 52 % vs 47 % radial).

Things to try:

* `"cut": {"thetaDeg": 90, "phiDeg": 13.8}` with `"detuneDeg": 0.3` -- detune the crystal and watch the efficiency.
* Change `"crystal"` to `"BBO"` (type I, `"cut": "auto"` gives 23.4 deg) or `"BiBO"`.
* `"overlap": "planeWave"` -- the textbook plane-wave estimate at the peak intensity (overestimates for Gaussian beams).
