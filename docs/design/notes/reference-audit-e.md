# Reference audit, group e (WP C1e)

Pages: `docs/reference/instruments.md`, `retrieval.md`, `measured-data.md`, `results.md`, `studies.md`.
Date: 2026-11-25. Method: every factual claim was read against the source (`Schema/BuiltInInstruments.cs`,
`Instruments/*.cs`, `Retrieval/*.cs`, `Api/Commands.cs`, `Measured/Measurements.cs`, `Pulses/MeasuredSpectrumPreparation.cs`,
`Pulses/PulseSpec.cs`, `Results/*.cs`, `Engine/Studies.cs`, `Engine/Executor.cs`) and the test classes. Generated files were not read.
Only problems are listed. Paths below are relative to `src/UltrafastEngine.Core/` unless they start with `tests/` or `python/`.
The mainLobe* additions on `results.md` and `instruments.md` were checked against `Results/PulseMetrics.cs:72-123`,
`Results/RunResult.cs:SummaryOf`, `Instruments/Instruments.cs:769-870` and `Engine/Executor.cs:189`: they are correct.

## results.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :7 summary.json holds "engine/grid info, ... instruments, notes, warnings, manifest" | Also writes `format`, `version`, `elapsedS`, `system`, `run`, `totals` (energyTransmission, arrivalTimeS, systemGroupDelayS, systemGddS2, systemTodS3, bIntegralRad, bIntegralPulseAveragedRad, bIntegralBeamAveragedRad, lengthM), `tail` (delayed-light ledger), `graph` (Branched systems), `fidelityReport`, `unverifiedInputs`, `validity`, `probes`, plugin-contributed keys. The page never lists `totals`, `tail`, `graph`, `fidelityReport`, `unverifiedInputs`, `elapsedS` | Results/RunResult.cs:119-216 | missing |
| 2 | :60-75 per-element records list | Records also carry `aux` (aux-beam energies) and `arrays` (names of element arrays); `own` / `system` are objects `{groupDelayS, gddS2, todS3}`; `terms` is the key for "list of linear terms"; cumulative B is `cumulativeBIntegralRad` | Results/RunResult.cs:172-205 | missing |
| 3 | :3-13 files written | `provenance.json` and `summary.json` only when `outputs.json`; `result.npz` only when `outputs.npz`; all CSV only when `outputs.csv`; `outputs.maxPoints` crops arrays and CSV. The run-spec `outputs` switches are not mentioned | Results/RunResult.cs:314-325, 357 | missing |
| 4 | :11, :108 `spectrum.csv`, `temporal.csv` "written per pulse" when grids differ | Names are `spectrum_input.csv` + `spectrum.csv` and `temporal_input.csv` + `temporal.csv` (output keeps the plain name) | Results/RunResult.cs:350-359 | unclear |
| 5 | :19 `edgeEnergyFraction` "energy in the outer 5 % of the window" | 5 % of the samples on each side (N/20 each end), so 10 % of the window in total | Results/PulseMetrics.cs:257-260 | unclear |
| 6 | :36 `mainLobeFwhmS` "is null when the main lobe reaches the edge" | The value is NaN in C#; `R()` keeps NaN and the JSON writer maps it to null. Consistent, but the page does not say that `mainLobeEnergyFraction` is NOT null in that case (it is computed to the window edge) | Results/PulseMetrics.cs:94-100, RunResult.cs:105 | unclear |
| 7 | :46 tests `ValidityTests.MainLobe_...` | Both test names exist | tests/UltrafastEngine.Tests/ValidityTests.cs:210, 221 | (ok, no entry) |
| 8 | :101-102 complex envelopes `ax_re`... "on the ascending `omega_rad_s` axis" | `ax_re/ax_im/ay_re/ay_im` are exported in grid order, only when `outputs.maxPoints <= 0`; the manifest describes them "(grid order)". The page says "(full-resolution exports)" but not that this means `maxPoints = 0` | Results/RunResult.cs:244-249 | unclear |
| 9 | :100 `input_*`/`output_*` arrays list | Matches code, but `input_power_x_w` / `_y_w` are cropped power of the temporal field; `elements` columns are `index, cumulative_length_m, ..., s3` (31 named columns), not "see manifest" only; `mainLobe*` is not in the table | Results/RunResult.cs:253-310 | unclear |
| 10 | :52-55 validity codes | Codes match; thresholds are not stated (`criticalPower` 0.3, `windowEdge` 1e-3, `zeroFrequency` 1e-7 of the energy, `lidt` margin < 1) | Engine/Executor.cs:305, 354, 429, 434 | missing |
| 11 | :86-92 energy budget | `lossBudget` and `energyBudget` are the same object; correct. The `tail` block (delayed-light energy) is not part of the budget and is undocumented here | Results/RunResult.cs:146-155 | missing |

