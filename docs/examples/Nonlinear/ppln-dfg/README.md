# 10 -- Mid-IR difference-frequency generation in MgO:PPLN

A 1064 nm pump (1 uJ, 1 ps) and a 1550 nm signal (10 nJ) generate a 3.39 um idler in 5 mm of periodically poled
MgO:LiNbO3 (type 0, all extraordinary, d33). `"output": "idler"` makes the mid-IR the main beam from here on; the
signal and pump are kept as by-products.

```bash
ultrafast run dfg.ufs run.json -o results
ultrafast-plot results
```

* The QPM period is solved for the nominal wavelengths (30.6 um at 24.5 C) unless `"periodM"` is given.
  `"model": "averaged"` (default) uses deff x 2/pi with the grating phase; `"domains"` flips the sign of d domain
  by domain (the integrator lands on every domain wall) -- slower, and it captures the higher QPM orders.
* The pump is ~70 % depleted. Back-conversion with group-velocity mismatch carves sharp features into all three
  pulses and broadens their spectra, so the crystal is given `"spectralMargin": 5` (internal window 5x the input
  bandwidth instead of 2.5x). If a crystal's spectra reach the edge of its internal window, the engine absorbs that
  part, books it as "numerical" in the energy budget and warns once it exceeds 0.1 % -- the cue to raise the margin.
* Instruments: `period_tuning` (QPM period acceptance), `temperature_tuning` (MgO:LN Sellmeier is temperature
  dependent: ~50 K FWHM here), `conversion`.

## Cross-check against pyNLO

`compare_pynlo.py` runs the same DFG in [pyNLO](https://github.com/pyNLO/PyNLO) and in the engine (with pyNLO's
plane-wave coupling and domain-resolved poling) and plots the idler spectra:

```bash
python python/tools/get_pynlo.py           # once: clones the pinned pyNLO into python/third_party/pynlo
PYNLO_SRC=$(pwd)/python/third_party/pynlo/src python docs/examples/10-ppln-dfg/compare_pynlo.py
```

For the default case (0.5 uJ pump, 3 mm) idler, signal and pump energies agree to ~0.1 % and the spectra overlay
(`pynlo_comparison.png`); `compare_pynlo.py 2e-6 5e-3` (signal gain ~50, 35 % pump depletion) agrees to 0.1-0.5 %. The automated version is `python/tests/test_pynlo_dfg.py`.
