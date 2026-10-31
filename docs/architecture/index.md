# Architecture

How the engine is built, for anyone changing it. [Concepts](../concepts.md) explains the model of a pulse; these pages explain the code.

| page | what it holds |
|---|---|
| [Code map](code-map.md) | the folders, the main files and classes, and what each does |
| [Key processes](processes.md) | what happens in a run, step by step: compile, the graph, execution, grids, studies |

Why the branched design took its form is in [Design: branched systems](../design/branching.md).

The layers:

```text
CLI (src/UltrafastEngine.Cli)    C API (src/UltrafastEngine.Native)    Python (python/src/ultrafast)
        \                               |                               /
         Api/Simulation          the entry point for all of them
                |
         Schema  Specs  Systems  what a run may say, and what this one says
                |
         Engine/Compiler         a system file becomes CompiledElements
                |
         Engine/Executor         pulses go through them; GraphExecutor schedules Branched systems
                |
         Physics  Transverse  Crystals  Gain  Lasers  Optics  Materials  Instruments  Numerics
```

Rules that hold across the code:

* **Nothing compiled depends on a package.** There are no NuGet dependencies. The core targets `net10.0` and `net48`.
* **Compile, then execute.** The compiler decides everything that does not depend on the pulse's shape. Compiled runs can be reused.
* **Every key is declared** in `Schema/BuiltIn*.cs`. Docs, schemas and Python types are generated from that.
* **Energy is booked.** Each loss has a mechanism, and the budget closes exactly.
* **One element code path.** Sequential and Branched systems call the same `RunElement`.
* **Two beams can describe one pulse.** A tracked Gaussian (`BeamState`) is cheap and analytic; a carried profile (`TransverseProfile`) is a field on a grid. Each element uses what the run provides.
* **The frame is co-moving.** Fields lose their carrier phase and group delay as they go, and the `PathLedger` keeps them for when paths meet.

## File headers

New and substantially changed source files start with a comment giving what the file does, its main classes and their key signatures. `Systems/SystemGraph.cs` and `Engine/MultiPort.cs` are examples. Keep the header current when signatures change.