Verdict: the metric and validity sections are accurate; the summary.json key inventory is incomplete (no `totals`, `tail`, `graph`, `fidelityReport`, `unverifiedInputs`) and the output switches are not described.

## studies.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :10, :53-61 `mode` is `scan | optimize | sensitivity` | A fourth mode, `coherence` (repeated runs with independent quantum noise; keys `coherence.trials` default 10, `coherence.at` default `"end"`; needs no `parameters` / `outputs`; result in `analysis.coherence`, also writes `coherence.csv`). Not on the page at all. The error text also omits it | Engine/Studies.cs:76, 89-95, 107, 186, 161-169; python/src/ultrafast/core.py:175 | missing |
| 2 | :23 "`study.json` + `study.csv` (one row per point)" | Also `study.npz` (when `arrayOutputs`) and `coherence.csv`. `study.csv` has a trailing `error` column; `study.json` has `format: ultrafast-study-result`, `parameters`, `outputs`, `evaluations`, `elapsedS`, `analysis`, `notes`, `rows` (with `error`). Output file format is not described | Engine/Studies.cs:128-163 | missing |
| 3 | :9, :8 `"format": "ultrafast-study"` | The key is never read by `StudySpec.FromJson` or the CLI, so it is optional and not validated | Engine/Studies.cs:80-110 | unclear |
| 4 | :11 `"sampling"` / `samples` | Defaults are never stated: `sampling` defaults to `grid`, `samples` 64, `seed` 1, `parallel` 1, per-parameter `points` 5. In `grid` mode `samples` is ignored | Engine/Studies.cs:44, 83-84, 296-301 | missing |
| 5 | :35-39 parameter path table | `type:NAME/property` is also accepted (override target `type:`); `element:ID*` prefix wildcard is honoured by the prefix cache. An unknown prefix gives a specific error | Engine/Studies.cs:565-575, FirstAffected | missing |
| 6 | :41 "`nominal` (default the middle)" | Middle is the arithmetic mean for linear scale, the geometric mean for `scale: log`, the middle value of `values` for a list | Engine/Studies.cs:36 | unclear |
| 7 | :55-56 sobol "up to 16 parameters" | True for `sampling: sobol`. The Sobol sensitivity method draws a `2 d` dimensional sequence, so it supports at most 8 parameters; the limit is not stated and exceeding it fails inside `Sobol` | Engine/Studies.cs:772-776, 808-812 | unclear |
| 8 | :57-58 `optimize` object | Also `tolerance` (default 1e-4); `maxEvaluations` default 200, `goal` default `min`. The objective output is added to `outputs` automatically. `analysis` also holds `objective`, `goal`, `value`, `evaluations`, not only `optimum` | Engine/Studies.cs:101-105, 314-316 | missing |
| 9 | :59-61 sensitivity `sobol` "N (d + 2) runs" | `samples` base samples N gives 2N + dN = N (d + 2) runs: correct. `analysis.sensitivities[output][param] = {first, total}`; local method keys not documented | Engine/Studies.cs:772-799 | missing |
| 10 | :65, :68 `freezeGrid` / `prefixCache` "default true" | Correct; but in a Branched system the prefix restart is replaced by a node cache (`GraphCache`) and `prefixCache` has a different effect (note "element runs reused") | Engine/Studies.cs:203, 226, 334 | unclear |
| 11 | :71-76 `ensemble` default | Correct (-1 internal, 64 per set). `true` also means default; page says "default on with a GPU back-end": correct | Engine/Studies.cs:62-66, 88, 342-349 | (ok, no entry) |
| 12 | :49 `record:ID/field` "`BIntegralRad`, `EnergyOutJ`, `ArrivalS`, `PeakFluenceJPerM2`" | Field names are matched by reflection on the C# `ElementRecord` members (case-insensitive), so the names are the C# names, not the summary.json keys (`bIntegralRad`, `energyOutJ`, `arrivalTimeS`); `ArrivalS` is the C# name, summary.json calls it `arrivalTimeS`. Not said | Engine/Studies.cs:648-655, 676-688; Results/RunResult.cs:166 | unclear |
| 13 | :48 `pulse:LOC/metric` "`input` or `end`" | `LOC` may also be any element id, probe id, or `id#field` by-product; keys are the C# `PulseSummary` field names (case-insensitive); the new `mainLobeFwhmS` etc. are valid here but not mentioned | Engine/Studies.cs:643-647, 676 | unclear |
| 14 | :27 Python `uf.study(system, spec, parameters=..., outputs=..., mode="scan")` | Signature matches (`output=` folder and `**options` also exist) | python/src/ultrafast/core.py:171 | (ok, no entry) |
| 15 | :22-24 CLI | `ultrafast study` also takes `--parallel <n>` and `--backend <name>` | src/UltrafastEngine.Cli/Program.cs:50 | missing |
| 16 | :92-94 tests | `SpeedStudyTests` has `Study_ScanWithPrefixCacheMatchesFullRuns`, `Study_OptimizeAndSensitivities`, `Sobol_SequenceIsStratified`; no test of the point cache, ensembles or frozen-grid union is named although the page describes all three | tests/UltrafastEngine.Tests/SpeedStudyTests.cs:82-130 | unclear |

