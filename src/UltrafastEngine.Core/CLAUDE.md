# src/UltrafastEngine.Core -- the engine

No NuGet. Targets net10.0 and net48: `#if NET` around newer APIs (Vector128, Span-heavy code); C# `latest`,
nullable disabled, no implicit usings. Namespaces follow folders (`UltrafastEngine.Engine`, `.Physics`, ...).

## Pipeline

`Api/Simulation.Run(system, run)` → `Engine/Compiler.Compile` (materials, coatings, overrides with provenance;
each element → `CompiledElement` = `Pre`/`Post` linear terms + optional `NonlinearSegment` or `Active` element;
`BuildGridAndPulse` sizes the grid via `GridPlanner.Plan`) → `Engine/Executor.Execute` (applies terms, integrates
nonlinear segments, records an `ElementRecord` per element, runs instruments, compares `measured` data,
plugin hooks) → `Results/RunResult` (summary JSON, npz, CSVs).

| folder | what |
|---|---|
| `Schema/` | **declarations of every key** (`BuiltInElements/Instruments/Options.cs`), validator, JSON-schema + options.md generators, `Migrations.cs` (renamed keys) |
| `Engine/` | `TailLedger.cs` (the delayed-light ledger, `grid.tail`), `CompilerSubstrate.cs` (mirror substrate echoes, ripple, delayed scatter), `Compiler.cs` (+ `CompilerComponents.cs` plates/ghosts/TFP..., `CompilerFiber.cs`), `Executor.cs`, `GridPlanner.cs`, `Studies.cs`, `Active.cs`, `AuxBeams.cs` |
| `Physics/` | `Nonlinear.cs` (EnvelopeProblem, FieldProblem, RK4IP / ERK4(3)-IP steppers, vector Kerr + Raman, B-integral), `LinearTerms.cs`, `JonesSpectrum.cs`, `Ionization.cs` |
| `Transverse/` | radial / cartesian spatial propagation (`SpatialPropagation.cs` mirrors the 1-D nonlinear terms) |
| `Pulses/` | `PulseSpec` (input pulse, measured spectrum), `MeasuredSpectrumPreparation`, `FrequencyGrid`, `PulseField`, `Pedestals` |
| `Materials/` | index models, `MaterialLibrary` (built-ins), Raman models (`IVectorDelayedResponse`: h_a + h_b), gases |
| `Instruments/`, `Measured/` | instrument kinds, detector/axis/sampling device model; recorded-data comparison |
| `Crystals/`, `Gain/`, `Lasers/` | χ(2), amplifiers, oscillators |
| `Numerics/` | FFTs (mixed radix, Bluestein), SIMD kernels, interpolation, constants |

## Adding things

* **Option / key**: declare it in `Schema/BuiltIn*.cs` (name, unit, description, default) → read it in the compiler
  or spec parser → test → `/regen` (5 generated files; `SchemaTests` fails otherwise) → docs reference page.
* **Element**: `BuiltInElements.cs` spec + a `Compile<Type>` in the compiler producing linear terms / a nonlinear
  segment / an `IActiveElement`; energy losses must carry a mechanism tag (the loss budget must close).
* **Model kind** (index, delayedResponse, filter, beam, ...): `Models/ModelRegistry.cs` + a `model:<category>:<kind>`
  spec in `BuiltIn*.cs`.
* Renaming a key: add a migration, never remove the old name.

## Invariants (tests check them)

* Sign conventions in `docs/concepts.md`: E = Re{A e^{-iω0 t}}, +GDD puts red first, SPM is +iγ|A|²A.
* Scalar (x-only) propagation must not change when vector features change: pyNLO cross-checks guard it.
* Raman kernels are normalised to unit sum on the grid; the anisotropic part h_b uses the same scale.
* The reported B-integral is Im(Σ A_c* N_c)/(k0 Σ|A_c|²) at the peak (all χ(3) terms).
* Satellite room: `Compiler.SatelliteRoom` (amplitude-summed combinations above `grid.satelliteLevel`); an element
  that only sets `SatelliteDelayS` counts as level 1.
* Loss budget: input − output energy = Σ booked losses, exactly (with `grid.tail`, the energy parked in the tail is the booked
  mechanism `delayed light (tail)`).
* Delayed light: with `grid.tail` linear elements act on main + tail exactly (`TailLedger.Apply`), nonlinear / active elements
  on the main field (the tail passes their linear terms); both grids share dt and the carrier. Terms with narrow features
  must not enter the propagation frame (`ISmoothIndex`, `PhaseTerm.FrameReference`).
