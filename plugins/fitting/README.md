# fitting plugin

Makes the model reproduce your lab, then reuses what it learned.

* **`transform` elements**: fittable physical changes to the beam: spectral phase (Taylor or spline), spectral
  amplitude (Gaussian, tilt, spline or a filter model; passive; causal by Kramers–Kronig), energy, delay, Jones
  polarisation errors, replicas, spectral phase noise, incoherent ASE / exponential pedestals, wavefront (Zernike and
  pointing). Mark any number with `{"value": ..., "fit": true, "min", "max", "prior"}`. Add operator kinds from
  other plugins with `TransformOperators.Register`.
* **`"measured"`** on any instrument: CSV / NPZ / JSON recordings, units from column names, background,
  per-point or relative uncertainties, comparison in linear units or in dB, and a check of the recording's resolution.
* **`"fit"`** in the run spec: marked transform numbers plus any number by path (`pulse/...`, `element:ID/...`,
  `instrument:NAME/...`, `material:NAME/...`). Estimators are Levenberg–Marquardt, CMA-ES, ensemble MCMC or your
  own (`Estimators.Register`), with priors and smoothness. The fit writes `fit.json` (three layers: simulated,
  inferred, believed; influence map; provenance with data-file hashes; predictions with 1σ), plus
  `fitted-system.ufs` and `fitted-run.json` for new inputs.
* **`"backward"`** in the run spec: propagates a measured field back through the system. Linear terms are inverted
  with regularisation, nonlinear segments are integrated with z reversed, transforms are inverted, and active
  elements are linearised from a reference run. The reconstructed field is written to `reconstructed-pulse.json`.
* Python module `ultrafast_fitting`: builders (`transform`, the operator helpers, `fit_value`, `fit`, `backward`,
  `measured`, `fit_parameter`), accessors (`fit_report`, `fitted_system`, `reconstructed_pulse`, `comparisons`) and
  the `measurements` and `fit` figures of `ultrafast-plot`.

Documentation: [docs/plugins/fitting.md](../../docs/plugins/fitting.md). Worked examples: `docs/examples/26-transforms`
(transforms, new inputs, backward propagation) and `docs/examples/21-measured-data` (fitting by path), tutorial 4.

Build: `dotnet build plugins/fitting/src -c Release` lays out the installable folder in `artifacts/plugins/fitting`.
It is also built and copied with the CLI, the test runner and `python python/tools/build_engine.py`. Installing it
elsewhere: [docs/plugins/installing.md](../../docs/plugins/installing.md).
