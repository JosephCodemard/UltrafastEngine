# Internal roadmap: open engineering work

Internal page ([folder index](README.md)). Open items, with size (focused agent-days: S ≈ 1, M ≈ 2-3, L ≈ 4-6, XL ≥ 7), area, acceptance
criteria and what blocks them. The public summary is [docs/roadmap.md](../roadmap.md). Designs not scheduled are in
[future-plugins.md](future-plugins.md); limitations that are not planned work are in [limitations.md](limitations.md).
Classification: **core** (core public roadmap), **plugin** (separate plugin), **internal** (stays here: engineering with no user-visible promise).

## 0. Current plan: three work packages (from 2026-11-17, one session each)

| WP | contents | status |
|---|---|---|
| WP-1 | design docs consolidated into this folder; engine items from a user's 46-pass MPC report (structured validity and strict switch, zero-frequency false positive, warnings naming every element, pulse- and beam-averaged B, FWHM peak count, radial model through `sameAs` passes); ionisation: negligible-ionisation pre-check and the plasma term on the GPU; QPM sliver steps; dropped `ionization` / `kerrLens` keys on plates; mirror ellipticity test; glass on polarisers; lens thickness note; `uf.scan(fidelity=)` with a run file | done 2026-11-17 ([history](history.md), Stage 11) |
| WP-1b | memory safety (Phase 3 of the 2026-11-17 plan), a session of its own: fitting residual cache (keep scalars, latest and best results), `keepProfiles: used` in fit and study evaluations, a core memory estimate with an optional limit (`compute.memoryLimitBytes`), GPU free memory and concurrent slots, bounded kernel caches, Python `uf.estimate_memory` and pre-flight, `docs/reference/memory.md` | done 2026-11-17 ([history](history.md), Stage 12) |
| WP-2 | Python surface: `ultrafast.calc` (pulse, crystal, cell, beam / mode matching, nonlinear estimates; Lorentzian and asymmetric-exponential TBP shapes; the phase-matching calculator accepting `noncollinearDeg` for `sfg`; no `ultrafast-lite`, decision E7); plotting styles `ultrafast` / `paper` / `science` (decision V1), schematics for branched and sequential systems (loops, routing, glyphs, parameters, deterministic), wavelength-time-intensity 3-D plot, evolution animation | done 2026-11-17 ([history](history.md), Stage 12); the MCP slice moved to WP-2b |
| WP-2b | MCP first slice, a session of its own (decision E8): `ultrafast-mcp` in `python/src/ultrafast/mcp/` (`server.py` hand-written JSON-RPC 2.0 over stdio, `worker.py` persistent sandboxed subprocess killed and respawned on timeout, `policy.py` refusals); tools `describe`, `validate`, `run` (with fidelity; returns the fidelity report, warnings and `validity`), `scan`, `explain_warning`, `plot`; resources (schemas, fidelity table, examples index); `ultrafast_twin serve` starts it (fix its docstring and `plugin.json`); tests (handshake, 6 tools, each refusal, a call over budget killed and the next one works); `docs/agents.md` MCP section with Claude Code / Codex config. Still on the roadmap after it: `compare_to_measured`, `fit`, prompts, agent pack, evals | done 2026-11-18 ([history](history.md), Stage 14): first six tools, resources, worker and policy as planned; the server speaks both protocol eras (2026-07-28 and the `initialize` revisions); `compare_to_measured`, `fit`, prompts, the agent pack and the evals were added on 2026-11-18 ([history](history.md), Stage 15; what is left is in section 4) |
| WP-3 | WP9 slice 1 (`_template.md`, `example.json`, `build/example_lint.py` in CI, wave 1: the 12 S5 examples); release: version 1.0.0 everywhere, CHANGELOG folded into 1.0.0, `release.yml` with version check, tests, `twine check`, TestPyPI / PyPI trusted publishing behind protected environments (decision E4, no sdist E5), pip install docs; public roadmap in three blocks (`docs/roadmap.md` = `ROADMAP.md`); final docs; LaTeX report (not committed) | groundwork done 2026-11-18 ([history](history.md), Stage 14): version 1.0.0, `release.yml` (version gate, `twine check`, TestPyPI / PyPI behind environments), install docs, owner checklist, WP9 slice 1, public roadmap, `build/example_lint.py`; **open:** the owner's PyPI steps ([releasing](../releasing.md#publishing-to-pypi-maintainers-decisions-e4-e5)), the LaTeX report |

## 1. Release to 1.0.0 (needs the owner)

