# Changelog

## Unreleased

### Changed
- **Measured data is part of the core.** The instruments' `"measured"` key (comparison with recorded CSV / NPZ /
  JSON data), `RunResult.Comparisons` (`UltrafastEngine.Measured`), the `comparisons` / `measuredChi2` summary
  sections, the CLI's "measured data" lines and the `measurements` figure moved from the fitting plugin into the
  engine and work without any plugin. The fitting plugin keeps fits, transforms and backward propagation; its
  `result.Comparisons()`, `ultrafast_fitting.measured` and `ultrafast_fitting.comparisons` remain as aliases. Schema:
  `obj.fitting:measured` is now `obj.measured` (Python type `ultrafast.types.Measured`).
- Comparisons only compare the part of a recording that the simulated instrument covers: points outside its range
  are left out of χ², the rms error and the normalisation (kept with residual 0 and simulated NaN, so a fit's
  residual vectors keep their length), and counted in the notes.
- A recording coarser than the simulated instrument (`resolutionM` / `resolutionS` in `measured`) is matched by
  blurring the simulated curve with the missing Gaussian width (`matchResolution`, default true); a finer recording
  is reported as before. `background: "auto"` on curves uses the darker edge, and `"min"` / `"none"` are accepted.
- The time window makes room for ghost / replica satellites down to `grid.satelliteLevel` (default 1e-8): the
  longest delay at which the satellites of all elements together (combinations included, coincident delays summed
  in amplitude) reach that level, instead of every order of every element. 20 AR-coated plates now need about 1 ns
  of window instead of several.

### Added
- **Vector Raman response** (Hellwarth tensor: isotropic h_a and anisotropic h_b parts) for elliptical and circular
  light, in the 1-D and spatial models. `anisotropicFraction` on every delayed-response model; fused silica
  (`silica`, `blowWood`, `hollenbeck`, the built-in FusedSilica) defaults to 0.25, `linAgrawal` uses its own
  fc ha + fb hb. The Raman response now contributes to nonlinear ellipse rotation ((1 − fR)/3 + fR b/2 of B sin 2χ for
  long pulses) and circular light sees (1 − fR) 2/3 + fR (1 − b/2) of the linear index. Linearly polarised and scalar
  runs are unchanged. New tests `NerTests` check the analytic NER results.
- **Measured input spectra**: `background` (a number, `"auto"` = median of the darker edge, robust to a spectrum
  cut off on one side, `"min"`) and `threshold`; rows are sorted, repeated wavelengths merged and non-finite rows
  dropped, each reported in the notes; a floor above 0.3 % of the peak that is not subtracted gives a warning.
  `PulseSpec.Prepared()` / `MeasuredSpectrumPreparation` expose the cleaned spectrum.
- Python: core `b.measured(...)` (with `match_resolution`, `x_unit`, `y_unit`, `simulated`),
  `b.measured_spectrum(..., background=, threshold=)`, `b.measured_spectrum_csv(...)`, `Result.comparisons`,
  `Result.comparison(name)` (also on results folders), module `ultrafast.measured` (typed `Comparison`, `curve`,
  `trace`, `total_chi2`), `uf.plot(res, "measurements")`.
- Docs: [Measured data](docs/reference/measured-data.md) (input spectra, cleaning, background, comparisons, range and
  resolution, file formats, Python); nonlinear.md section on polarisation, circular light and NER; `grid.satelliteLevel`.

### Fixed
- A measured input spectrum with a repeated wavelength (common in digitised curves, together with rows out of order)
  turned every result into NaN with only a "could not size the grid" warning. Rows are now sorted and repeated
  wavelengths merged; a non-finite input energy stops the run with a clear message.
- The B-integral reported for elliptical and circular light left out the coherent term of the χ(3) polarisation
  (5/6 instead of 2/3 of the linear value for circular light). It is now the peak of the power-weighted effective
  index change over all polarisation terms. The propagation itself was correct.

### Added (earlier in this release: fitting plugin stage 2, fibres, instruments)
- **Fitting plugin, stage 2: `transform` elements.** Fittable physical changes to the beam, inserted where they
  happen. Operators: `spectralPhase` (Taylor and not-a-knot spline knots), `spectralAmplitude` (Gaussian /
  super-Gaussian, tilt in dB/nm, spline, any filter model; passive; `causal` minimum phase from the Kramers-Kronig /
  Bode relation), `energy`, `delay`, `jones`, `replica`, `phaseNoise`, incoherent `pedestal` (ASE / exponential) and
  `wavefront` (Zernike and pointing on the transverse profile). Consecutive linear operators compile into engine
  linear terms (grid planner, loss budget). Any number can be a fit object `{value, fit, min, max, prior, scale}` or be
  named in the operator's `fit` list with `bounds`, `priors` and `smoothness`. Other plugins add operator kinds
  with `TransformOperators.Register`.
