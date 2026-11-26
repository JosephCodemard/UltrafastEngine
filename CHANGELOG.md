# Changelog

## 1.0.0 (unreleased) -- first public release, as the alpha 1.0.0a1

The first version published on PyPI is the **alpha** `1.0.0a1` (`pip install --pre "ultrafast-engine[hosted]"`; the import name is `ultrafast`); later alphas fix what use in the lab shows, and `1.0.0` follows when they have settled. The files carry `1.0.0`; the alpha is the tag `v1.0.0a1` (decision E17). The version numbers 2.0.0-alpha.1 to 2.2.0 below were
internal development stages and were never released; they are kept as the development history. Plugins and the engine carry the same version.

**Docs (2026-11-26, WP D2-Wavefronts)**
- The two Wavefronts examples (`position-dependent-index`, `spatial-gain-thermal-lens`) follow the example template with a CLI and a Python tab, `example.json` and snippet tests; the quoted numbers match a fresh run (absorbed pump at 5 W is now quoted as 4.1 W, was 4.05 W).

**Docs (2026-11-25, WP C2f)**
- Calculators, pulse calculator, plotting and MCP reference pages rewritten to the reference template with complete signatures, result keys and limits; `concepts.md` and `capabilities.md` corrected (cw carrier, GPU ionisation and precision, plugin list, `modulator`, grid margin, strict switches). All 40 rows of audit F closed; code findings listed for WP C4.
**Docs (2026-11-25, WP C2e)**
- **Reference pages `instruments`, `retrieval`, `measured-data`, `results`, `studies`** rewritten to the style-guide template; every row of the group-e audit closed (`docs/design/notes/reference-audit-e.md`). New on the pages: the `coherence` study mode and the study output files, the `summary.json` key inventory (`totals`, `tail`, `graph`, `fidelityReport`, ...), validity thresholds, the main lobe at the window edge (finite lower bound, not null), retrieval option defaults, all 24 instrument kinds.
**Agent evals (2026-11-26, WP AG3)**
- `tests/agent-evals/run_live.py`: transcript adapters for Claude Code, Codex (`codex exec --json`) and Cursor (`cursor-agent` stream-json), `--agent claude|codex|cursor`, `--transcript FILE` (scores a saved transcript, runs nothing); hand-made replays in `tests/agent-evals/replays/` and `python/tests/test_agent_eval_adapters.py`.
**Agent pack (2026-11-26, WP AG1)**
- The pack that users get (wheel, `ultrafast-agent install`, CLI-embedded) now has a user `AGENTS.md` and `llms.txt` (`python/src/ultrafast/agents/pack/`) that explain how to drive the engine and mention nothing of the repository; `install` writes them for every target (`.claude/ultrafast/AGENTS.md`, `ultrafast-guide.mdc`, first part of the codex block, `.ultrafast/llms.txt`). A test greps every pack file for repository terms; `build/sync_skills.py --check` also needs both files. The repository's own `AGENTS.md` and `llms.txt` stay for developers.
**Docs (2026-11-26, WP C2a)**
- **Reference pages of group a rewritten** to the reference template and every row of `docs/design/notes/reference-audit-a.md` closed: system file (all element types listed, `topology`, `dispersionSource`, `crystal` keys), run spec (`fidelity`, `compute.*`, `physics.strict*`, `coherence`, `outputs.keepProfiles`), schemas (system format version 3), CLI (exit codes 0 to 3, 15 environment variables, `crystal` flags), C API, Python (current calculator signatures), plugins (interfaces, search order), extending. Nine code findings for WP C4 are listed in the audit file.
**Docs (2026-11-26, WP D1)**
- **Python next to the CLI in the example READMEs:** a "Run it" section with **CLI** and **Python** content tabs (`pymdownx.tabbed`; `pip install mkdocs pymdown-extensions` for the docs). The Python tab uses the `ultrafast` builders and prints the CLI's key numbers; a line `<!-- snippet -->` before its code marks it, and `python/tests/test_readme_snippets.py` runs every marked block at quick fidelity (`build/example_check.py` checks its quoted output at full fidelity). `example.json` `"python": true` makes `build/example_lint.py` require the tabs. `GettingStarted/quickstart` (now also with `two-plates.ufs` and `run.json`) and `GettingStarted/spm-plate` are converted.

