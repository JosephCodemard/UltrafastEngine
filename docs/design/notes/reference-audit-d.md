# Reference audit D (WP C1d)

Pages: `docs/reference/transverse.md`, `branches.md`, `models.md`, `fidelity.md`, `memory.md`, `speed.md`.
Date: 2026-11-25. Branch `dev/2026-11` (read-only audit; no other file touched).

Method: each factual claim was checked with grep and line reads against `Schema/BuiltIn*.cs`, `Specs/Fidelity.cs`,
`Engine/MemoryEstimate.cs`, `Engine/GraphExecutor.cs`, `Systems/SystemGraph.cs`, `Transverse/`, `Optics/MaskElement.cs`,
`Numerics/`, `plugins/gpu/src`, `tools/bench/Program.cs` and the named tests (existence of each cited test method was checked;
tests were not run). Generated files were not read. Benchmark numbers that no test or script reproduces are marked "unclear".
Only problems are listed.

## transverse.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| T1 | l.21 `"none"` is the default model | Only when the key is absent. A `transverse` object written without `model` is `radial` | `Transverse/TransverseProfile.cs:48` (`GetString("model","radial")`) | missing |
| T2 | l.30 `nx`, `ny` default `points` | `nx` defaults to `points`; `ny` defaults to `nx` (not to `points`) | `TransverseProfile.cs:56-57` | unclear |
| T3 | l.32 `halfWidthYM` "as above" | `halfWidthYM` defaults to `halfWidthXM`, else to `halfWidthXM`'s own default (`extentFactor` x radius) | `TransverseProfile.cs:59-60,101-102` | unclear |
| T4 | l.25-40 option table | `frequencyBlock` (default 0) is a transverse key but is absent from the table (described only at l.202). A string form `"transverse": "radial"` also works | `TransverseProfile.cs:44,78-80`; `BuiltInOptions.cs:193` | missing |
| T5 | l.275-277 `noise` keys | `components` (default 256, minimum 16) is not listed; `noise` and `ripple` throw on a radial grid (the page says "cartesian" only in l.58) | `TransverseProfile.cs:312-313,305` | missing |
| T6 | l.248-253 `coherence` | Defaults not stated: `modes` 12, `dimensions` 1 if `ny` (or `nx`) is 1 else 2; element is `spec.element` | `Engine/PartialCoherence.cs:30-39` | missing |
| T7 | l.45 Hankel matrix "about 1 s for 2048 points" | no test or script measures it | no evidence in `SpatialTests` / `tools/bench` | unclear |
| T8 | l.153-154 convergence "0.2 % ... 1.6 %" with 85 / 21 samples | no test asserts these numbers (`DiffractionTests.HardAperture_*` checks dx/2a only) | `DiffractionTests.HardAperture_*` | unclear |
| T9 | l.163 "power 10 nepers across the layer for a wave at a third of the grid's steepest angle" | the nonlinear-element absorption constants were not found in `SpatialPropagation.cs` by grep; only the linear layer exp(-5u^4) is confirmed | `Transverse/TransverseProfile.cs:378` (linear only) | unclear |
| T10 | l.187-188 "24 samples for 30 fs ... 96 for 60 fs" | no test or script | `TileSourceTests.DelayMismatch_*` uses a different check | unclear |
| T11 | l.208-209 "1587 MB with `all`, 336 MB with `used`, 268 MB profile" | no test or script reproduces these figures | `SpatialStreamingTests.KeepProfilesUsed_*` assert equality, not sizes | unclear |
| T12 | l.211-214 "2.7-3.4 GB", "1.9 s, 260 MB, 491 MB" ("development history") | `history.md` contains none of these numbers; `FullPanel_1920x1080_SingleFrequency_*` (slow test) exists, but its asserts were not checked for the 260 / 491 MB | `docs/design/history.md` (no match); `SpatialStreamingTests.cs` | unclear |
| T13 | l.287 "57 s on a laptop CPU (4 cores)" for the 10 mm example | no script or test | example README not audited here | unclear |
| T14 | l.17-40 page never says where the schema points | the schema entry of `transverse` sends readers to `docs/reference/spatial-models.md`, which does not exist (the page is `transverse.md`) | `BuiltInOptions.cs:180`; `ls docs/reference` (only `spatial-properties.md`) | stale |
| T15 | l.21 / l.133-139 radial `absorbingBoundary` | absorbing layer applies to linear elements and to nonlinear ones (CPU only). A hard-coded default fraction 0.15, valid range 0 to 0.5 (error otherwise), not stated | `TransverseProfile.cs:84-89` | missing |