- **Fits** (`"fit"`): marked transform numbers are collected automatically, and numbers can also be given by path,
  including `instrument:NAME/...` (instrument-response parameters such as resolution or calibration). Replaceable
  estimators: `levenbergMarquardt`, `cmaes` (global, polished by LM), `mcmc` (affine-invariant ensemble; credible
  intervals) and plugin estimators via `Estimators.Register`. Fits support Gaussian priors, knot smoothness and
  parallel evaluations. `fit.json` gains `layers` (simulated → inferred → believed), `influence` (Fisher-information
  share per instrument and per prior), `provenance` (versions, options, SHA-256 of every data file, parameter
  origins) and `dataChi2Final`. New output files: `fitted-system.ufs` (fit objects replaced by the fitted numbers,
  ready for new inputs) and `fitted-run.json`. New arrays: `<name>_nominal_on_measured`.
- **Backward propagation** (`"backward"`): the pulse is the field measured at `from`, and the run finds the field at
  every plane up to `to`. Linear terms use a Tikhonov-regularised Jones inverse; nonlinear segments are integrated
  with z reversed; transforms are inverted; active elements are refused, linearised from a forward reference run, or
  passed through. Instruments work at every plane, and the field is written to `reconstructed-pulse.json` as a
  pulse object.
- **Measured data**: `"scale": "log"` compares in dB (`sigmaDb`, `floorDb`) for contrast and cross-correlator data;
  data columns in dB are read; the cross-correlator can be compared; `resolutionM` / `resolutionS` state the
  recording's resolution and are checked against the simulated instrument.
- **Instrument `axis`** (every curve instrument): readings at explicit points or on a from / to / step grid, pixel or
  bin integration and a Gaussian resolution; dB curves follow their linear counterparts. Settings echo the axis.
- **Measured input spectra**: `resolutionM` (recording resolution), `deconvolve` (Richardson-Lucy),
  `extrapolate: "gaussian"` (tails fitted to the data), and a description of the measured range in the run notes.
- Schema: numbers may be declared `Fittable` (number or fit object). The run spec keys `fit` and `backward`, and the
  `transform` element, give a clear message when the fitting plugin is missing.
- `Executor.MeasureInstruments` (public): run the instruments on recorded pulses (used by run modes).
- Python: `ultrafast_fitting.transform` with every operator builder, `fit_value`, `fit(estimator=..., **options)`,
  `backward`, `measured(scale=..., resolution_m=...)`, `fitted_system`, `reconstructed_pulse`; `b.axis`,
  `b.measured_spectrum`. Figures: the fit figure shows credible intervals and the influence map; measurement figures
  show the simulated layer.
- Example 26 (transforms fitted to a CPA line, reused on a new seed, backward propagation to the seed); docs:
  rewritten fitting-plugin guide, new [Installing plugins](docs/plugins/installing.md) page, instrument `axis`
  section; README note on AI assistance.
- **`fiber` element** (`fibre`, `waveguide`): Taylor betas (SI or ps^k/km), dispersion curves D(lambda), or a material;
  gamma (constant or an expression in z) or n2 + effective area; loss (flat or vs wavelength); flat gain and gain
  spectra scaled to a target output; reports soliton number, dispersion / nonlinear / fission lengths and the
  dispersive-wave wavelength for the pulse that enters it. Python `b.fiber`. Docs: reference/fibers.md.
- **Raman models** `linAgrawal` (Lin & Agrawal 2006, fR 0.245) and `hollenbeck` (Hollenbeck & Cantrell 2002, 13 modes);
  `blowWood` alias of `silica`.
- **Quantum noise** on the input (`pulse.noise`: one photon per mode) and a **coherence study**
  (`"mode": "coherence"`, `uf.coherence`): |g12(lambda)| over independent noise realisations.
- **Instrument device model**: every instrument accepts a spectral `filter` and a `detector` (calibration, noise,
  noise floor / dynamic range, bit depth, seed); autocorrelator `resolutionS` and `shgAcceptanceFwhmM`; FROG and
  d-scan `spectralResolutionM` and `shgAcceptanceFwhmM`; spectrometer `wavelengthOffsetM`; polarimeter
  `extinctionRatio`. Every instrument output carries a `settings` block with each parameter's value in effect.