**Added (2026-11-26, WP AG2: twin MCP tools)**
- **MCP: `twin_design`, `twin_track`, `twin_init`** (16 tools): the next measurement to take (candidates ranked by expected information gain: a variable's values, the instrument placement or a list), drift tracking of new recordings (ensemble Kalman filter from the fit) and `ultrafast twin init` through the server; each with its refusals (at most 12 candidates / 240 engine runs, 12 recordings / 192 runs, recordings inside the workspace), its own `timeBudgetS` capped by the server budget, and tests ([MCP reference](docs/reference/mcp.md#digital-twin-tools)).

**Docs (2026-11-25, WP B1)**
- **Style guide** `docs/style.md`: voice, banned filler words, the British spelling word list and the term list (tables a linter can read), units and numbers, warning / error messages, docstrings, and templates for reference pages, examples and tutorials; `AGENTS.md` and `docs/CLAUDE.md` point to it.

- **Style lint** `build/style_lint.py` (WP B2): checks docs prose against the tables of `docs/style.md` (British spelling, banned filler, term list) and the unit suffix of numeric keys in JSON blocks; a baseline file holds the existing hits, a new hit fails the CI `docs-and-examples` job; `--update-baseline`; test `python/tests/test_style_lint.py`.
**Added (2026-11-25, WP Z1: Zemax connection core)**
- **Zemax plugin: own ZOS-API connection** (decision E19): three modes (`--interactive` default with `--instance N`, `--extension` for a User Extension, `--standalone`), `ZOSAPI_NetHelper` initialised before any ZOS-API type is loaded, licence (`LicenseStatus` in the message) and version checks (minimum 20.1, `--skip-version-check`), a standalone instance closed on exit, error and Ctrl+C, the user's OpticStudio never closed; `--file` outside standalone is refused. `ZosOpticStudio.cs` split into `ZosConnection.cs`, `ZosSystemAccess.cs`, `ZosAnalyses.cs`; the testable core is `ZosConnectionCore.cs` (any OS, `ZemaxConnectionTests`, 7 tests). Not yet run on OpticStudio: `HARDWARE-CHECKLIST.md` step 3b.
**Added (2026-11-25, WP P1)**
- **Glass in splitters:** `beamSplitter` and `polarizingSplitter` take `material` with `transmittedThicknessM` (a → c, b → d) and `reflectedThicknessM` (a → d, b → c): the glass of each path (dispersion and the material's absorption) is applied after the split, and its phase and delay join each output's path ledger. It equals the splitter followed by an ideal-AR `plate` in each arm to 1e-12, Mach-Zehnder interference included; without the keys the element is unchanged (`SplitterGlassTests`, `docs/reference/branches.md#glass-in-a-splitter`).
**Changed (2026-11-25, WP G0)**
- **OpenCL on macOS lists PoCL:** the `opencl` back-end enumerates Apple's `OpenCL.framework` and every vendor library named by an `.icd` file (Homebrew `brew install pocl`), so a CPU device with double precision is found and `GpuTests` run on a Mac; the Apple GPU is listed as single precision only (not usable). `ULTRAFAST_OPENCL_LIBRARY` takes a `:` / `;` separated list. `auto` still never picks a CPU OpenCL device. Tests `GpuTests.IcdFiles_NameTheVendorLibraries`, `GpuTests.MacOs_ListsTheAppleGpuAsSinglePrecision_AndPoclAsUsable`; docs `docs/plugins/gpu.md` (macOS).

**Fixed / added (2026-11-24, multipass-cell follow-up)**
- **GPU: pulse- and beam-averaged B-integral were 0** (`bIntegralPulseAveragedRad`, `bIntegralBeamAveragedRad`, per element and in `totals`) on the CUDA / OpenCL path. `StepStatistics.BIntegralPulseAveragedRad` defaulted to 0, which the executor took as a value reported by the stepper, so its shape-based estimate never ran. The default is now NaN ("not reported"), and the 1-D device stepper accumulates Σ P Δn / Σ P per step itself (kernel `uf_pmean`), so GPU equals CPU to 1e-8 (`GpuTests.BIntegral_PulseAndBeamAveraged_OnTheDevice_MatchTheCpu`, `ValidityTests.BIntegral_PulseAveraged_*`).
- **Main-lobe pulse width:** `mainLobeFwhmS`, `mainLobeEnergyFraction` and `satellitePeakCount` beside `fwhmS` in `input`, `output`, every element's `pulse` and the `pulseSummary` instrument (`compressedMainLobeFwhmS`, `compressedMainLobeEnergyFraction` in `compressor`). `fwhmS` spans the outermost half-maximum crossings and jumps when a satellite crosses 50 %; the main-lobe width follows the highest peak only (`docs/reference/results.md`). `ultrafast.plotting.evolution`, `elements` and `animate_evolution` show it when present; the multi-peak warning quotes it.

**Added (2026-11-25, WP Q1)**
- **mypy is a CI gate** on `build.py`, the generated `types.py` and `calc/` (`[tool.mypy]` in `python/pyproject.toml`; step in the `python` job; `python/tests/test_typecheck.py` also checks that a seeded type error fails).

**Fixed (2026-11-25, WP A2)**
- **Main-lobe width at the window edge:** a main lobe that reaches the edge of the time window now gets the width to the edge (a lower bound) instead of NaN, and the multi-peak warning says "at least ... fs ... enlarge the window" (never "NaN fs"). GPU docs state that the ensemble path estimates the pulse-averaged B (8-15 % off with Raman); the device B test tolerance is 1e-5.

**Added / changed (2026-11-18, release completion)**
- **Renamed keys** with the unit in the name: fibre `betasSkPerM`, Faraday rotator `fieldTesla`, Raman models `tau1S` / `tau2S` / `taubS`, `dnDTPerK`. The old names still load (migration, with a deprecation note).
- **Circular aperture on a decentred beam** is now computed (it was ignored); `outputs.keepProfiles: "used"` works in branched systems; the CLI banner no longer says "2.0".
- **New examples** `Branched/system-schematics` (every plot style) and `GettingStarted/calculators`; hero images for every example README; all figures regenerated with `ultrafast.plotting`.
- **MCP server complete:** five new tools, `study` (scan, optimize, sensitivity; at most 400 evaluations), `backward` (backward propagation) and the digital-twin tools `twin_fit`, `twin_validate`, `twin_predict` (13 tools); `tests/agent-evals/run_live.py` scores live agents against the evals (mandatory prompt passed by Sonnet and Haiku); the rule "explore in quick, confirm in full" is stated in the server instructions and the skills; one source for the skills (`python/src/ultrafast/agents/pack`, `build/sync_skills.py`).
- **Release as an alpha:** the first public release is `1.0.0a1` (`pip install --pre ultrafast-engine`); `check_version.py` and `release.yml` accept the tags `v1.0.0aN`; classifier "Alpha". `ultrafast-agent install` writes LF line ends on Windows too.
- **Agents:** the `design-system` skill (six skills in the pack) and `ultrafast agent list | install | mcp-config` in the C# command line (the skills are embedded from the pack; the files are byte-identical to `ultrafast-agent`, tested).
- **Docs:** `reference/cli.md`, `reference/mcp.md`, a Benchmarks section in `reference/speed.md` (Apple M3 CPU, RTX 3060 Ti), four-row front-page plot table, the design documents brought up to date, roadmap for v1.1.0.

**Added (2026-11-18)**
- **Photo-ionisation and plasma in χ(2) crystals** (`ionization` on `crystal` elements, off by default): Keldysh or MPI rate from the catalogue band gap, one per field at its own carrier and intensity (densities add); Drude index, inverse-bremsstrahlung absorption and ionisation loss on every field at its own carrier; 1-D and spatial models; CPU only. Band gaps of BBO, LBO, KDP, KTP, LiNbO₃ added to the catalogue as `unverified` estimates. Tests `Chi2IonizationTests`.

**Added (WP-3, 2026-11-18: release groundwork)**
- **Version 1.0.0** in the engine, the plugins, the wheel and the docs; `build/check_version.py` checks that every place agrees and that a tag is `vX.Y.Z` or `vX.Y.ZrcN`.
- **`release.yml`**: version gate, light tests, `twine check --strict` and the ZOS-API gate on the wheels, upload to TestPyPI (release-candidate tags) and PyPI (final tags) by
  trusted publishing behind the protected environments `testpypi` and `pypi`; no sdist. Nothing is uploaded without the owner (`docs/releasing.md`, "Publishing to PyPI").
- **Install from PyPI** documented (`docs/install.md`, `python/README.md`).
- **Examples:** `docs/examples/_template.md`, `example.json` in the 12 adaptive-optics, digital-twin and fibre examples and `build/example_lint.py` (structure and metadata; also in CI).
- **Plugins installed before 1.0.0** (in `~/.ultrafast/plugins`) declare `"engine": "2.2.0"`; the 1.0.0 engine lists them as not loaded ("needs engine 2.2.0 or newer"). Reinstall them from this release (`ultrafast plugins install <folder>`).
- **Public roadmap** in three blocks (`docs/roadmap.md`; `ROADMAP.md` is generated by `build/sync_roadmap.py`).

**Added (WP-2b, 2026-11-17: MCP server, first slice)**
- **`ultrafast-mcp`** (`python/src/ultrafast/mcp/`, entry point in the wheel): a local MCP server over stdio, workspace-sandboxed, no dependencies (decision E8).
  Hand-written JSON-RPC 2.0 for both protocol eras (2026-07-28 `server/discover` with per-request `_meta`; 2025-11-25 ... 2024-11-05 `initialize`).
  Tools `describe`, `validate`, `run` (with `fidelity`; returns the fidelity report, warnings and `validity`), `scan` (with `fidelity`), `explain_warning`, `plot`;
  resources: run and system schemas, the fidelity table, the examples index. The engine runs in a persistent child process with an empty private `ULTRAFAST_HOME`,
  killed and respawned when a call uses its time budget; refusals (callbacks, user code, paths outside the workspace or through links, bad names, unknown arguments,
  parameter paths, size, memory) come back as `isError` results. `ultrafast twin serve <folder>` starts it. Docs: `docs/agents.md` (MCP server).

**Added (WP-1b, 2026-11-17: memory safety; a fit of a 46-pass multipass cell had run out of memory after 2 h)**
- **Fitting keeps bounded memory.** The residual cache holds numbers only (same eviction rule, so the same evaluations); the latest and
  best evaluations keep their full result, the Jacobian runs keep what the uncertainties read (instruments, records), and all
  evaluations run with `outputs.keepProfiles: "used"` unless the run spec sets it. The fit result is bit-identical (tested).
- **Core memory estimate** (`Engine/MemoryEstimate.cs`): a compile note on every run, in `validate`, `uf.estimate_memory(system, run)`
  and the `memory` command; pulses, transverse profiles, solver workspace, tail grid. **`compute.memoryLimitBytes`** refuses a run
  above it before it starts; a warning above 80 % of the free physical memory (not on Mono / unknown).
- Study points run with `keepProfiles: "used"` unless the run spec sets it.
- **GPU:** free device memory (`cuMemGetInfo`; OpenCL: total minus held buffers) instead of the total, slots lowered when they would not fit
  together, ensembles halved for memory with a note, program caches bounded (32, least recently used) and released on teardown.
- **Python:** `uf.estimate_memory`, a pre-flight in `uf.scan(memory=)` and `uf.run(...)` of a fit (`MemoryWarning` / `MemoryError`),
  `close()` and `with` on systems, runs and results.
- New page `docs/reference/memory.md`.

**Added (WP-2 part B, 2026-11-17: plotting and schematics)**
- **Plot styles:** `ultrafast.plotting.use_style("ultrafast" | "ultrafast-dark" | "paper" | "science")` (a call, and a context manager), `ultrafast-plot --style` / `--column`, `ULTRAFAST_PLOT_STYLE`. `paper`: serif text with Computer Modern maths, inward ticks on all sides, minor ticks, no grid, 3.4 / 7.0 in, 600 dpi (capped at 4800 px across), a line style per series; `science` uses SciencePlots when installed (new extra `paper`) and otherwise `paper`. Palettes checked with the dataviz validator (reference/plotting.md). `set_theme(dark)` still works. `report` / `ultrafast-plot` default to the style's resolution (160 dpi for the ultrafast styles, as before).
- **Schematics of branched systems** (`up.system`): loops as arcs inside a shaded loop region, orthogonal routing around the boxes, runs of identical elements as one box × N (the 46 passes of a `sameAs` cell), instrument tags, aux and by-product arrows, optional parameter labels (`params=True`), a deterministic layout (`plotting/graph_layout.py`, golden tests). **Fixed:** the boxes of the dark theme were white (`theme["panel"]`). The old code skipped loops (`for lp in loops: pass`) and drew a cavity as a row of boxes.
- **`SystemGraph.ToMermaid`** (`ultrafast validate --graph --mermaid`) shows element types, virtual input and output ports and loops as subgraphs; a sequential system is its chain.
- **`up.spectrogram_3d(result, at=)`** (wavelength × time × intensity surface with a heat-map floor) and **`up.spectrogram`** (the data); **`up.animate_evolution(result, path)`** (GIF / MP4 through the probes, element by element). `docs/reference/plotting.md` with before / after images (`python/tools/make_plotting_docs.py`).

**Added (WP-2 part A, 2026-11-17: calculators)**
- **`ultrafast.calc` is a package**: `pulse` (the old `calc` module), `crystal` (`phase_match`), `cell` (`cell_mode`), and two new pure-Python modules that need no engine: `beam` (Gaussian `q`, waist, Rayleigh range, ABCD matrices, single- and two-lens mode matching, telescopes, M² from widths) and `nonlinear` (critical power, Marburger collapse distance, B-integral with the pulse (1/√2, 2/3) and beam (1/2) averages, peak power / intensity / fluence). `ultrafast.calc.tbp / limit / analyze / measure`, `ultrafast.crystal` and `ultrafast.cells` keep working. See `docs/reference/calculators.md`.
- **TBP shapes `lorentzian` (0.1420) and `asymmetric-exponential` (0.1103)** in `PulseCalculator` (C#, CLI, C API, Python), tested against the tabulated values and a numerical Fourier transform. `pulse.shapes()` and `tbp()` work without the engine; a test compares them with the engine's table.
- **Calculator keywords carry their unit**: `duration_s`, `center_m`, `fundamental_m`, `pump_m`, `signal_m`, `idler_m`, `a_m`, `b_m`, `length_m`, `pump_intensity_w_per_m2`, `wavelength_m`, `mirror_radius_m`, `separation_m`, `peak_power_w`. The old names work with a `DeprecationWarning`. Engine errors of the calculators are raised as `ValueError`.
- Tested: the phase-matching calculator already accepts `noncollinearDeg` for `sfg` (0 gives the collinear angle).

**Added and fixed (WP-1, 2026-11-17: issues from a user's 46-pass multipass cell, physics questions)**
- **Validity in machine-readable form:** `summary.json` `validity` lists every check that fired (`zeroFrequency`, `criticalPower`, `kerrLensCollapse`, `lidt`, `windowEdge`) per element with its value and threshold; one warning per check names every element (it used to name only the first pass). `physics.strictValidity` / `ULTRAFAST_STRICT_VALIDITY=1` makes them an error after the run. Tests `ValidityTests`.
- **Fixed: false "zero frequency" warning.** Any energy deficit of a nonlinear element (including the stepper's own error) was reported as broadening that reached zero frequency, so a narrow-band pass 1 of a cell warned. The check now needs the output spectrum to reach the blocked bins at ω ≤ 0.
- **Pulse- and beam-averaged B-integral** (`bIntegralPulseAveragedRad`, `bIntegralBeamAveragedRad`, per element and in `totals`) beside the on-axis peak `bIntegralRad`.
- **FWHM caveat:** pulse metrics carry `peakCount` and `fwhmEnergyFraction`; a multi-peaked output pulse gives a warning.
- **Negligible ionisation is skipped** (`ionization.skipNegligible`, default true): a conservative bound from the entry pulse; the element note gives the density bound. A 46-pass cell with ionisation on every air gap no longer runs 20x slower on the CPU for no change in the result.
- **Ionisation on the GPU** (1-D envelope): photo-ionisation, avalanche, recombination and the Drude index (at the carrier or at every frequency) run on CUDA and OpenCL; the rate comes from a ln W / ln I table (exact for MPI; 4e-6 in the ionised density for PPT / Keldysh). Test `GpuTests.Plasma_IonisationDrudeAndAvalanche_MatchTheCpu`.
- **Fixed: QPM sliver steps.** An adaptive step ending just short of a domain wall left a step of a few nm before it, so a domain-resolved result depended on the last bits of the step size (3e-5); steps within 10 % of a wall now land on it (3e-8). Test `Chi2Tests.Qpm_DomainResolved_NoSliverSteps_*`.
- **Fixed: `ionization` and `kerrLens` on `plate` / `window` / `ndFilter`** were accepted but not passed to the plate's inside.
- **Glass in polarisers:** `polarizer` takes `material` and `thicknessM` (Glan prisms); a `lens` with a material but no thickness says that it adds no dispersion. Tests `ComponentTests.ThickPolarizerAndLens_AddTheirGlassDispersion`, `PolarizationTests.CircularLight_ReflectedAtAnAngle_BecomesElliptical_AsFresnelSays` (circular light on bare glass at 20-70° against Fresnel).
- `uf.scan(fidelity=...)` works with a run file too (the `study` command takes `fidelity`).
- The radial model through 46 `sameAs` passes is tested against the passes written out (`SameAsTests`).

**Added (WP-C(B): GPU coverage, verified on an RTX 3060 Ti with CUDA and OpenCL)**
- **Gas rotational Raman on the GPU** (backlog B3, kernel 1): molecular gases (air, N2, O2, CO2 and mixtures, with `n2Scale`) no longer keep 1-D and spatial elements on the CPU, so the air gaps of a multipass cell run on the device. The causal orientational response is the CPU's zero-padded linear convolution, evaluated with the same kernel spectrum (`IPaddedKernelResponse`). Parity with the CPU 1e-14 (1-D) and 1e-16 (spatial). Tests `GpuTests.GasRotationalRaman_*`; the GPU parity tests now run on every back-end found (CUDA and OpenCL), not only the first.
- **Time-response expressions compiled to GPU kernels** (kernel 3): `timeResponse` `expression` (every function and variable, including the running fluence `F`), `twoPhotonAbsorption` and `kerr` no longer keep an element on the CPU (1-D and spatial). The expression tree is written as C (`CompiledExpression.ToC`, `Physics/DeviceResponses.cs`) and compiled per element on first use. Callbacks and the frequency-resolved Drude term stay on the CPU, with the reason in the element's notes. Parity 2e-14 (1-D), 8e-16 (spatial). Test `GpuTests.ExpressionResponses_CompiledToKernels_MatchTheCpu`.
- **Modulator and tile source on the GPU, checked** (kernel 2): the tiles + modulator parity test now runs on CUDA and OpenCL and adds a realistic panel (fill factor, gaps, cross-talk, 8-level quantisation, zero-order leak, rotation, vector pulse, Raman); both agree with the CPU to 2e-16. No code change was needed: the modulator is a host-side active element and the plate after it runs on the device.
- **χ(2) crystals on the GPU** (kernel 4): the 1-D coupled-wave model (SHG, SFG / DFG / OPA, χ(3), self-steepening, averaged or domain-resolved QPM, crossing beams) runs on the device (`Chi2Stepper.cs`, kernel `uf_chi2`; the coupling at each z comes from `Chi2Problem.CouplingAt`, shared with the CPU). The device stepper now lands on breakpoints (QPM domain walls) as the CPU does. Parity 1e-15 (BBO SHG, OPA), 4e-16 (PPLN, fixed steps), 1e-13 (PPLN, adaptive at tolerance 1e-10). Spatial χ(2) stays on the CPU. Test `GpuTests.Chi2CoupledWaves_ShgQpmAndOpa_MatchTheCpu`.
- Fixed RK4IP steps over a crystal with resolved QPM domains now say in the element's notes that they do not land on the domain walls (a 1 mm PPLN SHG crystal at half a domain per step is about 1 % off in energy against the adaptive stepper). Numbers are unchanged. Test `Chi2Tests.Qpm_DomainResolved_FixedSteps_WarnThatTheyIgnoreTheWalls`.
- **Study points as one device ensemble**: with a GPU back-end, the points of a study batch meet at a gate (`Numerics/Ensemble.cs`) and their 1-D envelope elements (Kerr, Raman, rotational Raman, steepening) are integrated in one set of kernel launches (`EnsembleStepper.cs`); each point keeps its own adaptive steps, so results equal the CPU study to 1e-15. Study key `ensemble` (default on with a device, up to 64 points; `0` off). 200 points, RTX 3060 Ti: 3.5-6x faster than a stream per point, level with a 20-core CPU at N = 4096 and 1.6x faster at N = 16384 (`docs/reference/speed.md`). Test `GpuTests.StudyPoints_AsOneDeviceEnsemble_MatchTheCpuStudy`.
- **`compute.precision`** (`double` | `mixed`, default `double`): on GPU back-ends the nonlinear operator of 1-D envelope and χ(2) elements, with its FFTs, in single precision (built-in FP32 Stockham FFT); state and linear step stay double. Refused when an instrument asks for more than 70 dB of dynamic range (the FP32 study: rounding fills the floor below about -85 dB). Measured: energy at most 8e-8 and spectrum L1 at most 1e-6 off double on the SPM plate, the 20-pass argon cell, an N = 3 soliton and BBO SHG; 1.3-1.5x faster than double on an RTX 3060 Ti. Tests `GpuTests.MixedPrecision_*`.

**Fixed (tests)**
- The GPU parity tests counted a run as "on the device" when the device had failed and the run had fallen back to the CPU (the note `device: ...` is written before the failure). They now fail on a fallback; this caught a malformed kernel for two-photon absorption during development.

**Added (WP-C(A): speed on the Mac)**
- **`fidelity`** run key (`full` | `standard` | `quick`, CLI `--fidelity`): named bundles of switches that fill only keys the run spec leaves unset; `summary.json` has a `fidelityReport` and quick runs warn when the dropped physics may matter (B, self-steepening, Raman, critical power, polarisation). Reference: `docs/reference/fidelity.md`. Tests: `FidelityTests`.
- **Studies:** the frozen grid now serves every point (grid union over the range ends), instruments are skipped when no output reads one, and `"cache": "folder"` stores every point under a hash of its inputs (resume and reuse). 5.7 times faster than a loop on the multipass cell (`speed.md`). Tests: `StudyCacheTests`.
- Python **`uf.scan`**: lazy, shaped arrays over named axes, with `fidelity` and `cache`.
- `cpu-fp32` back-end (study only) and the FP32 study: the operator in single precision is good to 1e-9 in energy and spectrum but fills the contrast floor below about -85 dB.
- `tools/bench` (developer benchmark, not packaged); `docs/design/gpu-windows-checklist.md`.

**Fixed (physics)**
- **`n2Scale` in a 1-D element now scales every third-order response**, not only the Kerr n2: the rotational Raman of gases (air, N2, ...) is multiplied by the same factor, as the spatial model already did. Before, `n2Scale: 0.5` on an air gap left the orientational response at full strength. Test `GasTests.N2Scale_In1D_ScalesTheRotationalRamanToo`.
- **The element's own `n2` key was ignored** (only run-spec overrides were read, so `"n2": 3e-20` on a bulk element silently kept the material's value). Found while writing the Kerr-lens tests. Test `KerrLensTests.ElementN2_OverridesTheMaterial`.

**Added (physics)**
- **`kerrLens` / `kerrLensModel`** on bulk elements: self-focusing of the 1-D Gaussian beam by the pulse's own Kerr lens (peak or pulse-weighted power, thin slices, second-moment radius), off by default. Exact reference: w² = w0²[1 + (1 − P/P_c)(z/zR)²]. See `docs/reference/nonlinear.md#kerr-lens-in-a-1-d-element`. The beam leaving the element is not carried on; use `cellMode` or `transverse` for that.

**Added (WP-E, small items)**
- Python: `result.unverified_inputs` (typed `UnverifiedInput` entries) and `result.ase_pedestal(element, duration_s)` (the `pulse.pedestal` component that carries one stage's inter-pulse ASE into the next). Tests `test_result_accessors.py`.
- A lens or curved mirror whose phase changes by more than π between neighbouring grid points at the beam edge now warns that it is under-sampled (this would have caught the tiled-array case). Test `LensSamplingTests`.
- Gain catalogue check (Fuchtbauer-Ladenburg): the Yb:YAG emission fit reproduces the catalogue lifetime to 6.7 %; the Nd:YAG fit is a single 1064 nm line that carries about 8 % of 1/τ, now said in its catalogue notes. Tests `GainCatalogueConsistencyTests`.
- The SPGD examples (`sensorless-correction`, `tiled-array`) use the default scale-free step instead of a tuned `gain=`.

**Added (examples)**
- `AdaptiveOptics/component-gallery`: the four `ao` elements against analytic answers (blazed-grating spot, two-pulse shaper, AOPDF chirp; the deformable-mirror sensor reading is 8 % below 2 × poke, cause not traced).
- `DigitalTwin/dependence-models`: which model of a saturating energy law a twin should use (proportional, quadratic, Chebyshev, network) judged on held-out energies.

**Fixed**
- The twin plugin crashed when predicting from a fit whose reported scalar (e.g. the spectral width) is `null` in `summary.json`; such scalars are now left out (found by the dependence-models example).

**Changed (Zemax)**
- The zemax plugin is documented as **experimental and unverified against OpticStudio**; it is removed from the README / getting-started / capabilities narrative and kept on its plugin page, with `plugins/zemax/HARDWARE-CHECKLIST.md` for the first run on a machine with OpticStudio. An unreadable or unknown OpticStudio lens unit is now an error instead of silently assuming millimetres.

**Added (install and build)**
- **`docs/install.md`**: one page with a "what do you want?" table (release install, source build, Python, C API, individual plugins), the hosted-versus-native Python engine, troubleshooting and a box of steps for AI agents. The README quick start, `getting-started.md`, `python/README.md` (its link to a missing `docs/python.md` is fixed), `releasing.md` and `llms.txt` point to it.
- **`build/setup.py`** (`./setup.sh`, `setup.bat`): source builds for this machine. `--cli`, `--python` (+ `--venv`, `--python-engine hosted|native`), `--cpp`, `--plugins A B` (only those and what their `plugin.json` requires), `--plugin-only A` (rebuild one plugin and refresh it in existing builds), `--check` (smoke test), `--doctor`, `--dry-run`, `--clean-native` (removes a stale NativeAOT library that shadows the hosted Python engine; never done unasked). `python/tools/build_engine.py` gains `--plugins` and `--framework` and is now importable; its output is unchanged without them.
- **`install.py --preset`** (`cli`, `cli-plugins`, `python`, `python-native`, `cpp`, `full`) as shorthand for `--include`, and a "Next:" summary after an install.
- Python tests `test_setup_and_calc.py` (plugin resolution, `setup.py --dry-run`, presets).

**Fixed (install)**
- **An engine library older than the Python package no longer breaks every call.** The loader bound all exports at load time, so a NativeAOT library built before `uf_pulse_calc` existed failed with `dlsym ... symbol not found` on the first engine call (`uf.version()`, `uf.run`), whatever the script did. Exports added since the first release are now optional (`_native.OPTIONAL`); calling a missing one raises an error that says to rebuild. Test `test_missing_optional_export_gives_a_clear_error_not_a_load_failure`.
- **`setup.py --check` tested the wrong engine:** it set `ULTRAFAST_LIBRARY` to the new hosted build, which hid a stale `bin/native` library that `import ultrafast` loads first. It now tests what a plain import loads, and `setup.py --python` exits with an error (not a warning) when a stale library shadows the build; `--clean-native` removes it.

**Added (pulse calculator)**
- **`ultrafast pulse shapes|limit|analyze|measure`**, `uf_pulse_calc` (C API) and `ultrafast.calc` (Python): time-bandwidth product of Gaussian (0.4413), sech² (0.3148) and rectangular (0.8859) pulses, transform-limited duration or bandwidth (Hz or m with a centre wavelength), the product, ratio to the limit and stretch factor of a pulse from its widths or from measured arrays (FWHM and rms products), and the GDD magnitude of a chirped Gaussian. Docs: `reference/pulse-calculator.md`. Tests: `PulseCalculatorTests` (5), `test_setup_and_calc.py`.

**Fixed (physics)**
- **A bulk / gap element's own `raman` model and `fR` were ignored** (declared in the schema as keys of nonlinear elements, read only through run overrides): `"raman": {"kind": "silica"}` / `"raman": "silica"` and `"fR"` on the element now give the same run as the same keys on the material (tests `CoreSweepTests`).
- **A crystal's budget did not close at the 1e-6 level**: the energy the resampling of its output onto the downstream grid dropped was not booked (only noted above 1e-4). It is booked as `grid boundary`; found by checking input - output = sum of losses on every example's summary.json (shg-lbo radial 1.4e-6, ppln-dfg 8.5e-7).
- **`thinFilmPolarizer.bandPosition` was read but not declared** (the stack model's position between the p and s band edges, documented in components.md): declared, so strict mode accepts it.
- **A crystal's budget did not close at the 1e-6 level**: the energy the resampling of its output onto the downstream grid dropped was not booked (only noted above 1e-4). It is booked as `grid boundary`; found by checking input - output = sum of losses on every example's summary.json (shg-lbo radial 1.4e-6, ppln-dfg 8.5e-7).
- **`thinFilmPolarizer.bandPosition` was read but not declared** (the stack model's position between the p and s band edges, documented in components.md): declared, so strict mode accepts it.
- **Avalanche and inverse bremsstrahlung were counted twice** (solids, where the avalanche is on by default): the avalanche loss `aval U / (2 k0 I)` equals `sigma rho / (2 k0)`, the Drude imaginary part, so the field lost the energy twice. The avalanche now only generates electrons; the field loses the energy once (`Plasma_Avalanche_DoesNotAbsorbTheFieldASecondTime`: output energy 5.15e-10 J, was 2.67e-10 J). Results of solid-state plasma runs with the avalanche on change.
- **Keldysh rate returned NaN in the far wing of a pulse** (field so weak that the Keldysh parameter exceeds 1e6): returns 0.
- **net48:** `ultrafast reference materials` printed `56.306399999999996` for `56.3064` (.NET Framework's `"R"`), so the committed page differed on Mono; the shortest round-trip form is used on both.

**Added**
- **`sameAs` on every element** (identical elements): `{"id": "pass2", "sameAs": "pass1"}` is the same physical object as `pass1` (same type and numbers, so a fit of a transform moves all its passes), but compiles on its own with its own pulse, beam, record and loss booking. A copy may set only `label`, `enabled`, wiring and per-pass geometry (`aoiDeg`, `aoiRad`, `tiltXRad`, `side`, `output`). Replaces the beam-splitter-only `sameAs`; existing files keep working. Python `same_as`. A fit path into a copy is refused. Docs: `reference/system-file.md#identical-elements`.
- `physics.strictMaterialsAllow`: catalogue paths (`"CS2.n2"`) or entry names that strict mode accepts although they are not verified.
- The generic defaults (thermal data of a user-defined gain medium: 10 W/(m K), 7.8e-6 1/K; ionisation inputs of a solid without data) are catalogue entries (`defaults:gainMedium`, `defaults:solidIonization`, status `unverified`); a run that uses one lists it in its notes and `unverifiedInputs`.
- Partial ionisation overrides: the run notes name the fields the run gave and say the rest come from the catalogue.

**Changed (release polish)**
- **Component check "plasma blue shift"** (`tests/components/media.py`): the single-carrier Drude formula is compared with `frequencyResolved: false` at 5 %; the default frequency-resolved operator (WP4) is a separate row at 8 %, because the 1/omega dependence across a 30 fs bandwidth lowers the shift by up to 5 % (measured -5.3 % at 1030 nm). This was the one CI failure of the component scripts.
- **Tests:** the release-install test no longer asserts an executable bit on Windows.
- **CI:** job timeouts (a hung `apt-get` ran for two hours) and `apt-get` retries.
- **Docs:** logo-palette theme and the long logo at the top of every page; README picture grid and contact badge; `docs/design/` condensed to `history.md`, `next-steps.md` and `backlog.md` (the branching design, the AO / twin record, the plan and the session notes are removed).

**Added (session F, WP8: release system)**
- **Release assets by component:** `build/package.py --components NAME...` (the old `--cli --python --cpp --csharp --zemax --examples --docs` are aliases) writes `dist/release-<version>/`: one reproducible `.tar.gz` per component and platform, `manifest.json` (size, SHA-256, platform, dependencies for each asset) and `install.py`. Components: `cli`, `python`, `python-native`, `cpp`, `csharp`, `zemax`, `plugins.<id>`, `schemas`, `docs`, `examples.<Group>` + `examples.shared`, `media` (not in `all`), `tests`, `component-tests`, `papers`, `source`. Source components are built from `git ls-files` (`build/release_components.py`).
- **`install.py`** (standard library only; in the repository and attached to each release): `--list`, `--include` / `--exclude` (dotted names, down to `examples.<Group>.<name>`; dependencies come along), `--version`, `--platform`, `--dest`, `--dry-run`, `--uninstall`, `--source git` (partial clone with a cone-mode sparse checkout). Hash-checked before anything is written; the install record `.ultrafast-install.json` makes uninstall remove exactly the files it wrote (a changed file is left with a warning); a second `--include` adds, a newer `--version` upgrades; wheels go in with `pip --find-links`, never from PyPI.
- **ZOS-API gate:** the asset writer refuses `ZOSAPI*` files, the packager and `release_components.py check-zosapi` look inside every archive and wheel, the installer refuses an archive that holds one.
- **`.github/workflows/release.yml`** on a `v*` tag (draft release): managed assets and the CLI for five platforms on Linux, NativeAOT components on Linux x64 / arm64 and Windows, manifest merge, the gate, installer smoke test. Not yet run on GitHub; macOS NativeAOT is built on the owner's Mac. No tag was created.
- 23 Python tests (`test_release.py`, no network or .NET): resolution, install / uninstall against a local release, changed files, hash mismatch, upgrade, the gate with a planted file, the git sparse path in a temporary repository, the packager options. Notes: [session-f.md](docs/design/notes/session-f.md).

**Added (session E, WP7: the open findings)**
- **Raman shift rate explained (`Fibres/raman-soliton-shift`):** the engine's rate is within 2 % of the exact-gain rate of a fixed-width soliton (`-gamma f_R integral (h_R * P) P' dt / E`, which reduces to Gordon's formula for a derivative-of-delta response) and within 0.3 % of pyNLO; Gordon's first-moment formula is 15-39 % low for 40-65 fs solitons because the soliton spectrum is not narrow against the Raman gain. No engine change. Test `test_raman_shift.py`.
- **SLM against Fourier explained (`AdaptiveOptics/slm-beam-shaping`):** a panel centred on the grid has a pixel edge on the sample at the axis, so each grid cell averaged two pixels and a blaze of P pixels per period kept `cos^2(pi/P)` of its light (0.854 at 8, 0.962 at 16). With `offsetXM = offsetYM = -pitchM / 2` the panel keeps all of it (1.0000) and the focal field equals an exact angular-spectrum propagation of the sampled field to 0.9-1.0 % (12-14 % with the absorbing edge, which removes the replica orders the periodic reference wraps round the window). The example now uses the offset and the exact reference. **New warning** in the modulator ("averages two device cells") when the pitch equals the grid step and the cell edges fall on grid points. Tests in `test_ao_plugin.py`.
- **Tiled-array SPGD reference explained (`AdaptiveOptics/tiled-array`):** at f = 0.5 m on 128 cells the lens phase at a tile 3 mm out changes by 3.7 rad per cell, so the lens was under-sampled and identical tiles reached the axis with fluences differing by a factor of 56 (the lock ended at 1.02 of the in-phase value). The lock now runs at f = 1 m (1.9 rad per cell) and ends at 1.00 with both bounds asserted. Test `test_tiled_array_identical_tiles_reach_the_axis_alike_only_when_the_lens_is_resolved`. Notes: [session-e.md](docs/design/notes/session-e.md).

**Added**
- **ASE over the inversion lifetime (session D, WP2):** `aseModel` on gain and amplifier elements (`true` or `{solidAngleSr, polarizations, points, bandLowerM, bandUpperM}`): steady-state forward and backward ASE per transverse cell and frequency of the emission band, quasi-static in z at frozen N2, depleting the inversion in the pumping and decay between pulses (so also in the pulse-train steady state). New file `Gain/AseModel.cs`, shared with fibre gain later. Reports `aseForwardPowerW`, `aseBackwardPowerW`, the spectra, `aseStimulatedPowerW`, `aseDepletionFraction`, `asePedestalEnergyJ` / `asePedestalEnergyFraction` (the strength for a `pulse.pedestal` of kind `ase`). Opt-in; `"ase": true` (noise in the window) is unchanged, no existing result moved. Transverse-resolved inversion yes, rays crossing cells no ([gain.md](docs/reference/gain.md#ase)).
- **Stress-birefringence pattern:** `thermalLens.stress` on a cartesian transverse grid (`ny > 1`) with an x-polarised input applies the four-lobed Jones map R(-phi) diag(1, e^{i delta}) R(phi) per point; the carried profile gets its own y shape; new value `depolarizationFractionPattern`. Its beam average equals the lumped fraction (1e-6). **Changed behaviour:** a spatial gain element on a cartesian grid with `stress` used the lumped rotation before; it now uses the pattern (the radial grid and a y input keep the lumped form, with a note).
- 9 tests (`AseStressTests`): n_sp h nu dnu (G-1) at 1e-12, RK4, Fuchtbauer-Ladenburg, saturated-amplifier law, cw photon balance, stress map. Notes: [session-d.md](docs/design/notes/session-d.md).

**Added (earlier in this iteration)**
- **Material catalogue (session C, WP3):** every material number of the engine now lives in a JSON catalogue file with its unit in the key name, `source`, `status` (`verified` | `approximate` | `unverified`), uncertainty and conditions: `src/UltrafastEngine.Core/Data/materials/{solids,gases,raman}` (41 entries, 115 quantities), embedded in the library. `MaterialLibrary`, `GasData`, `GasIndex`, `GasFormulaIndex`, `SolidIonizationData` and the Raman models (`LinAgrawalRamanResponse`, `HollenbeckCantrellRamanResponse`, the registry defaults) read from it through `Catalogue` / `CatalogueModels`; no table is left in C#. The move was a pure refactor: every index, n2, Raman kernel, rotational line and ionisation datum is pinned (`CatalogueSnapshotTests`, 2963 values) and reproduced to 1e-15.
- **Runs say what they used:** a nonlinear element that uses an `approximate` or `unverified` catalogue value (n2, Raman response and fraction, Kerr anisotropy, rotational Raman data, ionisation data) adds a note (`APPROXIMATE catalogue value used: FusedSilica.n2 ...`), and `summary.json` has the new key `unverifiedInputs`; `provenance.json` entries carry `catalogueStatus` / `catalogueSource`. A value the user overrode is not listed; a linear run lists an unverified index.
- **User catalogues:** `~/.ultrafast/materials/*.json` (or `$ULTRAFAST_MATERIALS`) add a material or replace single quantities of a built-in one, with a notice and a note in the run.
- `ultrafast reference materials -o docs/reference/materials-catalogue.md` generates the catalogue page (a sixth generated file; `/regen`, the protect hook and the up-to-date test know it). It ends with the "not verified yet" list.
- Dispersion sets checked against the refractiveindex.info pages (fused silica, N-BK7, N-SF11, CaF2, sapphire o/e, YAG, MgF2 o/e, quartz o/e, calcite o/e, BaF2, CS2, the Borzsonyi gases, O2, CO2, H2): 28 quantities are now `verified`, each with the comparison recorded.
- **Gain media, chi(2) crystals and Ciddor's constants moved into the catalogue (session C, part 2):** `Data/materials/gain` (Yb:YAG, Ti:sapphire, Nd:YAG: 3 entries) and `Data/materials/crystals` (BBO, LBO, KTP, KDP, LiNbO3, MgO:LiNbO3, BiBO, AgGaSe2: 8 entries) read by `GainMedium.Get` and `CrystalLibrary.Get`; the constants of `CiddorAirIndex` are quantities of the `Air` entry. Pure refactor again: 449 more pinned values (cross sections and saturation fluences of the three media at two temperatures, the principal indices, d tensors and scalars of every crystal at two temperatures, the air index and water-vapour fraction in three states) reproduced to 1e-15. Gain and crystal elements list their approximate / unverified catalogue values in the run notes. 15 more quantities are `verified`: the BBO, KTP, KDP, LiNbO3 (o), BiBO and AgGaSe2 index sets equal the refractiveindex.info pages; Ciddor's dry-air dispersion equals the standard-air page (test).
- **Strict mode:** `physics.strictMaterials` (or `ULTRAFAST_STRICT_MATERIALS=1`) refuses a run that uses an approximate or unverified catalogue value and lists them in the error.
- Tests: `CatalogueSnapshotTests`, `CatalogueTests` (see `docs/testing.md`).
- **Thermal lens as a distributed lens (session B, WP1):** the tracked beam crosses each slice of a pumped gain medium as a graded-index duct n(r) = n0 (1 - g² r²/2) with the exact reduced ABCD matrix (`BeamState.Duct`, `DuctMatrix`) instead of thin lenses. A regenerative amplifier can put its thermal lens on the tracked beam (`thermalLens.track`, `passGapM`, an integer `roundTrips`). Elements report `trackedThermalLensEffectiveFocalLengthM` (-1/C of the duct) beside the thin-lens equivalent `trackedThermalLensFocalLengthM`.
- **Frequency-resolved Drude plasma (session B, WP4):** `PlasmaResponse` implements the new `IDispersiveResponse`; the envelope, carrier-resolved and spatial propagators apply the free-electron index as the spectral operator `i (omega/c) R(omega) FFT[dn A]` with `R = omega0 (omega0 + i nu) / (omega (omega + i nu))`. `ionization.frequencyResolved` (default true; false = the carrier value of earlier versions) and `ionization.electronDensityM3` (a prepared plasma). New `Physics/DispersiveResponses.cs`.
- Tests: the duct matrix, its limits and convergence, a thick rod and regenerative round trips (`BeamTrackingTests`); the Drude filter of a prepared plasma in three field models, its narrow-band limit, the drift energy of electrons born in the field, envelope against carrier-resolved (`IonizationTests`).
- **`UF_WITH_AO`:** the `ao` plugin (`deformableMirror`, `slm`, `spectralShaper`, `aopdf`) is compiled into the NativeAOT library like `gpu` and `hitran`; `build/package.py` passes `UF_WITH_AO=false` when it is not selected.
- **Grid planner and shapers:** a `spectralShaper` with a stepped or gapped mask gets room for its side pulses (orders 1 to 8 at `2 pi / pixel bandwidth`, weighted by the spot size); an `aopdf` gets room for its delay, replica delay and `|GDD| x bandwidth`. With `grid.auto: false` the planner warns instead. New `CompiledElement.ProgrammedGddS2` and a `programmedGddS2` argument of `GridPlanner.Plan`. The Weiner example no longer needs `grid.windowS`.
- The unknown-element error names the `ao` plugin for its four element types.
- A Branched tree of four arms, recombined at one point, against the tile source on axis (`|sum exp(i phi)|^2 / 16`); documented the closed speed items (compile cache, streaming executor) with their revisit triggers in `docs/reference/speed.md`.
- Tests for the open edges of the `ao` plugin: the shaper's grating dispersion against the Littrow reference; Bolduc and double-phase hologram maps with a varying complex field through the `slm` element; tiled-array lobe fractions against the array factor.

**Changed**
- **TGG refractive index:** replaced the old two-point fit (up to 0.0106 away from the measured table) by a one-pole fit to the nine points of Schlarb and Sugg 1994 (largest deviation 6.6e-4, valid 0.40-1.18 um, `approximate`). The YVO4 vendor Sellmeier sets differ from Shi et al. 2001 by about 5e-4 (o) and 9e-4 (e); left as they are and recorded in the catalogue for the owner to decide.
- **A measured `thermalLens.focalLengthM` is the effective focal length** (-1/C of the whole medium, what is measured from outside) and is spread over the medium as one uniform duct; before it was one thin lens at the centre (the two differ by f (gL)²/6, 2e-4 for a 1 mm rod). The tracked radius behind a pumped rod moves by about (gL)²: 0.4 % for the 4 mm rods of the examples.
- **The Drude plasma term is frequency-resolved in all three models:** the 1/(omega (omega + i nu)) dependence across the pulse bandwidth is included (it was the carrier value in envelope mode and omega/omega0² in carrier-resolved mode). For a broad pulse the blue shift is smaller (a 10 fs, 2 µm pulse in argon: 7 % instead of 12 % at 2 cm), and the electrons born in the field now take their drift energy U_p (the `absorption` loss grows by about U_p/U_i: 17 % in the filament example, `Nonlinear/filament`). `frequencyResolved: false` restores the old behaviour. Note: `trackedThermalLensFocalLengthM` is the thin-lens equivalent n0 g² L; with a measured focal length it no longer equals the given value (use `trackedThermalLensEffectiveFocalLengthM`).
- **cw sources sit on the requested carrier:** the lattice is shifted so that a bin is the carrier (it used to be snapped to the nearest multiple of the frequency step, 1.2e-6 away at 1030 nm). A cw run and a one-stored-frequency pulse run at the same carrier now give the same device response (patterns agree to 1e-16 instead of 3e-6; with four stored frequencies to 1.3e-8, the pulse bandwidth). A single stored frequency of a pulse is its energy-centroid frequency rather than the lattice bin next to it. No reference test needed re-baselining; the two cw-against-pulse tests were tightened (2e-5 to 1e-7, 3e-6 to 1e-9). Carrier-resolved models with a cw source need `grid.snapCarrier: true` written out.
- **`sensorless.spgd` is scale-free by default** (`gain=None`): the update is normalised by the recent range of the metric difference, the step of a channel is at most `step` (0.4) x `amplitude`, and multiplying the metric by a constant changes nothing. The default reaches at least 0.94 of the optimum for `step` 0.3 to 1.0 on phase locking and a Strehl peak at metric scales 1e-3 to 1e3, and 0.9 on the engine's deformable-mirror bench. `gain=<number>` keeps the classical update (gain in units of 1 / metric), so existing calls behave as before; the fixed gains that lock at scale 1 do nothing at 1e-3 or 1e3.
- **Python package structure:** `ultrafast.plotting` is a package (`style`, `data`, `time_domain`, `spectral`, `run_figures`, `spatial`, `instrument_panels`, `diagrams`, `zernike`, `animate`); `ultrafast.diagram` moved to `ultrafast.plotting.diagrams` (the old name still imports and warns). New `ultrafast.system`, `components`, `sources`, `instruments`, `algorithms` and `results` group the builders by what they are (CapWords names are aliases of the `build` functions, so typed returns are unchanged). Every 2.2.0 public name still imports (a test checks a committed list).
- **Examples regrouped** from `docs/examples/NN-name` into `docs/examples/<Group>/<name>` (GettingStarted, LaserSystems, Nonlinear, Diffraction, Wavefronts, Branched, Diagnostics, Components, Computing, DigitalTwin, Interop, AdaptiveOptics); the index keeps the old numbers; `build/example_check.py` accepts a name, a group or an old number.
- CI: `checkout` v7, `setup-python` v7, `setup-dotnet` v6, `upload-artifact` v7 (Node 24); `pyproject.toml` uses an SPDX license string (setuptools >= 77).
- **Licence: MIT -> Apache-2.0**, with a `NOTICE` file naming the author (attribution is a licence condition). `CITATION.cff` and a README "Citing" section with BibTeX ask users to cite the software in published work (a request, not a licence condition). Author and contact (jac318@bath.ac.uk) added to the package metadata; the release packages ship `LICENSE`, `NOTICE` and `CITATION.cff`.
- **CI:** the unavailable `macos-14` job is replaced by `ubuntu-24.04-arm` (arm64 Linux, the stand-in for Apple silicon). macOS is verified by hand.

**Fixed**
- **`ny = 1` grids and one-row maps:** a `measured` profile or `mask` file whose map has a single row (for example a "1-D" scene saved at `y = (j - ny/2) dx` with `ny = 1`, whose row is at `y = -dx/2`) gave zero on a grid with `ny = 1`, because the grid samples `y = 0`, outside the one-row map. An axis with one sample is now constant along that axis.
- `transverse.spectralSamples: 1` stored the profile at the low edge of the band (the first bin above 1e-6 of the peak, 3e-6 off the carrier for a 1 ns pulse); it is now the bin nearest the energy centroid. Single-frequency spatial results move by up to a few 1e-6. The Fox-Li test that relied on the offset now starts from a curved-mode plane (unequal gaps), and checks that an eigenmode start converges on the analytic mode radius.

**Added**
- **`modulator` element** (docs/reference/modulator.md): a pixelated thin phase / amplitude device on the carried profile (SLM, deformable-mirror surface, tile mask): cell grid with pitch, offset and rotation; `phaseRad` / `opdM` / `amplitude` as an array, a callback `"@name"` (no file) or an NPZ; `fillFactor`, gaps, `zeroOrderLeak`, `phaseWrapRad`, `quantisationLevels`, Gaussian cross-talk or an explicit `crosstalkKernel`; chromatic rules `opd` (also `phaseAtDesign`; a wrapped map is a real grating), `fixedPhase` and `index` (liquid crystal: the phase follows ω Δn(ω), from `deltaIndexCauchy` or a `deltaIndex` table); cell averaging over 6 x 6 sub-samples; reports `spectralSamplingError` and warns when `spectralSamples` is too few. A callback map is read at every compile, so N trials of a loop equal N file runs. Python: `Modulator` / `build.modulator`.
- **Large grids:** `transverse.frequencyBlock` streams linear spatial propagation over the stored frequencies in blocks (same result to 1e-12; the propagator holds a block, a block-sized work buffer is added, so blocks under half the stored frequencies save memory); `outputs.keepProfiles: "used"` drops the profiles no instrument reads (1587 -> 336 MB held after a five-element 1024 x 1024 x 16 run). docs/reference/transverse.md#large-grids-streaming-and-memory.
- **Coherent tile source**: `initialProfile.kind = "tiles"` (square, hex, ring or explicit layouts; per-tile amplitude, piston, tilt, delay; random piston error); Python `TilesProfile`.
- `wavefrontSensor` reports slope maps `slope_x_rad`, `slope_y_rad` (zonal sensor output).
- `docs/design/notes/spikes.md`: measurements for the adaptive-optics core (a full 1920 x 1080 SLM panel with a 30 fs pulse: 32 s and 3.6 GB at 16 stored frequencies).
- `ultrafast.algorithms.zernike` (Noll polynomials, fit, evaluate; agrees with the engine's `wavefrontSensor` coefficients to 1e-6), `ultrafast.plotting.zernike` (decomposition figure: wavefront, fit, residual, coefficient bars, rms and Maréchal Strehl; several wavefronts side by side; animated), `ultrafast.plotting.animate` (animated GIF with Pillow, MP4 when ffmpeg exists, size budget check).
- **Computing examples:** 4f correlator, optical convolution, matrix-vector multiplication and parallelism-and-time. A lens at critical sampling f = N dx² / lambda is the DFT; each example is checked against numpy.
- `docs/examples/make_all.py` and a `make_figures.py` in every example (figures, animations, size budget: PNG 300 kB, GIF 2.5 MB); a hero figure in the README of every example that had none; `build/gallery.py` and `docs/gallery.md`.
- `build/package.py --examples-wheel` and the `ultrafast-examples` command (`list`, `copy`, `run`); extras `animate` and `examples`.
- Docs site: brand colours, long-logo footer on the plugin READMEs, Google Search Console verification (meta tag and HTML file).
- **`ao` plugin** (docs/plugins/ao.md): `deformableMirror`, `slm` (compile into the core `modulator`), `spectralShaper` and `aopdf` elements (parametric, no vendor data); `ultrafast_ao` (`DmBench` simulated AO bench, `tiled_array` and `combining_metrics` for coherent tiled-aperture arrays, NumPy twins of the devices); `ultrafast.algorithms.holography` (Gerchberg-Saxton with quantisation in the loop, weighted GS, MRAF, double-phase and Bolduc encoding, grey-level mapping), `.control` (Shack-Hartmann lenslet model, Zernike modal and Southwell reconstructors, interaction and control matrices, integrator loop) and `.sensorless` (SPGD, hill climbing, modal search, phase-stepping transmission matrix).
- **Fitting across conditions** (docs/plugins/fitting.md): `fit.conditions` (one set of shared parameters fitted to many operating conditions, held-out conditions with their own chi2 and prediction bands, `perCondition` parameters), `dependence` of a parameter on a condition variable (constant, proportional, linear, polynomial, power, Chebyshev, physics-constrained network), `fit.surfaces` (smooth surfaces in wavelength and condition with penalties), the output `fitted-conditions.json`, and the transform operator `nonlinearPhase`.
- **`twin` plugin** (docs/plugins/twin.md): the `ultrafast twin` command and the `ultrafast_twin` Python module: a project layout, validation with held-out conditions and credible bands (verdicts `ok`, `marginal`, `fits-training-only`, `unvalidated`), a placement and experiment planner (information gain), a what-if explorer with paired draws, drift tracking with an ensemble Kalman filter, goal files and a benchmark of design tasks with known answers. Not built: the MCP server.
- **Examples:** `AdaptiveOptics/` (dm-correction, sensorless-correction, aberrated-high-power, slm-beam-shaping with a full 1920 x 1080 panel, tiled-array, temporal-shaping, scatterer-focusing), `DigitalTwin/` (build-the-twin, explore-the-twin) and the new group `Fibres/` (soliton-breathing, raman-soliton-shift, spm-compression), each with a hero figure, an animation where an algorithm iterates and an assertion of its key number against theory; `docs/examples/_viz.py` (shared figure helpers).
- A logo generated from code (`assets/logo/make_logo.py`): a 3 x 3 grid of Zernike modes 2-10 with "Ultrafast Engine" to its right (dark and light text variants as SVG), and a flat-cell design for small sizes. The script also writes `assets/ultrafast.ico`, the icon of the CLI and the Zemax binding. README header uses it. A long logo (five modes in a row, "Ultrafast Engine" on one line, dark and light text) is the footer of the README, of `python/README.md` and of every page of the documentation site; the site also gets the sidebar logo and the favicon (`docs/img/`, `overrides/main.html`).

# Development history before 1.0.0 (internal version numbers)

## 2.2.0 (2026-11-08) -- stable release

The engine leaves beta: the November 2026 consolidation (component tests, twelve paper replications, the bug fixes and features below), a documentation site on GitHub Pages and a rewritten README.

### Engine fixes and features from the November 2026 consolidation (component tests, 12 paper replications)
**Changed**
- The input pulse is built at the **requested carrier**, not at the carrier snapped onto the frequency lattice (up to ~1.5 nm off at 1030 nm / 10 fs). Kerr phase, self-steepening, reported angular dispersion and carrier wavelength follow it (`grid.reportOmega0`). The fibre SPM phase error of 0.1-0.4 % that depended on the grid is gone. The window frame still moves with the lattice-centre group delay; `snapCarrier: false` removes the offset.
- `retrieve` no longer silently assumes SHG-FROG: the kind comes from `kind`, from the data, or from the instrument's `frogType`; an asymmetric trace without a kind is an error. `anisotropicFraction` outside 0..1.5 is an error (1 < b <= 1.5 is now allowed).
- `energyMeter` and camera `bandM` weight edge bins by the fraction inside the band.

**Fixed**
- `trackedAngularDispersion` of gratings and prisms carried an error of (lambda_req/lambda_snap)^2 - 1.
- A round aperture on an elliptical beam uses the exact integral (0.7897, not 0.8816, for 0.6 x 1.2 mm and a = 0.8 mm).
- A strongly phase-mismatched thick chi(2) crystal is integrated with at most 0.5 rad of mismatch phase per step (it was 150-960 times off the sinc^2 law with no warning).
- The grid planner includes the memory of a delayed response; a fixed window that is too short is reported.
- An element-level `selfSteepening` on fibres and bulk elements is honoured.
- Fibre D(lambda) / beta2 tables use a cubic spline (a 53-point table was 0.8 % off).
- KDP type-I d_eff with an explicit cut changed erratically with phi (the ordinary-mode eigenvector came from rounding noise).
- `anisotropicFraction` is honoured by `expression` and `callback` delayed responses; an explicit key on `linAgrawal` replaces its own split.
- The interferometric autocorrelator delay axis includes d = 0 and the background is analytic (peak:background 8, was 7.84).
- `crossCorrelator` with `resolutionS` reports trace, floor and `contrastAt_*_db` relative to the same (resolution-limited) peak.
- Generated option reference: the "More:" labels show the page they link to.

**Added**
- Warning, and the loss booked as `spectrum at non-positive frequency`, when nonlinear broadening reaches omega <= 0 (a 1.5-cycle pulse at 2 um with B = 3 is cut; a carrier-resolved model is needed there).
- FROG: `frog` `type: "sd"` (self-diffraction), instrument `xfrog` (alias `sfgFrog`) with a known `reference` pulse, retrieval kinds `sd-frog` and `xfrog` for `pcgpa` and `epie`, multi-trace retrieval `multiFrog` (double FROG) and Python `uf.retrieve_multi`. `crossCorrelator` `gateArm` (elements in the second-harmonic gate arm only).
- Built-in `CS2` (Kedenburg index; electronic + `orientational` delayed response) and `BaF2`; cubic Kerr anisotropy (`kerrAnisotropy`, `kerrCutAxis`, `kerrAzimuthDeg`) with cross-polarised-wave generation. CS2 n2 and split, and BaF2 n2 and sigma, are unverified literature values.
- `cellMode` command and `ultrafast.cells.cell_mode`: the Kerr-matched Gaussian eigenmode of a two-mirror multi-pass cell (Herriott or quasi-concentric) with gas or plates.
- Instrument `singleShotCorrelator`: the time-to-space cross-correlator (window and resolution from the crossing geometry, Ma et al. 2018 eqs. 1-2).
- `tests/components/` (was `verification/`): ten families, 1691 checks (see docs/component-tests.md). `docs/papers/`: twelve replications, each with a "How well the science matched" section.

### Added (plan of 2026-11: docs/design/plan-2026-11.md)
- `tests/components/` (was `verification/`): one script per component family (linear, splitters and masks, polarisation, crystals, instruments, optics_misc, media, crystals_chi2, spatial, instruments_more; 1691 checks) that runs each component alone, overlays the analytic answer and writes an HTML gallery (`run_all.py`); a CI job runs it without plots. C# component sheet (`tests/.../Components/`): every listed component at 515 nm / 1030 nm / 2 µm and 1 ps / 10 fs, energy, reported GDD, bandwidth, Sequential = Branched.
- `docs/papers/` (published results reproduced, with our plots and commentary; harness `build/paper_check.py`, a CI job for the fast tier) and the plan for November 2026. First four: Escoto 2022 (compression quality), Gordon 1986 (soliton self-frequency shift), Boyd-Kleinman 1968 (focused SHG), Bespalov-Talanov 1966 (small-scale self-focusing).
- `pulse.pedestal` kind `measured`: an input pedestal that follows a recorded contrast trace (`csv` with time and relative intensity or dB columns, or arrays).
- `ultrafast-plot` draws a Branched system as a graph (arms in lanes, merges, virtual ports, the output energy).
- Paper replications added: nonlinear ellipse rotation in a multi-pass cell (Escoto et al., CLEO 2024) and a lab beamline fitted to recordings (fitting plugin, measured pedestal, water lines of the air).
- Figures and commentary for examples 01, 02, 03, 08, 09, 11, 13, 14, 19, 22, 25 and 27.
- Claude Code skills and agents for replicating papers, mirroring a lab setup, optimising and debugging a setup, and hunting engine bugs; the commands `/replicate` and `/lab`.
- CI runs the C# and Python tests on macOS (Apple silicon) and Windows.
- Example 41: a pump-probe experiment (opposite circular pump and probe in a Branched system) against the exact cross-phase shift of the probe's spectral centroid.
- **Non-collinear OPA and DFG**: the angle from the arms' `tiltXRad` (or `externalAngleDeg`), refracted at the face; the idler leaves at the angle that conserves the transverse momentum, recorded on its path; a crystal's fed port can hold a virtual beam. Example 42 (a NOPA). Checked against transverse momentum, the parametric gain with mismatch (Cerullo & De Silvestri 2003), arms = stated angle, DFG photon balance.

### Fixed (2026-11)
- A tilted plate (also ND filter and thin-film polariser substrate) without an `inputBeam` now has the plane-wave phase of colours refracting at their own angles; its GDD was 0.3 fs² (40°) to 1.0 fs² (70°) too high for 5 mm of fused silica at 1030 nm.
- A virtual beam fed to a Sequential `beamSplitter` keeps its own carrier; what falls outside the run's grid is booked and warned about. A measured input spectrum lying mostly outside the grid's band is an error (a partly cut one warns) instead of being renormalised at the band edge.
- A `leakMirror`'s `gddFs2` / `leakGddFs2` is a phase about each beam's own carrier.
- A probe passed by several packets gives each its own arrays (`probe_<id>_packet<k>_*`, listed in `summary.probes`).
- Reported GDD and TOD are those at the requested carrier, not the snapped grid centre (`summary.grid.reportOmega0`).
- The critical-power constant (3.77, Marburger) is documented; the Hollenbeck-Cantrell table entry that could not be checked against the paper is noted in the reference.

### Fixed (component sheet, 2026-11-05)
- `leakMirror`: `gddFs2` / `leakGddFs2` were applied but the reported own and system GDD stayed 0 on both ports.
- `gratingPair` with no propagating diffraction order at the carrier removed the whole spectrum silently; it is now an error.
- A fed beam of another colour on a Branched splitter (`virtual_input` at 515 nm in a 1030 nm run) was built on the run's grid: incoherently it came out as a band-edge tail, coherently it interfered as if it were 1030 nm. Virtual inputs now get their own carrier's grid, and beams that meet go on a grid spanning both.
- `GridResampler.Exact` aliased when the carrier shift exceeded half the source band; the shift is now a frequency-bin shift after padding.
- **Raman kernel aliasing on coarse grids** (found replicating Gordon 1986): a delayed response sampled directly on a grid with dt above the 12 fs Raman period aliased; the soliton self-frequency shift came out 3 % too fast at dt = 6.5 fs and 43 % at dt = 23.5 fs. The kernel is now evaluated on a finer grid, band-limited and decimated, so its discrete spectrum is H(ω) on every grid. The pyNLO cross-checks run pyNLO at 16384 points because pyNLO's own sampling aliased the same way.
- **`compressor` instrument** reported the FWHM from a compact copy of the pulse (time step a fifth of the compressed FWHM after strong SPM): 4 % short at B = 40 rad (found replicating Escoto 2022). It now uses the input on its own grid.
- Example 32 quoted the energy closure (rounding noise, platform dependent) as a number; it now prints the bound it meets.

### Added (branched systems; plan: docs/design/branching.md)
- **Branched systems**: `"topology": "branched"`, `next` naming input ports (`"bs:1"`, `{"id": "bs", "port": 1}`), ports on every element type, a graph compiler with verification (`ultrafast validate --graph`), alerts and virtual ports for unused ports.
- **Interference**: a carrier-phase ledger per path; packets; `beamSplitter`, `delay`; virtual input beams, also in Sequential systems.
- **Loops**: `ringDown` and `steadyState`. **Crystal ports**: `aux` beams with `fromPort`.
- Python: `Sequential`, `Branched`, `link`, `virtual_input`, `loop`, `beam_splitter`, `delay`. Examples 32 and 33. System file version 3.
- Architecture docs (`docs/architecture/`), and file headers as a rule.
- **More elements**: `polarizingSplitter`, `leakMirror`, `dichroic`; `sameAs` and `side` for Michelson folding; tilts and offsets on `delay`. `cepRad` on pulses.
- **Space**: mode overlap of tracked beams (q, tilt, offset), carried profiles meeting, beams of different carriers meeting. Crystal waves are ports.
- **Phase**: crystals, gain media and spatial elements report carrier phase; harmonics scale it; tracked beams accumulate a Gouy phase.
- **Delayed light** follows each branch. **Studies**: a node cache for Branched systems, and `arrayOutputs` written to `study.npz`.
- Example 34. Python: `polarizing_splitter`, `leak_mirror`, `dichroic`.

### Fixed (review of T0 to T2 and branches, 2026-10-31)
- **M² and beam radius of off-axis and higher-order beams.** Moments were not central, and the curvature fit unwrapped phase along radius: a 1 mrad tilt read M² = 41, a vortex 84 to 155, HG₃₀ 76, and a 1 mm decentre w = 2.24 mm. All now match 2p + |l| + 1, 2m + 1 and 1 to 1e-6. Reported `beam.radiusXM` and `m2` change for such beams; centred Gaussians do not. Size-dependent settings derived from these radii (crystal overlap, gain mode) are affected the same way.
- **`steadyState` loops** claimed convergence after two passes, because only the shape was compared and a passive loop keeps its shape while it decays. Energy must repeat too; a decaying loop now warns and points to `ringDown`.
- **`graph.energyOutJ`** counted only the strongest packet at `end` and booked the others as a loss. It counts all of them; `packetsAtEnd` is new.
- **Crystal ports**: other packets at a fed port vanished from the budget (12.5 % in the test case), and fed energy was used once per main packet. Only the strongest packets now interact, and the rest are booked with a warning.
- Merging carried profiles lost the second arm's delayed-light tail.
- **Study node cache** never hit away from the nominal point: study parameters are run overrides, and all of them were hashed into every element's key. `element:<id>` overrides now count only for that element. An 81-point delay scan with 0.3 m of nonlinear glass upstream goes from 58.2 s to 9.6 s, with identical results.
- `build/example_check.py --against` hid a baseline that failed or wrote nothing, which read as "no differences". It now reports both. Python examples use the current library either way, so `--against` compares CLI examples only.
- `00-quickstart` wrote its output into the current folder; it now writes beside the script.
- **Delayed light through a solver.** Narrow gas lines inside a nonlinear or spatial element stayed on the main grid, which cannot resolve them, so the ring-down wrapped and almost never reached the tail (1e-25 of the true value in the test). They now go exactly on main + tail, half on each side of the solver. A vector profile bypassed the exact step, so its lines and ghosts wrapped too (a 49 ps ghost read 80 ps in a test); it now takes that step, and only polarisation-mixing terms go on the full field. The tail also skipped the material of spatial elements altogether.
- **Crystals fed at a port with a different colour** (SHG to THG): the fed beam was treated as the run's carrier, so Δk and the generated carrier were wrong and conversion was 1e-4 of the true value, with no error. Fed beams keep their own carrier, in compilation too, where each output port of a crystal now has its own; `cut: "auto"` solves for the colours that arrive. Same-colour systems were not affected.
- A compiled element could continue from a crystal's `aux` port link rather than its main input. A tilted arm into a crystal now warns that crystals are collinear. `crystals.md` wrongly listed `sfg` as accepting `noncollinearDeg`.
- `python/tools/build_engine.py` warns when a stale `bin/native` would shadow the hosted build. `aux_beam` gained `from_port`, without which a branch-fed crystal could not be written with builders.
- Plugins: the `GetTargetPath` error (MSB4057) shown in VS Code on each plugin project.

### Added (review)
- `python/tests/test_lightpipes.py` and `test_pynlo_branched.py` (cross-checks); `ReviewRegressionTests`; `m2Y` from `beamProfile` on 2-D grids.
- Examples 32, 33 and 34 use the Python API, with figures; 34 adds a FROG. New: 35 (CLI), 36 (Python), 37 (Python), 38 (CLI), 00-quickstart. The examples list says which interface each uses.
- **Non-collinear SFG and SHG**: `noncollinearDeg`, new `externalAngleDeg`, or the angle from the arms' `tiltXRad`. Phase matching with transverse momentum, overlap of crossing beams, pulse-front tilt and geometric smearing on a Cartesian grid, and output angles in the crystal's values and on each path. `shg` refuses an angle and points to `sfg`. Example 40.
- Example 39, a third-order cross-correlator as a Branched system. `CrystalChainTests`: Manley-Rowe through SHG and THG, the cross-correlation width, and conversion against length and bandwidth.
- Moved: tutorial files to `docs/tutorials/files/`. Removed: finished design plans, folded into `docs/design/history.md`.

### Fixed (physics review of the T1 and T2 work, 2026-10-30)
- Dispersed rays did not refract at normal incidence: glass between gratings counted as n L, not L / n, in GDD, TOD and FOD. Curved surfaces had the same flaw.
- `physics.spatioTemporal: "profile"`: a prism had no effect (and lost its dispersion); now it tilts the profile. A plate warns. A run with no profile warns. Grid checks added.
- Stress depolarisation ignored the beam size (0.26 against 0.025 for w = r₀/3, C = 6) and was not a rotation, so y input lost energy.
- `resonator`, linear: the return pass used forward matrices, wrong for anamorphic elements.
- Tracked curvature had the opposite sign to carried profiles, in pulse-front tilt and `spaceTime`.
- Fox-Li claimed convergence from the loss alone; it now also needs the shape to settle. `freeSpectralRangeHz` uses the group delay.
- `coherence`: too few modes now warns, and shares are renormalised so the energy is the source's.

### Added (engine depth before branches)
- **Exact dispersion from angular dispersion**: a five-ray bundle in the tracker gives GDD, TOD and FOD per element, replacing the second-order formula (Treacy's TOD reproduced). `prism` element.
  `physics.spatioTemporal: "profile"`: gratings and tilted interfaces tilt each frequency of a carried cartesian profile. `spaceTime` works from the tracked beam on 1-D runs.
- **Cw sources** (`shape: "cw"`, `powerW`), one spectral bin end to end; `beamProfile` reports `peakIntensityWPerM2`.
- **Absorbing boundary in nonlinear spatial elements**. Hankel matrix built with symmetric products (twice as fast).
- **Gerchberg-Saxton** (`ultrafast retrieve gerchbergSaxton`), multi-start. `measured` profiles: grayscale PNG, `threshold`, `center`.
- **`resonator` element**: round-trip matrix, stability, eigenmode on the tracked beam, Fox-Li mode and loss; ring or linear.
- **Partial coherence**: `coherence` in the run spec, a Gaussian-Schell beam as coherent modes.
- **Thermal stress birefringence** (`thermalLens.stress`): Koechner depolarisation, bifocal lens. Amplifier thermal lens on the tracked beam on request (`thermalLens.track`, `passGapM`).
- Python: `build.cw`, `prism`, `resonator`, `coherence`; `thermal_lens(pass_gap_m=, stress=)`. Example 31. The validation matrix counts cross-checks inside the engine apart.

### Added (beam sources, masks, checks)
- **Structured beams** ([docs](docs/reference/transverse.md#initial-profile)): `initialProfile.kind` `hermiteGauss`, `laguerreGauss`, `besselGauss`, `airy`, and `measured`
  (a near field from NPZ, with background subtraction). **`mask` element** ([docs](docs/reference/transverse.md#masks)): axicon, Fresnel zone plate (amplitude or binary
  phase), vortex plate, slits, amplitude / phase maps. Python: `build.mask`, more `build.initial_profile` keys. `BeamSourceTests` (12 tests).
- `build/example_check.py` runs every example and tutorial command and checks the numbers they quote; `--against <ref>` diffs every `summary.json`. `.github/workflows/ci.yml`:
  Linux CI, with the CLI compiled for every platform.
- `docs/reference/validation-matrix.md`, generated from `[Test(Reference = ..., Tolerance = ...)]` and `[TestArea]`.

### Fixed
- `docs/examples/10-ppln-dfg/compare_pynlo.py` could not import its test module (wrong relative path).
- Apertures now also mask the y polarisation's profile of a vector field.
- Stale documentation found by the check: example 18 (B = 0.416 rad), 21 and 26 (fit output), tutorials 1, 3 and 4 (tutorial 4's recordings regenerated, its fit numbers
  refreshed; the autocorrelation now compares all 201 points, those beyond the simulated range against zero).

### Added (diffraction, thermal lens tracking, spatio-temporal couplings)
- **Diffraction by apertures** ([docs](docs/reference/transverse.md#diffraction-and-apertures), [example 30](docs/examples/30-iris-diffraction/README.md),
  [tutorial 6](docs/tutorials/06-diffraction.md)): hard apertures on a carried profile give each edge cell the fraction of its area inside
  the opening (patterns converge with the grid; a 0/1 mask was off by up to a cell); a warning when an opening spans fewer than 10 samples
  (`apertureSamples`); rectangular / decentred openings on a radial grid warn. `transverse.absorbingBoundary` (`true` or `{widthFraction}`)
  absorbs light leaving the grid in linear elements (booked `grid boundary`, `absorbedAtBoundaryJ` / `Fraction`) instead of reflecting it onto
  the axis (radial) or wrapping it (cartesian). Checked against the Fresnel integral (< 1 % of the peak), Fresnel zones on the axis, Airy and
  sinc zeros (`DiffractionTests`, 8 tests).
- **Beam shapes**: `initialProfile.kind` `tophat` (hard edge), `geometry: "square"` (separable square / rectangular beams, cartesian),
  and the documented but unread keys `radiusM`, `radiusXM` / `radiusYM`, `radiusOfCurvatureM`, `tiltYRad` now work -- a spatial element
  can set its beam without a `beam` model. Unknown kinds are errors (they silently became Gaussian).
- `transverse.spectralSamples` (default 48): frequencies at which the profile is stored; fewer for narrowband pulses on 2-D grids.
- **Thermal lens in the Gaussian beam tracking** ([docs](docs/reference/gain.md#thermal-lens)): a pumped `gain` element puts its thermal
  lens on the tracked beam -- the heat model evaluated with the pump alone at compile time, one thin lens per slice, each the parabola fitted
  with the beam's intensity as weight. `thermalLens.focalLengthM` (measured value) and `thermalLens.track`; `trackedThermalLensFocalLengthM`;
  a note when the run's thermal lens differs by > 5 %. Gain elements take their mode radius from the tracked beam when they have no `beam`,
  `modeRadiusM` or profile (before: the pump radius).
- **Spatio-temporal couplings** ([docs](docs/reference/components.md#spatio-temporal-couplings)): with the tracked beam, spatial and angular
  dispersion per axis (Kostenbauder ray-pulse matrices) through gratings (`grating` with `linesPerMm`, `order`: anamorphic magnification and
  angular dispersion), tilted interfaces (signed angles; prisms from two interfaces), tilted plates and single-pass grating pairs, and the
  group-delay dispersion they add (grating + gap + grating = Treacy to 0.1 %, 4f stretcher = 0). Reports `trackedAngularDispersionXRadPerM`,
  `trackedSpatialChirpXMPerM`, `trackedPulseFrontTiltXSPerM` / `Deg` (from the pulse's spectrum: Hebling and spatial chirp x temporal chirp).
  `physics.spatioTemporal` (`auto` | `on` | `track` | `off`). `BeamTrackingTests` (9 tests).
- Python: `build.grating`, `build.thermal_lens`, `build.physics`; `build.aperture` takes rectangular / decentred openings and no longer
  needs `beam_radius_m`; `build.transverse(absorbing_boundary=, spectral_samples=)`; `build.initial_profile(geometry=, radius_x_m=,
  radius_y_m=, radius_of_curvature_m=)`.

### Changed
- **Faster spatial propagation**: linear spatial elements propagate only the carried profile at its stored frequencies (≤ 48) instead
  of the whole field (same result at those frequencies, 10⁻⁴); the radial (Hankel) transform matrix is built once per size with
  cache-friendly products (2048 points: 85 s → 1 s per run, instead of per element). The `beamProfile` instrument computes the fluence
  from the stored frequencies (as the cameras do) instead of expanding the whole field (a 512² run: 33 s / 8.8 GB → 3 s / 0.65 GB);
  its values change at the 10⁻³ level.
- The tracked beam now crosses gain elements at the host's index (it was treated as free space).
- Tilted interfaces and plates now disperse the tracked beam; with `physics.spatioTemporal` = `auto` their small coupling GDD is applied
  to the pulse (systems with traced `groupDelayS` are left alone).
- **Faster CPU propagation**: the managed FFT for powers of two is now a radix-4 Stockham autosort (3-10x faster than
  the radix-2 code at 8192-65536 points), the 1-D envelope operator works in place without allocations, and the
  adaptive stepper reuses its buffers. The supercontinuum example went from 23.4 s to 8.8 s (managed FFT) or 5.7 s
  (FFTW) on a 2-core machine, with the same output to 1e-12.
- Run specs without `compute.backend` use the process default (`cpu` unless changed) instead of always `cpu`.
- **Measured data is part of the core.** The instruments' `"measured"` key (comparison with recorded CSV / NPZ /
  JSON data), `RunResult.Comparisons` (`UltrafastEngine.Measured`), the `comparisons` / `measuredChi2` summary
  sections, the CLI's "measured data" lines and the `measurements` figure moved from the fitting plugin into the
  engine and work without any plugin. The fitting plugin keeps fits, transforms and backward propagation; its
  `result.Comparisons()`, `ultrafast_fitting.measured` and `ultrafast_fitting.comparisons` remain as aliases. Schema:
  `obj.fitting:measured` is now `obj.measured` (Python type `ultrafast.types.Measured`).
- Comparisons only compare the part of a recording that the simulated instrument covers: points outside its range
  are left out of χ², the rms error and the normalisation (kept with residual 0 and simulated NaN, so a fit's
  residual vectors keep their length), and counted in the notes.
- A recording coarser than the simulated instrument (`resolutionM` / `resolutionS` in `measured`) is matched by
  blurring the simulated curve with the missing Gaussian width (`matchResolution`, default true); a finer recording
  is reported as before. `background: "auto"` on curves uses the darker edge, and `"min"` / `"none"` are accepted.
- The time window makes room for ghost / replica satellites down to `grid.satelliteLevel` (default 1e-8): the
  longest delay at which the satellites of all elements together (combinations included, coincident delays summed
  in amplitude) reach that level, instead of every order of every element. 20 AR-coated plates now need about 1 ns
  of window instead of several.

### Added
- **Delayed light** ([docs](docs/reference/delayed-light.md)). **`grid.tail`**: a second, long, linear grid with the same time
  step carries ghosts, echoes, scatter and gas-line ring-down next to the small nonlinear grid; linear elements act on main +
  tail exactly (a smooth split moves light between the two, energy is conserved), nonlinear elements on the main field. Linear
  instruments and the cross-correlator probe see main + tail, the SHG gate sees the main field only; `summary.json -> tail`;
  the loss mechanism `delayed light (tail)` keeps the budget closed. Reproduces one 2 ns grid for water lines (energy 1e-5,
  tail power 2 %) in a fifth of the time for a gap + Kerr plate.
- **Window planning**: a manual window that is too small now warns by name (element or pedestal, delay, relative
  intensity in dB, energy, where it wraps to, the window that would hold it); `grid.extendForSatellites` grows it;
  `grid.strict` turns the warning into an error.
- **Mirror substrate echoes**: `mirror.substrate` (catalogue names `FS-6.35mm` ..., the file's `substrates`, or inline
  material / thickness / back coating or reflectance / wedge): the exact transfer r + t^2 r_b exp(i 2 n(w) w d cos(theta)/c)
  with Stokes reciprocity, so the delay is 2 n_g d cos(theta) / c (61.98 / 92.7 / 123.96 ps for 6.35 / 9.5 / 12.7 mm of
  fused silica) and the leakage follows the coating's T(lambda).
- **Coating ripple** (`ripple`): random phase / amplitude ripple whose pedestal lies within +-`maxDelayS` (5 ps).
- **Delayed scatter** (`delayedScatter` on mirrors, interfaces and plates): a fraction of the power returned incoherently
  with a uniform / exponential / table delay kernel and a lambda^-n weight; plates default to 0 .. 2 n_g d cos(theta)/c.
- **`environment.air.applyToGaps`** (off by default): the system's temperature, pressure and humidity are the "Air" of
  every gap, with a per-gap `air` override and a run note. Plain Ciddor reproduces the built-in Air.
- **Detector**: `saturationLevel` (detector, cross-correlator) and cross-correlator `segments` for attenuation-range
  stitching (generic, user configured).
- **HITRAN plugin** (`plugins/hitran`, [docs](docs/plugins/hitran.md)): the air index model `"kind": "hitran"`: Voigt lines of H2O,
  CO2 and O2 with temperature scaling and pressure shift, absorption and dispersion from one complex line shape
  (Kramers-Kronig consistent), Python builders and `python -m ultrafast_hitran download` (HITRAN API). Compiled into the
  NativeAOT library like the GPU plugin (`UF_WITH_HITRAN`). Data are not shipped.
- Core: `ISmoothIndex` (index models with narrow features offer a smooth part for the propagation frame), a material whose
  index model is also an `IAttenuation` uses it for both, `Substrates` in the system file, Python `build.substrate`,
  `coating_ripple`, `delayed_scatter`, `delay_*`, `air`, `environment`, `grid`, `tail_grid`, `detector_segment`.
- **Follow-ups of the delayed-light work.**
  - *2-D plate*: `transverse.initialProfile.noise` / `ripple` seed small-scale self-focusing on cartesian grids; the spatial element reports
    `fluenceContrastIn` / `Out`, `smallScaleGain`, `peakPowerOverCritical` and fluence cuts; validated against Bespalov-Talanov growth
    (`SmallScaleTests`); example 29 (128 x 128, P / P_cr = 96).
  - *Tail*: smooth elements (dispersion, flat loss, bare surfaces ...) act on main and tail separately instead of the exact long-grid step
    (`LinearTerm.IsSmooth`, `grid.tail.exactSteps` to force the exact step); first-order cross-phase of the tail by the main pulse in Kerr
    elements (`grid.tail.crossPhase`).
  - *Surface scatter*: `delayedScatter.model = "surface"`: roughness and correlation length against the mode radius (TIS, in-mode share
    `1 - exp(-(l/w)^2)`, lambda^-2).
  - *Detector*: `detector.response` (spectral response table), `counting: "photons"`, and reusable **presets** (`detectors` in the run spec or a JSON
    file; no instrument numbers built in). Python: `build.detector`, `detector_response`, `detector_preset`, `initial_profile`, `profile_noise`, `profile_ripple`.
  - *HITRAN*: TIPS partition sums (written by the downloader, per isotopologue); the downloader skips isotopologues without lines; CO2 and O2 bands and H2O at
    250 - 350 K validated against HAPI's Voigt absorption (`plugins/hitran/tools/make_hapi_reference.py`, opt-in test): integrated within 0.7 %.
- **GPU plugin** (`plugins/gpu`): compute back-ends `cuda` (NVIDIA driver API, kernels compiled at run time by NVRTC,
  cuFFT / cuBLAS when found) and `opencl` (any double-precision OpenCL 1.2 device). One kernel source serves both.
  On the device: 1-D envelope propagation (scalar / vector Kerr with the coherent coupling, scalar / vector Raman,
  self-steepening, z-dependent beam area) and radial / cartesian spatial propagation (the Hankel transform as one
  matrix product over all frequencies, batched FFTs; envelope or UPPE coupling), with the same ERK4(3)-IP / RK4IP
  schemes, step control and B-integral as the CPU; results agree with the CPU to rounding (~1e-15 in the
  self-test). Unsupported elements (user responses, rotational Raman, position-dependent properties, carrier-resolved,
  χ(2), gain) stay on the CPU with a note; a device error falls back to the CPU. Concurrent runs (studies, fits)
  get one stream / queue each (`compute.deviceSlots`). No NuGet packages; vendor libraries load at run time, so the
  plugin loads everywhere. Docs: [GPU](docs/plugins/gpu.md).
- `"compute": {"backend": "auto"}`: each nonlinear element goes to the fastest device back-end that supports it and
  is large enough (`gpuMinPoints`, default 8192 samples 1-D, 32768 spatial), else to the CPU. New `compute` keys:
  `fft`, `device`, `gpuMinPoints`, `deviceSlots`. Process default: `ULTRAFAST_BACKEND`, `ultrafast run --backend`,
  `uf.set_default_backend`, `uf_set_default_backend`.
- `ultrafast devices [--selftest] [--benchmark]`, Python `uf.devices()`, `uf.benchmark()`, `uf.set_default_backend()`,
  builder `b.compute()` (and `run_spec(compute=...)`), C `uf_devices_json`, `uf_set_default_backend`, commands
  `devices` / `compute.default`; the NativeAOT library has the gpu plugin compiled in. C++ example `devices.cpp`.
- **Native CPU FFT providers**: Apple Accelerate (vDSP) on macOS, FFTW 3 on Windows / Linux / macOS when installed
  (or Intel MKL through `ULTRAFAST_FFTW_LIBRARY`), chosen with `"compute": {"fft"}` or `ULTRAFAST_FFT` (default
  `auto`); each is checked against the managed FFT before use.
- **Release packaging**: `build.sh` / `build.bat` (`build/package.py`, standard library only) compile and lay out
  release folders without source code for any of win-x64, win-x86, win-arm64, linux-x64, linux-arm64, osx-arm64,
  osx-x64 or portable: the self-contained `ultrafast` CLI, a Python wheel (hosted engine for every OS, or the native
  engine for one platform), the C API (header, NativeAOT library, CMake example), C# libraries (net10.0, net48) and
  the OpticStudio extension, each with only the plugins chosen (`--plugins fitting zemax | none`), a README, the
  schemas and a SHA-256 manifest. ZOS-API assemblies are never packaged. Docs: [Releasing](docs/releasing.md).
- **Claude Code setup**: `CLAUDE.md` (project memory, imports `AGENTS.md`), folder `CLAUDE.md` files for the engine,
  tests, Python, plugins and docs, and `.claude/` with project commands (`/test`, `/regen`, `/verify`,
  `/physics-change`, `/new-option`, `/bug-report`, `/release`, `/example-check`), subagents (`test-runner`,
  `physics-reviewer`, `docs-sync`), skills (`engine-physics`, `run-simulations`) and hooks that block edits to the
  ZOS-API assemblies, generated files and build output. Docs: [Claude Code](docs/claude-code.md).
- **Vector Raman response** (Hellwarth tensor: isotropic h_a and anisotropic h_b parts) for elliptical and circular
  light, in the 1-D and spatial models. `anisotropicFraction` on every delayed-response model; fused silica
  (`silica`, `blowWood`, `hollenbeck`, the built-in FusedSilica) defaults to 0.25, `linAgrawal` uses its own
  fc ha + fb hb. The Raman response now contributes to nonlinear ellipse rotation ((1 − fR)/3 + fR b/2 of B sin 2χ for
  long pulses) and circular light sees (1 − fR) 2/3 + fR (1 − b/2) of the linear index. Linearly polarised and scalar
  runs are unchanged. New tests `NerTests` check the analytic NER results.
- **Measured input spectra**: `background` (a number, `"auto"` = median of the darker edge, robust to a spectrum
  cut off on one side, `"min"`) and `threshold`; rows are sorted, repeated wavelengths merged and non-finite rows
  dropped, each reported in the notes; a floor above 0.3 % of the peak that is not subtracted gives a warning.
  `PulseSpec.Prepared()` / `MeasuredSpectrumPreparation` expose the cleaned spectrum.
- Python: core `b.measured(...)` (with `match_resolution`, `x_unit`, `y_unit`, `simulated`),
  `b.measured_spectrum(..., background=, threshold=)`, `b.measured_spectrum_csv(...)`, `Result.comparisons`,
  `Result.comparison(name)` (also on results folders), module `ultrafast.measured` (typed `Comparison`, `curve`,
  `trace`, `total_chi2`), `uf.plot(res, "measurements")`.
- Docs: [Measured data](docs/reference/measured-data.md) (input spectra, cleaning, background, comparisons, range and
  resolution, file formats, Python); nonlinear.md section on polarisation, circular light and NER; `grid.satelliteLevel`.

### Fixed
- The NativeAOT library is named `libultrafast.so` / `libultrafast.dylib` on Linux / macOS (it was `ultrafast.so`,
  which the Python package and CMake's `find_library` did not find); the Python loader accepts both names.
- Fits count only the compared points (not those outside an instrument's range) in the point count and the
  reduced χ².
- A measured input spectrum with a repeated wavelength (common in digitised curves, together with rows out of order)
  turned every result into NaN with only a "could not size the grid" warning. Rows are now sorted and repeated
  wavelengths merged; a non-finite input energy stops the run with a clear message.
- The B-integral reported for elliptical and circular light left out the coherent term of the χ(3) polarisation
  (5/6 instead of 2/3 of the linear value for circular light). It is now the peak of the power-weighted effective
  index change over all polarisation terms. The propagation itself was correct.

### Added (earlier in this release: fitting plugin stage 2, fibres, instruments)
- **Fitting plugin, stage 2: `transform` elements.** Fittable physical changes to the beam, inserted where they
  happen. Operators: `spectralPhase` (Taylor and not-a-knot spline knots), `spectralAmplitude` (Gaussian /
  super-Gaussian, tilt in dB/nm, spline, any filter model; passive; `causal` minimum phase from the Kramers-Kronig /
  Bode relation), `energy`, `delay`, `jones`, `replica`, `phaseNoise`, incoherent `pedestal` (ASE / exponential) and
  `wavefront` (Zernike and pointing on the transverse profile). Consecutive linear operators compile into engine
  linear terms (grid planner, loss budget). Any number can be a fit object `{value, fit, min, max, prior, scale}` or be
  named in the operator's `fit` list with `bounds`, `priors` and `smoothness`. Other plugins add operator kinds
  with `TransformOperators.Register`.
- **Fits** (`"fit"`): marked transform numbers are collected automatically, and numbers can also be given by path,
  including `instrument:NAME/...` (instrument-response parameters such as resolution or calibration). Replaceable
  estimators: `levenbergMarquardt`, `cmaes` (global, polished by LM), `mcmc` (affine-invariant ensemble; credible
  intervals) and plugin estimators via `Estimators.Register`. Fits support Gaussian priors, knot smoothness and
  parallel evaluations. `fit.json` gains `layers` (simulated → inferred → believed), `influence` (Fisher-information
  share per instrument and per prior), `provenance` (versions, options, SHA-256 of every data file, parameter
  origins) and `dataChi2Final`. New output files: `fitted-system.ufs` (fit objects replaced by the fitted numbers,
  ready for new inputs) and `fitted-run.json`. New arrays: `<name>_nominal_on_measured`.
- **Backward propagation** (`"backward"`): the pulse is the field measured at `from`, and the run finds the field at
  every plane up to `to`. Linear terms use a Tikhonov-regularised Jones inverse; nonlinear segments are integrated
  with z reversed; transforms are inverted; active elements are refused, linearised from a forward reference run, or
  passed through. Instruments work at every plane, and the field is written to `reconstructed-pulse.json` as a
  pulse object.
- **Measured data**: `"scale": "log"` compares in dB (`sigmaDb`, `floorDb`) for contrast and cross-correlator data;
  data columns in dB are read; the cross-correlator can be compared; `resolutionM` / `resolutionS` state the
  recording's resolution and are checked against the simulated instrument.
- **Instrument `axis`** (every curve instrument): readings at explicit points or on a from / to / step grid, pixel or
  bin integration and a Gaussian resolution; dB curves follow their linear counterparts. Settings echo the axis.
- **Measured input spectra**: `resolutionM` (recording resolution), `deconvolve` (Richardson-Lucy),
  `extrapolate: "gaussian"` (tails fitted to the data), and a description of the measured range in the run notes.
- Schema: numbers may be declared `Fittable` (number or fit object). The run spec keys `fit` and `backward`, and the
  `transform` element, give a clear message when the fitting plugin is missing.
- `Executor.MeasureInstruments` (public): run the instruments on recorded pulses (used by run modes).
- Python: `ultrafast_fitting.transform` with every operator builder, `fit_value`, `fit(estimator=..., **options)`,
  `backward`, `measured(scale=..., resolution_m=...)`, `fitted_system`, `reconstructed_pulse`; `b.axis`,
  `b.measured_spectrum`. Figures: the fit figure shows credible intervals and the influence map; measurement figures
  show the simulated layer.
- Example 26 (transforms fitted to a CPA line, reused on a new seed, backward propagation to the seed); docs:
  rewritten fitting-plugin guide, new [Installing plugins](docs/plugins/installing.md) page, instrument `axis`
  section; README note on AI assistance.
- **`fiber` element** (`fibre`, `waveguide`): Taylor betas (SI or ps^k/km), dispersion curves D(lambda), or a material;
  gamma (constant or an expression in z) or n2 + effective area; loss (flat or vs wavelength); flat gain and gain
  spectra scaled to a target output; reports soliton number, dispersion / nonlinear / fission lengths and the
  dispersive-wave wavelength for the pulse that enters it. Python `b.fiber`. Docs: reference/fibers.md.
- **Raman models** `linAgrawal` (Lin & Agrawal 2006, fR 0.245) and `hollenbeck` (Hollenbeck & Cantrell 2002, 13 modes);
  `blowWood` alias of `silica`.
- **Quantum noise** on the input (`pulse.noise`: one photon per mode) and a **coherence study**
  (`"mode": "coherence"`, `uf.coherence`): |g12(lambda)| over independent noise realisations.
- **Instrument device model**: every instrument accepts a spectral `filter` and a `detector` (calibration, noise,
  noise floor / dynamic range, bit depth, seed); autocorrelator `resolutionS` and `shgAcceptanceFwhmM`; FROG and
  d-scan `spectralResolutionM` and `shgAcceptanceFwhmM`; spectrometer `wavelengthOffsetM`; polarimeter
  `extinctionRatio`. Every instrument output carries a `settings` block with each parameter's value in effect.
- **Ghosts** of the end faces of `bulk`, `crystal`, `gain`, `amplifier` and thick `lens` elements.
- Example 25 (PCF supercontinuum benchmark with coherence); docs: capabilities and limitations page, rewritten
  README and overview, forward-looking roadmap (history in docs/design/history.md).

### Fixed (earlier in this release)
- Plots no longer cut off data: dB axes (spectra, contrast, cross-correlator, probes, comparisons) follow the data's
  own floor (instead of fixed -50 / -70 / -100 dB). The contrast panel shows the whole window, the cross-correlator
  has its own dB panel, and contrast comparisons use a log scale with a dB residual.
- Fit figure: values in nm, fs², dB and dimensionless units are no longer given SI prefixes ("1.03 knm").
- Spectrometer `normalize`: the schema declared "none" | "peak" | "area" while the instrument read a boolean. The
  instrument now implements all three ("none" = spectral energy density in J/m), keeps true / false as legacy forms
  (declared in the schema), and defaults to "peak" as before.
- Declared defaults now match the code: d-scan `steps` (101), and the defaults of points, grid caps and delay-scan
  ranges that were undocumented.
- The Raman kernel is normalised on the time grid (the delayed fraction is exactly fR at any step); fibre results now
  agree with pyNLO to better than 0.3 %.
- The grid planner ignores the input's quantum noise.
- `ultrafast study` prints long analysis arrays as their length (they are in the results folder).
- Source comments and docs point to the current documentation paths; `pip install` instructions reflect the
  from-source installation.

## 2.1.8-beta (2026-10) -- Stage 1 complete: the stable core

Version jump: 2.1.8 marks Stage 1 sub-stages 1.2-1.8 on top of 2.0.0-alpha.2. File formats are unchanged (version 2);
plugins declare `"engine": "2.1.8-beta"`.

### Behaviour changes (check your results)
- **Air and N2: n2 is now the electronic part only** (N2 8.0e-24, air 8.5e-24 m²/W per bar at 273 K; before: 1.1e-23
  for N2, air scaled from it). The rotational Raman response, which was lumped into n2 before, is a delayed response
  of its own and on by default for N2, O2, CO2, H2 and air (`"rotationalRaman": false` in a material switches it
  off). Long pulses (≫ 100 fs) see about the same total; short pulses see less nonlinearity than before.
- The grid planner estimates the B-integral of each nonlinear element with the peak power *after* the dispersion
  before it (a stretched pulse in an amplifier no longer forces a huge grid).
- A Gaussian `inputBeam` now describes the beam at the system input and is tracked through the system
  (`physics.beamTracking`: `auto`); before, it was copied unchanged to every element without a `beam`, with z
  measured from each element's start. Use `"beamTracking": "off"` for the old behaviour.
- Study rows (`study.csv`) are in the order of the points, also with `parallel` > 1.

### 1.2 Optical components
- Gaussian beam tracking (reduced q-parameters x / y) through every element; tracked radius and curvature in each
  element's `values`; used by nonlinear bulk, plates, crystals, spatial elements, surfaces (fluence, clipping), cameras.
- Off-axis curved mirrors (f_t = R cos θ / 2, f_s = R / 2 cos θ), astigmatic lenses (`focalLengthXM` / `YM`).
- Aberrations (`aberration`: Zernike in Noll order, random figure) on mirrors, surfaces and lenses; `wavefront`
  element; Maréchal Strehl estimate on 1-D beams.
- `plate` / `window` with ghost replicas (delay, intensity, mode overlap from walk-off and wedge); `ndFilter`
  (absorptive / reflective, OD or OD(λ)); `thinFilmPolarizer` / `tfp` (TMM quarter-wave stack at its angle);
  `faradayRotator` (Verdet dispersion, non-reciprocal); thick multi-order, zero-order and compound waveplates with
  birefringent materials (Quartz, MgF2, Sapphire, Calcite, YVO4, aBBO, TGG approximate).
- GDD ripple of chirped mirrors (`rippleGddFs2`, complementary pairs) and the `gddRipple` coating kind;
  `chirpedMirror` accepts `gddFs2` / `todFs3` / `fodFs4`.

### 1.3 Contrast and polarisation
- `pulse.pedestal`: replica / pre- / post-pulse, ASE, spectral phase noise, exponential pedestal.
- `crossCorrelator` (`thirdOrderCorrelator`) instrument: third-order correlation with noise floor, fine/coarse scan,
  resolution, SH acceptance, polarisation, pre/post contrast.
- Vector (Jones) fields on radial and cartesian grids (`transverse.polarization`); polarisation elements act on
  carried profiles.

### 1.4 Gases
- Rotational Raman of N2, O2, CO2, H2 and air (isotropic and anisotropic tensor response, pressure and temperature).
- refractiveindex.info reader (formulas 1-9, tabulated n / k / nk, SPECS scaling), 20 bundled pages, user library
  (`$ULTRAFAST_HOME/materials`), `ULTRAFAST_RII_PATH`; `ultrafast materials list|show|import`.

### 1.5 L3 propagation
- UPPE coupling on spatial grids (`transverse.equation: "uppe"`).
- Photo-ionisation (PPT with Coulomb correction, ADK, Keldysh, MPI) and Drude plasma (avalanche, recombination,
  ionisation losses); `ionization` on elements or `physics.ionization`; electron density reported.

### 1.6 Speed and studies
- Mixed-radix (2-13) and Bluestein FFTs for any length (cartesian grids of any even size); SIMD complex kernels.
- `IComputeBackend` (`run.compute`), plugin registration; CPU back-end built in.
- Parameter studies (`ultrafast study`, `uf.study`): grid / random / LHS / Sobol scans, Nelder-Mead optimisation,
  local and Sobol sensitivities, frozen grid, prefix caching, parallel batches.

### 1.7 Pulse retrieval
- `IRetrieval`: PCGPA and ePIE (FROG), d-scan; `ultrafast retrieve` (also straight from a results folder's
  `result.npz`, `--instrument` to pick the trace), `uf.retrieve`; retrieved pulse usable as a run input.

### 1.8 Consolidation
- `uf_command` C export, `uf.command`; Python `ultrafast.types` (TypedDicts generated from the schemas; builders
  return them, so editors complete and check element keys) and a generated API reference.
- Docs reorganised: reference pages in `docs/reference/`, examples in `docs/examples/`; new pages: components, gases,
  ionisation, retrieval, studies, speed, Python API, for AI agents; tutorial 5 (contrast); `docs/assets/`
  (synthetic measurements with an answer key); `AGENTS.md`, `llms.txt`.
- Examples 22 (contrast chain + study), 23 (components and beam tracking), 24 (air focus with rotational Raman,
  ionisation and FROG retrieval).

## 2.0.0-alpha.2 (2026-10)

### Stage 1.1: architecture -- plugins, option registry, schemas, unified camera (2026-10-16)
- **Plugin system** (`UltrafastEngine.Plugins`): `IPlugin` + `plugin.json`, discovery (`ULTRAFAST_PLUGIN_PATH`,
  `plugins/` next to the engine, the source checkout, `~/.ultrafast/plugins`), `ultrafast plugins
  list|paths|install|remove`, Python `uf.plugins`. Extension points: element types, instrument kinds, models,
  instrument hooks, run modes, result sections and files, CLI commands and reports, spec keys; plugin Python modules
  register figures. Plugins load into the engine's own load context (works under Python's hostfxr host); NativeAOT
  hosts register them in code.
- **Fitting moved to a plugin** (`plugins/fitting`): `"measured"`, `"fit"`, `fit.json`, the CLI reports, and the
  Python module `ultrafast_fitting` (builders `measured`, `fit`, `fit_parameter`; the `measurements` and `fit`
  figures). `RunResult.Comparisons` / `Fit` became `result.Comparisons()` / `result.Fit()` (plugin extension methods).
- **Zemax moved to a plugin** (`plugins/zemax`): sampling library, the `zemax-build` / `zemax-replay` / `zemax-demo`
  commands, the OpticStudio program in `tools/net48`, the ZOS-API stubs.
- **Option registry** (`UltrafastEngine.Schema`): every element, instrument, model kind and spec section declares
  its keys (type, unit, default, meaning) once. From it: JSON Schemas `schemas/ufs.schema.json` and
  `schemas/run.schema.json` (`ultrafast schema`; `"$schema"` gives editor completion), unknown-key / type warnings
  with "did you mean" suggestions on every run (`ULTRAFAST_STRICT_OPTIONS=1` makes them errors), and the generated
  [option reference](docs/reference/options.md) (`ultrafast reference`).
- **Format version 2** with automatic migrations (`ultrafast migrate`): v1 cameras become the unified camera.
- **One `camera`**: `imaging` (`near`, `far`, `relay` through thin lenses and gaps; `propagateM`, `focalLengthM`,
  `magnification`) + `sensor` (pixels, pixel size, quantum efficiency as any filter model, bit depth, full scale,
  read noise, background, hot pixels; raw `counts` array). `nearFieldCamera` / `farFieldCamera` remain as aliases.
- **Unique instrument names**: repeated or missing names get `_2`, `_3`, ... (warning when a given name repeats).
- All examples carry `"$schema"` and version 2. Docs: reference/plugins.md, reference/schema.md,
  reference/options.md (generated), plugins/fitting.md (was measurements.md; sampling moved to instruments.md),
  plugins/zemax.md; camera section rewritten.
- Tests: `PluginTests` (plugin element + instrument, discovery, install/remove, versions, unique names, unified
  camera incl. a 2f relay), `SchemaTests` (all examples use declared options, typo suggestions, schemas cover every
  element and instrument, committed schemas and reference are current, migrations); Python `test_schema.py`
  (examples validate against the schemas; typos caught). The whole C# and Python suites pass in strict-options mode.

## 2.0.0-alpha.1 (2026-10)

### Phase 2.5: sampling, measured data, calibration (2026-10-14)
- **Sampling** (`"sampling"` on any non-imaging instrument): what part of the beam the instrument sees -- near
  field (optionally after `propagateM`) or far field (focal plane of a lens of `focalLengthM`), through a
  `pinhole`, `iris`, `fibre` (with an NA check), `slit`, `point` or the whole beam (`integrated`), centred anywhere.
  The aperture's spectral transmission and averaged phase are applied before the instrument measures; reports
  `sampling` and `samplingTransmission`. Uses the carried transverse profile, or the Gaussian beam model in 1-D.
- **Measured data** (`"measured"` on any instrument): CSV (header, units from column-name suffixes such as `_nm`,
  `_fs`, `_ps`, `_mm`, `_fs2`, `_uj`), NPZ (float/int/bool, compressed or not, C/Fortran order) or JSON scalar
  readings. Curves (spectrometer, autocorrelator, scope, beam profile, delay scan, phase-matching curve), traces
  (FROG, d-scan, cameras) and values (any instrument scalar). Background (`auto`: median at the axis edges),
  normalisation (`peak` with a least-squares data scale, `maximum`, `area`, `scale`, `none`), `rangeX`, σ per point
  or relative, weights, explicit column mapping, and a generic `simulated` mapping for any instrument.
  `summary.json → comparisons[]` / `measuredChi2`, arrays `<name>_measured_*` and `_simulated_on_measured`; a bad file
  is a warning, not a failure.
- **Calibration fit** (`"fit"` in the run spec): Levenberg-Marquardt on scaled, bounded parameters addressed by path
  (`pulse/...`, `element:ID/...` including list indices, `material:NAME/prop` as an override, `run/...`,
  `system/...`); grid frozen at twice the planned bandwidth; covariance from a pseudo-inverse (scaled by the reduced
  χ² when > 1); warnings for strong correlations, unidentifiable combinations, parameters without effect and
  values at a bound. **Predictions** with 1σ for every instrument value (`<value>Sigma`) and per-element pulse
  metrics, and 1σ bands on the compared curves. `fit.json`, `summary.json → fit`; the CLI prints the parameters and
  per-instrument χ² / rms before → after; `uf.run` and `uf_run_execute` fit when the spec has `"fit"`.
- CLI prints a comparison table for runs with measured data; fit runs report their total elapsed time.
- `ultrafast-plot`: `measurements` figure (data vs simulation with residuals, trace differences, scalar readings
  in σ) and `fit` figure (parameters ± σ, cost per iteration, correlation matrix, χ² per instrument).
- Python builders `b.sampling`, `b.measured`, `b.fit_parameter`, `b.fit`, `run_spec(fit=)`.
- Pulse chirp and the `dispersion` element accept `gddFs2` / `todFs3` / `fodFs4`; the NPZ reader handles every
  common dtype and npy v2/v3 headers.
- Example **21** (recorded fibre spectrum, autocorrelation, d-scan NPZ and power reading: compare, calibrate three
  unknowns, predict unmeasured instruments). Examples 11 and 17 gained sampled instruments (pinhole, slit, fibre in
  the far field). New `examples/tutorial` files.
- Docs: **four tutorials** (first run and the CLI; diagnostics and sampling; a CPA + SHG chain with overrides and
  Python; your own measured data and fitting), `measurements.md`; instruments, run-spec, results, python, index,
  getting-started updated.
- Tests: 13 C# (aperture transmissions vs Gaussian analytics, far-field fibre colour, CSV/NPZ units, zero
  residual on self-generated data for curves / traces / values, normalisations and background, missing files,
  pseudo-inverse null space, fit paths, fit recovery of energy and chirp, unidentifiable parameter) and 4 Python.

### Phase 2.3: oscillators, saturable absorbers, broadband χ(2), ASE, thermal lens to OpticStudio (2026-10-13)
- **`oscillator` element** (replaces the incoming beam): `parametric` (given output pulse), `haus` (lumped master
  equation: saturated gain with spectral shape, loss, GDD/TOD, SPM, saturable absorber) and `cavity` (a real gain
  medium with cw pump and average-power inversion dynamics, plus any engine elements run every round trip --
  chirped mirrors, SESAMs, Kerr media, apertures, lenses, spatial grids). Start from quantum noise or from the
  run's pulse; convergence detection; iterates on `gridPoints` of the run's window and embeds the result exactly.
- Diagnostics: soliton area theorem (`solitonFwhmS`), multi-pulsing and cw-background detection, Q-switched
  mode-locking criterion (Hönninger), round-trip GDD/B, timing offset; evolution arrays and an `oscillator` figure
  (build-up, pulse and spectrum vs round trip).
- **`saturableAbsorber` element** (`sesam`): slow (recovery time, saturation fluence), fast and Kerr-lens loss
  modulation. **Apertures act on carried transverse profiles** (hard circle/rectangle, soft Gaussian / super-Gaussian).
- **Broadband χ(2)** in the carrier-resolved mode (`timeResponse` kind `chi2`, optional QPM period), with a dealiased
  drive that conserves energy at high conversion.
- **ASE** in 1-D gain and amplifier elements (`"ase": true`).
- **Thermal lens to OpticStudio**: gain elements record the lens as a Gradient-1 GRIN; `UltrafastEngine.Zemax
  thermal-lens` writes it into a surface (typed ZOS-API `ISurfaceGradient1`).
- Expression time responses see `x`, `y`, `r`, `theta`, `spatial` and the running fluence `F`.
- Python: `ultrafast.laser.Oscillator` builder, `b.saturable_absorber`, `b.soft_aperture`.
- Fixes: d-scan panels with a glass insertion axis; circled instrument numbers above 10 (font); the carrier-resolved
  energy diagnostic counts only the propagated (positive-frequency) half; amplifier/gain output pulses carry the
  element's repetition rate (pulse picker); ZOS-API stubs complete again (environment data).
- Examples 17 (every instrument), 18 (your own n(x, y, z, t, I, F)), 19 (KLM Ti:sapphire, Haus, from noise),
  20 (SESAM Yb:YAG oscillator → stretcher → amplifier → compressor). Docs: oscillators.md; nonlinear, transverse,
  gain, zemax, models, python updated.
- Validation: soliton area theorem (Haus and real cavity), cw gain clamping, SESAM saturation, fast absorbers,
  lens-guide cavity eigenmode, aperture transmission, χ(2) low-conversion formula and energy conservation, ASE
  budget, position/fluence variables, Gradient-1 export.

### Phase 2.2: gain media, amplifiers, position-dependent properties (2026-10-13)
- **Position-dependent material properties** on any element: `indexProfile` (Δn), `absorptionProfile` (α) and
  `n2Scale` as functions of x, y, z, r, θ -- number, expression, Gaussian / super-Gaussian, table (r, z, rz, xy,
  xyz) or callback (`"@name"`: Python `fields=` / `Run.field`, C# `FieldCallback`, C `uf_run_set_field_callback`).
  Applied point by point on spatial grids (with step control), beam-averaged in 1-D. `ResponseContext` gained
  `X`, `Y`, `R`, `Theta`, `Spatial`, so custom n(t, I) responses can also depend on position.
- **Gain media**: Yb:YAG, Ti:sapphire, Nd:YAG library (McCumber-linked cross sections for quasi-three-level media)
  and user media (`base` + overrides, Gaussian bands, tables, expressions, callbacks).
- **Pump and inversion**: cw or pulsed pumps (top hat / Gaussian / super-Gaussian, single or double pass, delay),
  rate equations between pulses with pump bleaching, or the inversion given as a fraction or small-signal gain;
  doping profiles.
- **`gain` element**: spectrally resolved gain with Frantz-Nodvik-exact saturation per slice and time-ordered
  saturation (arrival-ordered spectral groups -> CPA red shift), host dispersion and SPM; 1-D or spatial (per-point
  inversion, diffraction, gain guiding).
- **`amplifier` element**: multipass (`betweenPasses` loss / GDD / TOD / materials) and regenerative
  (`roundTrips` N or `"auto"`, cavity loss / dispersion / materials).
- **Pulse-train steady state** at the repetition rate (energy model, convergence and period-doubling detection;
  the last pulse with the full model), **thermal lens** from the heat load (radial heat equation; applied as Δn(r, z)
  in spatial gain, as a thin lens on a carried profile in 1-D, reported by amplifiers).
- Element arrays in `result.npz` (`el_<id>_<name>`: gain spectrum, inversion, build-up, train). `gain` figure
  in `ultrafast-plot`; gain / amplifier glyphs (with pump arrow, ×N) in the system diagram. Python builders
  `b.pump`, `b.gain`, `b.multipass_amplifier`, `b.regenerative_amplifier`.
- Examples 13 (Yb:YAG multipass CPA, 1.46 mJ at 10 kHz), 14 (Ti:sapphire regen, 1.8 mJ, 42 fs), 15 (spatial
  gain + thermal lens with cameras), 16 (GRIN lens four ways). Docs: gain.md, spatial-properties.md.
- Validation: Frantz-Nodvik, small-signal gain, cw rate-equation steady state, thermal-lens focal lengths,
  regen growth (G0²(1-L))^N, auto ejection, time-ordering red shift, train vs full model, energy budgets,
  GRIN focus, exact Gaussian absorption, field kinds agree, n2 scaling.

### Diagnostics and plotting (2026-10-13)
- Beam cameras: `nearFieldCamera` (pixels, pixel size, magnification, refocus `propagateM`, band-pass, bit depth,
  read noise), `farFieldCamera` (focal plane of a lens by exact Fourier/Hankel integration; divergence, Strehl),
  `wavefrontSensor` (wavefront map, Zernike fit, radius of curvature). Ideal-Gaussian fallback on 1-D elements.
- Virtual instruments, flagged `"virtual": true`: `wigner` (Wigner distribution or spectrogram), `fieldProbe`
  (phase, instantaneous wavelength, GD/GDD vs wavelength, CEP, real field), `spaceTime` (I(r, t), S(r, λ),
  pulse-front delay, spatial chirp).
- `ultrafast-plot`: `system` figure (schematic with carrier-coloured beam, aux beams, by-products, probes and
  numbered instruments; repeated units collapsed), the schematic at the top of `overview`, instrument panels in
  system order titled with their number and location, image panels for cameras/wavefront/Wigner/space-time.
- Element records list the aux beams they took in (`elements[i].aux`).
- Fix: beam radius and M² of a carried profile were averaged with equal weight over its frequency samples (so a
  spectrally broadened beam's radius was biased towards the SPM wings); they are now energy-weighted. The Kerr
  numbers in example 11 changed accordingly (the plate exit radius is 98 um, not 61 um).
- Example 02 has diagnostics at the input, after the stretcher and at the output; example 11 has cameras, a
  wavefront sensor and a space-time map.

### Phase 2.1: active elements, spatial models, χ(2) crystals
- **Active elements** (`IActiveElement`): one main beam in, one out; aux beams (`pulse`, or `from` a previous run's
  results, `delayS` or `"auto"`), by-products (`res.At["id#field"]`, `probe_<id>_<field>_*`), signed energy budget
  (`energyBudget`; aux energy negative), carrier changes with exact resampling onto a re-planned downstream grid.
- **`crystal` element**: SHG, SFG, OPA, DFG in BBO, LBO, KTP, KDP, LiNbO3, MgO:LiNbO3, BiBO, AgGaSe2 or user crystals;
  uniaxial and biaxial eigenmodes, walk-off, deff from the d tensor; types I/II/0; `cut: "auto"`; non-collinear and
  magic angle; QPM (averaged, or domain-resolved with the integrator landing on every wall; chirped gratings);
  exact dispersion of every field; χ(3) SPM/XPM; noise seeding; 1-D overlap models `gaussian` / `planeWave` / `areaM2`;
  `spectralMargin` with an absorbing spectral boundary and a warning when energy reaches it.
- **Phase-matching calculator**: `ultrafast crystal`, `uf.crystal.phase_match`, `uf_crystal_phase_match`.
- **Spatial models**: `transverse: radial` (Hankel) / `cartesian` (FFT) for bulk, gap and crystal elements; the transverse
  profile travels with the beam (lens, curved mirror, free space); beam radius and M² per element.
- **User elements** (`type: "user"`) in Python (`Run.element`), C# (`ElementCallback`) and C (`uf_run_set_element_callback`).
- Instruments: `conversion`, `phaseMatchingCurve`, `delayScan`, `beamProfile`. Plotting: signed energy budget,
  `byproducts` figure, panels for the new instruments, carrier / beam radius / M² per element; mixed input/output grids.
- Examples 08 (SHG in LBO), 09 (NOPCPA in BBO), 10 (DFG in MgO:PPLN + pyNLO comparison), 11 (self-focusing),
  12 (Python pulse shaper). Docs: crystals, spatial models, active elements.
- Validation: Armstrong SHG, cosh² OPA gain, Manley-Rowe, sinc² detuning, cascaded χ(2) phase, QPM models,
  handbook phase-matching angles, radial vs 1-D, pyNLO three-wave mixing (0.1-0.7 %, also at gain 50).
- Fixes: LBO indices now Kato (1994) (the Chen/Hanson-Dick fit gave 0.8° too large an angle at 1030 nm);
  the grid planner now widens the provisional window for chirped inputs (a strongly chirped pulse could wrap around);
  carried transverse profiles keep their exact energy between frequency samples; QPM with zero carrier mismatch;
  `ULTRAFAST_DEBUG=1` prints stack traces from the CLI.

### Plotting and fixes (2026-10-13)
- `ultrafast-plot` / `python -m ultrafast.plot` / `ultrafast.plotting`: overview, instruments, probes, elements and
  comparison figures (PNG/SVG/PDF + multi-page `report.pdf`) from a CLI results folder or a live Result; no engine needed.
- `uf.load(folder)` opens CLI results with the Result interface.
- Fixed: the ideal compressor could report a result worse than no compressor (its TOD search assumed a single
  maximum); it now scans GDD and TOD on a grid tied to the transform limit and falls back to no compression.
- Phase 2 proposal (gain, χ(2), oscillators): docs/design/phase2-active-media.md; roadmap re-ordered (spatial -> Phase 3).

### Build (2026-10-13)
- Retargeted from .NET 8 to **.NET 10** (LTS); `global.json` requires SDK 10.0.100+. net48 targets unchanged.
- Zemax binding compiles against the real ZOS-API assemblies (`src/UltrafastEngine.Zemax/lib`, git-ignored);
  coating, semi-diameter and environment are now read through the typed interfaces.
- `nuget.config` lists nuget.org (needed only for NativeAOT publishing).
- Application icon for `ultrafast` and `UltrafastEngine.Zemax` (`assets/ultrafast.ico`).

First release of the 2.0 architecture. **Breaking:** v2 run configs and the material catalogue are replaced by
the system file / run spec / export config formats (see docs).

### Architecture
- Zemax-free physics core `UltrafastEngine.Core` (net10.0, net48), no NuGet dependencies.
- C API (`ultrafast.h`) via `UltrafastEngine.Native`, NativeAOT-ready; Python package `ultrafast-engine`; CLI `ultrafast`.
- OpticStudio binding rebuilt on an `IOpticStudio` abstraction (`UltrafastEngine.Zemax.Sampling`), with a fake
  OpticStudio, record/replay of ZOS-API sessions and raw-sample re-export.
- Model registry, overrides with priorities, provenance report.

### Physics (beyond v2)
- Absolute indices (Ciddor air), gases (Borzsonyi), material library.
- Thin-film coatings (TMM), Fresnel, clipping, scatter, LIDT; exact loss budget.
- Jones polarisation with 3D basis transport; waveplates, polarisers.
- Vector Kerr coupling with the coherent term; user time responses; carrier-resolved field mode.
- Adaptive ERK4(3)-IP stepper.
- Instruments (spectrometer, AC/IAC, FROG, d-scan, scope, contrast, polarimeter, energy meter, ideal compressor).
- Grid planner sizes the window from the largest delay spread anywhere in the system and the expected nonlinear broadening.

### Kept from v2 (with fixes)
- Group-delay (group-path) dispersion from traced rays -- exact through gratings.
- Batched wavelength sampling through the WDE with restore; operand-based geometry with matrix cross-check.
- Per-pass nonlinear elements with GBPS/GBPW beam profiles; medium inheritance through coordinate breaks and mirrors.
- Physics sign convention and its directional tests; the v2 18 tests are covered by the new suites.