Verdict: physics and keys are accurate (edge warning 1e-4 in the outer 15 %, exp(-5u^4), flatTop order 10, tiles keys, mask kinds, aperture keys, all 30 cited tests exist); defects are omitted defaults, one dead schema link and 7 untraceable numbers.

## branches.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| B1 | l.3 "Design: [Branched systems](../reference/branches.md)" | the page links to itself; no separate design page is linked | `docs/reference/branches.md:3` | stale |
| B2 | l.171 "loop without settings or delay" is an error | errors are: loop with no settings, more than one `loop` object, `loop` on an element not on a loop, no way out of the loop, bad `kind`. Nothing checks "delay" | `Systems/SystemGraph.cs:292,301,307,311` | wrong |
| B3 | l.171 error list | omitted errors: duplicate id, unknown output port, unknown input port, loop with no exit ("no way out") | `SystemGraph.cs:135,165,222,311` | missing |
| B4 | l.122-134 `loop` | defaults `maxRoundTrips` 1000 and `tolerance` 1e-6 are not stated (the example shows 1e-9) | `SystemGraph.cs:306` | missing |
| B5 | l.84 `leakMirror` | defaults not given: `transmission` 0.01, `output` `out`; `dichroic` `edgeWidthM` 10e-9, `reflectLong` true; `polarizingSplitter` both 1; splitter `reflectivity` 0.5 | `Schema/BuiltInElements.cs:189-208` | missing |
| B6 | l.105 phase-unknown list incl. "regenerative amplifiers" | the rule is opt-in: an element that does not implement `IReportsCarrierPhase`, or that sets `PhaseLost`, loses the phase. `AmplifierElement` implements it and nothing in the grep sets `PhaseLost` for it, so the claim for regenerative amplifiers is not confirmed. Traced-delay data rule is true (`PhaseUnknown = t[2] != 0`) | `Engine/Executor.cs:274`; `Physics/LinearTerms.cs:65,211`; `Gain/GainElements.cs` | unclear |
| B7 | l.8 example uses `"version": 3` | correct for the Branched builder (`version` 3); but all shipped Branched examples checked are version 2 files, so the page does not say what version a branched file needs (the engine accepts both) | `python/src/ultrafast/build.py:97-100`; `docs/examples/Branched/*/*.ufs` | unclear |
| B8 | l.134 "A single-frequency (`cw`) source gives steady-state interference directly in a `ringDown` loop" | no test cited | not traced | unclear |
| B9 | l.158 study prefix cache "gives a note with the count" | note exists ("node cache: N element runs reused from the nominal run"); wording differs from "prefix" used in memory.md ("point cache") | `Engine/Studies.cs:334` | unclear |

Verdict: correct on ports, Stokes relations, units, loop semantics and booked-loss names; one wrong error row (B2), several omitted defaults.

## models.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| M1 | l.16-35 kind lists, expression variables, override targets/properties, priority | not verified: the registry (`Models/ModelRegistry`) and `Overrides` code were not within the time budget of this audit | n/a | unclear |

Verdict: not audited in depth (the only page not checked line by line); needs a follow-up pass against `Models/` and `Schema/BuiltInModels*.cs`.

