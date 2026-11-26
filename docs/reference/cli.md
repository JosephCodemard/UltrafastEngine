# Command line

`ultrafast` is the command-line front end of the engine (`dotnet ultrafast.dll ...` from a source build, `ultrafast` from a release package). Three more
commands come with the Python package: `ultrafast-plot`, `ultrafast-mcp` and `ultrafast-agent`. Every command prints its usage with `--help`.
Options of files are in the [run spec](run-spec.md) and [system file](system-file.md) pages, the full list of keys in the [option reference](options.md).

## Keys

Commands take positional files and `--flags`; there are no config files. The sections below list every command, the Python-package commands and the
environment variables. Exit codes and the default output folder are at the end.

## Running

| command | what it does |
|---|---|
| `ultrafast run <system.ufs> <run.json> [-o dir] [--quiet] [--threads n] [--backend cpu\|auto\|cuda\|opencl] [--device i] [--fidelity full\|standard\|quick]` | propagates the pulse and writes `summary.json`, `result.npz`, `provenance.json` and CSVs ([results](results.md)). `--backend` and `--device` override the run spec's `compute` section ([speed](speed.md)); `--fidelity` is the [fidelity switch](fidelity.md). `-o` (alias `--out`) defaults to `results/<run name>` next to the run spec. A spec with a key owned by a plugin (`fit`) runs that plugin's run mode; a spec with `coherence` runs the coherent-mode run |
| `ultrafast study <study.json> [-o dir] [--parallel n] [--backend name]` | a parameter scan (grid, random, LHS, Sobol), an optimisation or a sensitivity study ([studies](studies.md)) |
| `ultrafast validate <system.ufs> [run.json] [--graph [--mermaid]]` | checks the files without propagating: errors, warnings, the memory estimate ([memory](memory.md)); `--graph` prints the element graph of a [Branched](branches.md) system (`--mermaid`: as a Mermaid diagram) |
| `ultrafast describe <system.ufs>` | lists the elements and what the engine will do with each (terms, nonlinear segments, provenance) |
| `ultrafast template <run.json>` | writes a commented starter run spec |
| `ultrafast retrieve <pcgpa\|epie\|dscan\|gerchbergSaxton> <trace> [--kind ...] [--fundamental spectrum.csv] [--material M] [--instrument name] [--iterations n] [-o dir]` | pulse retrieval from a FROG or d-scan trace, or from a results folder's instrument ([retrieval](retrieval.md)) |

## Calculators

