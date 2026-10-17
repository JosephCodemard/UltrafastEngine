# Tutorials

Five step-by-step tutorials. Each builds on the previous one, and every file they use is in
`docs/examples/tutorial/`, ready to run.

| | what you learn | files |
|---|---|---|
| [1. Your first simulation](01-first-run.md) | write a system file (`.ufs`) and a run spec (`run.json`), run them with the CLI, read the results, plot them | `plate.ufs`, `run.json` |
| [2. Diagnostics](02-diagnostics.md) | put instruments where you would put them on the table; probes; spatial models; near/far field and apertures (`sampling`) | `run-diagnostics.json` |
| [3. Larger systems](03-larger-systems.md) | stretcher, amplifier, compressor and SHG; probes and overrides; the same system from Python, with a parameter scan | `cpa.ufs`, `run-cpa.json`, `cpa.py` |
| [4. Your own measured data](04-measured-data.md) | load CSV / NPZ / JSON recordings, compare them with the model, calibrate unknown parameters with error bars, predict what you did not measure | `make_data.py`, `data/`, `run-measured.json`, `run-fit.json` |
| [5. Contrast of an amplifier chain](05-contrast.md) | ghosts, pedestals and a third-order cross-correlator; how a post-pulse becomes a pre-pulse; scanning a parameter with a study | `../22-contrast-chain/` |

Before you start: build the engine once (see [Getting started](../getting-started.md)):

```bash
dotnet build UltrafastEngine.sln -c Release
pip install -e "python[hosted,plot]"          # for ultrafast-plot and the Python parts
python python/tools/build_engine.py
```

In these tutorials, `ultrafast` means the command-line tool. From a checkout without installing anything:

```bash
alias ultrafast="dotnet $PWD/src/UltrafastEngine.Cli/bin/Release/net10.0/ultrafast.dll"    # macOS / Linux
```

On Windows, use `dotnet run --project src\UltrafastEngine.Cli -c Release --` or publish the CLI (see
[Getting started](../getting-started.md#macos-linux)). Run the commands from `docs/examples/tutorial/`.
