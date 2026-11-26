# Code map

Paths are under `src/UltrafastEngine.Core/` unless stated. Line counts change; use this to find where to start.

## Entry points

| file or folder | what it does |
|---|---|
| `Api/Simulation.cs` | `Run(system, run)`, `Validate(system, run)`; the entry point for every front end |
| `src/UltrafastEngine.Cli/Program.cs` | the `ultrafast` command: `run`, `validate`, `study`, `retrieve`, `schema`, ... |
| `src/UltrafastEngine.Native/` | the C API (`include/ultrafast.h`) |
| `python/src/ultrafast/` | the Python package: `build.py` builders, `types.py` generated TypedDicts; `system/`, `components/`, `sources.py`, `instruments/` (the builders by kind, CapWords aliases), `algorithms/` (`zernike`, NumPy only), `results/`, `plotting/` (package: figures, `style` (`use_style`, the four styles), `zernike`, `animate`, `diagrams` (system schematic), `graph_layout` (deterministic layers, lanes, grouped runs, loop arcs, orthogonal routes of a branched graph; no drawing), `spectrogram` (`spectrogram`, `spectrogram_3d`, `animate_evolution`)), `core.py` (running) |

## Describing a run

| file | main classes | what it does |
|---|---|---|
| `Systems/SystemDescription.cs` | `SystemDescription`, `SystemElement` | a `.ufs` file in memory; each element stays JSON (`sameAs` copies are expanded; `SystemElement.SameAsSpec` keeps the written form for saving) |
| `Systems/SameAsResolver.cs` | `SameAsResolver` | expands `"sameAs"` copies into full element bodies when a system is read; `InstanceKeys` are the keys a copy may set |
| `Systems/SystemGraph.cs` | `SystemGraph`, `GraphEdge`, `GraphLoop`, `GraphStage`, `Ports` | the connected form of a system: order, loops, virtual ports, verification. Knows no physics |
| `Specs/` | `RunSpec` | a `run.json` in memory |
| `Pulses/` | `PulseSpec`, `PulseField`, `FrequencyGrid`, `GridResampler` | input pulses; a field on a grid; moving between grids |
| `Pulses/Pedestals.cs`, `Pulses/MeasuredSpectrumPreparation.cs` | | input pedestals (replica, ASE, phase noise, exponential, measured contrast trace); cleaning of a measured input spectrum (sort, merge, background, deconvolution, Gaussian tails) |
| `Models/` | `ModelRegistry`, `ModelSpec`, `Expression`, `SpatialFields` | the replaceable models (index, beam, filter, responses), expressions, position-dependent fields |
| `Serialization/` | | JSON with comments, `.npz` reading and writing |
| `Schema/` | `ComponentSpec`, `OptionRegistry`, `Validator`, `Migrations` | declarations of every key and port; generators of docs and JSON schemas; upgrades of old files |

## Compile

