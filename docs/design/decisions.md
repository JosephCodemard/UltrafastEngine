# Owner decisions and their reasons

Internal page ([folder index](README.md)). Decisions still in force, merged from the 2.2.0 backlog (2026-11-08), the 1.0.0 release plan
(2026-11-16) and later sessions. A superseded decision is struck through and says what replaced it.

## Product and scope

| # | date | decision | reason |
|---|---|---|---|
| P1 | 11-16 | The first public version is **1.0.0**; the internal 2.x numbering is retired; the changelog is folded into one 1.0.0 entry with the 2.x history below it. Done 2026-11-17. | 2.x was never public |
| P2 | 11-16 | Zemax is a plugin, not the product: experimental, never verified against OpticStudio, out of the core docs, never in a wheel or `[all]`. | the owner has no OpticStudio to test on |
| P3 | ~~11-16~~ | ~~Metal (Apple GPU) is deferred past 1.0; the FP32 study decides whether it is ever worth it.~~ Superseded by E18. | Apple GPUs have no FP64; FP32 fills the contrast floor below about -85 dB |
| P4 | 11-16 | A **fidelity switch** (`full` / `standard` / `quick`) exists; agents and the MCP server must know it ("explore in quick, confirm in full"). | cheap exploration without silent errors |
| P5 | 11-16 | WP9 (all example READMEs in one structure) is done in waves: wave 1 (the 12 S5 examples) and the mechanics first, the rest after 1.0. | 60 READMEs are too many for one session |
| P6 | 11-08 | Open-air focus for fibres: frequency-dependent A_eff, hollow capillary (Marcatili), rate-equation gain with ASE shared with bulk gain, GRIN multimode example. GMMNLSE and a mode solver are out for now. | the owner's work is open-air post-compression and CPA contrast |
| P7 | 11-08 | ASE is part of the rate-equation gain (forward and backward, spectrally resolved, as a pedestal in the contrast). | ASE pedestal is a contrast question |
| P8 | 11-08 | Paper replications cover a range of topics; the goal is each paper's headline result; when one cannot be reproduced its README says why. | trust through published results |
| P9 | 11-08 | The owner's separate lab project is not read or modified; only its recorded data files may be copied into `docs/papers/`. | ownership |

## Physics

| # | date | decision | reason |
|---|---|---|---|
| Ph1 | 11-16 | `n2Scale` scales every third-order response of the element (Kerr, delayed Raman, XPW, rotational Raman), not user `responses`. | consistent with the spatial model |
| Ph2 | 11-16 | Kerr lens in the 1-D beam model: the most physically accurate option that fits one Gaussian (opt-in `kerrLens: peak / pulseWeighted`). | the spatial model is the exact answer; 1-D is an estimate |
| Ph3 | 11-08 | XPW (anisotropic cubic χ(3)) in 1-D first; spatial and carrier-resolved later. | contrast cleaning is 1-D first |
| Ph4 | 11-17 | Ionisation that is negligible at the element's entry peak intensity is skipped automatically (`ionization.skipNegligible`, default true, with a note giving the estimated density); the plasma term also runs on the GPU. | an MPC with ionising air gaps ran 20× slower on the CPU with no change in the result |
| Ph5 | 11-18 | Ionisation in χ(2) crystals is per element (the run-level `physics.ionization` is for bulk elements only), off by default; one rate per field at its own carrier and own intensity, densities added; no mixed-colour multiphoton terms; CPU only. Band gaps of BBO, LBO, KDP, KTP, LiNbO₃ are `unverified` estimates from the UV edge until the owner supplies sources. | a rate built at one carrier gives the wrong photon order for the other fields (800 nm SHG in BBO: 5 photons against 2); a run-level switch would change every existing crystal run |

## Engineering, packaging, release

