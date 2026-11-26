# Reference audit D (WP C1d)

Pages: `docs/reference/transverse.md`, `branches.md`, `models.md`, `fidelity.md`, `memory.md`, `speed.md`.
Date: 2026-11-25. Branch `dev/2026-11` (read-only audit; no other file touched).

Method: each factual claim was checked with grep and line reads against `Schema/BuiltIn*.cs`, `Specs/Fidelity.cs`,
`Engine/MemoryEstimate.cs`, `Engine/GraphExecutor.cs`, `Systems/SystemGraph.cs`, `Transverse/`, `Optics/MaskElement.cs`,
`Numerics/`, `plugins/gpu/src`, `tools/bench/Program.cs` and the named tests (existence of each cited test method was checked;
tests were not run). Generated files were not read. Benchmark numbers that no test or script reproduces are marked "unclear".
Only problems are listed.

## transverse.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| T1 | l.21 `"none"` is the default model | Only when the key is absent. A `transverse` object written without `model` is `radial` | `Transverse/TransverseProfile.cs:48` (`GetString("model","radial")`) | missing | fixed (Models table: `none` without a `transverse` key, `radial` for an object without `model`) |
| T2 | l.30 `nx`, `ny` default `points` | `nx` defaults to `points`; `ny` defaults to `nx` (not to `points`) | `TransverseProfile.cs:56-57` | unclear | fixed (`nx` defaults to `points`, `ny` to `nx`) |
| T3 | l.32 `halfWidthYM` "as above" | `halfWidthYM` defaults to `halfWidthXM`, else to `halfWidthXM`'s own default (`extentFactor` x radius) | `TransverseProfile.cs:59-60,101-102` | unclear | fixed (`halfWidthYM` defaults to `halfWidthXM`; alias `halfWidthM`) |
| T4 | l.25-40 option table | `frequencyBlock` (default 0) is a transverse key but is absent from the table (described only at l.202). A string form `"transverse": "radial"` also works | `TransverseProfile.cs:44,78-80`; `BuiltInOptions.cs:193` | missing | fixed (`frequencyBlock` row; the string form stated; Keys link `options.md#transverse`) |
| T5 | l.275-277 `noise` keys | `components` (default 256, minimum 16) is not listed; `noise` and `ripple` throw on a radial grid (the page says "cartesian" only in l.58) | `TransverseProfile.cs:312-313,305` | missing | fixed (`components` 256, min 16; `seed` 1; `correlationLengthM` / `periodM` required; ripple / noise an error on a radial grid) |
| T6 | l.248-253 `coherence` | Defaults not stated: `modes` 12, `dimensions` 1 if `ny` (or `nx`) is 1 else 2; element is `spec.element` | `Engine/PartialCoherence.cs:30-39` | missing | fixed (`modes` 12, `dimensions` rule, 2-D uses modes(modes+1)/2 Hermite-Gauss pairs) |
| T7 | l.45 Hankel matrix "about 1 s for 2048 points" | no test or script measures it | no evidence in `SpatialTests` / `tools/bench` | unclear | fixed (the unsourced "about 1 s for 2048 points" removed) |
| T8 | l.153-154 convergence "0.2 % ... 1.6 %" with 85 / 21 samples | no test asserts these numbers (`DiffractionTests.HardAperture_*` checks dx/2a only) | `DiffractionTests.HardAperture_*` | unclear | fixed (numbers removed; the tested bound dx/2a of `DiffractionTests.HardAperture_*` quoted) |
| T9 | l.163 "power 10 nepers across the layer for a wave at a third of the grid's steepest angle" | the nonlinear-element absorption constants were not found in `SpatialPropagation.cs` by grep; only the linear layer exp(-5u^4) is confirmed | `Transverse/TransverseProfile.cs:378` (linear only) | unclear | fixed (traced: `TransverseOps.AbsorberAlpha`, α = 50 θ/(w E) u⁴ gives 10 nepers for θ = θmax/3, `SpatialPropagation.cs:523`; the page now states the formula); audit partly wrong: the constants exist |
| T10 | l.187-188 "24 samples for 30 fs ... 96 for 60 fs" | no test or script | `TileSourceTests.DelayMismatch_*` uses a different check | unclear | fixed (example sample counts removed; the 0.5 rad rule kept) |
| T11 | l.208-209 "1587 MB with `all`, 336 MB with `used`, 268 MB profile" | no test or script reproduces these figures | `SpatialStreamingTests.KeepProfilesUsed_*` assert equality, not sizes | unclear | fixed (1587 / 336 MB, sourced only in CHANGELOG, replaced by the 260 / 491 MB that the slow test `FullPanel_*` prints, re-run 2026-11-26 on an Apple M3) |
| T12 | l.211-214 "2.7-3.4 GB", "1.9 s, 260 MB, 491 MB" ("development history") | `history.md` contains none of these numbers; `FullPanel_1920x1080_SingleFrequency_*` (slow test) exists, but its asserts were not checked for the 260 / 491 MB | `docs/design/history.md` (no match); `SpatialStreamingTests.cs` | unclear | fixed (260 / 491 MB reproduced by the slow test, 2.2 s on the M3 today; 2.7-3.4 GB kept with its provenance); audit wrong: `history.md:137` holds the 2.7-3.4 GB |
| T13 | l.287 "57 s on a laptop CPU (4 cores)" for the 10 mm example | no script or test | example README not audited here | unclear | fixed ("57 s on a laptop" removed; the 134 MB size is arithmetic) |
| T14 | l.17-40 page never says where the schema points | the schema entry of `transverse` sends readers to `docs/reference/spatial-models.md`, which does not exist (the page is `transverse.md`) | `BuiltInOptions.cs:180`; `ls docs/reference` (only `spatial-properties.md`) | stale | code: C4-d2 (the page is right; the schema description links the missing `spatial-models.md`) |
| T15 | l.21 / l.133-139 radial `absorbingBoundary` | absorbing layer applies to linear elements and to nonlinear ones (CPU only). A hard-coded default fraction 0.15, valid range 0 to 0.5 (error otherwise), not stated | `TransverseProfile.cs:84-89` | missing | fixed (linear and nonlinear elements, 0.15 default, 0 ≤ f < 0.5) and code: C4-d4 (stale comment) |

