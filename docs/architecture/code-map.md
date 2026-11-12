# Code map

Paths are under `src/UltrafastEngine.Core/` unless stated. Line counts change; use this to find where to start.

## Entry points

| file or folder | what it does |
|---|---|
| `Api/Simulation.cs` | `Run(system, run)`, `Validate(system, run)`; the entry point for every front end |
| `src/UltrafastEngine.Cli/Program.cs` | the `ultrafast` command: `run`, `validate`, `study`, `retrieve`, `schema`, ... |
| `src/UltrafastEngine.Native/` | the C API (`include/ultrafast.h`) |
| `python/src/ultrafast/` | the Python package: `build.py` builders, `types.py` generated TypedDicts; `system/`, `components/`, `sources.py`, `instruments/` (the builders by kind, CapWords aliases), `algorithms/` (`zernike`, NumPy only), `results/`, `plotting/` (package: figures, `zernike`, `animate`, `diagrams`), `core.py` (running) |

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
| `Engine/Studies.cs` | | scans and optimisation, with the prefix cache |
| `Engine/PartialCoherence.cs` | `PartialCoherence` | a run over coherent modes |
| `Results/` | `RunResult`, `PulseMetrics` | `summary.json`, `result.npz` |

## Physics

| folder | what it holds |
|---|---|
| `Physics/` | `LinearTerms.cs` (`LinearTerm`, `PhaseTerm`, `GroupDelayTerm`, `JonesTerm`, `DispersionPoint`); `Nonlinear.cs` (`EnvelopeProblem`, `FieldProblem`, steppers); `DispersiveResponses.cs`; `Ionization.cs` (rates, `PlasmaResponse` with its Drude operator) |
| `Transverse/` | `SpatialPropagation.cs` (the spatial nonlinear solver; `ExecuteLinear` streams the stored frequencies in blocks, `transverse.frequencyBlock`); `TransverseBasis.cs` (`RadialBasis`, `CartesianBasis`); `TransverseProfile.cs` (`TransverseProfile`, `TransverseOps`: expand, collapse, regrid, `Curvature`, `BeamQuality`); `BeamShapes.cs` (initial profiles) |
| `Optics/` | `BeamTracking.cs`; `DispersiveSurface.cs` (gratings, prisms); `SurfaceOptics.cs` (lenses, curved mirrors); `MaskElement.cs`; `ModulatorElement.cs` (pixelated phase / amplitude device: cell grid, fill factor, quantisation, cross-talk, chromatic rules `opd` / `fixedPhase` / `index`, `PhaseScale`); `ComponentTerms.cs` (Zernike, figure); `MirrorSubstrate.cs`, `DelayedTerms.cs` |
| `Crystals/` | χ(2): `CrystalElement`, phase matching |
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
