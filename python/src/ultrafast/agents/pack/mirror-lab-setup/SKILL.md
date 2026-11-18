---
name: mirror-lab-setup
description: Build a model of a user's real laser / beamline in UltrafastEngine from what they know about it (layout, components, measured spectra and traces), calibrate it against their data and say where the model and the lab disagree. Use when asked to model, simulate or digitally twin a lab setup.
---
# Mirroring a lab setup

Tools: `run-simulations` skill, `docs/reference/measured-data.md`, the fitting plugin (`docs/plugins/fitting.md`),
branched systems for interferometers and pump arms (`docs/reference/branches.md`).

1. **Interview first.** Ask for, in this order: the layout as a list of components in beam order with distances; the
   source (energy, duration, spectrum, polarisation, contrast, beam size); every medium (material, length, pressure,
   temperature); coatings and their dispersion; the diagnostics and where they sit. Ask which numbers were measured
   and which are nominal. Do not guess a number the user can look up.
2. **Use their data as given**: input spectrum and traces as `measured` (units from column names, background removed,
   resolution stated). Keep data files with the model; never edit them.
3. **Build in stages and validate each**: source alone against its measured spectrum and duration; then one component
   at a time against the measurement just after it, so a disagreement points at a component.
4. **Calibrate few unknowns with the fitting plugin** (energy reaching a stage, input chirp, one transform per
   unknown). One unknown per measurement; report uncertainties and correlations; do not fit what the data cannot constrain.
5. **Report the comparison honestly**: what matches, what does not, the most likely cause in the physics or the
   data (polarisation setting, measurement point, wavelength calibration), and what extra measurement would settle it.
6. Save `system.ufs`, `run.json`, the data and a README with the assumptions so the model can be re-run.
