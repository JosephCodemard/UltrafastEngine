# Project status -- 2.2.0

What is verified, where, and what is not checked yet. Capabilities and limits: [docs/capabilities.md](docs/capabilities.md).
What comes next: [ROADMAP.md](ROADMAP.md). Changes: [CHANGELOG.md](CHANGELOG.md).

## Verified (latest full run, macOS arm64, .NET 10, Python 3.12, 2026-11-10)

| check | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` (net10.0 and net48) | 0 errors; the only warning is the expected missing ZOS-API (the Zemax binding then builds against stubs) |
| C# tests, `--slow` | **562 pass, 0 fail, 9 skipped** (8 `GpuTests` without a GPU device; the opt-in HAPI test) |
| Python tests (hosted engine, `ULTRAFAST_SLOW=1`, with pyNLO and LightPipes) | **223 pass, 2 skipped** (one needs `ULTRAFAST_NATIVE_LIB`) |
| Component tests (`tests/components/run_all.py`: 10 families covering linear optics, splitters, polarisation, crystals, nonlinear media, spatial components, instruments) | **1688 / 1691 checks** pass against analytic answers; **3 are marked KNOWN** (a 1.5-cycle pulse broadened to zero frequency at 2 µm / 10 fs, reported by a warning), 0 fail |
| Paper replications (`build/paper_check.py`) | 12 folders, 0 hard failures (fast tier); each README ends with *How well the science matched*; the soft checks mark what does not reproduce ([index](docs/papers/README.md)) |
| Examples and tutorials (`build/example_check.py`, strict options) | 0 problems (60 example folders in 13 groups, 35 with a committed hero figure; the new AdaptiveOptics, DigitalTwin and Fibres examples checked on the last run); the C++ example and the OpticStudio export (`Interop/`) are manual |
| `mkdocs build --strict` | 0 warnings |
| Generated files (schemas, options.md, Python types, python-api.md, validation matrix) | current (checked by tests) |

## Continuous integration (GitHub Actions)

On every push to `main` and `dev/**` and every pull request: build net10.0 and net48 and the C# tests (including slow) on Linux
and on Mono; C# and Python tests on Linux arm64 (the stand-in for Apple silicon: the macOS runner is not available to this repository) and on Windows; the CLI compiled for linux-x64, linux-arm64, win-x64,
osx-x64 and osx-arm64; Python tests with pyNLO; component tests; the paper replications (fast tier); the docs build and the
example check.

## Not checked

| | |
|---|---|
| GPU back-ends (CUDA, OpenCL) | need a device with FP64; the tests skip without one (this includes the S2 parity test of the modulator and tile source, and the `ao` elements) |
| MCP server for agents (twin plugin, T6) | designed, **not built**; `ultrafast twin serve` exits with 2 |
| NativeAOT library with the `ao` plugin | compiled in (`UF_WITH_AO`); the check that it lists the four elements (`test_native_library_lists_the_four_ao_elements`) runs only when `ULTRAFAST_NATIVE_LIB` points at a built library |
| OpticStudio / Zemax | needs a Windows machine with OpticStudio; the binding is built against stubs in CI |
| Release packages | built and smoke-tested per platform with `build.sh` on request; no release workflow yet |
| macOS in CI | not run on GitHub (the `macos-14` runner configuration is not available to this repository); the macOS arm64 build and tests are run by hand on the development machine, and the Linux arm64 job covers the arm64 code paths |
| Mono / net48 test run | Mono is not installed on the development machine; CI runs it on Linux |
| `docs.yml` (GitHub Pages) | written, not yet run on GitHub; needs Settings → Pages → Source: GitHub Actions |
| Code coverage | not measured: `dotnet-coverage` does not attach on macOS arm64; to be run on a Linux CI job |
| Unverified built-in constants | CS2 n2 / split / time constants, BaF2 n2 and σ (flagged in the docs) |
| Independent physics review | the `physics-reviewer` agent and the cross-checks (pyNLO, LightPipes, HAPI, published values) stand in for it |

## Where the physics is pinned

The test matrix lists every test with the analytic or published value it checks:
[docs/reference/validation-matrix.md](docs/reference/validation-matrix.md) (generated). Components alone, with plots:
[docs/component-tests.md](docs/component-tests.md). Published results: [docs/papers/README.md](docs/papers/README.md). Cross-code references:
pyNLO (fibre propagation with Raman and gain, DFG in PPLN, branched arms), LightPipes (diffraction, lenses, apertures, tilt,
Talbot, interferometers), HAPI (water, CO2 and O2 lines).

What the last consolidation did, what is still open and what to do next: [HANDOFF.md](HANDOFF.md).