- **Ghosts** of the end faces of `bulk`, `crystal`, `gain`, `amplifier` and thick `lens` elements.
- Example 25 (PCF supercontinuum benchmark with coherence); docs: capabilities and limitations page, rewritten
  README and overview, forward-looking roadmap (history in docs/design/history.md).

### Fixed (earlier in this release)
- Plots no longer cut off data: dB axes (spectra, contrast, cross-correlator, probes, comparisons) follow the data's
  own floor (instead of fixed -50 / -70 / -100 dB). The contrast panel shows the whole window, the cross-correlator
  has its own dB panel, and contrast comparisons use a log scale with a dB residual.
- Fit figure: values in nm, fs², dB and dimensionless units are no longer given SI prefixes ("1.03 knm").
- Spectrometer `normalize`: the schema declared "none" | "peak" | "area" while the instrument read a boolean. The
  instrument now implements all three ("none" = spectral energy density in J/m), keeps true / false as legacy forms
  (declared in the schema), and defaults to "peak" as before.
- Declared defaults now match the code: d-scan `steps` (101), and the defaults of points, grid caps and delay-scan
  ranges that were undocumented.
- The Raman kernel is normalised on the time grid (the delayed fraction is exactly fR at any step); fibre results now
  agree with pyNLO to better than 0.3 %.
- The grid planner ignores the input's quantum noise.
- `ultrafast study` prints long analysis arrays as their length (they are in the results folder).
- Source comments and docs point to the current documentation paths; `pip install` instructions reflect the
  from-source installation.

## 2.1.8-beta (2026-10) -- Stage 1 complete: the stable core

Version jump: 2.1.8 marks Stage 1 sub-stages 1.2-1.8 on top of 2.0.0-alpha.2. File formats are unchanged (version 2);
plugins declare `"engine": "2.1.8-beta"`.

### Behaviour changes (check your results)
- **Air and N2: n2 is now the electronic part only** (N2 8.0e-24, air 8.5e-24 m²/W per bar at 273 K; before: 1.1e-23
  for N2, air scaled from it). The rotational Raman response, which was lumped into n2 before, is a delayed response
  of its own and on by default for N2, O2, CO2, H2 and air (`"rotationalRaman": false` in a material switches it
  off). Long pulses (≫ 100 fs) see about the same total; short pulses see less nonlinearity than before.
- The grid planner estimates the B-integral of each nonlinear element with the peak power *after* the dispersion
  before it (a stretched pulse in an amplifier no longer forces a huge grid).
- A Gaussian `inputBeam` now describes the beam at the system input and is tracked through the system
  (`physics.beamTracking`: `auto`); before, it was copied unchanged to every element without a `beam`, with z
  measured from each element's start. Use `"beamTracking": "off"` for the old behaviour.
- Study rows (`study.csv`) are in the order of the points, also with `parallel` > 1.

### 1.2 Optical components
- Gaussian beam tracking (reduced q-parameters x / y) through every element; tracked radius and curvature in each
  element's `values`; used by nonlinear bulk, plates, crystals, spatial elements, surfaces (fluence, clipping), cameras.
- Off-axis curved mirrors (f_t = R cos θ / 2, f_s = R / 2 cos θ), astigmatic lenses (`focalLengthXM` / `YM`).
- Aberrations (`aberration`: Zernike in Noll order, random figure) on mirrors, surfaces and lenses; `wavefront`
  element; Maréchal Strehl estimate on 1-D beams.
- `plate` / `window` with ghost replicas (delay, intensity, mode overlap from walk-off and wedge); `ndFilter`
  (absorptive / reflective, OD or OD(λ)); `thinFilmPolarizer` / `tfp` (TMM quarter-wave stack at its angle);
  `faradayRotator` (Verdet dispersion, non-reciprocal); thick multi-order, zero-order and compound waveplates with
  birefringent materials (Quartz, MgF2, Sapphire, Calcite, YVO4, aBBO, TGG approximate).
- GDD ripple of chirped mirrors (`rippleGddFs2`, complementary pairs) and the `gddRipple` coating kind;
  `chirpedMirror` accepts `gddFs2` / `todFs3` / `fodFs4`.

### 1.3 Contrast and polarisation
- `pulse.pedestal`: replica / pre- / post-pulse, ASE, spectral phase noise, exponential pedestal.
- `crossCorrelator` (`thirdOrderCorrelator`) instrument: third-order correlation with noise floor, fine/coarse scan,
  resolution, SH acceptance, polarisation, pre/post contrast.