Verdict: the core of scan / optimize / sensitivity is accurate; the `coherence` mode, output files and the defaults are missing.

## instruments.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :22-42 table of kinds | The table omits `camera` (aliases `nearFieldCamera`, `farFieldCamera`), `wavefrontSensor`, `wigner`, `fieldProbe`, `spaceTime` and `singleShotCorrelator`; they are described in later sections, but a reader of the table sees 18 of 24 kinds | Schema/BuiltInInstruments.cs:64-176 | unclear |
| 2 | :24-37 parameters per kind | Keys declared in the schema but absent from the table: autocorrelator `acceptancePoints` (512), `maxN` (65536); contrast `points` (8192), `dynamicRangeDb` default 120, `windowS` default 5 x FWHM; compressor `points` (4096), `maxN` (131072); dscan `steps` default 101; scope `points` 4096; polarimeter `points` 1024 | Schema/BuiltInInstruments.cs:77-110 | missing |
| 3 | :27 autocorrelator `rangeS`, `points` | `rangeS` (default 3 x FWHM) and `points` (default 4001) apply to the interferometric type only; the table implies both types | Instruments/Instruments.cs:440-447; Schema/BuiltInInstruments.cs:79 | unclear |
| 4 | :27 "deconvolved FWHM (Gaussian sqrt2, sech^2 1.543)" | Code divides by 1.5427 and reports `pulseFwhmGaussianS` and `pulseFwhmSech2S`; the key names are not given on the page | Instruments/Detector.cs:61 | unclear |
| 5 | :27 IAC "peak-to-background (8:1 ...)" | Reported key is `peakToBackground`; correct otherwise (step lambda0/16c, odd points, note) | Instruments/Instruments.cs:440-479 | (ok, no entry) |
| 6 | :34 `polarimeter` reports "azimuth, ellipticity" | Keys are `azimuthDeg`, `ellipticityAngleDeg`, `degreeOfPolarization`, `s1..s3` | Instruments/Instruments.cs:741-745 | unclear |
| 7 | :37 `compressor` reports | Matches (`compressedMainLobeFwhmS`, `compressedMainLobeEnergyFraction` exist) | Instruments/Instruments.cs:869 | (ok, no entry) |
| 8 | :18-20 "a repeated or missing name is made unique (`spectrometer_plate`, ...)" | Default name is `kind` at `end`, `kind_<id>` elsewhere; name uniqueness compares sanitised names; a warning only when a name was given, otherwise a note | Engine/Executor.cs:477-495 | (ok, no entry) |
| 9 | :55 `filter` kinds "`constant`, `table`, `gaussian`, `expression`, `callback`" | The registered filter kinds also include `superGaussian`; `expression` and `callback` are open models | Schema/BuiltInInstruments.cs (model:filter, lines 188-198) | unclear |
| 10 | :61-83 detector keys | Schema also has `response` / `counting` correctly; but `segments` and `saturationLevel` are not detector keys: they belong to `crossCorrelator` (and are accepted via a preset `instrument` object). The page text is consistent, but the section on "Cameras and the cross-correlator have detector models of their own" omits the `singleShotCorrelator` (own `dynamicRangeDb`, `noiseFloor`, `noise`, `seed`) | Schema/BuiltInInstruments.cs:50-60, 129-137 | unclear |
| 11 | :225-226 `singleShotCorrelator` table | `windowAroundPeakS` (default 5 x FWHM), `noiseFloor`, and the defaults `dynamicRangeDb` 60, `points` 256 are not listed; outputs `x_m`, `delay_s`, `signal`, `signal_db`, `signal_ideal`, `signal_raw` match | Schema/BuiltInInstruments.cs:162-166; Instruments/SingleShotCorrelator.cs:161-198 | missing |
| 12 | :293-297 sampling "`aperture.kind` default `pinhole` if an aperture is given, else `integrated`" | Code agrees (`sampling.aperture` object present gives pinhole). The schema, however, says default `pinhole` for `samplingAperture.kind` | Instruments/Sampling.cs:40; Schema/BuiltInInstruments.cs:44 | unclear |
| 13 | :268 sampling reports `samplingTransmission` | Not written for `kind: point` (and not for `integrated`) | Instruments/Sampling.cs:44-46, 118 | unclear |
| 14 | :270 imaging instruments "ignore `sampling`" (camera, wavefrontSensor, beamProfile, spaceTime) | `ResolveDetector` copies `sampling` into the settings of every instrument; no code path rejects or warns for those four kinds | Instruments/Detector.cs:336-337 | unclear |
| 15 | :366-368 virtual instruments "flagged `virtual: true` in their values" | Only `wigner`, `fieldProbe`, `spaceTime` set it. `pulseSummary`, `compressor`, `conversion`, `beamProfile`, `phaseMatchingCurve`, `delayScan` do not; the page's final sentence ("also shown as virtual") refers to the plots only | Instruments/VirtualInstruments.cs:87, 179, 262 | unclear |
| 16 | :310-355 camera | Keys match the schema. `strehlRatio` is the key for the far-field Strehl ratio, not named on the page | Instruments/CameraInstruments.cs:388 | unclear |
| 17 | :356-365 `wavefrontSensor` | Matches schema (pixels 64, threshold 0.01, zernikeTerms 15) and `slope_x_rad` | Instruments/CameraInstruments.cs:591 | (ok, no entry) |
| 18 | :205-225 FROG section | Tests `FrogKindsTests`, `SpatialSfgOfTwoCrossedBeams_PutsAReplicaAtTheInstrumentsPosition` exist | tests/UltrafastEngine.Tests/FrogKindsTests.cs:14, SingleShotCorrelatorTests.cs:130 | (ok, no entry) |
| 19 | :335-337 validated by `PluginTests.Camera_UnifiedImaging_*` | Only one test of that prefix exists, `Camera_UnifiedImaging_MatchesTheOldCameras` | tests/UltrafastEngine.Tests/PluginTests.cs:155 | unclear |

