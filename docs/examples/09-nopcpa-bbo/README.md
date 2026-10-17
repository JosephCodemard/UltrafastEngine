# 09 -- Non-collinear OPCPA in BBO

A 15 fs (transform-limited) 800 nm seed, stretched to ~0.6 ps, is amplified in 2 mm of BBO pumped by the 515 nm
second harmonic from [example 08](../08-shg-lbo/README.md). **Run example 08 first**: the pump is read from its saved results
(`"from": "../08-shg-lbo/results"`), so the pump has the exact temporal and spectral shape that the SHG produced.

```bash
(cd ../08-shg-lbo && ultrafast run shg.ufs run.json -o results)
ultrafast run nopcpa.ufs run.json -o results
ultrafast-plot results
```

How the crystal is set up:

* `"process": "opa"`, main beam = the signal (the run's pulse), aux beam = the pump. The idler is generated.
* `"noncollinearDeg": "magic"` solves for the pump-signal angle at which the idler's group velocity projected on
  the signal direction equals the signal's (2.56 deg); `"cut": "auto"` then phase-matches at the nominal
  wavelengths (signal at 22.0 deg to the optic axis, i.e. a crystal cut for the pump at 24.6 deg).
* `"delayS": "auto"` centres the pump on the signal. The `delayScan` instrument re-runs the crystal for a range of
  delays (best here ~-0.1 ps, from the pump-signal group-velocity mismatch). Make the crystal 3 mm long and the
  scan grows two maxima: at zero delay the centre of the pulse is over-saturated and back-converts.

What to look at:

* `elements[0].values` -- signal (~44 uJ, gain ~900) and idler energies,
  `conversionEfficiency` (signal out / all energy in, ~21 %) and `manleyRoweResidual` (photon bookkeeping, ~1e-8).
* `gain_bandwidth` -- the low-gain phase-matching curve vs signal wavelength (~220 nm FWHM for 2 mm: the reason
  for the non-collinear geometry; collinear would give ~20 nm).
* `ideal_compressor` -- the GDD/TOD that best compresses the amplified signal: ~24 fs, at its transform limit
  (gain narrowing took the 15 fs seed bandwidth down to ~24 fs).
* `results/result.npz` -> `probe_nopa_idler_*`, `probe_nopa_pump_*`: the idler (at ~1.4 um) and the depleted pump.

No example-08 results? Replace the aux `"from"` with a pulse:
`"pulse": {"shape": "gaussian", "centerWavelengthM": 515e-9, "durationFwhmS": 0.8e-12, "energyJ": 200e-6}`.

The 1-D model treats the beams through an overlap of Gaussian modes. For spatial gain narrowing,
walk-off and the pump's spatial depletion add `"transverse": {"model": "radial"}` (fast) or
`{"model": "cartesian"}` (needed for walk-off and the non-collinear geometry; slower) to the crystal.
