# Digital twin plugin (`twin`)

A **digital twin** of a lab is the engine model of it, fitted to the lab's measurements, *with an honest statement of what it can
predict*. The `twin` plugin is the project layer on top of the [fitting plugin](fitting.md): a folder layout, the multi-condition fit,
validation against held-out conditions with credible bands, a planner for the next measurement, a what-if explorer and drift tracking.
It is Python (`ultrafast_twin`, NumPy only) plus one command, `ultrafast twin`.

Examples: [build-the-twin](../examples/DigitalTwin/build-the-twin/README.md) and [explore-the-twin](../examples/DigitalTwin/explore-the-twin/README.md).

## Project layout

```
lab/
  twin.json        the project: system, run, instrument, sigma, conditions, parameters
  system.ufs       what you believe the lab is
  data/*.csv       one file per condition (the instrument's reading)
  fits/<name>/     fit.json, fitted-system.ufs, fitted-run.json, fitted-conditions.json   (latest.txt names the last fit)
  reports/         validation.json, validation.md
  goals.json       optional: checks a good twin must pass
```

`twin.json` keys: `system` (path), `run` (run spec with the instrument), `instrument` (name of the instrument that reads the data),
`sigma` (noise as a fraction of the peak), `conditions[]` (`name`, `variables`, `set`, `data`, `heldOut`), `parameters[]` (paths with
`initial`, `min`, `max`, optionally a `dependence` on a condition variable), `fit` (solver options). A condition **sets** values
(`"set": {"pulse/energyJ": 1.2e-6}`) in the system or the run for that measurement. The dependences, surfaces and networks come from the
[fitting plugin](fitting.md#fitting-across-conditions).

## Commands

```bash
ultrafast twin init lab [--template synthetic-lab | synthetic-lab-saturating] [--force]
ultrafast twin fit lab [--name NAME]
ultrafast twin validate lab [--samples 12] [--json]
ultrafast twin predict lab --var energyJ=2e-6 --set pulse/energyJ=2e-6
ultrafast twin design lab --variable energyJ --values 0,0.5e-6,1e-6,2e-6 --set-key pulse/energyJ      # or --placement
ultrafast twin track lab --data day1.csv --data day2.csv --var energyJ=1.2e-6 --process filter.centerNm=0.3
ultrafast twin check lab [--goals goals.json] | --benchmark
```

`ultrafast twin` starts `python -m ultrafast_twin`. It uses `ULTRAFAST_PYTHON` as the interpreter (else `python3`, or `python` on Windows):
point it at a Python that has NumPy and the `ultrafast` package (in a source checkout also set `PYTHONPATH=python/src`).
`ultrafast twin serve` (the MCP server for agents) is **not implemented**; it exits with code 2.

## Python

```python
import ultrafast as uf
uf.plugins.load_python_plugins()
import ultrafast_twin as tw

proj = tw.init("lab", "synthetic-lab")              # or tw.TwinProject("lab") for an existing project
twin = proj.fit("fit")                              # the multi-condition fit; returns a Twin
report = twin.validate()                            # verdict, per-condition chi2 and coverage, flags; writes reports/
p = twin.predict(variables={"energyJ": 2e-6}, set={"pulse/energyJ": 2e-6}, n_samples=12)    # x, y, y_sigma (1 sigma band), scalars
w = twin.whatif({"1 uJ": {...}, "3 uJ": {...}})     # paired draws: the difference has a proper uncertainty
d = twin.design(ultrafast_twin.design.energy_candidates(twin, "energyJ", [...], set_key="pulse/energyJ"))
trk = twin.tracker(parameters=["filter.centerNm"], process_sigma={"filter.centerNm": 0.3}); trk.update("day3.csv", variables=..., set=...)
```

## Validation rules

Stated in `ultrafast_twin/validate.py`, not tuned. `rt` = max(1, reduced chi² of the training conditions). A held-out condition is
**flagged** when its chi² per point exceeds 3 rt or fewer than 80 % of its points lie within 2 sigma of the credible band combined with
the stated noise; **marginal** above 1.5 rt. The verdict is `unvalidated` (no held-out condition), `fits-training-only` (any held-out
condition flagged), `marginal` or `ok`. Credible bands come from draws of the fit covariance (the Jacobian-based one of the fitting
plugin), so they cover the parameters the data constrain and nothing else: a model that is wrong is not made right by a band.

## Planner, what-if, tracking

* **Information gain** of a candidate measurement: `0.5 (logdet(F + J^T J / sigma^2) - logdet F)` for the fit's information matrix `F`
  (covariance inverse) and the candidate's Jacobian `J` (finite differences through the engine). `picks > 1` selects greedily, each pick
  updating the covariance. Candidates: values of a condition variable, or the instrument at each element (`placement_candidates`).
* **What-if** simulates each variant with the same parameter draws, so `deltaSigma` is the sigma of the paired difference.
* **Drift tracker**: an ensemble Kalman filter on the chosen parameters. Each measurement adds random-walk process noise (`process_sigma`,
  in the parameter's unit, default the fitted sigma times `drift`) and inflation, then updates from the recorded file through the engine.
  `drifted` lists parameters that have moved more than `alarm` (3) fitted sigma from the fit.

## Goal files and the benchmark

`goals.json` lists checks an agent or a CI job can run (`ultrafast twin check lab`): `validationVerdict`, `parameter`,
`parameterSigma`, `heldOutChi2`, `coverage`, `prediction`. `ultrafast twin check --benchmark` runs three design tasks whose answers follow from the
construction of the synthetic lab (placement; a phase proportional to the energy is invisible at zero energy; saturation is flagged).

## What was measured (S4 notes)

* `init` + `fit` on the synthetic lab: 1.6 s, 135 runs. Recovered centre 1033.008 nm (truth 1033), FWHM 8.497 nm (8.5), slope 2.9998 rad/µJ (3.0).
* Credible-band coverage inside 2 sigma: 94-98 % on all seven conditions.
* The saturating lab: held-out chi² per point 11.9 and 180: verdict `fits-training-only`.
* **Information on a nonlinear-phase slope is not monotonic in energy**: when the fringes become finer than the spectrometer resolution
  (12 rad at 4 µJ against 0.2 nm), the linearised information drops. A zero-energy shot pins the filter parameters the slope is
  correlated with and shrinks its uncertainty most in the synthetic lab.
* A tracker needs the expected drift in physical units: at the fitted sigma (0.006 nm) a 0.5 nm shift was followed only 17 % in three
  steps; with `process_sigma` 0.3 nm it followed in one update.

## Limits and not built

* The MCP server for agents (T6: eight workspace-scoped tools, user expressions refused, time and memory budgets) is designed
  (`docs/design/notes/s4-twin.md`, section 5) and **not built**.
* The only project template is the synthetic spectral lab; a spatial (wavefront) template does not exist.
* Gradients through the engine are finite differences (one run per parameter and condition); the network transform needed 1050 runs
  in its test.