| item | class | size | acceptance | blocked by |
|---|---|---|---|---|
| First run of `release.yml` on a `v1.0.0rc1` tag; fix the fallout (Linux / Windows NativeAOT jobs, wheel in a fresh venv) | internal | M | all jobs green; TestPyPI install in a fresh venv on Linux, Windows, macOS runs the quickstart, a strict example, `ultrafast devices`, the `ultrafast-mcp` handshake | the owner creates the tag, the PyPI / TestPyPI pending publishers and the two GitHub environments ([releasing](../releasing.md#publishing-to-pypi-maintainers-decisions-e4-e5)) |
| Real PyPI upload of 1.0.0 | internal | S | `pip install "ultrafast-engine[hosted]"` works on the three OS | the owner's explicit go-ahead (name `ultrafast-engine` checked free on 2026-11-17, decision E13) |
| GitHub Pages: Settings → Pages → Source: GitHub Actions | internal | S | site deployed from `main` | the owner |
| macOS NativeAOT wheel | internal | S | built on the owner's Mac and attached to the release | no macOS runner for this repository |

## 2. Engine (core)

| item | class | size | acceptance / notes |
|---|---|---|---|
| `outputs.keepProfiles: used` in branched systems (today `Executor.Execute` applies it to sequential systems only) and the instruments' two-dimensional outputs in the memory estimate | core | S | a branched Mach-Zehnder with spatial elements keeps only the profiles an instrument reads; estimate within 2x of the retained memory (`MemoryEstimateTests`) |
| Kerr-lensed beam carried to later elements (today `kerrLens` acts inside one element; the beam leaving it is not carried on) | core | M | run-time beam override after a `kerrLens` element; Herriott cell with a Kerr plate per pass against `cellMode` (same lens per pass) to 1 %; P = 0 bit-identical. Physics change: `/physics-change` |
| Field snapshots along z inside an element (for evolution maps and videos within one fibre or crystal) | core | M | `outputs.zSnapshots: N` stores N fields per element; memory accounted by the memory estimate; snapshot at z = L equals the element output to 1e-12 |
| Carrier frame: move the window frame to the pulse carrier (a dispersive pulse off the lattice drifts by GDD × offset × L); `snapCarrier` still changes the Schulte cell by 1.4 % in bandwidth | core | M | Schulte result independent of `snapCarrier` to 1e-3; 10 fs soliton exact on the default grid |
| Pulses near one cycle (SPM reaching zero frequency): a carrier-resolved path | core | L | the three KNOWN `media.py` checks pass |
| Crystal band gaps from a published optical-gap measurement (BBO, LBO, KDP, KTP, LiNbO₃ are estimated from the UV edge, `unverified`); the plasma term of `Chi2Problem` on the GPU | core | S | the owner supplies the sources; catalogue status `verified`; device parity 1e-9 |
| Built-in metal mirrors (Ag, Au, Al n,k) | core | S | data read from a published table (Rakić et al., Appl. Opt. 37, 5271 (1998), or Johnson & Christy 1972), not from memory; 45° silver: ellipticity of circular light against the Fresnel value |
| Frequency-dependent effective area and a user-set shock time (see [future-plugins](future-plugins.md) C1) | core | M | Lægsgaard form; constant table equals today to 1e-12 |
| Unit-in-key renames with migrations. Proposed: `betas` → `betasSkPerM` (alias `betasPsKm` stays), `dnDT` → `dnDTPerK`, `fieldT` → `fieldTesla`, `tau1` / `tau2` / `taub` → `tau1S` / `tau2S` / `taubS`; instrument axis keys stay (their unit is the axis'). `n2` stays (the physics symbol) | core | M | migrations with deprecation warnings; every example strict; owner approves the names |
| `multiFrog` two-trace case ends in the time-reversed twin in most starts | core | M | initial guess / step order fixed or documented as a limitation (`docs/papers/kida-2014-double-frog/repro_2.py`) |
| CW sources sit on the nearest bin (relative error 2.8e-7) | internal | S | off-bin cw with `snapCarrier: false` |
| A schema for study specs (`schemas/study.schema.json`; the keys `mode`, `sampling`, `parameters`, `outputs`, `cache`, `freezeGrid`, `prefixCache`, `ensemble`, ... are documented in `docs/reference/studies.md` only), generated and checked like the other schemas | internal | M | schema generated from a `ComponentSpec("study")`; the Python `study` / `scan` arguments typed from it |
| Glass in beam splitters (`beamSplitter`, `polarizingSplitter`: the cube's glass on the transmitted and reflected paths) | core | S | per-port glass; today: a `plate` in the arm |
| GPU plasma term in spatial elements and carrier-resolved propagation; exact (series) PPT / Keldysh rates on the device instead of the table | gpu | M | parity 1e-12 for PPT |
| Delayed-response window planning for generic `timeResponse` and gas rotational revivals | core | S | planner window covers the response memory; test with a 10 ps revival |
| Round-aperture decentre | core | S | decentred aperture transmission against the offset-Gaussian integral |
| Non-collinear OPA crossed-beam overlap test | internal | S | gain loss against the analytic Gaussian-overlap average |
| `cellMode`: an element option or run mode, astigmatic injection, pulse (not only peak-power) Kerr lens, stability check | core | M | |
| Birth phase of tunnelling electrons (drift energy assumes a uniform birth phase) | core | M | needs a reference calculation (Corkum 1993) |
| Single-shot cross-correlator: crystal propagation, air scatter, stripe filter, face reflections, fibre array | core | L | |
| XPW / cubic Kerr in the spatial and carrier-resolved models and on the GPU | core | L | 1-D result recovered for a plane wave |
| Retrieval: multi-trace from the CLI; XFROG with a measured reference file | core | S | |

## 3. GPU (plugin `gpu`)

| item | size | acceptance |
|---|---|---|
| Spatial χ(2) and spatial mixed precision on the device | L | parity 1e-9 (double) / energy 1e-6 (mixed) on CUDA and OpenCL |
| Single-precision cuFFT plan for mixed precision (the mixed path uses the built-in FFT) | S | mixed-precision tolerances kept; speed measured |
| Carrier-resolved propagation and spatial gain on the device | L | parity 1e-9 |
| Ensembles with user-response kernels and fixed steps | M | ensemble equals stream-per-point to 1e-12 |
| PoCL in CI (a CPU OpenCL device with FP64) so `GpuTests` run there | S | `GpuTests` run in CI on Linux |
| AMD / Intel OpenCL and data-centre NVIDIA cards | — | needs the hardware |

## 4. Agents and MCP (WP-A remainder)

Done 2026-11-18 (this session): MCP tools `compare_to_measured` and `fit` (time budget, memory pre-flight, killed at the budget, next call works), prompts `explore-in-quick` and
`compare-to-measurement`, the agent pack (`ultrafast-agent install --target claude|codex|cursor`, five skills shipped in the wheel) and five scripted evals (`tests/agent-evals/`).

| item | size | acceptance |
|---|---|---|
| The `design-system` skill (the shipped pack has five of the six planned) | S | listed by `ultrafast-agent list`; installs for all three targets |
| Evals run by a live agent: the prompts and criteria exist, the replays check the tools; no transcript of a live agent has been scored | M | results in `docs/agents.md`: the mandatory quick-then-confirm prompt passes with at least two different agents |
| Split `.claude/` (development) from the shipped user pack; `AGENTS.md` / `llms.txt` written for users, development rules in a dev-only file (the pack and `.claude/skills` are two copies kept equal by a test) | S | one source for the skills |
| `ultrafast agent install` inside the C# CLI (today the Python command `ultrafast-agent`) | S | same behaviour from the CLI |
| MCP: tools for `study` and `backward` sections; tools of the digital twin | M | each with its refusals and a budget test |

## 5. Documentation (WP9 remainder)

| item | size | notes |
|---|---|---|
| WP9 wave 1 done (12 S5 examples, `example.json`, lint). Waves 2-4 (plus `component-gallery`, `dependence-models`, `transforms`, which do not follow the template yet): `Wavefronts`, `LaserSystems` (several results per chain), `Nonlinear`; then `Diffraction`, `Branched` (with the cavity figure), `Diagnostics`, `Components`, `GettingStarted`, `DigitalTwin/transforms`; then the landing pages (`docs/index`, `examples/README`, `concepts`) and the plugin pages' "what is checked" sections | L | each converted example gets an `example.json`, so `build/example_lint.py` checks it |
| Fox-Li *convergence history* for `Branched/cavity-and-coherence` (the hero figure shows the final Fox-Li radius against the ABCD eigenmode and the Gaussian-Schell beam against w(z); the engine does not output the radius per round trip) | S | an output array of the radius per round trip; a fourth panel |
| Complex-field shaping example with a phase-only SLM (double-phase / Bolduc; the algorithms exist, the end-to-end SLM path is untested) | M | |
| Deformable-mirror wavefront-sensor reading 8 % below 2 × poke in `AdaptiveOptics/component-gallery` (cause not traced) | S | |

## 6. Data that needs the owner's papers

| item | notes |
|---|---|
| CS2 n2 (3.0e-18 m²/W), 90 / 10 electronic-orientational split, 1.6 / 0.2 ps; BaF2 n2 (2.0e-20 m²/W) and σ = -1.2; rotational Raman `T2At1Bar` of N2, O2, CO2, H2; other n2 values | then re-run `liu-2013`, `shimizu-1967`, `kaur-2024` |
| LBO (Kato 1994), Jundt / Gayer LiNbO3 e-ray forms | |
| Nd:YAG Stark lines (emission fit is one 1064 nm line, about 8 % of 1/τ) | |
| Stress: slice-by-slice retardation, radial / tangential lenses on the field, vector gain for a y input (Koechner, read from the source) | package of its own |

## 7. Process

| item | notes |
|---|---|
| net48 test run under Mono on the development machines (CI runs it on Linux) | |
| Code coverage on a Linux CI job (`dotnet-coverage` does not attach on macOS arm64) | |
| Linting / typing in CI: `mypy` on `build.py` and `types.py` (documented in `python/CLAUDE.md`, not run in CI) | |
