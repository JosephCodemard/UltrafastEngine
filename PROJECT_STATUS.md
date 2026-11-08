# Project status -- 2.1.8-beta

What is verified, where, and what is not checked yet. Capabilities and limits: [docs/capabilities.md](docs/capabilities.md).
What comes next: [ROADMAP.md](ROADMAP.md). Changes: [CHANGELOG.md](CHANGELOG.md).

## Verified (latest full run, macOS arm64, .NET 10, Python 3.12, 2026-11)

| check | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` (net10.0 and net48) | 0 errors; the only warning is the expected missing ZOS-API (the Zemax binding then builds against stubs) |
| C# tests, `--slow` | **468 pass, 0 fail, 8 skipped** (7 `GpuTests` without a GPU device; the opt-in HAPI test) |
| Python tests (hosted engine, `ULTRAFAST_SLOW=1`, with pyNLO and LightPipes) | **126 pass, 1 skipped** |
| Component tests (`tests/components/run_all.py`: 10 families covering linear optics, splitters, polarisation, crystals, nonlinear media, spatial components, instruments) | **1688 / 1691 checks** pass against analytic answers; **3 are marked KNOWN** (a 1.5-cycle pulse broadened to zero frequency at 2 µm / 10 fs, reported by a warning), 0 fail |
| Paper replications (`build/paper_check.py`) | 12 folders, 0 hard failures (fast tier); the soft checks mark what does not reproduce (see each README) |
| Examples and tutorials (`build/example_check.py`, strict options) | 0 problems; the C++ example and the OpticStudio export (07) are manual |
| `mkdocs build --strict` | 0 warnings |
| Generated files (schemas, options.md, Python types, python-api.md, validation matrix) | current (checked by tests) |

## Continuous integration (GitHub Actions)

On every push to `main` and `dev/**` and every pull request: build net10.0 and net48 and the C# tests (including slow) on Linux
and on Mono; C# and Python tests on macOS (Apple silicon) and Windows; the CLI compiled for linux-x64, linux-arm64, win-x64,
osx-x64 and osx-arm64; Python tests with pyNLO; component verification; the paper replications (fast tier); the docs build and the
example check.

## Not checked

| | |
|---|---|
| GPU back-ends (CUDA, OpenCL) | need a device with FP64; the tests skip without one |
| OpticStudio / Zemax | needs a Windows machine with OpticStudio; the binding is built against stubs in CI |
| Release packages | built and smoke-tested per platform with `build.sh` on request; no release workflow yet |
| Independent physics review | the `physics-reviewer` agent and the cross-checks (pyNLO, LightPipes, HAPI, published values) stand in for it |

## Where the physics is pinned

The test matrix lists every test with the analytic or published value it checks:
[docs/reference/validation-matrix.md](docs/reference/validation-matrix.md) (generated). Components alone, with plots:
[docs/component-tests.md](docs/component-tests.md). Published results: [docs/papers/README.md](docs/papers/README.md). Cross-code references:
pyNLO (fibre propagation with Raman and gain, DFG in PPLN, branched arms), LightPipes (diffraction, lenses, apertures, tilt,
Talbot, interferometers), HAPI (water, CO2 and O2 lines).
