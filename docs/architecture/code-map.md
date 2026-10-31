# Code map

Paths are under `src/UltrafastEngine.Core/` unless stated. Line counts change; use this to find where to start.

## Entry points

| file or folder | what it does |
|---|---|
| `Api/Simulation.cs` | `Run(system, run)`, `Validate(system, run)`; the entry point for every front end |
| `src/UltrafastEngine.Cli/Program.cs` | the `ultrafast` command: `run`, `validate`, `study`, `retrieve`, `schema`, ... |
| `src/UltrafastEngine.Native/` | the C API (`include/ultrafast.h`) |
| `python/src/ultrafast/` | the Python package: `build.py` builders, `types.py` generated TypedDicts |

## Describing a run

| file | main classes | what it does |
|---|---|---|
| `Systems/SystemDescription.cs` | `SystemDescription`, `SystemElement` | a `.ufs` file in memory; each element stays JSON |
| `Systems/SystemGraph.cs` | `SystemGraph`, `GraphEdge`, `GraphLoop`, `GraphStage`, `Ports` | the connected form of a system: order, loops, virtual ports, verification. Knows no physics |
| `Specs/` | `RunSpec` | a `run.json` in memory |
| `Pulses/` | `PulseSpec`, `PulseField`, `FrequencyGrid`, `GridResampler` | input pulses; a field on a grid; moving between grids |
| `Schema/` | `ComponentSpec`, `OptionRegistry`, `Validator`, `Migrations` | declarations of every key and port; generators of docs and JSON schemas; upgrades of old files |

## Compile

| file | main classes | what it does |
|---|---|---|
| `Engine/Compiler.cs` | `Compiler`, `CompiledRun`, `CompiledElement`, `CompilerServices` | each element to linear terms, a nonlinear segment or an active element. Beam tracking |
| `Engine/CompilerComponents.cs`, `CompilerFiber.cs`, `CompilerSubstrate.cs` | | plates, polarisers, prisms; fibres; mirror substrates |
| `Engine/GridPlanner.cs` | `GridPlanner` | the time grid a run needs |
| `Optics/BeamTracking.cs` | `BeamState` | Gaussian q, ray matrices and a five-ray bundle through the system |

## Execute

| file | main classes | what it does |
|---|---|---|
| `Engine/Executor.cs` | `Executor`, `ElementRecord` | `Execute`; `RunElement` runs one element on one pulse; instruments |
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
| `Physics/` | `LinearTerms.cs` (`LinearTerm`, `PhaseTerm`, `GroupDelayTerm`, `JonesTerm`, `DispersionPoint`); `Nonlinear.cs` (`EnvelopeProblem`, `FieldProblem`, steppers); ionisation |
| `Transverse/` | `SpatialPropagation.cs` (the spatial nonlinear solver); `TransverseBasis.cs` (`RadialBasis`, `CartesianBasis`); `TransverseProfile.cs` (`TransverseProfile`, `TransverseOps`: expand, collapse, regrid, `Curvature`, `BeamQuality`); `BeamShapes.cs` (initial profiles) |
| `Optics/` | `BeamTracking.cs`; `DispersiveSurface.cs` (gratings, prisms); `SurfaceOptics.cs` (lenses, curved mirrors); `MaskElement.cs`; `ComponentTerms.cs` (Zernike, figure); `MirrorSubstrate.cs`, `DelayedTerms.cs` |
| `Crystals/` | χ(2): `CrystalElement`, phase matching |
| `Gain/`, `Lasers/` | amplifiers; `Oscillator.cs`, `Resonator.cs`, saturable absorbers |
| `Materials/` | index models, the library, Raman |
| `Instruments/` | `Instruments.cs` (the registry), `ActiveInstruments.cs` (`beamProfile`, ...), `CameraInstruments.cs`, `Detector.cs`, `Sampling.cs` |
| `Measured/` | comparison with recorded data |
| `Retrieval/` | FROG, d-scan, Gerchberg-Saxton |
| `Numerics/` | FFTs, SIMD kernels, interpolation, constants |
| `Plugins/` | how `plugins/` (fitting, zemax, gpu, hitran) connect |

## Tests

| where | what |
|---|---|
| `tests/UltrafastEngine.Tests/` | the C# suite, with its own runner; every physics test names its reference |
| `python/tests/` | the Python package; `test_pynlo*.py` and `test_lightpipes.py` are cross-checks with independent codes, skipped when those are absent |
| `build/example_check.py` | runs every example and compares the numbers quoted in its README |