Verdict: physics and keys are accurate (edge warning 1e-4 in the outer 15 %, exp(-5u^4), flatTop order 10, tiles keys, mask kinds, aperture keys, all 30 cited tests exist); defects are omitted defaults, one dead schema link and 7 untraceable numbers.

## branches.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| B1 | l.3 "Design: [Branched systems](../reference/branches.md)" | the page links to itself; no separate design page is linked | `docs/reference/branches.md:3` | stale | fixed (self-link removed) |
| B2 | l.171 "loop without settings or delay" is an error | errors are: loop with no settings, more than one `loop` object, `loop` on an element not on a loop, no way out of the loop, bad `kind`. Nothing checks "delay" | `Systems/SystemGraph.cs:292,301,307,311` | wrong | fixed (error table lists the errors `SystemGraph.cs` raises; no "delay") |
| B3 | l.171 error list | omitted errors: duplicate id, unknown output port, unknown input port, loop with no exit ("no way out") | `SystemGraph.cs:135,165,222,311` | missing | fixed (duplicate id, unknown ports, no way out, loop kind, `loop` off a loop added) |
| B4 | l.122-134 `loop` | defaults `maxRoundTrips` 1000 and `tolerance` 1e-6 are not stated (the example shows 1e-9) | `SystemGraph.cs:306` | missing | fixed (`kind` ringDown, `maxRoundTrips` 1000, `tolerance` 1e-6) |
| B5 | l.84 `leakMirror` | defaults not given: `transmission` 0.01, `output` `out`; `dichroic` `edgeWidthM` 10e-9, `reflectLong` true; `polarizingSplitter` both 1; splitter `reflectivity` 0.5 | `Schema/BuiltInElements.cs:189-208` | missing | fixed (defaults in the splitter table; links to the `options.md` element anchors) |
| B6 | l.105 phase-unknown list incl. "regenerative amplifiers" | the rule is opt-in: an element that does not implement `IReportsCarrierPhase`, or that sets `PhaseLost`, loses the phase. `AmplifierElement` implements it and nothing in the grep sets `PhaseLost` for it, so the claim for regenerative amplifiers is not confirmed. Traced-delay data rule is true (`PhaseUnknown = t[2] != 0`) | `Engine/Executor.cs:274`; `Physics/LinearTerms.cs:65,211`; `Gain/GainElements.cs` | unclear | kept, because the claim is right: `GainElements.cs:605` sets `PhaseLost` for `geometry: "regenerative"`; audit wrong. Page now names the geometry and the `IReportsCarrierPhase` rule |
| B7 | l.8 example uses `"version": 3` | correct for the Branched builder (`version` 3); but all shipped Branched examples checked are version 2 files, so the page does not say what version a branched file needs (the engine accepts both) | `python/src/ultrafast/build.py:97-100`; `docs/examples/Branched/*/*.ufs` | unclear | fixed (current version 3; version 2 files load unchanged) |
| B8 | l.134 "A single-frequency (`cw`) source gives steady-state interference directly in a `ringDown` loop" | no test cited | not traced | unclear | fixed (cited: `BranchingTests.FabryPerot_LoopGivesTheAiryFunction`, cw source in a default ringDown loop, Airy to 1e-5) |
| B9 | l.158 study prefix cache "gives a note with the count" | note exists ("node cache: N element runs reused from the nominal run"); wording differs from "prefix" used in memory.md ("point cache") | `Engine/Studies.cs:334` | unclear | fixed (the note's text quoted: `node cache: N element runs reused from the nominal run`) |

Verdict: correct on ports, Stokes relations, units, loop semantics and booked-loss names; one wrong error row (B2), several omitted defaults.

## models.md

Audited line by line by WP C2d (2026-11-26) against `Models/ModelRegistry.cs`, `Models/ModelSpec.cs`, `Models/Expression.cs`,
`Models/SpatialFields.cs`, `Physics/ModelImplementations.cs`, `Materials/IndexModels.cs`, `Materials/MaterialLibrary.cs:175`
and `Engine/Compiler.cs` (override lookup). Row M1 of C1d is replaced by these rows.

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| M1 | l.9 forms: data, expression, callback | also `{"callback": name}`, `{"value": x}` and `{"expr": ...}` without `kind`; kinds are case-insensitive | `ModelSpec.cs:FromJson`; `ModelRegistry.cs:25` | missing | fixed (Keys) |
| M2 | l.18 `delayedResponse` kinds | also `blowWood` (alias of `silica`), `linAgrawal`, `hollenbeck`, `orientational` | `ModelRegistry.cs:196-207` | missing | fixed (all kinds; link to `nonlinear.md#raman-models` for shapes and fR) |
| M3 | l.19 `timeResponse` kinds | also `chi2` (`deffPmPerV`, `periodM`, `n0`); `expression` takes `exprIm` / `exprRe` | `ModelRegistry.cs:224`; `ModelImplementations.cs:30-36` | missing | fixed |
| M4 | l.16, l.20 index / filter keys | `ciddor`, `gas` defaults; filter `table` `phaseRad` / `groupDelayS` / `outside`; `superGaussian` order 2 | `ModelRegistry.cs:160-161,228-233`; `ModelImplementations.cs:169` | missing | fixed |
| M5 | l.14-22 categories | the `field` category (spatial properties, doping) is missing from the page | `SpatialFields.cs:52-61` | missing | fixed (row with link to `spatial-properties.md#field-kinds`) |
| M6 | l.24 `ultrafast info` prints the live list | it prints `ModelRegistry.Categories`, which omits `field` | `ModelRegistry.cs:36`; `Cli/Program.cs:409` | unclear | fixed (stated) and code: C4-d1 |
| M7 | l.31 filter variables "the above + domega, omega0" | the filter has no `nu` | `ModelImplementations.cs:222` (`Vars`) | wrong | fixed (variables listed) |
| M8 | l.32 timeResponse variables | list matches `ExpressionTimeResponse.Vars`; units not stated | `ModelImplementations.cs:22` | missing | fixed (units) |
| M9 | l.36-38 constants, functions, operators | match; lookup order variable > params > constant not stated; `gauss(x, s)` meaning not stated | `Expression.cs:218-220,368-372,382-409` | missing | fixed |
| M10 | l.39-40 GPU kernel for a `timeResponse` expression | also `kerr` and `twoPhotonAbsorption`; callbacks stay on the CPU | `ModelImplementations.cs` (`IDeviceTimeResponse`); `docs/plugins/gpu.md` | missing | fixed |
| M11 | l.46-47 responses "added (mode add) or replace (mode replace, or replaceKerr)" | an element's own `timeResponse` is added; a replace override also clears added responses; `replaceKerr` keeps them; element rule beats `global` | `Compiler.cs:1143-1165` | unclear | fixed |
| M12 | l.50 `carrierResolved` makes the element carrier-resolved | only with `physics.fieldMode` `auto` (default) | `Compiler.cs` field-mode loop (`fm == "auto"`) | missing | fixed |
| M13 | l.69 targets | `material:*` and prefixes work for materials too; matching is case-insensitive | `ModelSpec.cs:Matches` | missing | fixed |
| M14 | l.70-72 element properties "any numeric field" | only keys the compiler reads through the override lookup (`Num` / `Find`); also `replaceKerr`, `transverse`, `ionization`; `dispersionSource` also `auto` | `Compiler.cs:546-583,1071,1167,1181` | unclear | fixed |
| M15 | l.72-73 material properties | also `kerrAnisotropy`, `rotationalRaman`; an unknown material property is ignored silently | `MaterialLibrary.cs:175-228` | missing | fixed (and listed in Limits) |
| M16 | l.75-77 priority | correct; a tie between rules of one source goes to the later one; source `Api` above `RunOverride` | `ModelSpec.cs:13-21,236-241` | missing | fixed |
| M17 | l.78 unmatched overrides warn | two messages: "matched nothing" and "material not used in this system" | `Compiler.cs:418-426` | missing | fixed |
| M18 | l.82 provenance | `provenance.json` is written with `outputs.json`; entries carry `shadowed` and catalogue status | `ModelSpec.cs:ProvenanceEntry.ToJson`; `BuiltInOptions.cs:177` | missing | fixed |
| M19 | l.42 heading anchor | `ultrafast.h:53` links `models.md#time-response`, which did not exist | `src/UltrafastEngine.Native/include/ultrafast.h:53` | stale | fixed (`<span id="time-response">`) |

Verdict: kinds, functions and the priority were right; one wrong variable list (M7) and many omissions.

## fidelity.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| F1 | l.22 quick skips `frog` / `xfrog` / `dscan` | also skips `sfgFrog` | `Specs/Fidelity.cs:41` (`ExpensiveInstruments`) | wrong | fixed (`sfgFrog` added) |
| F2 | l.21 standard changes only the tolerance | correct, but a tolerance already looser than the bundle value is kept (not tightened); the page says "1e-5 instead of 1e-6" and does not state the "never tightens" rule | `Fidelity.cs:77-80` | missing | fixed ("only ever loosened" rule stated) |
| F3 | l.22 spatial models replaced "where every spatial element has a beam" | `CanCollapseTransverse` all-or-nothing, and collapse is also skipped when the run has a `coherence` section | `Engine/Compiler.cs:982,1154` | missing | fixed (all-or-nothing rule, `inputBeam` case, `coherence` keeps them) |
| F4 | l.37 "polarisation made scalar" warning, "input not linearly polarised along x or y" | threshold: minor component above 1e-3 of the major one | `Fidelity.cs` (`IsMixedPolarization`) | missing | fixed (1e-3 threshold, and only when the bundle made it scalar) |
| F5 | l.35-41 warning table | all four dimensionless thresholds match (B > 3, B/(w0 T0) > 0.01 with T0 = FWHM/1.665, B > 1 and < 2 ps, P/Pcr > 0.3). The collapsed-spatial warning also needs at least one element actually collapsed; page says "in a collapsed element" (ok) | `Fidelity.cs:Assess` | (ok, no entry) | no action (no defect) |
| F6 | l.51 "Apple silicon, one thread" for the bench table | `tools/bench` case `fidelity` exists and prints the table, but nothing in the case sets one thread; `compute.threads` defaults to all cores | `tools/bench/Program.cs:104-125` | unclear | fixed (bench re-run 2026-11-26, Apple M3, default threads: "one thread" removed) |
| F7 | l.54-61 table numbers (x1.9, x2.7, +3.11 %, 3 warnings ...) | the script produces them on request; no stored result file or golden holds them (`golden-osx-arm64.txt` has only digests) | `tools/bench/golden-osx-arm64.txt` | unclear | fixed (table re-measured with provenance; accuracy columns reproduce exactly, speed-ups changed: x2.5 / x3.6, x1.0 / x1.1, x1.6 / x1.8) |
| F8 | l.65-67 tests: "quick stays within 1 % of full at B = 0.15 and warns at B = 15" | `FidelityTests.Fidelity_QuickStaysCloseToFullAtLowB` (1 % on spectral width, 1e-4 on energy; B about 0.15) and `..._QuickWarnsWhenTheDroppedPhysicsMatters` (B about 15) exist | `FidelityTests.cs:58-80` | (ok, no entry) | no action (no defect) |

Verdict: accurate except the instrument list (F1); bench numbers are reproducible by script but not archived.

## memory.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| Mem1 | l.30-31 retained memory "between a tenth of it and all of it" | matches the test; but the `MemoryEstimate.cs` header (l.5) and the Python docstring of `estimate_memory` say "within a factor of about 2" - three statements of the accuracy disagree | `MemoryEstimateTests.RealRun_RetainedManagedMemory_*` (0.1 x est < retained <= est); `MemoryEstimate.cs:5`; `python/src/ultrafast/core.py:140` | stale | fixed on the page (test bound kept, cited) and code: C4-d3 (header comment and Python docstring) |
| Mem2 | l.38-40 "`GCMemoryInfo` on .NET, `GlobalMemoryStatusEx` on .NET Framework / Windows and `/proc/meminfo` on Linux" | on modern .NET (every OS) only `GCMemoryInfo` is used; `GlobalMemoryStatusEx` and `/proc/meminfo` exist only in the net48 build (so `/proc/meminfo` = Mono / .NET Framework on Linux only) | `Engine/MemoryEstimate.cs:AvailableBytes` (`#if NET` / `#else`) | wrong | fixed (net10 GCMemoryInfo on every OS; net48 GlobalMemoryStatusEx on Windows, /proc/meminfo elsewhere) |
| Mem3 | l.13 delayed-light tail "(6 + places) x 2 x N_long x 16 B" | formula right, but `N_long` is capped by `grid.maxN`, and no tail is counted in a branched system | `MemoryEstimate.cs:93-100` (`cr.Graph?.IsBranched != true`, `Math.Min(nLong, MaxN)`) | missing | fixed (`grid.maxN` cap; not counted in branched systems; places listed) |
| Mem4 | l.16-18 `keepProfiles` `"used"` | the estimate for `used` is a bound: min(spatial elements, instruments + 1) + 1 profiles of the largest element, not the exact set | `MemoryEstimate.cs:70-73` | missing | fixed (the bound min(spatial, max(1, instruments) + 1) stated; largest element only) |
| Mem5 | l.47-49 "`all` when the point cache resumes from it" | the code sets `all` when `PrefixCache` is on (term "point cache" in the page = `prefixCache`/"node cache" elsewhere) | `Engine/Studies.cs:204` | unclear | fixed (nominal run keeps `all` while `prefixCache` is on, the default; "point cache" renamed) |
| Mem6 | l.50-55 fit memory behaviour ("slimmed copy of the Jacobian runs ... tested bit for bit") | `keepProfiles: "used"` default in fits is confirmed; the slimmed Jacobian copy and bit-for-bit claim were not traced to a test | `plugins/fitting/src/Fitting.cs:310` | unclear | fixed (slimmed content as `Eval.Slim` does; "bit for bit" replaced by the tested 1e-9 relative, `FittingMemoryTests`) |
| Mem7 | l.41-43 Python `uf.scan` / `uf.run` with `fit` check estimate | confirmed at `core.py:208,712`; `uf.study` (the lower-level call) does not, which the page does not say | `python/src/ultrafast/core.py:208,712` | missing | fixed (`uf.study` does not check) |
| Mem8 | l.68-70 GPU: "run fewer concurrent propagations ... when that many would not fit" | rule is 0.8 x TOTAL device memory / needed (between 1 and slots), not free memory | `plugins/gpu/src/GpuMemory.cs:5` | unclear | fixed (floor(0.8 × total / needed), at least 1; acceptance against free memory) |

Verdict: sizes, limits, keys, defaults and Python behaviour match the code; one wrong platform statement (Mem2) and three inconsistent accuracy statements.

## speed.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| S1 | l.5-17 / l.13 `auto` order and the net48 case | `accelerate` and `fftw` are compiled only for `NET`; on .NET Framework 4.8 only `managed` exists. The table says "managed ... .NET Framework 4.8 too" but not that the others are absent there | `Numerics/NativeFft.cs:49,86` (`#if NET`) | missing | fixed (provider table: accelerate / fftw .NET 10 only; managed the only provider of net48, with the fallback message) |
| S2 | l.19-20 "128-bit SIMD on .NET 10: SSE2 on x64, NEON on ARM64" | code uses `Vector128.IsHardwareAccelerated` (`#if NET5_0_OR_GREATER`), so it is on every .NET 5+ build, not .NET 10 only | `Numerics/ComplexKernels.cs:19-26` | unclear | kept, because the core builds only for net10.0 and net48, so "in the .NET 10 build" is exact; audit wrong |
| S3 | l.27-29 supercontinuum 23.4 s -> 8.8 s -> 5.7 s | taken from CHANGELOG (l.393); no script in `tools/bench` | `CHANGELOG.md:393` | unclear | fixed (history numbers removed) |
| S4 | l.39 `gpuMinPoints: 0` "(0: 8192 samples 1-D, 32768 spatial)" | 0 means those defaults only with `backend: auto`; with an explicit `cuda` / `opencl` 0 means no minimum | `plugins/gpu/src/GpuBackend.cs:104-112`; `BuiltInOptions.cs:152` | wrong | fixed (0 = 8192 / 32768 with `auto`, no minimum with an explicit back-end) |
| S5 | l.33-42 `compute` block | the key that switches mixed precision, `compute.precision` (`"double"` | `"mixed"`), appears nowhere on the page (l.158-179 describe the feature without naming it); the `cpu-fp32` back-end and `ensemble` study key are also not in the back-end table | `BuiltInOptions.cs:155`; `GpuTests.MixedPrecision_*` | missing | fixed (`precision`, `memoryLimitBytes`, `cpu-fp32` and the `ensemble` study key in Keys) |
| S6 | l.83-84 "5 evaluations of the nonlinear operator per step" | the stepper is embedded ERK4(3)-IP with first-same-as-last (k1 reused), i.e. four new evaluations per accepted step | `Physics/Nonlinear.cs:966-967` | unclear | fixed (four new evaluations per accepted step, FSAL) |
| S7 | l.108-123 study vs loop table (6.5 s -> 1.1 s, x5.7 ...) | `tools/bench` cases `scan-mpc`, `loop-mpc`, `scan-length`, `loop-length` exist and reproduce the comparison; times are not archived (golden file has digests only) | `tools/bench/Program.cs:59-63` | unclear | fixed (re-run 2026-11-26 on the M3: x5.3, x1.4, x2.0, with the command) |
| S8 | l.122-123 "200-point study allocates 0.8 GB ... run on 4096 points allocates 11 MB ... radial 1 GB" | no script reports allocation | none | unclear | fixed (allocations from the same bench run: 2.5 / 1.9 GB, 2.3 / 0.79 GB, 2.3 / 0.85 GB, 15 MB, 1.0 GB); audit wrong: `tools/bench` prints MB allocated (`Program.cs:68`) |
| S9 | l.129-139 RTX 3060 Ti ensemble table; l.166-177 mixed-precision table and timings (0.75 s -> 0.52 s ...) | the tests `GpuTests.MixedPrecision_*` exist (and assert the accuracy criteria), but the timings and the 200-point wall times have no script in the repository | `GpuTests.cs:390,426`; PROJECT_STATUS.md line 31 (one machine) | unclear | kept with provenance (RTX 3060 Ti, Windows, driver 610.88, CUDA 13.3, `history.md:187`), marked "no script repeats these timings"; the 0.75 / 0.52 s detail dropped (no source) |
| S10 | l.187-202 "Apple M3 ... measured 2026-11-18" fibre benchmark | no script or test builds this fibre case; `tools/bench` has no case for it | `tools/bench/Program.cs:53-64` | unclear | kept with provenance (Apple M3, 2026-11-18, `history.md` Stage 16), marked "no script builds this case" |
| S11 | l.147-151 FP32 table | `tools/bench` case `fp32` exists (`Fp32Study`); results not archived | `tools/bench/Program.cs:58,134` | unclear | fixed (re-run 2026-11-26: all values reproduce to the quoted digit) |
| S12 | l.162-164 mixed precision refusal above 70 dB | constant `MixedPrecisionMaxDynamicRangeDb = 70`; test uses 120 (contrast), 110 (crossCorrelator), 90 refused and 60 accepted | `Specs/RunSpec.cs:219`; `GpuTests.cs:426` | (ok, no entry) | no action (no defect) |
| S13 | l.214 "`stepper.tolerance` default 1e-6" | correct (`Physics/Nonlinear.cs:850`); but the page does not point out that `fidelity: standard` does exactly this | `Nonlinear.cs:850`; `Specs/Fidelity.cs:39` | missing | fixed ("that is what `fidelity: standard` sets") |
| S14 | l.185 `ultrafast devices --benchmark` "prints none on a Mac" | the CLI times only back-ends that pass the self-test; not independently confirmed here | `src/UltrafastEngine.Cli/Program.cs:145-161` | unclear | fixed (described for G0: on a Mac none, or a PoCL CPU device if installed; device discovery paragraph added) |
| S15 | l.222-223 "A cache for the compiled system ... 10 ms including the compile"; "2.7 to 3.4 GB at 1920 x 1080 with 16 stored frequencies" | measurements cited to `history.md`, which contains none of these numbers | `docs/design/history.md` (no match) | unclear | kept with provenance (`history.md:137,149`; no test repeats them); audit wrong: history.md has both numbers. The "4096 x 4096 cw 3.4 GB and 20 s" claim removed (no source) |

Verdict: structure, defaults and keys are right except `gpuMinPoints` (S4) and the missing `compute.precision` key (S5); almost all timings and sizes are machine-specific with no archived script (S3, S8-S11, S15).


## Resolution summary (WP C2d, 2026-11-26)

All 74 rows closed: 65 fixed on the page (3 of them also with a code finding: T15, Mem1, M6), 3 kept with provenance
(S9, S10, S15), 2 kept because the audit was wrong (B6, S2), 3 needed no action (F5, F8, S12), 1 code only (T14). Audit rows found
wrong or partly wrong: B6, S2, S8, S15, T9, T12.

Benchmarks re-run for provenance on 2026-11-26: `dotnet run -c Release --project tools/bench -- spm-plate mpc-argon
spatial-kerr scan-mpc loop-mpc scan-length loop-length scan-gdd loop-gdd fidelity fp32` on an Apple M3 (8 cores, 16 GB),
macOS, .NET 10.0.13; the slow test `SpatialStreamingTests.FullPanel_1920x1080_SingleFrequency_RunsWithinTheSpikeBudget`
(2.21 s, 260 / 491 MB).

## Code findings for WP C4

Collected by WP C2d. The pages document what the code does today; none of these was changed in code.

| id | file:line | finding | proposed fix |
|---|---|---|---|
| C4-d1 | `src/UltrafastEngine.Core/Models/ModelRegistry.cs:36` | `Categories` omits `field`, so `ultrafast info` and `Describe()` do not list the spatial-field kinds | add `"field"` to `Categories` |
| C4-d2 | `Schema/BuiltInOptions.cs:180` | the `transverse` description links `docs/reference/spatial-models.md`, which does not exist | link `reference/transverse.md`; regenerate |
| C4-d3 | `Engine/MemoryEstimate.cs:6`; `python/src/ultrafast/core.py:142` | both say the estimate is "within a factor of about 2"; the test asserts 0.1 x estimate < retained <= estimate | write the tested bound in both |
| C4-d4 | `Transverse/TransverseProfile.cs:47`; `Schema/BuiltInOptions.cs:194` | the comment and the schema description of `absorbingBoundary` say "linear elements"; nonlinear elements use it too (`SpatialPropagation.cs:520`) | fix both; regenerate |
| C4-d5 | `Engine/MemoryEstimate.cs:63` | profiles are doubled only for `polarization: "vector"`; `auto` that becomes vector is counted as scalar | count `auto` as vector when the pulse has a y component |
| C4-d6 | `Engine/MemoryEstimate.cs:66-67` | the time-domain state of a nonlinear spatial element (M x N_t, x 2 vector, several stepper arrays) is not counted; the estimate is low by 10x or more for cartesian Kerr runs, so the limit and the 80 % warning do not protect them | count M x N x (stages + state) for nonlinear spatial elements; add a test measuring the peak, not the retained memory |
| C4-d7 | `Engine/MemoryEstimate.cs:95-96` | `SpecOf` reads `transverse` from the element or `physics.transverse` only; a `transverse` override is ignored | read it through the override set |
| C4-d8 | `tests/UltrafastEngine.Tests/BranchingTests.cs:113-124` | `Ring_RoundTripLongerThanTheWindow_GivesAPulseTrain` names R^(n-1) T^2 per pass in its Reference but asserts only the first reflection and the budget | assert the energies of passes n >= 1 |

Physics review (WP C2d, `physics-reviewer` on the six pages): no wrong equation, sign or normalisation; fixed on the pages:
the uncounted nonlinear spatial state (memory Limits, C4-d6), the ring test's actual assertion, quick collapses only bulk and gap
spatial models (crystals and gain keep theirs) and its wider condition, the grid extent from the largest expected radius,
the leakMirror GDD now in each port's dispersion (limit removed; port mapping for `back`), the unit of `E` (√W), the
boundary bookkeeping of nonlinear elements, `modes` = number of orders, the collapsed-spatial warning condition, the ensemble
and FP32 ratios, Stockham from 256, and the vector N = 4096 case of `gpuMinPoints`.