| # | date | decision | reason |
|---|---|---|---|
| E1 | 11-08 | Push to GitHub: yes. PoCL in CI so `GpuTests` run there (not done). | CI coverage of the GPU path |
| E2 | ~~11-08~~ | ~~No PyPI publishing.~~ Superseded by E3. | |
| E3 | ~~11-16~~ | ~~Upload 1.0.0 to PyPI with `twine`.~~ Superseded by E4 (same rule: only on the owner's explicit go-ahead). | |
| E4 | 11-17 | PyPI through **trusted publishing** (GitHub OIDC, `pypa/gh-action-pypi-publish`), TestPyPI for `v*rc*` tags, PyPI for final tags, each job behind a GitHub environment with required reviewers. No token is stored anywhere. Nothing is uploaded without the owner's explicit go-ahead; tags are the owner's. | least privilege; no long-lived secret |
| E5 | 11-17 | No sdist is published: the package needs the compiled engine, and an sdist without it would install a package that cannot run. Platform wheels carry the NativeAOT engine; the `py3-none-any` wheel needs the hosted engine. | a broken sdist is worse than none |
| E6 | 11-08 | Plugins as pip extras: `ultrafast-engine[hitran,cuda,fitting,opencl]`, `[all]` (never Zemax). The native library in the wheel is built with every non-Zemax plugin compiled in. | NativeAOT can only use compiled-in plugins |
| E7 | 11-17 | No separate `ultrafast-lite` package. The base wheel depends on numpy only; the closed-form calculators (`ultrafast.calc.beam`, `.nonlinear`, `.pulse` shapes) work without the engine; optional pieces are extras (`plot`, `animate`, `paper`, `hosted`). | a second distribution doubles release work for a few hundred kB of Python |
| E8 | 11-16 | MCP: local, workspace-sandboxed stdio only; no HTTP / remote transport; hand-written JSON-RPC (no `mcp` package); shipped in the wheel as `ultrafast-mcp`. | safety, zero dependencies |
| E9 | 11-16 | Agent pack covers Claude Code and Codex (AGENTS.md style); Cursor if cheap. | the tools users have |
| E10 | 11-08 | (delegated) FP32 study first; then mixed precision in CUDA / OpenCL; Metal only if FP32 is enough. Done: mixed precision is refused above 70 dB of instrument dynamic range. | |
| E11 | 11-08 | Estimators stay in the fitting plugin; design plugins declare `"requires": ["fitting"]`. | one home for optimisers |
| E12 | 11-16 | Work on `dev/*` branches, one commit per piece, merged to `main` after `/verify` and green CI. | |
| E13 | 11-17 | The PyPI distribution is named **`ultrafast-engine`** (import name `ultrafast`). `ultrafast` was the owner's first choice: it had no project on PyPI on 2026-11-17 but is taken on TestPyPI by another author, so a release candidate could not be rehearsed there under that name. The name is checked by `build/check_version.py`. | rehearsal on TestPyPI; the name already in all docs |
| E14 | 11-17 | Version numbers restart at **1.0.0**: the 2.x numbers of the development history were never released. The CHANGELOG keeps them as development history. A release candidate is only a tag (`v1.0.0rc1`), never a number in the files. | one version in every file |
| E15 | 11-18 | The unit-in-key renames (`betasSkPerM`, `fieldTesla`, `tau1S` / `tau2S` / `taubS`, `dnDTPerK`) are made before 1.0.0, by load-time migration; old names keep working with a deprecation note. | no key changes after the first public release |
| E16 | ~~11-18~~ | ~~1.0.0 has no open items: design-heavy work is on the v1.1.0 roadmap. Release stops at a merged PR; the owner creates the pending publishers and environments and tags `v1.0.0rc1` and `v1.0.0`. WP9 for 1.0.0 is images and the new plotting backend only.~~ Superseded by E20. | owner's go-ahead for every tag |
| E17 | 11-18 | The first public release is an **alpha**, `1.0.0a1` (owner decision), so that use in real labs can fix what is found before 1.0.0. The files keep `1.0.0` (the pre-release is only in the tag, as for E14); tags: `v1.0.0aN` -> PyPI as a pre-release (`pip install --pre`), `v1.0.0rcN` -> TestPyPI, `v1.0.0` -> PyPI. Classifier `Development Status :: 3 - Alpha`. | the same files build every alpha, candidate and the final release |
| E18 | 11-25 | FP32-only devices are supported (supersedes P3): integrated GPUs and Apple silicon through a full single-precision mode on OpenCL first (`OpenCL.framework` is present on arm64 macOS), a native Metal back-end later. Every reduced-precision run warns; runs needing more than 70 dB of dynamic range stay refused. | the owner wants laptops and integrated graphics to run the engine; the FP32 study shows energy, spectrum and shape pass |
| E19 | 11-25 | The Zemax plugin's ZOS-API connection is our own C# (net48) code; ZOSPy (MIT) may be read as a guide, is never shipped or required, and closely ported logic carries attribution. | no third-party Python dependency in the plugin; the ZOS-API DLLs stay the user's |
| E20 | 11-25 | The push to 1.0.0 includes the public roadmap's Next items 1, 2, 4, 5, 7 and the Later items waveguides and gain, XPW, Zemax depth and the single-shot cross-correlator, as the work packages of `roadmap-internal.md` section 0 (one fresh Claude instance per WP). Live agent-eval scoring is run by the owner, not by the WPs. | the owner wants these in v1; small WPs keep each instance's context small |

## Plotting

| # | date | decision | reason |
|---|---|---|---|
| V1 | 11-17 | Two built-in styles: `ultrafast` (brand: logo palette, default) and `paper` (publication: serif, inward ticks, column widths). `science` uses SciencePlots when it is installed (optional `paper` extra), otherwise `paper`. No SciencePlots code or assets are copied. | paper figures without a hard dependency |
| V2 | 11-17 | Schematics are drawn by deterministic code from `summary.graph` (same system, same picture), not by hand. | reproducible figures |
