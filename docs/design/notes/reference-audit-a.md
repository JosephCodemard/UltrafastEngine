# Reference audit, group a (WP C1a)

Pages: `docs/reference/` `system-file.md`, `run-spec.md`, `schema.md`, `cli.md`, `c-api.md`, `python.md`, `plugins.md`, `extending.md`.
Date: 2026-11-25. Branch `dev/2026-11` at `3763dbe`. Read-only audit; no page was edited.

Method: every factual claim was checked against the source of truth: `Schema/BuiltIn*.cs` (keys, defaults), `Specs/`, `Systems/`, `Engine/`,
`src/UltrafastEngine.Cli/Program.cs` and `AgentCommand.cs` (plus a run of the built CLI: `--help`, `validate`, exit codes),
`src/UltrafastEngine.Native/include/ultrafast.h`, `python/src/ultrafast/*.py` (not `types.py`), `Plugins/Plugins.cs` and the model / instrument /
retrieval interfaces. Generated files were not read. Only problems are listed. Severity: wrong (contradicts the code), stale (was true,
no longer), unclear (misleading or ambiguous), missing (important behaviour absent).
Line numbers are those of the page at this commit.

## system-file.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:9` example `"version": 3` with `"topology"` | The file format version is 3, but the registry declares `version` with default `"2"`, so the generated reference and schema say 2 | `Schema/Migrations.cs:16` (`SystemVersion = 3`) vs `Schema/BuiltInOptions.cs:28` (default "2"); `Systems/SystemDescription.cs:51` | unclear (page right, registry default wrong) |
| 2 | `:11` `"topology": "sequential"` ... "(the default)" `:32` | `topology` is parsed and written (`SystemDescription.cs:98,137`) but is not declared in the `system` ComponentSpec, so it is absent from the schema and option reference and the page is the only place it is documented | `Schema/BuiltInOptions.cs:25-37` (no `topology`); `Systems/SystemDescription.cs:98` | missing |
| 3 | `:8` `"$schema"` / `:17` `"source": { "kind": "zemax", ... }` | fine, but the page omits the top-level `substrates` key (mirror substrates by name) that the registry declares | `Schema/BuiltInOptions.cs:35` | missing |
| 4 | `:20` `"inputBeam"` "optional default beam for nonlinear elements without one" | correct; but `environment.air.applyToGaps` (false by default; uses the file's air as the "Air" of every gap) is not mentioned next to `environment` | `Schema/BuiltInOptions.cs:46` | missing |
| 5 | `:55-63` "Common element fields" table: `id, type, label, surfaces, enabled, lengthM, sameAs` | The common group also has `next`, `inputs`, `loop` (page mentions them only in the topology paragraph), `wavelengthsM` (per element, mentioned in the per-wavelength text), `indexProfile`, `absorptionProfile`, `n2Scale`, `params`. `lengthM` is not common (it is per type; "required for bulk" is true only for bulk/gap, informational elsewhere) | `Schema/BuiltInOptions.cs:249-263` | unclear |
| 6 | `:88` "## Element types" (the section is presented as the list of element types) | Types the code declares but the page never lists: `plate` (alias `window`), `ndFilter`, `fiber` (aliases `fibre`, `waveguide`), `prism`, `resonator`, `mask`, `modulator`, `beamSplitter`, `polarizingSplitter`, `leakMirror`, `dichroic`, `delay`, `thinFilmPolarizer`, `faradayRotator`, `wavefront`. The page mentions `mask` only by a builder elsewhere, `modulator` nowhere. Only the `fiber`/`modulator`/branch ones have their own pages, and no sentence says "the others are in X" | `Schema/BuiltInElements.cs:100-340` | missing |
| 7 | the element-type headings (e.g. `### bulk / gap`, `### interface / mirror / grating`) | The registry's `DocAt(...)` points every element at `reference/elements.md#<type>`, a page that does not exist (`docs/reference/elements.md`). The generated option reference therefore links to nothing; the page's headings (`#bulk--gap...`) do not match those anchors either | `Schema/BuiltInElements.cs:9` (`const string D = "reference/elements.md#"`); `ls docs/reference` has no `elements.md` | stale (links) |
| 8 | `:99-110` `bulk`/`gap` table: `groupDelayS` "used instead of the material index unless ... `dispersionSource: "material"`" | Registry says `dispersionSource` default is `data`; the compiler default is `"auto"` (`Str(c, e, "dispersionSource", "auto")`), so the two disagree and the page does not state the values (`data`, `material`, auto) or the default | `Schema/BuiltInElements.cs:25` vs `Engine/Compiler.cs:1041` | unclear |
| 9 | `:99-110` same table | `gap` also takes `air` (own air, overrides `environment.air`) and both take `ghosts`, `airPathM`, `groupDelayYS`; `bulk` has alias `medium`. None listed | `Schema/BuiltInElements.cs:17-30` | missing |
| 10 | `:112-129` surface table: `efficiency, efficiencyS, efficiencyP`, `aperture`, ... | Missing surface keys: `linesPerMm`/`linesPerM` and `order` (grating groove density and diffraction order, the main grating inputs), `ripple`, `delayedScatter`, `substrate`, `aberration`, `ghosts`-like keys. A grating cannot be built from this table alone | `Schema/BuiltInElements.cs:39-67` | missing |
| 11 | `:131` `gratingPair`: "`order` (-1), `passes` (2), `efficiency` (per diffraction)" | right; but `aoiDeg` and `separationM` are required and `linesPerM` is an alternative, not stated | `Schema/BuiltInElements.cs:68-72` | missing |
| 12 | `:134` `chirpedMirror`: "`gddS2`, `todS3`, `fodS4` per bounce, `bounces`, `reflectance`" | also has the ripple keys (`rippleGddFs2`, `ripplePeriodNm`, `complementaryPairs`, ...) and `gddFs2`/`todFs3`/`fodFs4`; page silent | `Schema/BuiltInElements.cs:75-83` | missing |
| 13 | `:138` "`filter` / `custom`: `filter` (or `model`)" | right | - | - |
| 14 | `:150-153` `crystal` list includes `chi3`, `n2` | `chi3` and `n2` are read by the crystal compiler but are **not declared** in the `crystal` ComponentSpec, so they give an unknown-key warning (an error under `ULTRAFAST_STRICT_OPTIONS=1`). Declared but not on the page: `detuneDeg`, `noiseSeed`, `ghosts`, `ionization` | `Crystals/CrystalElement.cs:776-778`; `Schema/BuiltInElements.cs:253-270` | wrong (page lists keys the registry rejects) |
| 15 | `:150` `crystal` ... `seedNoise` (no default on page) | Registry says `seedNoise` default `true`; `CrystalElement` reads `GetBool("seedNoise", false)` | `Schema/BuiltInElements.cs:268` vs `Crystals/CrystalElement.cs:773` | unclear (registry/doc default wrong; page silent) |
| 16 | `:156-159` `lens`: "`medium` (default Air), `thicknessM`" | also `focalLengthXM`, `focalLengthYM` (cylindrical lens), `aberration`, `ghosts` | `Schema/BuiltInElements.cs:202-207` | missing |
| 17 | `:160-163` `gain`: key list `medium, lengthM, pump, inversion, beam / modeRadiusM, transverse, thermalLens, slices, segments, spm, host, repRateHz, steadyState, dopingProfile` | also `ase`, `aseSeed`, `aseModel`, `temperatureK`, `dopingDensityM3`. `amplifier` `passes` default 4, `geometry` (multipass / regenerative), `maxRoundTrips` (300) not stated | `Schema/BuiltInElements.cs:276-296` | missing |
| 18 | `:171` "`aperture` also takes `softRadiusM` (+ `order`)" | right, but already listed in the `aperture` item above (`:140`) without them: page says `aperture` is `radiusM` or `halfWidth...`, then repeats | `Schema/BuiltInElements.cs:84-87` | unclear |
| 19 | `:15-21`/`:174-181` "Unknown top-level keys are preserved when a system file is loaded and saved again." | True (`Extra`), but `substrates` is not in the known-key list at `SystemDescription.cs:127`, so it is carried as an "unknown" key and re-written in a different position | `Systems/SystemDescription.cs:127-128` | unclear |
| 20 | `:187` "Validation: ... compiles the system with a probe pulse" ; "`System.validate()`" | `uf_system_validate` / `System.validate()` exist; `ultrafast validate` also accepts a run spec and prints the memory estimate (`cli.md`). Fine. | `Api/Simulation.cs:49`; `python/src/ultrafast/core.py:373` | - |
| 21 | `:168-171` Coatings: "kinds `none`, `stack`, `quarterWave`, `ideal`, `ar`, `gdd`, `table`, `efficiency`" | `ideal`, `gdd`, `efficiency` verified in `Physics/Interfaces.cs:194,205,274`; others not re-checked (see coatings.md audit, group c) | `Physics/Interfaces.cs` | - |

Verdict: the page is largely right on what it lists but covers only about half of the declared element types and omits many keys; one wrong key
list (`crystal` `chi3` / `n2`), and anchors in the registry point to a page (`elements.md`) that does not exist.

## run-spec.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:7-23` skeleton lists `pulse, grid, physics, stepper, probes, instruments, overrides, inputBeam, compute, fit, outputs` | Also declared: `fidelity` (`full`/`standard`/`quick`, default `full`), `detectors` (detector presets), `coherence` (listed later as a heading only). `fidelity` is the only way to select the fidelity bundle in a spec and is in the CLI (`--fidelity`) | `Schema/BuiltInOptions.cs:56-74` | missing |
| 2 | `:16` `"probes": "all"` ... | `probes` default is `"end"`; not stated | `Schema/BuiltInOptions.cs:65` | missing |
| 3 | `:28-39` `pulse` table `shape`: `gaussian`, `sech2`, `superGaussian` (+ `order`), `spectrum` | `cw` is also a shape (documented in its own subsection below); default `gaussian`, `order` default 2 not given in the table | `Schema/BuiltInOptions.cs:76-79` | unclear |
| 4 | `:28-39` `pulse` table | Missing keys: `centerWavelengthNm`, `durationFwhmFs`, `bandwidthFwhmNm`, `powerW` (cw), `noise` (one photon per mode, `onePhotonPerMode`/`none`, `seed`, `polarization`), default `polarization` `"x"` | `Schema/BuiltInOptions.cs:76-90, 92-95` | missing |
| 5 | `:37` `spectrum`: "`background` (a number, `"auto"`, `"min"`)" | also `"none"` (the default); `wavelengthsNm`, `resolutionNm`, `wavelengthUnit` default `nm` | `Schema/BuiltInOptions.cs:103-112` | unclear |
| 6 | `:55-58` Cw: "`grid.windowS` (default 1 ns) only sets the energy" | `PulseSpec.CwWindowS = 1e-9`; correct. But `snapCarrier` for cw is *not* applied by default (a cw line sits on `centerWavelengthM` unless `snapCarrier` is written) and the page, in the same section, says nothing; registry text says so | `Pulses/PulseSpec.cs:249`; `Schema/BuiltInOptions.cs:121` | missing |
| 7 | `:92-102` `grid` table | `windowPs` (alt. to `windowS`) missing; `tail` sub-keys `exactSteps` (default false) and `crossPhase` (default true) not on the page (only `windowS`, `warnEnergyFraction`) | `Schema/BuiltInOptions.cs:111,123` | missing |
| 8 | `:93` `n`, `windowS` default "4096, 30 x TL" | matches (`n` 4096, `windowS` "30 x the transform limit") | `Schema/BuiltInOptions.cs:113-114` | - |
| 9 | `:115-129` `physics` table | Missing keys: `strictMaterials`, `strictMaterialsAllow`, `strictValidity` (each also set by the env var `ULTRAFAST_STRICT_MATERIALS` / `ULTRAFAST_STRICT_VALIDITY`). `strictValidity` turns validity findings into an error; users reading the page cannot find it | `Schema/BuiltInOptions.cs:140-142` | missing |
| 10 | `:140-144` `compute` table: only `backend`, `threads` | Declared: `fft` (`auto`/`managed`/`fftw`/`accelerate`, default `ULTRAFAST_FFT` or `auto`), `device` (-1), `gpuMinPoints` (8192 1-D / 32768 spatial), `memoryLimitBytes` (0 = no limit), `deviceSlots` (4), `precision` (`double`/`mixed`). `memoryLimitBytes` refuses runs and is the memory control | `Schema/BuiltInOptions.cs:171-185` | missing |
| 11 | `:143` `backend` default `cpu` "compute back-end for the nonlinear steps" | right, but `ultrafast.set_default_backend` / `ULTRAFAST_BACKEND` change the default for runs that name none (the registry says default `cpu`) | `Numerics/ComputeBackend.cs:131-134` | unclear |
| 12 | `:156-158` `## coherence` "A partially coherent source: a run over coherent modes" | no keys listed; registry has `model` (`gaussianSchell`), `m2` (required), `modes` (12), `dimensions`, `element` | `Schema/BuiltInOptions.cs:152-157` | missing |
| 13 | `:160-170` `fit` | page shows `maxIterations`, `tolerance`, `freezeGrid`; `parallel` (used by `uf.run` memory check) is not listed. The code defaults differ by estimator (`maxIterations` 20 or 60, `tolerance` 1e-4 or 1e-6) while the page gives none | `plugins/fitting/src/Estimators.cs:103,174`; `python/src/ultrafast/core.py` (`fit_opts.get("parallel")`) | missing |
| 14 | `:172-176` `outputs` table: `json`, `npz`, `csv`, `maxPoints` | `keepProfiles` (`all` / `used`, default `all`) missing; `json` writes `summary.json` **and** `provenance.json` | `Schema/BuiltInOptions.cs:222-225` | missing |
| 15 | `:131-138` `stepper` | right (defaults 1e-6, 0.01, 0.05 check). The registry's description points to `docs/reference/solvers.md`, which does not exist | `Schema/BuiltInOptions.cs:216`; no `docs/reference/solvers.md` | stale (link in registry) |
| 16 | `:10` `"version": 2` | right (`RunVersion = 2`) | `Schema/Migrations.cs:18` | - |
| 17 | `:3` "`options.md#run-spec`" | the generated page has a run-spec section; not re-read (generated) | - | - |

Verdict: the keys present are right and their defaults match; the page leaves out about 25 declared keys, in particular `fidelity`, `compute.*`
beyond two keys, `physics.strict*`, `outputs.keepProfiles` and the whole `coherence` section.

## schema.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:57` "The current version is **2** for both." | System files are at version **3**, run specs at 2 | `Schema/Migrations.cs:16-18` | wrong |
| 2 | `:62-65` migrations table (run 1 to 2; system 1 to 2) | There is no system 2 to 3 migration (`MigrateSystem` only handles `v < 2`), so a v2 file loads as is and the 3 is only a marker for `topology` / `next`. The page says nothing about what version 3 added (it is said in `system-file.md:9`: "`topology` and `next` need 3") | `Schema/Migrations.cs:66-78` | missing |
| 3 | `:67-75` rename table: `dnDT` -> `dnDTPerK` "refractiveindex index model, gain media" | the rename scope is `"*"`: it applies to **any** object with a `dnDT` key | `Schema/Migrations.cs:30` | unclear |
| 4 | `:67` "(an override of the old name works too)" for `betas` | stated in the code comment for the fibre compiler; no test name given. Not re-verified at run time | `Schema/Migrations.cs:22` | unclear |
| 5 | `:44` "keys starting with `_` and `comment`, `description`, `notes` are free text everywhere" | `$schema` is also exempt; `_comment` is explicit, any `_`-prefixed key works | `Schema/Validator.cs:19,71` | - |
| 6 | `:8-12` "From that single source the engine produces: schemas, warnings, option reference, Python TypedDicts" | also the Python API docs (`python-api.md`) and `materials-catalogue.md` are generated (the `/regen` list has six files) | `AGENTS.md` (Rules) | missing |
| 7 | `:77-82` "Adding an option": steps 1-3 | Step 3 omits `python3 python/tools/gen_types.py` and `gen_api_docs.py`; AGENTS.md says tests check all five generated files | `AGENTS.md` | stale |
| 8 | `:56` "`ultrafast migrate file -o new`" | right; `migrate` accepts `.ufs` or `run.json`; page does not say it accepts run specs too | `Program.cs` usage text | missing |
| 9 | `:60` "will not be removed before 2.0" | a policy statement; not in code. The decision log was not checked | - | unclear |
| 10 | `:50-52` "The test `SchemaTests.ExamplesUseOnlyDeclaredOptions` ... (`SchemaTests.Migration_*`, `UnitInKeyRenames_*`)" | tests exist (`SchemaTests.cs:27,104,121`) | `tests/UltrafastEngine.Tests/SchemaTests.cs` | - |
| 11 | `:44-46` "`WARNING: Element 'plate' (bulk): unknown parameter ...`" example | an example for `bulk`, but the element type `plate` is a different type; cosmetic | `Schema/BuiltInElements.cs:140` | unclear |

Verdict: mostly right; one wrong statement (the system format version), and the migration table and the contributor steps are out of date.

## cli.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:66` "Exit codes: 0 success, 1 a failed command or a file with errors." | 0 success; **1** for usage errors and an unknown command; **2** for an error in a file or a model (any caught `JsonException`, `ModelException`, missing file, ...); **3** when a `devices --selftest` fails. Checked by running: `validate /nonexistent.ufs` exits 2, `foo` exits 1 | `Program.cs` (`return 2` in `Main`'s catch; `DevicesCommand` `return ok ? 0 : 3`); run of the built CLI | wrong |
| 2 | `:11` `ultrafast run` "[-o dir]" | The default output folder is `results/<run name>` next to the run file (not stated); `--out` is an alias; `run` also runs a plugin run mode when the spec has its key (`fit`) and `coherence` runs | `Program.cs` (`Run`) | missing |
| 3 | `:23` `ultrafast crystal` options | Missing: `--a`, `--b` (biaxial indices), `--plane XY\|XZ\|YZ`, `--pump-intensity <W/m^2>`, and the whole `ultrafast crystal --request <request.json>` form | `Program.cs` usage text (crystal block) | missing |
| 4 | `:34` `ultrafast reference [-o options.md]` | `ultrafast reference materials` writes the material catalogue reference (Markdown); not mentioned | `Program.cs:86-88` | missing |
| 5 | `:43` `ultrafast agent ... mcp-config [--workspace DIR] [--client codex]` | the CLI's own usage text lists `mcp-config` without options; code reads `--workspace` (default `.`) and `--client` (default `claude`); page is right, `--help` is incomplete | `AgentCommand.cs:64-65` | - |
| 6 | `:3-4` "Three more commands come with the Python package: `ultrafast-plot`, `ultrafast-mcp` and `ultrafast-agent`." | right (`pyproject.toml` scripts) | `python/pyproject.toml:35-37` | - |
| 7 | `:52` `ultrafast-plot ... [--labels ...] [--show]` | right; page omits that `--out` default depends on the number of folders (`DIR/plots` or `.`) | `python/src/ultrafast/plot.py:33` | unclear |
| 8 | `:53` `ultrafast-mcp [--workspace DIR] [--budget-s s] [--memory-limit-gb g]` | right; defaults (`.`, 300 s, 8 GB) not stated | `python/src/ultrafast/mcp/server.py:220-222` | missing |
| 9 | `:58-64` Environment variables (5 listed) | The code reads many more: `ULTRAFAST_BACKEND`, `ULTRAFAST_DEVICE`, `ULTRAFAST_FFT`, `ULTRAFAST_FFTW_LIBRARY`, `ULTRAFAST_CUDA_LIB_DIR`, `ULTRAFAST_OPENCL_LIBRARY`, `ULTRAFAST_PLUGIN_PATH`, `ULTRAFAST_NO_PLUGINS`, `ULTRAFAST_STRICT_VALIDITY`, `ULTRAFAST_STRICT_MATERIALS`, `ULTRAFAST_HITRAN_DIR`, `ULTRAFAST_RII_PATH`, `ULTRAFAST_MATERIALS`, `ULTRAFAST_DEBUG` (prints the stack trace of an error), `ULTRAFAST_MCP_MEMORY_LIMIT_BYTES` | `grep ULTRAFAST_` over `src`, `python/src`, `plugins` | missing |
| 10 | `:63` `ULTRAFAST_HOME` "folder of user plugins and the user material library" | right (`~/.ultrafast` default); the page does not give the default | `Plugins/Plugins.cs:237-243` | unclear |
| 11 | `:62` `ULTRAFAST_LIBRARY` "path of the native library the Python package loads (a source build)" | right, plus a `UltrafastEngine.Native.dll` for the hosted back-end | `python/src/ultrafast/_native.py:12,137` | - |
| 12 | `:64` `ULTRAFAST_SLOW=1` "runs the slow Python tests" | the C# suite uses `--slow`; `ULTRAFAST_SLOW` is read only by Python tests (not in `src`) | `CLAUDE.md` commands | unclear (not an engine variable; belongs in a contributor page) |
| 13 | `:16` `ultrafast retrieve <pcgpa\|epie\|dscan\|gerchbergSaxton> <trace> ...` | right; `uf.retrieve` / `uf_command` document only three methods (see python.md #14) | `Api/Commands.cs:194` | - |
| 14 | `:38-47` `ultrafast twin`, `zemax-*` | these appear in `--help` only when the plugin is installed; page does not say so for `twin` (it does for zemax: "experimental") | `Program.cs` (plugin commands via `FullUsage`) | unclear |

Verdict: commands and flags are accurate but the exit codes are wrong, `crystal` flags and the environment table are incomplete.

## c-api.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:28-29` "Codes: `ARGUMENT -1`, `PARSE -2`, `MODEL -3`, `RUN -4`, `NOT_FOUND -5`, `CALLBACK -6`, `CANCELLED -7`, `BUFFER -8`." | the header also defines `UF_E_INTERNAL -99` | `ultrafast.h:45` | missing |
| 2 | `:31` "`uf_capabilities` returns the built-in model kinds, materials, element types and instruments (JSON)." | the header also says crystals (`model kinds, materials, crystals and instruments`) | `ultrafast.h:112` | unclear |
| 3 | `:66-70` `uf_response_ctx` field list | matches the struct (n, t, t_lab, t_arrival, z, length, intensity, intensity_x/y, instant_intensity, field, omega0, area, carrier_resolved, element_id, x, y, spatial) | `ultrafast.h:54-72` | - |
| 4 | `:72-73` `uf_element_ctx` field list | matches | `ultrafast.h:87-102` | - |
| 5 | `:51-52` the callback table lists `uf_run_set_element_callback`'s `fn` as `int fn(void*, uf_element_ctx*)` and the progress callback as "return non-zero to cancel" | right. But the page says callbacks "return 0 for success (non-zero aborts the run with `UF_E_CALLBACK`)" (`:40-41`), and the progress callback's non-zero return cancels with `UF_E_CANCELLED` (header: `UF_E_CANCELLED -7`); the rule is stated for all callbacks | `ultrafast.h:42-43,105` | unclear |
| 6 | `:88-95` `uf_command` "Commands: `study`, `retrieve`, `materials.list`, `materials.show`, `materials.import`, `devices` and `compute.default` (below)" | all seven exist. `compute.default` is described below only by `uf_set_default_backend`; its request shape (`{"command": "compute.default", "backend": ...}`) is never shown | `Api/Commands.cs:25-33` | missing |
| 7 | `:101-104` "`uf_set_default_backend` ... NULL restores `ULTRAFAST_BACKEND` or `"cpu"`" | the native code sets `DefaultName = n` with `n` null/empty accepted; `DefaultName`'s getter then falls back to `ULTRAFAST_BACKEND` then `"cpu"`. Consistent | `Native/Exports.cs:197-205`; `Numerics/ComputeBackend.cs:131-134` | - |
| 8 | `:6-10` "dotnet publish ... -r win-x64 ... ; `-p:UF_NO_AOT=true` builds the managed, hosted variant" | right; `UF_NO_AOT` is a csproj switch (AOT only when a `RuntimeIdentifier` is given) | `Native/UltrafastEngine.Native.csproj:13,22-23` | - |
| 9 | `:34-35` table "library" group: `uf_command(request, &json)` listed; `uf_crystal_phase_match`, `uf_pulse_calc` | all in the header | `ultrafast.h:108-129` | - |
| 10 | `:41-45` "A Branched system ... `uf_result_summary_json` gains `graph`" | right (`Graph` is added to the summary when present) | `Results/RunResult.cs:198` | - |
| 11 | `:112-115` "The Python package is a thin `ctypes` layer over this same API." | for the native back-end; the hosted back-end loads the managed DLL through `clr_loader` and calls the exports by function pointer (not by the C header). Pure `ctypes` is the native mode | `python/src/ultrafast/_native.py:1-12` | unclear |
| 12 | `:79` "The library has the gpu plugin compiled in (unless it was packaged without it)" | a packaging statement, not checkable from the source tree | - | unclear |

Verdict: accurate and complete as to functions; one missing error code and a few vague points.

## python.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:9` "`pip install -e "python[hosted,plot,test]"` from the repository" | `hosted` and `plot` are in the default install since 1.0.0a2 (the extras stay for compatibility); `paper`, `animate`, `all` extras exist but are not mentioned; `pip install ultrafast-engine` from PyPI is not mentioned at all | `python/pyproject.toml:22-30` | stale |
| 2 | `:14-15` "looks for the engine in `$ULTRAFAST_LIBRARY`, then `ultrafast/bin/native`, `ultrafast/bin/hosted`, then a repository build" | matches | `python/src/ultrafast/_native.py:12-15` | - |
| 3 | `:22` API table: `uf.calc.limit(shape, duration= / bandwidth_hz= / bandwidth_m=, center=)`, `analyze(duration=, ..., center=)` | the parameters are `duration_s` and `center_m`; `duration` / `center` work only through the `renamed_kwargs` compatibility layer | `calc/pulse.py:24,102-113` | stale |
| 4 | `:21` `uf.crystal.phase_match(crystal, process, type=None, fundamental=, pump=, signal=, idler=, noncollinear=, qpm=, length=, temperature_c=, ...)` | parameters are `fundamental_m`, `pump_m`, `signal_m`, `idler_m`, `length_m`; the short names work only via `renamed_kwargs`. Also has `a_m`, `b_m`, `plane`, `theta_deg`, `phi_deg`, `pump_intensity_w_per_m2`, `polarizations` | `calc/crystal.py:25-36` | stale |
| 5 | `:23` `uf.calc.tbp(shape)` ... "stretch factor, GDD" | `tbp` and `measure` exist; `measure(time_s, intensity, spectrum, wavelength_m=)` right (also `frequency_hz`, `shape`) | `calc/pulse.py:92-125` | - |
| 6 | `:27` `uf.run(system, spec, callbacks={name: fn}, elements={name: fn})` | also `progress=`, `fields=` (stated lower down), and `memory="warn"\|"raise"\|"off"` (preflight for a `"fit"` spec) which is not mentioned | `core.py:703-707` | missing |
| 7 | `:35` `uf.plot(result, "overview" \| "temporal" \| "spectrum" \| "phase" \| "losses" \| "elements")` | also `"measurements"` and `"numbers"` | `plotting/__init__.py:102-114` | missing |
| 8 | `:36` "`uf.study(..., mode="scan" \| "optimize" \| "sensitivity", **options)`" | `mode` also accepts `"coherence"`; `uf.coherence(...)`, `uf.scan` (documented lower), `uf.estimate_memory`, `MemoryWarning`, `uf.retrieve_multi` exist and are exported but not in the table | `core.py:171-185,273,301,131-146` | missing |
| 9 | `:38` `uf.materials.available(), .show(name), .import_page(path, name=), .refractiveindex(page, pressure_bar=)` | right; `refractiveindex` also has `temperature_k` | `materials.py:19-37` | - |
| 10 | `:31-33` "`b.link(element, next, inputs=, loop=)`", `b.virtual_input(pulse=, delay_s=, phase_rad=, coherent_with=)`, `b.loop(kind, max_round_trips=, tolerance=)` | match the signatures; `b.same_as(...)` (identical elements, documented in `system-file.md`) is not in the Python page | `build.py:35-64,42` | missing |
| 11 | `:39` "`b.compute(backend, threads=, fft=, device=, gpu_min_points=)`" | right (precision, memory limit, device slots are not exposed by the builder; use a dict) | `build.py:765-776` | unclear |
| 12 | `:57-62` "`ultrafast.system`: `Sequential`, `Branched`, `link`, `loop`, `virtual_input`, `system`, `System`, `load`, `save`" | not re-verified each name (`ultrafast/system/` package); the page says `uf.System` is in `ultrafast.system` while `uf.System` is `core.System` (the class that wraps a C handle), so two different `System`s? | `__init__.py:17`; `python/src/ultrafast/system/` | unclear |
| 13 | `:81` "Everything that was public in 2.2.0 still imports (a test checks a list of the old names)" | the package version is **1.0.0** (`__version__`); "2.2.0" is the pre-release internal number and means nothing to a reader; `crystal.py` still comments "names of the 2.2.0 module" | `python/src/ultrafast/__init__.py:30`; `crystal.py:3` | stale |
| 14 | `:40` `uf.retrieve("pcgpa" \| "epie" \| "dscan", ...)` | the engine also has `gerchbergSaxton` (and alias `gs`); CLI documents it, Python page does not | `Api/Commands.cs:194` | unclear |
| 15 | `:82-85` `ResponseContext` fields | match `core.py` slots (plus `n`) | `core.py:380-405` | - |
| 16 | `:98` `Helpers: ...` list "`system, bulk, gap, interface, mirror, dispersion, grating, grating_pair, mask, chirped_mirror, aperture, waveplate, polarizer, filter_element, probe, ... sellmeier, gas, coating_gdd, coating_quarter_wave, pulse, instrument, override, run_spec, save`" | all exist. Exist but not listed: `delay`, `beam_splitter`, `polarizing_splitter`, `leak_mirror`, `dichroic` (listed in the API table), `thermal_lens` (listed), `measured_spectrum`, `constant_beam`, `sampled_beam`, `fiber` (listed) | `build.py` | - |
| 17 | `:116-124` the `crystal` example uses `noncollinear="magic"` | right; `b.aux_beam("pump", b.pulse(...), radius_m=0.6e-3)` right | `build.py:499-520` | - |
| 18 | `:197` `uf.scan(system, run_spec, axes, outputs, fidelity=None, cache=None, parallel=1)` | also `memory="warn"` and `**options` | `core.py:246-247` | unclear |
| 19 | `:208-218` `ultrafast-plot` figure table: `system, overview, instruments, measurements, fit, probes, elements, oscillator, gain, byproducts, compare` | the built-in list is `FIGURES = [system, overview, instruments, measurements, probes, elements, gain, oscillator, byproducts]`; `fit` comes from the fitting plugin and `compare` is a separate command mode. Matches in practice | `plotting/__init__.py:45` | - |
| 20 | `:224-234` `up.fit(res)` | `fit` is not a function of `ultrafast.plotting` itself (registered by the fitting plugin's Python module); `up.fit` raises `AttributeError` without it. Not verified at run time | `plotting/__init__.py` (no `def fit`) | unclear |
| 21 | `:76` `ultrafast.diagram` "moved" | right: `diagram.py` re-exports from `plotting.diagrams` with a `DeprecationWarning` | `diagram.py` | - |
| 22 | `:6-12` `uf.System.load(path \| dict \| json)` ... `.validate()` | right | `core.py:357-378` | - |
| 23 | the page never mentions `ultrafast.laser.Oscillator` beyond one line, `ultrafast.cells`, `ultrafast.measured` (only inside an example) or the `ultrafast.calc` beam / nonlinear calculators (`calculators.md` covers them) | not an error | `__init__.py:20-29` | - |

Verdict: mostly right; the calculator signatures use old short names, "2.2.0" is stale, and six public functions are not in the table.

## plugins.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:30-37` search-order table, row 3: "`artifacts/plugins` of a source checkout (Python package used from the checkout)" | the C# `PluginManager.SearchPaths()` (used by the CLI and `ultrafast plugins paths`) has only three entries: `ULTRAFAST_PLUGIN_PATH`, `plugins/` next to `UltrafastEngine.Core.dll`, the user folder. `artifacts/plugins` is in the Python helper `uf.plugins.search_paths()` only. Where a source build places plugins is `plugins/` beside the assembly (via `Plugin.targets`) | `Plugins/Plugins.cs:221-235`; `python/src/ultrafast/plugins.py:49-66` | unclear |
| 2 | `:31` "`ULTRAFAST_PLUGIN_PATH` (folders separated by `:` on macOS/Linux, `;` on Windows)" | `Path.PathSeparator`; right | `Plugins/Plugins.cs:224` | - |
| 3 | `:25` Python example `[p.id for p in uf.plugins.installed()]       # ['fitting', 'gpu', 'hitran', 'zemax']` | six first-party plugins exist: `ao`, `fitting`, `gpu`, `hitran`, `twin`, `zemax`; the intro of the page lists all six, the example lists four | `ls plugins/` | stale |
| 4 | `:39-43` "A run spec with `"fit"` or `"backward"`, or a system with a `transform` element, stops with a message naming the fitting plugin" | right; the same happens for the `ao` plugin's element types (message names the ao plugin) which is not stated | `Api/Simulation.cs:34`; `Engine/Compiler.cs:700,1629` | missing |
| 5 | `:45-47` "NativeAOT builds ... registered in code (`PluginManager.Register(new FittingPlugin())`)" | `PluginManager.Register(IPlugin, string source = "code")` exists | `Plugins/Plugins.cs:207` | - |
| 6 | `:56-66` `plugin.json` example and key table | `Manifest.Load` reads `id` (required), `name`, `version`, `description`, `engine`, `entry`, `assemblies`, `python.module`, `commands`: matches. Page says `id` "must equal `IPlugin.Id`": case-insensitive compare | `Plugins/Plugins.cs:146-160, 291-292` | - |
| 7 | `:98-114` C# example: `ctx.RegisterElement("attenuator", (services, element, compiled) => compiled.Post.Add(new FilterTerm(new ConstantFilter(...), "attenuator")), new ComponentSpec(...))` | `CompiledElement.Post` is a `List<LinearTerm>`; `FilterTerm(ISpectralFilter, string)` and `ConstantFilter` exist. The `ComponentSpec` call `.Num("transmission", "", "Power transmission", "0.5")` matches the builder | `Engine/Compiler.cs:74`; `Physics/LinearTerms.cs:573`; `Physics/ModelImplementations.cs:272` | - |
| 8 | `:119-132` extension-point table, `RegisterSpecKeys(spec)` "category `runTop` ... or `instrumentCommon`" | right; the registry's categories are exactly `runTop` and `instrumentCommon` | `Schema/ReferenceGenerator.cs:27-28`; `Plugins/Plugins.cs:133` | - |
| 9 | `:119-132` "`RegisterModels(configure)` ... (index, attenuation, timeResponse, filter, beam, delayedResponse, aperture)" | categories are strings given to `Register<T>(category, kind, ...)`; also `field` (spatial fields) is a category in the registry and not listed | `Models/SpatialFields.cs:54-58` | missing |
| 10 | `:134-136` "`RunResult.Extensions[key]` ... `result.Fit()` ... `result.Comparisons`" | `Comparisons` and `Extensions` exist; `Fit()` is an extension method of the fitting plugin | `Results/RunResult.cs:61,62`; `plugins/fitting/src/FittingPlugin.cs:139` | - |
| 11 | `:138-142` "Building the folder: `build/Plugin.targets`" | file exists; not re-read | `build/` | - |
| 12 | `:144-148` Python `register(host)`; `host.register_figure(name, maker, after=None)` | right (`_Host.register_figure`) | `plugins.py:99-150` | - |
| 13 | `:150-160` Tests: "`PluginTests` ... orders pre-release versions" | tests exist: `VersionComparison_OrdersPreReleases`, `TooNewPlugin_IsReportedNotLoaded`, `Install_AndRemove_InTheUserFolder`; there are also `TwinPlugin_RegistersItsCommand_...` and `Camera_UnifiedImaging_...` / `RepeatedInstruments_...` which are not plugin tests | `tests/.../PluginTests.cs:41-155` | unclear |
| 14 | `:6-9` "the live-data plugin and ML plugins follow after the stable release" | roadmap statement, not code | - | unclear |
| 15 | the page documents no interface member lists for `IInstrumentHook`, `IRunMode`, `IResultExtension`, `IRunReporter`, `ICliCommand`, only their names | `IRunMode.Run(system, run, registry, progress, cancel)`, `IInstrumentHook.Keys` / `AfterMeasure`, `ICliCommand.Name` / `Usage` / `Run(args, out, err)` are what a plugin author needs | `Plugins/Plugins.cs:57-96` | missing |

Verdict: mostly right; one unclear search-order row, a stale plugin list in the example, and the hook / run-mode interfaces undocumented.

## extending.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | `:20` "Delegates: `IndexCallback`, `TimeResponseCallback`, `FilterCallback`, `BeamCallback`, `ArrayCallback`." | two more exist: `ElementCallback(UserElementContext)` and `FieldCallback(x, y, z, valueOut)` (position-dependent properties) | `Models/Interfaces.cs:185`; `Models/SpatialFields.cs:34` | missing |
| 2 | `:8` "reg.RegisterTimeResponse(\"plasma\", (ctx, dnRe, dnIm) => ...)" and `ctx.T`, `ctx.N`, `ctx.Intensity` | `ResponseContext` has `T`, `N`, `Intensity` | `Models/Interfaces.cs:43-78` | - |
| 3 | `:26` "Implement the interface (`IRefractiveIndex`, `IAttenuation`, `ITimeDomainResponse`, `ISpectralFilter`, `IBeamModel`, `IAperture`, `IDelayedResponse`)" | also `ISpatialField` (category `field`), `ISmoothIndex`, `IDispersiveResponse` (optional extensions of `ITimeDomainResponse`) | `Models/Interfaces.cs:17-150`; `Models/SpatialFields.cs` | missing |
| 4 | `:31-35` example `ThermalSellmeier : IRefractiveIndex` with `public void Evaluate(double[] omega, double[] n)` | interface is `Evaluate(double[] omega, double[] nOut)`; parameter name irrelevant. `spec.GetRequired("temperatureK")` exists on `ModelSpec` | `Models/Interfaces.cs:19-20` | - |
| 5 | `:47-49` "Derive from one of the term base classes in `Physics/LinearTerms.cs` ... `PhaseTerm`, `GroupDelayTerm`, `JonesTerm`" | not re-verified one by one (`FilterTerm : PhaseTerm` is confirmed); "Element types are mapped to terms in `Engine/Compiler.cs` (`CompileElement`)" is outdated for a new element type: a plugin registers it with `ctx.RegisterElement` (see plugins.md); no mention of this route here | `Engine/Compiler.cs:569`; `Plugins/Plugins.cs:105` | stale / missing |
| 6 | `:53-65` instrument example `o.Add("trace", someArray, "W", "what it is")`, `PulseMetrics.Summarize(ctx.Pulse).PeakPowerW`, `InstrumentFactory.Register("myMeter", spec => new BeamQualityMeter())` | `InstrumentOutput.Add(string suffix, double[] data, string unit, string description, params int[] shape)`; `Summarize(PulseField)` and `PeakPowerW` exist; `InstrumentFactory.Register(kind, Func<JsonObject, IInstrument>)` exists. The class is named `BeamQualityMeter` but it measures peak power; a new instrument also needs a `ComponentSpec` registered so options are not flagged as unknown (plugins.md does this via `RegisterInstrument(kind, factory, spec)`) | `Instruments/Instruments.cs:42,95,131`; `Results/PulseMetrics.cs:229` | unclear |
| 7 | `:67-71` "Implement `Numerics.IFft` (one in-place power-of-two transform)" | the interface is `TransformInPlace(Complex[] data, int exponentSign, bool normalize)` plus `Name`; its comment says "Any length (time grids are powers of two)". The page's parenthesis contradicts both the next sentence ("Other lengths use the built-in mixed-radix / Bluestein transforms") and the interface comment | `Numerics/ComplexFft.cs:269-275` | unclear |
| 8 | `:71` "FFTW, MKL (through its FFTW interface) and Apple Accelerate are built in" | providers `managed`, `fftw`, `accelerate`, `auto` are the values of `compute.fft`; MKL is reached only via the FFTW library path (`ULTRAFAST_FFTW_LIBRARY`), no MKL provider of its own | `Schema/BuiltInOptions.cs:176`; `Numerics/ComplexFft.cs:292-298` | unclear |
| 9 | `:79-82` "`IComputeBackend` ... `ctx.RegisterBackend(...)` ... the gpu plugin is a complete example (`IDeviceBackend`, `DeviceSpec()`)" | `IDeviceBackend : IComputeBackend` exists; `DeviceSpec()` is a method of the problem classes (`EnvelopeProblem`, `SpatialProblem`, `Chi2Problem`), not of the backend | `Numerics/ComputeBackend.cs:53`; `Physics/Nonlinear.cs:360` | unclear |
| 10 | `:84-86` "Implement `Retrieval.IRetrieval` (`Name`, `TraceKinds`, `Retrieve(TraceData, JsonObject options)`)" | the interface also requires `string Description { get; }`; a class written from the page does not compile | `Retrieval/Retrieval.cs:218-225` | wrong |
| 11 | `:96-99` "See `CONTRIBUTING.md`" | file exists at the repository root | `CONTRIBUTING.md` | - |
| 12 | `:3-6` "three levels ... 3. New model kinds, element types, instruments or FFT back-ends in C#" | does not say how to ship them: a **plugin** (plugins.md) is the supported way; a library user can also register in code | `Plugins/Plugins.cs` | missing |

Verdict: mostly right; one wrong member list (`IRetrieval` lacks `Description`), the delegate list is incomplete and the page ignores the plugin route.

## Cross-cutting findings (not tied to one row)

* `Schema/BuiltIn*.cs` disagrees with the compiler on three defaults: `system.version` (declared 2, engine 3), `crystal.seedNoise` (declared true, code false) and
  `bulk.dispersionSource` (declared `data`, code `auto`). The generated option reference inherits these.
* `Schema/BuiltInElements.cs` links every element to `reference/elements.md` and the stepper to `reference/solvers.md`, and the transverse spec to
  `reference/spatial-models.md`: none of these three pages exists in `docs/reference/` (the pages are `system-file.md`, `nonlinear.md`, `transverse.md`).
* The crystal compiler reads `chi3` and `n2` which the registry does not declare (a typo-check would flag them in strict mode).
