# fitting plugin

Compares instruments with recorded data and calibrates parameters with error bars.

* `"measured"` on any instrument: CSV / NPZ / JSON recordings, units from column names, normalisation, background,
  per-point or relative uncertainties; curves, 2-D traces (FROG, d-scan, cameras) and scalar readings.
* `"fit"` in the run spec: Levenberg-Marquardt over paths into the system / run files, covariance, correlation and
  identifiability warnings, predictions with 1-sigma for every instrument and element (`fit.json`).
* Python module `ultrafast_fitting`: builders `measured`, `fit`, `fit_parameter`; the `measurements` and `fit`
  figures of `ultrafast-plot`.

Documentation: [docs/plugins/fitting.md](../../docs/plugins/fitting.md). Worked example: `docs/examples/21-measured-data`,
tutorial 4. Stage 2 of the roadmap rebuilds this plugin around `transform` elements.

Build: `dotnet build plugins/fitting/src -c Release` lays out the installable folder in `artifacts/plugins/fitting`
(it is also built and copied with the CLI, the test runner and `python python/tools/build_engine.py`).