## fidelity.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| F1 | l.22 quick skips `frog` / `xfrog` / `dscan` | also skips `sfgFrog` | `Specs/Fidelity.cs:41` (`ExpensiveInstruments`) | wrong |
| F2 | l.21 standard changes only the tolerance | correct, but a tolerance already looser than the bundle value is kept (not tightened); the page says "1e-5 instead of 1e-6" and does not state the "never tightens" rule | `Fidelity.cs:77-80` | missing |
| F3 | l.22 spatial models replaced "where every spatial element has a beam" | `CanCollapseTransverse` all-or-nothing, and collapse is also skipped when the run has a `coherence` section | `Engine/Compiler.cs:982,1154` | missing |
| F4 | l.37 "polarisation made scalar" warning, "input not linearly polarised along x or y" | threshold: minor component above 1e-3 of the major one | `Fidelity.cs` (`IsMixedPolarization`) | missing |
| F5 | l.35-41 warning table | all four dimensionless thresholds match (B > 3, B/(w0 T0) > 0.01 with T0 = FWHM/1.665, B > 1 and < 2 ps, P/Pcr > 0.3). The collapsed-spatial warning also needs at least one element actually collapsed; page says "in a collapsed element" (ok) | `Fidelity.cs:Assess` | (ok, no entry) |
| F6 | l.51 "Apple silicon, one thread" for the bench table | `tools/bench` case `fidelity` exists and prints the table, but nothing in the case sets one thread; `compute.threads` defaults to all cores | `tools/bench/Program.cs:104-125` | unclear |
| F7 | l.54-61 table numbers (x1.9, x2.7, +3.11 %, 3 warnings ...) | the script produces them on request; no stored result file or golden holds them (`golden-osx-arm64.txt` has only digests) | `tools/bench/golden-osx-arm64.txt` | unclear |
| F8 | l.65-67 tests: "quick stays within 1 % of full at B = 0.15 and warns at B = 15" | `FidelityTests.Fidelity_QuickStaysCloseToFullAtLowB` (1 % on spectral width, 1e-4 on energy; B about 0.15) and `..._QuickWarnsWhenTheDroppedPhysicsMatters` (B about 15) exist | `FidelityTests.cs:58-80` | (ok, no entry) |

Verdict: accurate except the instrument list (F1); bench numbers are reproducible by script but not archived.

## memory.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| Mem1 | l.30-31 retained memory "between a tenth of it and all of it" | matches the test; but the `MemoryEstimate.cs` header (l.5) and the Python docstring of `estimate_memory` say "within a factor of about 2" - three statements of the accuracy disagree | `MemoryEstimateTests.RealRun_RetainedManagedMemory_*` (0.1 x est < retained <= est); `MemoryEstimate.cs:5`; `python/src/ultrafast/core.py:140` | stale |
| Mem2 | l.38-40 "`GCMemoryInfo` on .NET, `GlobalMemoryStatusEx` on .NET Framework / Windows and `/proc/meminfo` on Linux" | on modern .NET (every OS) only `GCMemoryInfo` is used; `GlobalMemoryStatusEx` and `/proc/meminfo` exist only in the net48 build (so `/proc/meminfo` = Mono / .NET Framework on Linux only) | `Engine/MemoryEstimate.cs:AvailableBytes` (`#if NET` / `#else`) | wrong |
| Mem3 | l.13 delayed-light tail "(6 + places) x 2 x N_long x 16 B" | formula right, but `N_long` is capped by `grid.maxN`, and no tail is counted in a branched system | `MemoryEstimate.cs:93-100` (`cr.Graph?.IsBranched != true`, `Math.Min(nLong, MaxN)`) | missing |
| Mem4 | l.16-18 `keepProfiles` `"used"` | the estimate for `used` is a bound: min(spatial elements, instruments + 1) + 1 profiles of the largest element, not the exact set | `MemoryEstimate.cs:70-73` | missing |
| Mem5 | l.47-49 "`all` when the point cache resumes from it" | the code sets `all` when `PrefixCache` is on (term "point cache" in the page = `prefixCache`/"node cache" elsewhere) | `Engine/Studies.cs:204` | unclear |
| Mem6 | l.50-55 fit memory behaviour ("slimmed copy of the Jacobian runs ... tested bit for bit") | `keepProfiles: "used"` default in fits is confirmed; the slimmed Jacobian copy and bit-for-bit claim were not traced to a test | `plugins/fitting/src/Fitting.cs:310` | unclear |
| Mem7 | l.41-43 Python `uf.scan` / `uf.run` with `fit` check estimate | confirmed at `core.py:208,712`; `uf.study` (the lower-level call) does not, which the page does not say | `python/src/ultrafast/core.py:208,712` | missing |
| Mem8 | l.68-70 GPU: "run fewer concurrent propagations ... when that many would not fit" | rule is 0.8 x TOTAL device memory / needed (between 1 and slots), not free memory | `plugins/gpu/src/GpuMemory.cs:5` | unclear |

Verdict: sizes, limits, keys, defaults and Python behaviour match the code; one wrong platform statement (Mem2) and three inconsistent accuracy statements.