| command | what it does |
|---|---|
| `ultrafast pulse shapes` / `limit` / `analyze` / `measure` | time-bandwidth product, transform limit, chirp of a duration and bandwidth, a measured pulse ([pulse calculator](pulse-calculator.md)) |
| `ultrafast crystal <name> --process shg\|sfg\|opa\|dfg [--type I\|II\|0] [--fundamental m] [--pump m] [--signal m] [--idler m] [--a m --b m] [--plane XY\|XZ\|YZ] [--theta deg] [--phi deg] [--nc deg\|magic] [--qpm] [--period m] [--length m] [--temperature C] [--pump-intensity W/m^2] [--json]`, or `ultrafast crystal --request <request.json>` | phase-matching calculator: angles or QPM period, d_eff, walk-off, acceptance, group-velocity mismatch, gain spectrum ([crystals](crystals.md#phase-matching-calculator)) |

The same calculators, and the beam and nonlinear ones that need no engine, are in Python: [calculators](calculators.md).

## Information, files and the library

| command | what it does |
|---|---|
| `ultrafast info` | the model kinds, materials, crystals, element types and instruments, as JSON |
| `ultrafast materials list \| show <name\|page> \| import <page.yml> [--name n]` | built-in materials and gases, bundled refractiveindex.info pages, adding a page to the user library ([materials](materials.md)) |
| `ultrafast schema [system\|run] [-o file]` | JSON Schema of system files and run specs for editor completion ([schema](schema.md)) |
| `ultrafast reference [-o options.md]` | writes the option reference ([options](options.md)); `ultrafast reference materials` writes the material catalogue reference ([materials](materials.md)) |
| `ultrafast migrate <file.ufs\|run.json> [-o out]` | upgrades an older system file or run spec to the current format; renamed keys keep working with a deprecation warning ([schema](schema.md)) |
| `ultrafast version` / `--version` | the engine version |

## Compute devices and plugins

| command | what it does |
|---|---|
| `ultrafast devices [--selftest] [--benchmark] [--backend name] [--device i] [--json]` | the back-ends and devices (CPU, CUDA, OpenCL), the CPU FFT in use; `--selftest` compares each device with the CPU, `--benchmark` also times both ([speed](speed.md#benchmarks)) |
| `ultrafast agent list \| install --target claude\|codex\|cursor [--dest DIR] [--force] \| mcp-config [--workspace DIR] [--client claude\|codex]` | installs the agent skills and prints the MCP configuration (`ultrafast --help` lists `mcp-config` without its options); same files as `ultrafast-agent` ([MCP](mcp.md#agent-pack-and-evals)) |
| `ultrafast plugins [list \| install <folder\|zip> \| remove <id> \| paths]` | installed plugins and where they are searched ([plugins](plugins.md)) |
| `ultrafast twin <init\|fit\|validate\|predict\|design\|track\|check\|serve>` | the digital twin of a lab (provided by the twin plugin; listed only when it is installed; [twin](../plugins/twin.md)); `serve <folder>` starts the MCP server on that folder |
| `ultrafast zemax-build`, `zemax-demo`, `zemax-replay` | the Zemax plugin's commands, experimental ([zemax](../plugins/zemax.md)) |

## Python package commands

| command | what it does |
|---|---|
| `ultrafast-plot <results dir> ... [--only parts] [--format png,svg,pdf] [--no-pdf] [--out dir] [--db] [--style ultrafast\|ultrafast-dark\|paper\|science] [--column single\|double] [--dark] [--dpi n] [--labels ...] [--show]` | figures and a report PDF from results folders; several folders make a comparison ([plotting](plotting.md)). `ULTRAFAST_PLOT_STYLE` sets the default style. `--out` defaults to `DIR/plots` for one folder and to `.` for several |
| `ultrafast-mcp [--workspace DIR] [--budget-s s] [--memory-limit-gb g]` | the MCP server for agents ([MCP](mcp.md)); defaults: workspace `.`, budget 300 s, memory limit 8 GB |
| `ultrafast-agent list \| install --target claude\|codex\|cursor [--dest DIR] [--force] \| mcp-config [--workspace DIR] [--client claude\|codex]` | installs the agent skills and prints the MCP configuration ([MCP](mcp.md#agent-pack-and-evals)) |


## Environment variables

| variable | effect |
|---|---|
| `ULTRAFAST_STRICT_OPTIONS=1` | unknown keys in files are errors (every shipped example runs with it) |
| `ULTRAFAST_STRICT_MATERIALS=1`, `ULTRAFAST_STRICT_VALIDITY=1` | the same as `physics.strictMaterials` and `physics.strictValidity` ([run spec](run-spec.md#physics)) |
| `ULTRAFAST_DEBUG=1` | prints the stack trace of an error |
| `ULTRAFAST_BACKEND`, `ULTRAFAST_DEVICE` | default compute back-end and device of runs that name none ([speed](speed.md)) |
| `ULTRAFAST_FFT`, `ULTRAFAST_FFTW_LIBRARY` | CPU FFT provider (`auto`, `managed`, `fftw`, `accelerate`) and the FFTW library to load (`mkl_rt` selects Intel MKL's FFTW interface) |
| `ULTRAFAST_CUDA_LIB_DIR`, `ULTRAFAST_CUDA_NO_VENDOR=1`, `ULTRAFAST_OPENCL_LIBRARY`, `ULTRAFAST_OPENCL_ALLOW_CPU=1` | GPU library locations; `ALLOW_CPU` lets `backend: auto` use an OpenCL CPU device ([gpu plugin](../plugins/gpu.md)) |
| `ULTRAFAST_HOME` | folder of user plugins and the user material library; default `~/.ultrafast` |
| `ULTRAFAST_PLUGIN_PATH`, `ULTRAFAST_NO_PLUGINS=1` | extra plugin folders; run the bare core ([plugins](plugins.md)) |
| `ULTRAFAST_MATERIALS`, `ULTRAFAST_RII_PATH`, `ULTRAFAST_HITRAN_DIR` | user catalogue folder (default `~/.ultrafast/materials`), a local refractiveindex.info `data` folder, the HITRAN line files (default `~/.ultrafast/hitran`) |
| `ULTRAFAST_PLOT_STYLE` | default plot style |
| `ULTRAFAST_PYTHON` | the Python interpreter that `ultrafast twin` starts (default `python3`, `python` on Windows; [twin plugin](../plugins/twin.md)) |
| `ULTRAFAST_LIBRARY` | path of the native library the Python package loads (a source build) |
| `ULTRAFAST_MCP_MEMORY_LIMIT_BYTES` | memory limit of the MCP server's worker (set by `ultrafast-mcp --memory-limit-gb`) |

`ULTRAFAST_SLOW=1` (slow Python tests) is read only by the test suite, not by the engine; see `python/CLAUDE.md`.

## Exit codes

| code | meaning |
|---|---|
| 0 | success |
| 1 | a usage error (missing argument) or an unknown command |
| 2 | an error in a file or a model: a missing file, invalid JSON, a failed compile or run (`ULTRAFAST_DEBUG=1` adds the stack trace) |
| 3 | `ultrafast validate` printed `INVALID`, or `ultrafast devices --selftest` found a device that disagrees with the CPU |

Unknown-key warnings do not change the exit code unless `ULTRAFAST_STRICT_OPTIONS=1` makes them errors.

## Validation

Exit codes 2 and 1 were checked by running `ultrafast validate /nonexistent.ufs` and `ultrafast foo`; code 3 by validating a file with an unknown key
under `ULTRAFAST_STRICT_OPTIONS=1`. The commands are exercised by the examples' READMEs, each of which shows the command and its output.

## Limits

* `ultrafast --help` is the shortest reference; plugin commands appear in it only when the plugin is installed. An unknown command that belongs to a
  first-party plugin answers with the plugin's name (exit code 1).
* The CLI writes plain JSON when it migrates a file: comments are lost.