- Vector (Jones) fields on radial and cartesian grids (`transverse.polarization`); polarisation elements act on
  carried profiles.

### 1.4 Gases
- Rotational Raman of N2, O2, CO2, H2 and air (isotropic and anisotropic tensor response, pressure and temperature).
- refractiveindex.info reader (formulas 1-9, tabulated n / k / nk, SPECS scaling), 20 bundled pages, user library
  (`$ULTRAFAST_HOME/materials`), `ULTRAFAST_RII_PATH`; `ultrafast materials list|show|import`.

### 1.5 L3 propagation
- UPPE coupling on spatial grids (`transverse.equation: "uppe"`).
- Photo-ionisation (PPT with Coulomb correction, ADK, Keldysh, MPI) and Drude plasma (avalanche, recombination,
  ionisation losses); `ionization` on elements or `physics.ionization`; electron density reported.

### 1.6 Speed and studies
- Mixed-radix (2-13) and Bluestein FFTs for any length (cartesian grids of any even size); SIMD complex kernels.
- `IComputeBackend` (`run.compute`), plugin registration; CPU back-end built in.
- Parameter studies (`ultrafast study`, `uf.study`): grid / random / LHS / Sobol scans, Nelder-Mead optimisation,
  local and Sobol sensitivities, frozen grid, prefix caching, parallel batches.

### 1.7 Pulse retrieval
- `IRetrieval`: PCGPA and ePIE (FROG), d-scan; `ultrafast retrieve` (also straight from a results folder's
  `result.npz`, `--instrument` to pick the trace), `uf.retrieve`; retrieved pulse usable as a run input.

### 1.8 Consolidation
- `uf_command` C export, `uf.command`; Python `ultrafast.types` (TypedDicts generated from the schemas; builders
  return them, so editors complete and check element keys) and a generated API reference.
- Docs reorganised: reference pages in `docs/reference/`, examples in `docs/examples/`; new pages: components, gases,
  ionisation, retrieval, studies, speed, Python API, for AI agents; tutorial 5 (contrast); `docs/assets/`
  (synthetic measurements with an answer key); `AGENTS.md`, `llms.txt`.
- Examples 22 (contrast chain + study), 23 (components and beam tracking), 24 (air focus with rotational Raman,
  ionisation and FROG retrieval).

## 2.0.0-alpha.2 (2026-10)

### Stage 1.1: architecture -- plugins, option registry, schemas, unified camera (2026-10-16)
- **Plugin system** (`UltrafastEngine.Plugins`): `IPlugin` + `plugin.json`, discovery (`ULTRAFAST_PLUGIN_PATH`,
  `plugins/` next to the engine, the source checkout, `~/.ultrafast/plugins`), `ultrafast plugins
  list|paths|install|remove`, Python `uf.plugins`. Extension points: element types, instrument kinds, models,
  instrument hooks, run modes, result sections and files, CLI commands and reports, spec keys; plugin Python modules
  register figures. Plugins load into the engine's own load context (works under Python's hostfxr host); NativeAOT
  hosts register them in code.
- **Fitting moved to a plugin** (`plugins/fitting`): `"measured"`, `"fit"`, `fit.json`, the CLI reports, and the
  Python module `ultrafast_fitting` (builders `measured`, `fit`, `fit_parameter`; the `measurements` and `fit`
  figures). `RunResult.Comparisons` / `Fit` became `result.Comparisons()` / `result.Fit()` (plugin extension methods).
- **Zemax moved to a plugin** (`plugins/zemax`): sampling library, the `zemax-build` / `zemax-replay` / `zemax-demo`
  commands, the OpticStudio program in `tools/net48`, the ZOS-API stubs.
- **Option registry** (`UltrafastEngine.Schema`): every element, instrument, model kind and spec section declares
  its keys (type, unit, default, meaning) once. From it: JSON Schemas `schemas/ufs.schema.json` and
  `schemas/run.schema.json` (`ultrafast schema`; `"$schema"` gives editor completion), unknown-key / type warnings
  with "did you mean" suggestions on every run (`ULTRAFAST_STRICT_OPTIONS=1` makes them errors), and the generated
  [option reference](docs/reference/options.md) (`ultrafast reference`).