Verdict: the kind descriptions and the mainLobe additions are accurate; the kind table is incomplete (six kinds, many keys and defaults), and a few claims (virtual flag, sampling ignored) are looser than the code.

## retrieval.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :7-13 method table | The `multiFrog` row is separated from the table by a blank line, so it renders as a stray table with no header | docs/reference/retrieval.md:12-13 | wrong |
| 2 | :10 ePIE "stops at a trace error of about 1e-3" | Stop rule is `err < tolerance` with `tolerance` default 1e-5 (and `maxIterations` default 300); the 1e-3 figure is an empirical outcome, not a stop criterion | Retrieval/Retrieval.cs:498, 538 | unclear |
| 3 | :102-103 options list | Defaults not stated: `maxIterations` 300 (pcgpa, epie), 60 (dscan), 600 (multiFrog); `tolerance` 1e-5; `stepSize` 0.5 (ePIE); `seed` 1; `dtS` option exists (FROG and multiFrog); `iterations` / `restarts` / `seed` for Gerchberg-Saxton | Retrieval/Retrieval.cs:452, 498, 561, 289-293; MultiTraceRetrieval.cs:71-125; PhaseRetrieval.cs:99 | missing |
| 4 | :44-47 CLI outputs | For `multiFrog` with `output`, files are `retrieved_spectrum_<pulse>.csv` and `retrieval.json` (no `retrieval.npz`). For Gerchberg-Saxton, `phase.npz`. Neither is documented for multiFrog | Api/Commands.cs:183-187 | missing |
| 5 | :36-37 CLI `ultrafast retrieve <method>` | The CLI accepts `pcgpa`, `epie`, `dscan`, `gerchbergSaxton` (alias `gs`), `multiFrog` (alias `doubleFrog`) through the JSON command; only the first and the last-but-one are shown. `gs` and `doubleFrog` aliases are undocumented (the table lists `doubleFrog`) | Api/Commands.cs:193-197 | unclear |
| 6 | :40-41 trace file keys | d-scan files also accept `shg_wavelength_m` as column axis and `fundamental_wavelength_m` + `fundamental_intensity`; `--fundamental` CSV is read with `wavelengthUnit` default `nm` | Retrieval/Retrieval.cs:61-63, 123-127; Api/Commands.cs:205-210 | missing |
| 7 | :26-29 kind inference | Matches: asymmetry threshold 10 %, note text | Retrieval/Retrieval.cs:310-321, 405-406 | (ok, no entry) |
| 8 | :117-118 tests | `FrogKindsTests`, `MultiTraceRetrievalTests`, `RetrievalTests` exist; the Gerchberg-Saxton test class is `FieldToolsTests` (cited above, ok) | tests/UltrafastEngine.Tests | (ok, no entry) |
| 9 | :114-115 "available by name in the CLI, the C API and Python" | `Retrievals.Register` adds it to the FROG/d-scan path; `Commands.Retrieve` routes anything else through `Retrievals.Run`, so true | Api/Commands.cs:208-215 | (ok, no entry) |

