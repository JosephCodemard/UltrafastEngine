# UltrafastEngine: how to drive it

Instructions for an AI coding agent that uses UltrafastEngine (a nonlinear ultrafast pulse propagation engine) to simulate optical
systems. Documentation: https://josephcodemard.github.io/UltrafastEngine/ ; an index for agents is in `llms.txt` next to this file.
Skills for specific jobs (run simulations, debug a setup, model a lab, optimise, replicate a paper, design a system) are installed
beside it; load the one that fits.

## What you write

* A **system file** (`.ufs`, JSON with comments): the elements in beam order (`bulk`, `gap`, `plate`, `mirror`, `lens`, `gratingPair`,
  `crystal`, `gain`, ...). For beam splitters, interferometers and loops use `"topology": "branched"` and give each element `next`.
* A **run spec** (`run.json`): the input pulse, physics switches, instruments, overrides, studies.
* Units are SI and the unit is in the key name (`lengthM`, `energyJ`, `durationFwhmS`); the one exception is `gddFs2` (fs^2).
  Wavelengths are in metres (`1.03e-6`, not `1030`).

## Discover before you write

| question | command |
|---|---|
| every element type, option, unit, default | `ultrafast schema system`, `ultrafast schema run` (JSON Schema); `ultrafast reference` |
| what is built in (materials, crystals, models, instruments) | `ultrafast info`; `ultrafast materials list`, `ultrafast materials show NAME` |
| a commented starting point | `ultrafast template run.json` |
| is my file valid | `ultrafast validate system.ufs run.json` (exit code 3 when it prints INVALID, 2 for an unreadable file) |
| what will the engine do with each element | `ultrafast describe system.ufs` |

Set `ULTRAFAST_STRICT_OPTIONS=1` while writing files: an unknown key (a typo) then fails instead of being ignored.

## Run and read

```bash
ultrafast run system.ufs run.json -o results
```

Python: `import ultrafast as uf; res = uf.run(system, spec)`; builders in `ultrafast.build` return typed dicts (autocompletion).
Outputs in the folder: `summary.json` (scalars: `output`, `elements[]`, `instruments[]`, `energyBudget`, `warnings`, `notes`, `grid`),
`result.npz` (arrays), CSVs and `provenance.json` (where every number came from). `uf.load("results")` opens a folder without the engine.
Read `warnings` first; do not report a number the engine warned about without saying so.

## Habits that avoid wrong answers

* Nonlinearity needs a beam: give `inputBeam` in the run (or `beam` on the element) so intensity = power / area is defined.
* Leave `grid` to the planner. If you override it, check `edgeEnergyFraction` in `output` (should be below 1e-6).
* Many runs: use a **study** (frozen grid, comparable results, parallel), not a loop.
* Check energy: the energy budget (`energyBudget`, or `graph.energyClosureJ` for branched systems) must close to rounding level.
* A 1-D run warns above the critical power for self-focusing; use a `transverse` model when spatial effects matter.
* Measured data (input spectra, instrument recordings) goes in through `pulse.spectrum` and an instrument's `measured`; compare on the
  recording's own axis.
* Say which model level you used and what it leaves out.

## MCP server

`ultrafast-mcp` is a local MCP server (stdio, sandboxed to a workspace folder) with the tools `describe`, `validate`, `run`, `scan`,
`explain_warning`, `compare_to_measured`, `fit`, `study`, `backward`, `plot` and the twin tools. Print its configuration for your client with
`ultrafast agent mcp-config --workspace . --client claude` (or `codex`). Prefer the tools over shell commands when they are connected.

## More

Concepts and conventions: https://josephcodemard.github.io/UltrafastEngine/concepts/ ; capabilities and limits:
https://josephcodemard.github.io/UltrafastEngine/capabilities/ ; the agent guide:
https://josephcodemard.github.io/UltrafastEngine/agents/ ; tutorials and examples on the same site.