- **Format version 2** with automatic migrations (`ultrafast migrate`): v1 cameras become the unified camera.
- **One `camera`**: `imaging` (`near`, `far`, `relay` through thin lenses and gaps; `propagateM`, `focalLengthM`,
  `magnification`) + `sensor` (pixels, pixel size, quantum efficiency as any filter model, bit depth, full scale,
  read noise, background, hot pixels; raw `counts` array). `nearFieldCamera` / `farFieldCamera` remain as aliases.
- **Unique instrument names**: repeated or missing names get `_2`, `_3`, ... (warning when a given name repeats).
- All examples carry `"$schema"` and version 2. Docs: reference/plugins.md, reference/schema.md,
  reference/options.md (generated), plugins/fitting.md (was measurements.md; sampling moved to instruments.md),
  plugins/zemax.md; camera section rewritten.
- Tests: `PluginTests` (plugin element + instrument, discovery, install/remove, versions, unique names, unified
  camera incl. a 2f relay), `SchemaTests` (all examples use declared options, typo suggestions, schemas cover every
  element and instrument, committed schemas and reference are current, migrations); Python `test_schema.py`
  (examples validate against the schemas; typos caught). The whole C# and Python suites pass in strict-options mode.

## 2.0.0-alpha.1 (2026-10)

### Phase 2.5: sampling, measured data, calibration (2026-10-14)
- **Sampling** (`"sampling"` on any non-imaging instrument): what part of the beam the instrument sees -- near
  field (optionally after `propagateM`) or far field (focal plane of a lens of `focalLengthM`), through a
  `pinhole`, `iris`, `fibre` (with an NA check), `slit`, `point` or the whole beam (`integrated`), centred anywhere.
  The aperture's spectral transmission and averaged phase are applied before the instrument measures; reports
  `sampling` and `samplingTransmission`. Uses the carried transverse profile, or the Gaussian beam model in 1-D.
- **Measured data** (`"measured"` on any instrument): CSV (header, units from column-name suffixes such as `_nm`,
  `_fs`, `_ps`, `_mm`, `_fs2`, `_uj`), NPZ (float/int/bool, compressed or not, C/Fortran order) or JSON scalar
  readings. Curves (spectrometer, autocorrelator, scope, beam profile, delay scan, phase-matching curve), traces
  (FROG, d-scan, cameras) and values (any instrument scalar). Background (`auto`: median at the axis edges),
  normalisation (`peak` with a least-squares data scale, `maximum`, `area`, `scale`, `none`), `rangeX`, σ per point
  or relative, weights, explicit column mapping, and a generic `simulated` mapping for any instrument.
  `summary.json → comparisons[]` / `measuredChi2`, arrays `<name>_measured_*` and `_simulated_on_measured`; a bad file
  is a warning, not a failure.
- **Calibration fit** (`"fit"` in the run spec): Levenberg-Marquardt on scaled, bounded parameters addressed by path
  (`pulse/...`, `element:ID/...` including list indices, `material:NAME/prop` as an override, `run/...`,
  `system/...`); grid frozen at twice the planned bandwidth; covariance from a pseudo-inverse (scaled by the reduced
  χ² when > 1); warnings for strong correlations, unidentifiable combinations, parameters without effect and
  values at a bound. **Predictions** with 1σ for every instrument value (`<value>Sigma`) and per-element pulse
  metrics, and 1σ bands on the compared curves. `fit.json`, `summary.json → fit`; the CLI prints the parameters and
  per-instrument χ² / rms before → after; `uf.run` and `uf_run_execute` fit when the spec has `"fit"`.
- CLI prints a comparison table for runs with measured data; fit runs report their total elapsed time.
- `ultrafast-plot`: `measurements` figure (data vs simulation with residuals, trace differences, scalar readings
  in σ) and `fit` figure (parameters ± σ, cost per iteration, correlation matrix, χ² per instrument).
- Python builders `b.sampling`, `b.measured`, `b.fit_parameter`, `b.fit`, `run_spec(fit=)`.
- Pulse chirp and the `dispersion` element accept `gddFs2` / `todFs3` / `fodFs4`; the NPZ reader handles every
  common dtype and npy v2/v3 headers.
- Example **21** (recorded fibre spectrum, autocorrelation, d-scan NPZ and power reading: compare, calibrate three
  unknowns, predict unmeasured instruments). Examples 11 and 17 gained sampled instruments (pinhole, slit, fibre in
  the far field). New `examples/tutorial` files.
- Docs: **four tutorials** (first run and the CLI; diagnostics and sampling; a CPA + SHG chain with overrides and
  Python; your own measured data and fitting), `measurements.md`; instruments, run-spec, results, python, index,
  getting-started updated.