| file | main classes | what it does |
|---|---|---|
| `Engine/Compiler.cs` | `Compiler`, `CompiledRun`, `CompiledElement`, `CompilerServices` | each element to linear terms, a nonlinear segment or an active element. Beam tracking |
| `Engine/CompilerComponents.cs`, `CompilerFiber.cs`, `CompilerSubstrate.cs` | | plates, polarisers, prisms; fibres; mirror substrates |
| `Engine/GridPlanner.cs` | `GridPlanner` | the time grid a run needs |
| `Materials/Catalogue.cs`, `CatalogueModels.cs`, `CatalogueReference.cs` | `Catalogue`, `CatalogueEntry`, `CatalogueQuantity`, `CatalogueStatus` | the material catalogue (kinds solid, gas, raman, gain, crystal): loads the embedded files of `Data/materials/` (+ the user's `~/.ultrafast/materials`), builds index / Raman objects from them, writes `docs/reference/materials-catalogue.md`. `MaterialLibrary`, `GasData`, the Raman models, `GainMedium.Get`, `CrystalLibrary.Get` and `CiddorAirIndex` read their numbers here |
| `Optics/BeamTracking.cs` | `BeamState` | Gaussian q, ray matrices and a five-ray bundle through the system; `Duct` / `DuctMatrix` (graded-index duct, the thermal lens of a gain slice), `ThinPowerForEffectiveFocalLength` |
| `Physics/DispersiveResponses.cs` | `DispersiveTerm`, `DispersiveResponses` | the frequency-resolved part of a time-domain response (the Drude term of `PlasmaResponse`, `IDispersiveResponse`): evaluated at the carrier in time, applied as `i (omega/c) R(omega) FFT[dn A]` by the envelope, carrier-resolved and spatial propagators |

## Execute

| file | main classes | what it does |
|---|---|---|
| `Engine/Executor.cs` | `Executor`, `ElementRecord` | `Execute`; `RunElement` runs one element on one pulse; instruments; `ProfilesInUse` (`outputs.keepProfiles: "used"` drops unread profiles) |
| `Engine/GraphExecutor.cs` | `Executor` (partial), `GraphCache` | `RunGraph`: queues at ports, loops, the graph budget, the study node cache |
| `Engine/MultiPort.cs` | `BeamPacket`, `Packets`, `MergeContext`, `BeamSplitterElement`, `BeamTiltElement`, `DelayTerm` | beams at ports; how packets merge, with mode overlap; the splitter family |
| `Engine/PathLedger.cs` | `PathLedger` | carrier phase, delay and totals of one path |
| `Engine/Active.cs` | `IActiveElement`, `ActiveContext` | the interface of elements that do more than linear terms and a nonlinear segment |
| `Engine/AuxBeams.cs` | `AuxBeamSpec`, `VirtualInput` | beams on ports with no beamline |
| `Engine/TailLedger.cs` | `TailLedger` | delayed light outside the main window |
| `Engine/Studies.cs` | `Studies`, `StudySpec` | scans and optimisation: frozen grid (union over the range ends), prefix cache, point cache (`cache`), instruments skipped when unused |
| `Specs/Fidelity.cs` | `FidelityPlan`, `Fidelity` | the run-level `fidelity` bundles (applied in `RunSpec.FromJson`) and the unreliability warnings after a run |
| `Numerics/Fp32Emulation.cs` | `Fp32EmulatedBackend` | `cpu-fp32`: nonlinear operator in single precision, for the FP32 study |
| `Engine/PartialCoherence.cs` | `PartialCoherence` | a run over coherent modes |
| `Results/` | `RunResult`, `PulseMetrics` | `summary.json`, `result.npz` |

## Physics

| folder | what it holds |
|---|---|
| `Physics/` | `LinearTerms.cs` (`LinearTerm`, `PhaseTerm`, `GroupDelayTerm`, `JonesTerm`, `DispersionPoint`); `Nonlinear.cs` (`EnvelopeProblem`, `FieldProblem`, steppers); `DispersiveResponses.cs`; `DeviceResponses.cs` (time-domain responses as C source for the gpu plugin: `IDeviceTimeResponse`, `DeviceResponses.Build`); `Numerics/Ensemble.cs` (study points as one device ensemble: `EnsembleGate`, `IEnsembleBackend`; the gpu plugin's `EnsembleStepper.cs`); `Ionization.cs` (rates, `PlasmaResponse` with its Drude operator) |
| `Transverse/` | `SpatialPropagation.cs` (the spatial nonlinear solver; `ExecuteLinear` streams the stored frequencies in blocks, `transverse.frequencyBlock`); `TransverseBasis.cs` (`RadialBasis`, `CartesianBasis`); `TransverseProfile.cs` (`TransverseProfile`, `TransverseOps`: expand, collapse, regrid, `Curvature`, `BeamQuality`); `BeamShapes.cs` (initial profiles) |
| `Optics/` | `BeamTracking.cs`; `DispersiveSurface.cs` (gratings, prisms); `SurfaceOptics.cs` (lenses, curved mirrors); `MaskElement.cs`; `ModulatorElement.cs` (pixelated phase / amplitude device: cell grid, fill factor, quantisation, cross-talk, chromatic rules `opd` / `fixedPhase` / `index`, `PhaseScale`); `ComponentTerms.cs` (Zernike, figure); `MirrorSubstrate.cs`, `DelayedTerms.cs` |
| `Crystals/` | χ(2): `CrystalElement`, phase matching |
| `Pulses/PulseCalculator.cs` | `PulseCalculator.Solve` (ops `shapes`, `limit`, `analyze`, `measure`), `TbpOf`: time-bandwidth product, transform limit, stretch factor, Gaussian GDD; CLI `ultrafast pulse`, C API `uf_pulse_calc`, Python `ultrafast.calc.pulse` |
| `python/src/ultrafast/calc/` | the calculators package: `pulse.py` (TBP table without the engine; `limit`, `analyze`, `measure` through `uf_pulse_calc`), `crystal.py` (`phase_match`, `uf_crystal_phase_match`), `cell.py` (`cell_mode`), `beam.py` (pure Python: Gaussian q, ABCD, single- and two-lens mode matching, telescope, M²), `nonlinear.py` (pure Python: critical power, Marburger collapse distance, B-integral, intensity, fluence), `_compat.py` (`renamed_kwargs`, `engine_call`: engine errors as `ValueError`). `ultrafast.crystal` and `ultrafast.cells` re-export `calc.crystal` / `calc.cell` |
| `python/src/ultrafast/mcp/` | the MCP server `ultrafast-mcp`: `server.py` (`McpServer`: JSON-RPC 2.0 over stdio, both protocol eras; `main`), `policy.py` (`Workspace`, `check_document`, `check_set_path`, `check_arguments`, `Refused`), `tools.py` (the six tool definitions, `prepare`), `worker.py` (`Worker`: persistent sandboxed child, killed on timeout, size pre-flight), `_worker_main.py` (the child's loop), `ops.py` (what the child runs: validate, describe, run, scan, plot; memory pre-flight), `explain.py` (`explain_warning` table), `resources.py` (schemas, fidelity, examples) |
| `build/check_version.py` | the version gate: `collect()` lists every place that carries the version, `check(tag)` returns (version, wheel version, release candidate) or exits; used by `release.yml`, CI and `test_wp3.py` |
| `build/example_lint.py`, `docs/examples/_template.md` | WP9: structure and metadata of the examples that have an `example.json` (`lint_folder`); never runs an example and, with `"python": true`, the CLI / Python tabs of "Run it" |
| `build/readme_snippets.py` | WP D1: `find_snippets(text) -> list[Snippet]` finds the ```python blocks marked `<!-- snippet -->` (the Python tab of an example README); used by the lint, `build/example_check.py` and `python/tests/test_readme_snippets.py` |
| `build/sync_roadmap.py` | writes `ROADMAP.md` from `docs/roadmap.md` (`--check` in CI) |
| `build/setup.py`, `python/tools/build_engine.py`, `install.py` | source builds with plugin choice (CLI, Python hosted / native, C API; `resolve_plugins` follows `plugin.json` `requires`); release install with `PRESETS` |
| `Gain/`, `Lasers/` | amplifiers; `AseModel.cs` (`AseSpec`, `AseModel`: inter-pulse ASE, both directions, per transverse cell and frequency, at frozen N2; `Rates` feeds `GainCrystal.PumpCw` / `Decay`, `ModeOutput` gives the ASE in the signal mode); `GainElements.ApplyStressPattern` (the four-lobed Jones map); `Oscillator.cs`, `Resonator.cs`, saturable absorbers; `CellMode.cs` (`CellModeSolver.Solve`: Kerr-matched mode of a two-mirror multi-pass cell, command `cellMode`) |
| `Materials/` | index models, the library, Raman; the catalogue (`Catalogue.cs`) with the numbers and their sources (`Data/materials/`) |
| `Instruments/` | `Instruments.cs` (the registry), `ActiveInstruments.cs` (`beamProfile`, ...), `CameraInstruments.cs`, `Detector.cs`, `Sampling.cs`, `SingleShotCorrelator.cs` (`singleShotCorrelator`, `SingleShotGeometry.Solve`: time-to-space correlator) |
| `Measured/` | `Measurements.cs`: comparison of instruments with recorded data |
| `Retrieval/` | FROG, d-scan, Gerchberg-Saxton |
| `Numerics/` | FFTs, SIMD kernels, interpolation, constants |
| `Plugins/` | how `plugins/` (fitting, zemax, gpu, hitran) connect |

## Tests

| where | what |
|---|---|
| `tests/UltrafastEngine.Tests/` | the C# suite, with its own runner; every physics test names its reference |
| `python/tests/` | the Python package; `test_pynlo*.py` and `test_lightpipes.py` are cross-checks with independent codes, skipped when those are absent |
| `build/example_check.py` | runs every example and compares the numbers quoted in its README |
| `build/gallery.py` | writes `docs/gallery.md` from the `hero.png` / `animation.gif` files next to the examples |
| `install.py` | the selective installer / uninstaller of a release: `fetch_manifest`, `expand` / `resolve` (dotted names, dependencies, platform assets), `install`, `uninstall`, `install_git`; the install record `.ultrafast-install.json` |
| `build/release_components.py` | the component registry (`definitions`), source file selection from `git ls-files` (`collect`), the reproducible asset writer, manifest build and merge, the ZOS-API gate (`forbidden`, `check_archives`) |
| `build/package.py` (`--components`) | `build_release` slices what the single-folder `Builder` steps produce into per-component assets; `.github/workflows/release.yml` runs it per OS and merges |
| `build/examples_cli.py` | the `ultrafast-examples` command (`list`, `copy`, `run`); `build/package.py --examples-wheel` installs it with the examples as package data |
| `docs/examples/make_all.py`, `_figures.py`, `<Group>/<name>/make_figures.py` | run every example and write its figures (`figures/`, git-ignored) or, for the Computing group, the committed `hero.png` and `animation.gif`; checks the size budget |