Verdict: the algorithm descriptions match the code; one broken table row, and defaults and multiFrog output files are missing.

## measured-data.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :56 "the run warns when the floor ... is above 0.3 % of the peak and `background` is not set" | Warning only fires when `background` mode is `none` (explicit or default); correct. Threshold constant 3e-3 | Pulses/MeasuredSpectrumPreparation.cs:52, 126 | (ok, no entry) |
| 2 | :58, :189 `"auto"` takes "the first and last 5 % of the points" | Window is `max(3, ceil(5 % N))` points, capped at N/4; for short spectra it is 3 points, not 5 % | Pulses/MeasuredSpectrumPreparation.cs:104-105 | unclear |
| 3 | :27-35 key table | `background` also accepts a boolean (`true` = auto, `false` = none); `threshold` must satisfy 0 <= t < 1 (error otherwise); `extrapolate` only `zero` / `gaussian`; `deconvolve` requires `resolutionM` (error otherwise) | Pulses/PulseSpec.cs:131-148 | missing |
| 4 | :30 `csv` "header and `#` lines skipped"; `wavelengthUnit` | Python docs list `nm`, `um`, `m`; the page does not list the allowed units | python/src/ultrafast/build.py:684 | unclear |
| 5 | :146-153 units table | `µm` is also accepted; the table omits `w`, `j`, `hz` style suffixes if any (none present: unit function covers exactly the listed set plus `µm`) | Measured/Measurements.cs:65-70 | unclear |
| 6 | :161-172 compared-with table | Layout names in code: `scope` compares `signal_w`; `delayScan` compares `output_energy_j`; `beamProfile` falls back to a Cartesian cut layout when no `radius_m`; `frog` row also serves `xfrog`; `crossCorrelator` also serves `thirdOrderCorrelator`. The page does not name `xfrog` or the alias | Measured/Measurements.cs:149-164 | unclear |
| 7 | :210-213 `comparisons[]` keys | Also `file`, `xAxis`, `rowAxis`, `colAxis`; `reducedChi2` is `chi2 / points`; `values[name]` has `measured`, `simulated`, `sigma`, `deviation = simulated - measured`. `file`, `xAxis`, `rowAxis`, `colAxis` are missing | Measured/Measurements.cs:92-108 | missing |
| 8 | :214-215 instrument values | Also `measuredRmsErrorDb` (with `scale: log`); `measured.<value>` is written for scalar readings; `measuredChi2`, `measuredRmsError`, `measuredScale` correct | Measured/Measurements.cs:275-284, 362, 380 | missing |
| 9 | :216-217 arrays | Correct names; for traces `simulated_on_measured` is 2-D with the measured shape, and the NaN-outside-range statement is stated in the manifest only | Measured/Measurements.cs:376-409 | (ok, no entry) |
| 10 | :187 `normalize` options | `absolute` is accepted as an alias of `none`; any unknown string falls through to `peak` (no error) | Measured/Measurements.cs:456, 470 | unclear |
| 11 | :247-249 Python `b.measured(...)` | Signature also has `simulated=`; it lacks `value_weight` / `valueWeight`, `row_unit`, `col_unit` (the page's option table lists those keys, so the Python builder cannot set them except through `**extra`) | python/src/ultrafast/build.py:692-698 | unclear |
| 12 | :55 "0.66 % floor ... shortens the transform limit from 240 fs to 26 fs" | No test or source reproduces these two numbers in this repository's tests under the audited paths; they come from a case study | (no evidence found in Pulses/ or tests grep of the audited classes) | unclear |
| 13 | :65-66 description string "measured 950.0-1110.0 nm (1200 points, ...)" | Not checked against the description builder; treat as illustrative | - | unclear |

Verdict: the processing rules and normalisation modes match the code; the output-key inventory and the builder / unit details are incomplete.

## Summary

| page | wrong | stale | unclear | missing |
|---|---|---|---|---|
| results.md | 0 | 0 | 5 | 6 |
| studies.md | 0 | 0 | 6 | 7 |
| instruments.md | 0 | 0 | 11 | 4 |
| retrieval.md | 1 | 0 | 2 | 3 |
| measured-data.md | 0 | 0 | 9 | 3 |