- Tests: 13 C# (aperture transmissions vs Gaussian analytics, far-field fibre colour, CSV/NPZ units, zero
  residual on self-generated data for curves / traces / values, normalisations and background, missing files,
  pseudo-inverse null space, fit paths, fit recovery of energy and chirp, unidentifiable parameter) and 4 Python.

### Phase 2.3: oscillators, saturable absorbers, broadband χ(2), ASE, thermal lens to OpticStudio (2026-10-13)
- **`oscillator` element** (replaces the incoming beam): `parametric` (given output pulse), `haus` (lumped master
  equation: saturated gain with spectral shape, loss, GDD/TOD, SPM, saturable absorber) and `cavity` (a real gain
  medium with cw pump and average-power inversion dynamics, plus any engine elements run every round trip --
  chirped mirrors, SESAMs, Kerr media, apertures, lenses, spatial grids). Start from quantum noise or from the
  run's pulse; convergence detection; iterates on `gridPoints` of the run's window and embeds the result exactly.
- Diagnostics: soliton area theorem (`solitonFwhmS`), multi-pulsing and cw-background detection, Q-switched
  mode-locking criterion (Hönninger), round-trip GDD/B, timing offset; evolution arrays and an `oscillator` figure
  (build-up, pulse and spectrum vs round trip).
- **`saturableAbsorber` element** (`sesam`): slow (recovery time, saturation fluence), fast and Kerr-lens loss
  modulation. **Apertures act on carried transverse profiles** (hard circle/rectangle, soft Gaussian / super-Gaussian).
- **Broadband χ(2)** in the carrier-resolved mode (`timeResponse` kind `chi2`, optional QPM period), with a dealiased
  drive that conserves energy at high conversion.
- **ASE** in 1-D gain and amplifier elements (`"ase": true`).
- **Thermal lens to OpticStudio**: gain elements record the lens as a Gradient-1 GRIN; `UltrafastEngine.Zemax
  thermal-lens` writes it into a surface (typed ZOS-API `ISurfaceGradient1`).
- Expression time responses see `x`, `y`, `r`, `theta`, `spatial` and the running fluence `F`.
- Python: `ultrafast.laser.Oscillator` builder, `b.saturable_absorber`, `b.soft_aperture`.
- Fixes: d-scan panels with a glass insertion axis; circled instrument numbers above 10 (font); the carrier-resolved
  energy diagnostic counts only the propagated (positive-frequency) half; amplifier/gain output pulses carry the
  element's repetition rate (pulse picker); ZOS-API stubs complete again (environment data).
- Examples 17 (every instrument), 18 (your own n(x, y, z, t, I, F)), 19 (KLM Ti:sapphire, Haus, from noise),
  20 (SESAM Yb:YAG oscillator → stretcher → amplifier → compressor). Docs: oscillators.md; nonlinear, transverse,
  gain, zemax, models, python updated.
- Validation: soliton area theorem (Haus and real cavity), cw gain clamping, SESAM saturation, fast absorbers,
  lens-guide cavity eigenmode, aperture transmission, χ(2) low-conversion formula and energy conservation, ASE
  budget, position/fluence variables, Gradient-1 export.

