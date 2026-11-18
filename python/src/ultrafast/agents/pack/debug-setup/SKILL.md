---
name: debug-setup
description: Find problems in a user's UltrafastEngine system file or results - wrong units, unresolved grids, missing physics switches, energy that does not close, polarisation or carrier mistakes, results that disagree with a measurement. Use when a simulation looks wrong, a run warns, or a model does not match the lab.
---
# Debugging a setup

Checks in this order (cheap first):

1. `ultrafast validate system.ufs run.json` (add `--graph` for branched) and read every warning; `ultrafast describe`
   shows what each element became. `ULTRAFAST_STRICT_OPTIONS=1` turns unknown keys into errors (typos).
2. **Units and names**: SI with the unit in the key (`lengthM`, `energyJ`, `durationFwhmS`). FWHM of intensity, not T0.
3. **Grid**: `summary["output"]["edgeEnergyFraction"]` < 1e-6 (else the window or band is too small); the spectrum must
   not touch the band edge; at least ~20 points across the shortest feature. Let the planner size it before a manual grid.
4. **Energy and budget**: input − output = booked losses (`summary["elements"]`, `graph.energyClosureJ` at rounding level).
5. **Physics switches**: Raman, self-steepening, plasma, ionisation and the spatial model only if the regime needs them
   (B-integral, peak intensity vs the ionisation threshold, beam collapse P/P_cr). Compare with the analytic estimate
   in `docs/reference/nonlinear.md`.
6. **Polarisation and carrier**: circular and linear light differ by 2/3 in the electronic Kerr effect; waveplate
   angles and which port a measurement sits on; a beam of another colour needs its own carrier.
7. **Vs the lab**: measurement point and instrument response (`measured` with the resolution), background, calibration;
   one stage at a time (`mirror-lab-setup`). Vary the suspect parameter in a study to see whether it can explain the gap.
8. If a minimal case still contradicts an analytic or published value, it is an engine bug: `/bug-report`.
Report: the cause, the evidence (numbers), the fix to the file, and what you could not check.
