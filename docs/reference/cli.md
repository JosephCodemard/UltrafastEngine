# Command line

`ultrafast` is the command-line front end of the engine (`dotnet ultrafast.dll ...` from a source build, `ultrafast` from a release package). Three more
commands come with the Python package: `ultrafast-plot`, `ultrafast-mcp` and `ultrafast-agent`. Every command prints its usage with `--help`.
Options of files are in the [run spec](run-spec.md) and [system file](system-file.md) pages, the full list of keys in the [option reference](options.md).

## Running

| command | what it does |
|---|---|
| `ultrafast run <system.ufs> <run.json> [-o dir] [--quiet] [--threads n] [--backend cpu\|auto\|cuda\|opencl] [--device i] [--fidelity full\|standard\|quick]` | propagates the pulse and writes `summary.json`, `result.npz`, `provenance.json` and CSVs ([results](results.md)). `--backend` and `--device` override the run spec's `compute` section ([speed](speed.md)); `--fidelity` is the [fidelity switch](fidelity.md) |
| `ultrafast study <study.json> [-o dir] [--parallel n] [--backend name]` | a parameter scan (grid, random, LHS, Sobol), an optimisation or a sensitivity study ([studies](studies.md)) |
| `ultrafast validate <system.ufs> [run.json]` | checks the files without propagating: errors, warnings, the memory estimate ([memory](memory.md)) |
| `ultrafast describe <system.ufs>` | lists the elements and what the engine will do with each (terms, nonlinear segments, provenance) |
| `ultrafast template <run.json>` | writes a commented starter run spec |
| `ultrafast retrieve <pcgpa\|epie\|dscan\|gerchbergSaxton> <trace> [--kind ...] [--fundamental spectrum.csv] [--material M] [--instrument name] [--iterations n] [-o dir]` | pulse retrieval from a FROG or d-scan trace, or from a results folder's instrument ([retrieval](retrieval.md)) |

## Calculators

| command | what it does |
|---|---|
| `ultrafast pulse shapes` / `limit` / `analyze` / `measure` | time-bandwidth product, transform limit, chirp of a duration and bandwidth, a measured pulse ([pulse calculator](pulse-calculator.md)) |
| `ultrafast crystal <name> --process shg\|sfg\|opa\|dfg [--type I\|II\|0] [--fundamental m] [--pump m] [--signal m] [--idler m] [--theta deg] [--phi deg] [--nc deg\|magic] [--qpm] [--period m] [--length m] [--temperature C] [--json]` | phase-matching calculator: angles or QPM period, d_eff, walk-off, acceptance, group-velocity mismatch, gain spectrum ([crystals](crystals.md#phase-matching-calculator)) |

The same calculators, and the beam and nonlinear ones that need no engine, are in Python: [calculators](calculators.md).

## Information, files and the library

| command | what it does |
|---|---|
| `ultrafast info` | the model kinds, materials, crystals, element types and instruments, as JSON |
| `ultrafast materials list \| show <name\|page> \| import <page.yml> [--name n]` | built-in materials and gases, bundled refractiveindex.info pages, adding a page to the user library ([materials](materials.md)) |
| `ultrafast schema [system\|run] [-o file]` | JSON Schema of system files and run specs for editor completion ([schema](schema.md)) |
| `ultrafast reference [-o options.md]` | writes the option reference ([options](options.md)) |
| `ultrafast migrate <file> [-o out]` | upgrades an older file to the current format; renamed keys keep working with a deprecation warning ([schema](schema.md)) |
| `ultrafast version` / `--version` | the engine version |

## Compute devices and plugins

| command | what it does |
|---|---|
| `ultrafast devices [--selftest] [--benchmark] [--backend name] [--device i] [--json]` | the back-ends and devices (CPU, CUDA, OpenCL), the CPU FFT in use; `--selftest` compares each device with the CPU, `--benchmark` also times both ([speed](speed.md#benchmarks)) |
| `ultrafast agent list \| install --target claude\|codex\|cursor [--dest DIR] [--force] \| mcp-config [--workspace DIR] [--client codex]` | installs the agent skills and prints the MCP configuration; same files as `ultrafast-agent` ([MCP](mcp.md#agent-pack-and-evals)) |
| `ultrafast plugins [list \| install <folder\|zip> \| remove <id> \| paths]` | installed plugins and where they are searched ([plugins](plugins.md)) |
| `ultrafast twin <init\|fit\|validate\|predict\|design\|track\|check\|serve>` | the digital twin of a lab ([twin](../plugins/twin.md)); `serve <folder>` starts the MCP server on that folder |
| `ultrafast zemax-build`, `zemax-demo`, `zemax-replay` | the Zemax plugin's commands, experimental ([zemax](../plugins/zemax.md)) |

## Python package commands

| command | what it does |
|---|---|
| `ultrafast-plot <results dir> ... [--only parts] [--format png,svg,pdf] [--no-pdf] [--out dir] [--db] [--style ultrafast\|ultrafast-dark\|paper\|science] [--column single\|double] [--dark] [--dpi n] [--labels ...] [--show]` | figures and a report PDF from results folders; several folders make a comparison ([plotting](plotting.md)). `ULTRAFAST_PLOT_STYLE` sets the default style |
| `ultrafast-mcp [--workspace DIR] [--budget-s s] [--memory-limit-gb g]` | the MCP server for agents ([MCP](mcp.md)) |
| `ultrafast-agent list \| install --target claude\|codex\|cursor [--dest DIR] [--force] \| mcp-config [--client codex]` | installs the agent skills and prints the MCP configuration ([MCP](mcp.md#agent-pack-and-evals)) |

## Environment variables

| variable | effect |
|---|---|
| `ULTRAFAST_STRICT_OPTIONS=1` | unknown keys in files are errors (every shipped example runs with it) |
| `ULTRAFAST_PLOT_STYLE` | default plot style |
| `ULTRAFAST_LIBRARY` | path of the native library the Python package loads (a source build) |
| `ULTRAFAST_HOME` | folder of user plugins and the user material library |
| `ULTRAFAST_SLOW=1` | runs the slow Python tests |

Exit codes: 0 success, 1 a failed command or a file with errors.
