# Current architecture (internal summary)

Internal page ([folder index](README.md)). Files, classes and step-by-step processes are in `docs/architecture/`
([index](../architecture/index.md), [code map](../architecture/code-map.md), [key processes](../architecture/processes.md)); this page
states only the shape of the system and the invariants a change must keep, so they live in one place.

## Shape

| layer | where | notes |
|---|---|---|
| Front ends | CLI `src/UltrafastEngine.Cli`, C API `src/UltrafastEngine.Native` (`include/ultrafast.h`), Python `python/src/ultrafast` | all go through `Api/Simulation`; Python loads either the NativeAOT library or the hosted .NET engine (`clr_loader`) |
| Contract | `Schema/BuiltIn*.cs` (every element, option, instrument), `Specs/` (run spec, fidelity), `Systems/` (system file, `sameAs`, graph) | generated artefacts: `schemas/*.json`, `docs/reference/options.md`, `python/src/ultrafast/types.py`, `docs/reference/python-api.md`, `docs/reference/validation-matrix.md` (`/regen`) |
| Compile | `Engine/Compiler*.cs`, `Engine/GridPlanner.cs` | a system file becomes `CompiledElement`s; the grid is planned once; notes and provenance are attached here |
| Execute | `Engine/Executor.cs`, `Engine/GraphExecutor.cs`, `Engine/Studies.cs` | sequential and branched systems; studies with a frozen grid, prefix cache, point cache, ensembles |
| Physics | `Physics/` (NLSE, Raman, ionisation, interfaces), `Transverse/` (radial, cartesian, UPPE), `Crystals/` (χ(2)), `Gain/`, `Lasers/`, `Optics/`, `Materials/` | three model levels: 1-D envelope with a tracked Gaussian beam, carrier-resolved, spatial |
| Measurement | `Instruments/`, `Measured/`, `Retrieval/` | instruments read the field; measured data is compared or used as input |
| Numerics | `Numerics/` (FFT providers, compute back-ends, ensembles, FP32 emulation) | the GPU back-ends are a plugin (`plugins/gpu`) behind `IComputeBackend` |
| Plugins | `plugins/fitting`, `gpu`, `hitran`, `ao`, `twin`, `zemax` | loaded through `IPlugin.Register`; never required by the core; Zemax experimental and never packaged |
| Python layers | `build.py` (builders returning TypedDicts), `result.py`, `plotting/`, `calc/`, `mcp/` | builders keep autocompletion; plotting and calculators need no engine for saved results / closed forms |

## Invariants

* No NuGet packages; `net10.0` and `net48` both build (newer APIs under `#if NET`). Python core depends on numpy only.
* SI units with the unit in the key name; renamed keys keep working through `Schema/Migrations.cs`.
* Every element key is read from the element JSON **and** from overrides (`Num/Bool/Str` helpers). The bug "documented key read only through
  overrides" happened four times (`n2`, `raman`, `fR`, `selfSteepening`); a test sets the key on the element and on the material and compares.
* Energy closes: every loss mechanism is booked in the element's loss budget (`lossJ`), including numerical ones (`grid boundary`).
* `fidelity: full` (default) is bit-identical to the engine without the fidelity switch; reduced fidelity is always reported (`fidelityReport`).
* GPU results match the CPU to 1e-9 relative L2 (double precision) or to energy 1e-6 / spectrum 1e-4 (mixed precision); a device run that
  fell back to the CPU must not pass a parity test.
* Generated files are never edited by hand; tests check they are current.