## speed.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| S1 | l.5-17 / l.13 `auto` order and the net48 case | `accelerate` and `fftw` are compiled only for `NET`; on .NET Framework 4.8 only `managed` exists. The table says "managed ... .NET Framework 4.8 too" but not that the others are absent there | `Numerics/NativeFft.cs:49,86` (`#if NET`) | missing |
| S2 | l.19-20 "128-bit SIMD on .NET 10: SSE2 on x64, NEON on ARM64" | code uses `Vector128.IsHardwareAccelerated` (`#if NET5_0_OR_GREATER`), so it is on every .NET 5+ build, not .NET 10 only | `Numerics/ComplexKernels.cs:19-26` | unclear |
| S3 | l.27-29 supercontinuum 23.4 s -> 8.8 s -> 5.7 s | taken from CHANGELOG (l.393); no script in `tools/bench` | `CHANGELOG.md:393` | unclear |
| S4 | l.39 `gpuMinPoints: 0` "(0: 8192 samples 1-D, 32768 spatial)" | 0 means those defaults only with `backend: auto`; with an explicit `cuda` / `opencl` 0 means no minimum | `plugins/gpu/src/GpuBackend.cs:104-112`; `BuiltInOptions.cs:152` | wrong |
| S5 | l.33-42 `compute` block | the key that switches mixed precision, `compute.precision` (`"double"` | `"mixed"`), appears nowhere on the page (l.158-179 describe the feature without naming it); the `cpu-fp32` back-end and `ensemble` study key are also not in the back-end table | `BuiltInOptions.cs:155`; `GpuTests.MixedPrecision_*` | missing |
| S6 | l.83-84 "5 evaluations of the nonlinear operator per step" | the stepper is embedded ERK4(3)-IP with first-same-as-last (k1 reused), i.e. four new evaluations per accepted step | `Physics/Nonlinear.cs:966-967` | unclear |
| S7 | l.108-123 study vs loop table (6.5 s -> 1.1 s, x5.7 ...) | `tools/bench` cases `scan-mpc`, `loop-mpc`, `scan-length`, `loop-length` exist and reproduce the comparison; times are not archived (golden file has digests only) | `tools/bench/Program.cs:59-63` | unclear |
| S8 | l.122-123 "200-point study allocates 0.8 GB ... run on 4096 points allocates 11 MB ... radial 1 GB" | no script reports allocation | none | unclear |
| S9 | l.129-139 RTX 3060 Ti ensemble table; l.166-177 mixed-precision table and timings (0.75 s -> 0.52 s ...) | the tests `GpuTests.MixedPrecision_*` exist (and assert the accuracy criteria), but the timings and the 200-point wall times have no script in the repository | `GpuTests.cs:390,426`; PROJECT_STATUS.md line 31 (one machine) | unclear |
| S10 | l.187-202 "Apple M3 ... measured 2026-11-18" fibre benchmark | no script or test builds this fibre case; `tools/bench` has no case for it | `tools/bench/Program.cs:53-64` | unclear |
| S11 | l.147-151 FP32 table | `tools/bench` case `fp32` exists (`Fp32Study`); results not archived | `tools/bench/Program.cs:58,134` | unclear |
| S12 | l.162-164 mixed precision refusal above 70 dB | constant `MixedPrecisionMaxDynamicRangeDb = 70`; test uses 120 (contrast), 110 (crossCorrelator), 90 refused and 60 accepted | `Specs/RunSpec.cs:219`; `GpuTests.cs:426` | (ok, no entry) |
| S13 | l.214 "`stepper.tolerance` default 1e-6" | correct (`Physics/Nonlinear.cs:850`); but the page does not point out that `fidelity: standard` does exactly this | `Nonlinear.cs:850`; `Specs/Fidelity.cs:39` | missing |
| S14 | l.185 `ultrafast devices --benchmark` "prints none on a Mac" | the CLI times only back-ends that pass the self-test; not independently confirmed here | `src/UltrafastEngine.Cli/Program.cs:145-161` | unclear |
| S15 | l.222-223 "A cache for the compiled system ... 10 ms including the compile"; "2.7 to 3.4 GB at 1920 x 1080 with 16 stored frequencies" | measurements cited to `history.md`, which contains none of these numbers | `docs/design/history.md` (no match) | unclear |

Verdict: structure, defaults and keys are right except `gpuMinPoints` (S4) and the missing `compute.precision` key (S5); almost all timings and sizes are machine-specific with no archived script (S3, S8-S11, S15).
