# Project status -- 1.0.0 alpha

What is verified, where, and what is not checked yet. Capabilities and limits: [docs/capabilities.md](docs/capabilities.md).
What comes next: [ROADMAP.md](ROADMAP.md). Changes: [CHANGELOG.md](CHANGELOG.md).

## Verified (latest full run, macOS arm64, .NET 10, Python 3.12, 2026-11-12)

| check | result |
|---|---|
| `dotnet build UltrafastEngine.sln -c Release` (net10.0 and net48) | 0 errors; the only warning is the expected missing ZOS-API (the Zemax binding then builds against stubs) |
| C# tests, `--slow` | 2026-11-26 on macOS arm64 (WP fix-2, `--slow`): **710 tests: 709 pass, 0 fail, 1 skipped** (the validation matrix failed until regenerated, then re-run green); 2026-11-25 on macOS arm64 (WP Z1, fast run): **668 pass, 0 fail, 29 skipped**; fast run (no `--slow`) 2026-11-24 on Windows with the RTX 3060 Ti: **681 pass, 0 fail, 9 skipped** (GPU tests ran on CUDA); **684 tests: 664 pass, 0 fail, 20 skipped** (hardware and opt-in tests: GPU, HAPI), run 2026-11-18 on macOS arm64 (the two failures of the first run were the generated schema and matrix, regenerated and re-run green); earlier: run 2026-11-17 on Windows with an RTX 3060 Ti: all 17 `GpuTests` ran on CUDA **and** OpenCL. net48 build-checked only (no Mono) |
| Python tests (hosted engine, `ULTRAFAST_SLOW=1`, with pyNLO and LightPipes) | latest 2026-11-24 on Windows (hosted engine, no pyNLO / LightPipes / slow): **404 pass, 15 skipped**; **439 pass, 2 skipped** (run 2026-11-18 on macOS arm64, pyNLO included) (no NativeAOT library; run 2026-11-17 (before that 268, and 267 on 2026-11-16) on Windows with the RTX 3060 Ti, the hosted engine, pyNLO and LightPipes; before that 266 pass, 2 skipped on the Mac (no FP64 GPU); earlier run 2026-11-11 with `ULTRAFAST_LIBRARY` set to the hosted engine, otherwise a stale `bin/native` shadows it and the plugin tests fail; includes the 23 release tests and 8 for `setup.py`, `install.py --preset` and the pulse calculator) |
| Component tests (`tests/components/run_all.py`: 10 families covering linear optics, splitters, polarisation, crystals, nonlinear media, spatial components, instruments) | **1691 / 1694 checks** pass against analytic answers; **3 are marked KNOWN** (a 1.5-cycle pulse broadened to zero frequency at 2 µm / 10 fs, reported by a warning), 0 fail |
| Paper replications (`build/paper_check.py`) | 12 folders, 0 hard failures (fast tier); each README ends with *How well the science matched*; the soft checks mark what does not reproduce ([index](docs/papers/README.md)) |
| Examples and tutorials (`build/example_check.py`, strict options) | 0 problems (60 example folders in 13 groups, 35 with a committed hero figure; the new AdaptiveOptics, DigitalTwin and Fibres examples checked on the last run); the C++ example and the OpticStudio export (`Interop/`) are manual |
| `mkdocs build --strict` | 0 warnings |
| Source-build front door (`build/setup.py`) | CLI with a plugin subset, Python into a fresh venv with `--check`, 2026-11-12 (macOS arm64). **Not run:** `--cpp` / `--python-engine native` (NativeAOT), Windows `setup.bat`, Linux; `release.yml` has never run on GitHub |
| Generated files (schemas, options.md, Python types, python-api.md, validation matrix) | current (checked by tests) |

## Continuous integration (GitHub Actions)

On every push to `main` and `dev/**` and every pull request: build net10.0 and net48 and the C# tests (including slow) on Linux
and on Mono; C# and Python tests on Linux arm64 (the stand-in for Apple silicon: the macOS runner is not available to this repository) and on Windows; the CLI compiled for linux-x64, linux-arm64, win-x64,
osx-x64 and osx-arm64; Python tests with pyNLO; component tests; the paper replications (fast tier); the docs build and the
example check.

## Not checked

| | |
|---|---|
| GPU back-ends (CUDA, OpenCL) | checked on one machine (RTX 3060 Ti, driver 610.88, CUDA 13.3; WP-C(B), 2026-11-16): `GpuTests` pass on CUDA and OpenCL. On macOS arm64 they run on Homebrew PoCL 7.2 (CPU OpenCL, FP64; 20 pass, 0 fail, 2026-11-25, WP G0). Elsewhere the tests skip without an FP64 device; AMD / Intel OpenCL and data-centre cards are not checked |
| MCP server (`ultrafast-mcp`) with a live language-model agent | built and tested through replayed reference trajectories (`tests/agent-evals/`); no live agent has been scored ([MCP reference](docs/reference/mcp.md)) |
| NativeAOT library with the `ao` plugin | compiled in (`UF_WITH_AO`); the check that it lists the four elements (`test_native_library_lists_the_four_ao_elements`) runs only when `ULTRAFAST_NATIVE_LIB` points at a built library |
| OpticStudio / Zemax | needs a Windows machine with OpticStudio; the binding is built against stubs in CI |
| Release workflow (`release.yml`) | written and checked by `build/check_version.py`, `build/example_lint.py` and the light tests; **has never run on GitHub** (the first run is the release-candidate tag; owner's steps in `docs/releasing.md`) |
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

What was built, session by session: [docs/design/history.md](docs/design/history.md); open engineering work: [docs/design/roadmap-internal.md](docs/design/roadmap-internal.md).