### Phase 2.2: gain media, amplifiers, position-dependent properties (2026-10-13)
- **Position-dependent material properties** on any element: `indexProfile` (Δn), `absorptionProfile` (α) and
  `n2Scale` as functions of x, y, z, r, θ -- number, expression, Gaussian / super-Gaussian, table (r, z, rz, xy,
  xyz) or callback (`"@name"`: Python `fields=` / `Run.field`, C# `FieldCallback`, C `uf_run_set_field_callback`).
  Applied point by point on spatial grids (with step control), beam-averaged in 1-D. `ResponseContext` gained
  `X`, `Y`, `R`, `Theta`, `Spatial`, so custom n(t, I) responses can also depend on position.
- **Gain media**: Yb:YAG, Ti:sapphire, Nd:YAG library (McCumber-linked cross sections for quasi-three-level media)
  and user media (`base` + overrides, Gaussian bands, tables, expressions, callbacks).
- **Pump and inversion**: cw or pulsed pumps (top hat / Gaussian / super-Gaussian, single or double pass, delay),
  rate equations between pulses with pump bleaching, or the inversion given as a fraction or small-signal gain;
  doping profiles.
- **`gain` element**: spectrally resolved gain with Frantz-Nodvik-exact saturation per slice and time-ordered
  saturation (arrival-ordered spectral groups -> CPA red shift), host dispersion and SPM; 1-D or spatial (per-point
  inversion, diffraction, gain guiding).
- **`amplifier` element**: multipass (`betweenPasses` loss / GDD / TOD / materials) and regenerative
  (`roundTrips` N or `"auto"`, cavity loss / dispersion / materials).
- **Pulse-train steady state** at the repetition rate (energy model, convergence and period-doubling detection;
  the last pulse with the full model), **thermal lens** from the heat load (radial heat equation; applied as Δn(r, z)
  in spatial gain, as a thin lens on a carried profile in 1-D, reported by amplifiers).
- Element arrays in `result.npz` (`el_<id>_<name>`: gain spectrum, inversion, build-up, train). `gain` figure
  in `ultrafast-plot`; gain / amplifier glyphs (with pump arrow, ×N) in the system diagram. Python builders
  `b.pump`, `b.gain`, `b.multipass_amplifier`, `b.regenerative_amplifier`.
- Examples 13 (Yb:YAG multipass CPA, 1.46 mJ at 10 kHz), 14 (Ti:sapphire regen, 1.8 mJ, 42 fs), 15 (spatial
  gain + thermal lens with cameras), 16 (GRIN lens four ways). Docs: gain.md, spatial-properties.md.
- Validation: Frantz-Nodvik, small-signal gain, cw rate-equation steady state, thermal-lens focal lengths,
  regen growth (G0²(1-L))^N, auto ejection, time-ordering red shift, train vs full model, energy budgets,
  GRIN focus, exact Gaussian absorption, field kinds agree, n2 scaling.

### Diagnostics and plotting (2026-10-13)
- Beam cameras: `nearFieldCamera` (pixels, pixel size, magnification, refocus `propagateM`, band-pass, bit depth,
  read noise), `farFieldCamera` (focal plane of a lens by exact Fourier/Hankel integration; divergence, Strehl),
  `wavefrontSensor` (wavefront map, Zernike fit, radius of curvature). Ideal-Gaussian fallback on 1-D elements.
- Virtual instruments, flagged `"virtual": true`: `wigner` (Wigner distribution or spectrogram), `fieldProbe`
  (phase, instantaneous wavelength, GD/GDD vs wavelength, CEP, real field), `spaceTime` (I(r, t), S(r, λ),
  pulse-front delay, spatial chirp).
- `ultrafast-plot`: `system` figure (schematic with carrier-coloured beam, aux beams, by-products, probes and
  numbered instruments; repeated units collapsed), the schematic at the top of `overview`, instrument panels in
  system order titled with their number and location, image panels for cameras/wavefront/Wigner/space-time.
- Element records list the aux beams they took in (`elements[i].aux`).
- Fix: beam radius and M² of a carried profile were averaged with equal weight over its frequency samples (so a
  spectrally broadened beam's radius was biased towards the SPM wings); they are now energy-weighted. The Kerr
  numbers in example 11 changed accordingly (the plate exit radius is 98 um, not 61 um).
- Example 02 has diagnostics at the input, after the stretcher and at the output; example 11 has cameras, a
  wavefront sensor and a space-time map.

### Phase 2.1: active elements, spatial models, χ(2) crystals
- **Active elements** (`IActiveElement`): one main beam in, one out; aux beams (`pulse`, or `from` a previous run's
  results, `delayS` or `"auto"`), by-products (`res.At["id#field"]`, `probe_<id>_<field>_*`), signed energy budget
  (`energyBudget`; aux energy negative), carrier changes with exact resampling onto a re-planned downstream grid.
- **`crystal` element**: SHG, SFG, OPA, DFG in BBO, LBO, KTP, KDP, LiNbO3, MgO:LiNbO3, BiBO, AgGaSe2 or user crystals;
  uniaxial and biaxial eigenmodes, walk-off, deff from the d tensor; types I/II/0; `cut: "auto"`; non-collinear and
  magic angle; QPM (averaged, or domain-resolved with the integrator landing on every wall; chirped gratings);
  exact dispersion of every field; χ(3) SPM/XPM; noise seeding; 1-D overlap models `gaussian` / `planeWave` / `areaM2`;
  `spectralMargin` with an absorbing spectral boundary and a warning when energy reaches it.
- **Phase-matching calculator**: `ultrafast crystal`, `uf.crystal.phase_match`, `uf_crystal_phase_match`.
- **Spatial models**: `transverse: radial` (Hankel) / `cartesian` (FFT) for bulk, gap and crystal elements; the transverse
  profile travels with the beam (lens, curved mirror, free space); beam radius and M² per element.
- **User elements** (`type: "user"`) in Python (`Run.element`), C# (`ElementCallback`) and C (`uf_run_set_element_callback`).
- Instruments: `conversion`, `phaseMatchingCurve`, `delayScan`, `beamProfile`. Plotting: signed energy budget,
  `byproducts` figure, panels for the new instruments, carrier / beam radius / M² per element; mixed input/output grids.
- Examples 08 (SHG in LBO), 09 (NOPCPA in BBO), 10 (DFG in MgO:PPLN + pyNLO comparison), 11 (self-focusing),
  12 (Python pulse shaper). Docs: crystals, spatial models, active elements.
- Validation: Armstrong SHG, cosh² OPA gain, Manley-Rowe, sinc² detuning, cascaded χ(2) phase, QPM models,
  handbook phase-matching angles, radial vs 1-D, pyNLO three-wave mixing (0.1-0.7 %, also at gain 50).
- Fixes: LBO indices now Kato (1994) (the Chen/Hanson-Dick fit gave 0.8° too large an angle at 1030 nm);
  the grid planner now widens the provisional window for chirped inputs (a strongly chirped pulse could wrap around);
  carried transverse profiles keep their exact energy between frequency samples; QPM with zero carrier mismatch;
  `ULTRAFAST_DEBUG=1` prints stack traces from the CLI.

### Plotting and fixes (2026-10-13)
- `ultrafast-plot` / `python -m ultrafast.plot` / `ultrafast.plotting`: overview, instruments, probes, elements and
  comparison figures (PNG/SVG/PDF + multi-page `report.pdf`) from a CLI results folder or a live Result; no engine needed.
- `uf.load(folder)` opens CLI results with the Result interface.
- Fixed: the ideal compressor could report a result worse than no compressor (its TOD search assumed a single
  maximum); it now scans GDD and TOD on a grid tied to the transform limit and falls back to no compression.
- Phase 2 proposal (gain, χ(2), oscillators): docs/design/phase2-active-media.md; roadmap re-ordered (spatial -> Phase 3).

### Build (2026-10-13)
- Retargeted from .NET 8 to **.NET 10** (LTS); `global.json` requires SDK 10.0.100+. net48 targets unchanged.
- Zemax binding compiles against the real ZOS-API assemblies (`src/UltrafastEngine.Zemax/lib`, git-ignored);
  coating, semi-diameter and environment are now read through the typed interfaces.
- `nuget.config` lists nuget.org (needed only for NativeAOT publishing).
- Application icon for `ultrafast` and `UltrafastEngine.Zemax` (`assets/ultrafast.ico`).

First release of the 2.0 architecture. **Breaking:** v2 run configs and the material catalogue are replaced by
the system file / run spec / export config formats (see docs).

### Architecture
- Zemax-free physics core `UltrafastEngine.Core` (net10.0, net48), no NuGet dependencies.
- C API (`ultrafast.h`) via `UltrafastEngine.Native`, NativeAOT-ready; Python package `ultrafast-engine`; CLI `ultrafast`.
- OpticStudio binding rebuilt on an `IOpticStudio` abstraction (`UltrafastEngine.Zemax.Sampling`), with a fake
  OpticStudio, record/replay of ZOS-API sessions and raw-sample re-export.
- Model registry, overrides with priorities, provenance report.

### Physics (beyond v2)
- Absolute indices (Ciddor air), gases (Borzsonyi), material library.
- Thin-film coatings (TMM), Fresnel, clipping, scatter, LIDT; exact loss budget.
- Jones polarisation with 3D basis transport; waveplates, polarisers.
- Vector Kerr coupling with the coherent term; user time responses; carrier-resolved field mode.
- Adaptive ERK4(3)-IP stepper.
- Instruments (spectrometer, AC/IAC, FROG, d-scan, scope, contrast, polarimeter, energy meter, ideal compressor).
- Grid planner sizes the window from the largest delay spread anywhere in the system and the expected nonlinear broadening.

### Kept from v2 (with fixes)
- Group-delay (group-path) dispersion from traced rays -- exact through gratings.
- Batched wavelength sampling through the WDE with restore; operand-based geometry with matrix cross-check.
- Per-pass nonlinear elements with GBPS/GBPW beam profiles; medium inheritance through coordinate breaks and mirrors.
- Physics sign convention and its directional tests; the v2 18 tests are covered by the new suites.
