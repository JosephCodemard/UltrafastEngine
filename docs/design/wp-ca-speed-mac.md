# WP-C(A): speed on the Mac, parameter studies, fidelity switch

Session 3. Apple GPUs have no FP64 and the engine is FP64, so on this machine "speed" means a better CPU path, much better parameter studies, and cheaper physics when the user asks for it.

## C1. Baseline and CPU path

Benchmarks first (`tools/bench`, table into `docs/reference/speed.md`): 1-D SPM plate, 50-transit MPC (air), spatial radial and cartesian, a 200-point scan. Then profile.
Do backlog B5: pooled buffers (25 `new Complex[` in `Nonlinear.cs`), blocked SIMD Hankel (NEON via `Vector128` under `#if NET`), a zero-allocation-per-RHS test, thread-scaling check.
Accelerate batched FFTs for the spatial model only if the benchmark says FFT dominates. Bit-level parity with the old path to 1e-12 on the existing physics tests.

## C2. Parameter studies (the biggest win)

* **Frozen grid + cached prefix:** a study that changes element k recomputes only from k on; the grid is planned once (union of the points' needs, refused with a message if one point needs a larger grid).
* **Linear-only fast path:** when everything after the changed element is linear (GDD, compressor, angle scans), apply the transfer function: one FFT, no integration.
* **Batched points:** points as an ensemble dimension. CPU: one threaded FFT batch; the GPU layout (WP-C(B)) reuses it.
* **Result cache and resume:** key = hash of (compiled system, run, engine version); a partially finished study resumes.
* **`uf.scan` API** in Python with lazy arrays; builders keep TypedDict completion.
* Benchmark against a plain Python loop; publish the table. Tests: every study result equals the independent run (1e-12 linear fast path 1e-9).

## C3. Fidelity switch ("quick mode"): turn off expensive calculations

**Idea.** A run-level `fidelity` key: `"full"` (default, today's behaviour, bit-identical), `"standard"`, `"quick"`. Each level is a named bundle of switches, and every individual switch is also settable, so the bundle is only a shortcut.
Candidate switches (cost vs. effect, each with a measured error against `full` in a table in `docs/reference/fidelity.md`):

| switch | quick behaviour | what is lost |
|---|---|---|
| spatial model → 1-D beam model with ABCD | collapse a transverse model to the on-axis / averaged 1-D one | transverse structure, wavefront |
| carrier-resolved → envelope | envelope NLSE | carrier effects, THG, few-cycle |
| vector → scalar | x-only | polarisation coupling, XPW |
| Raman / rotational Raman off | Kerr only | delayed response, NER |
| self-steepening off | | shock, asymmetry |
| `grid` size cap / coarser z-step (`stepTolerance` looser) | fewer points, larger steps | accuracy; error estimate reported |
| satellites / tail / pedestal / ghosts off | | contrast floor, delayed light |
| ionisation / plasma off | | ionisation loss, blue shift |
| gain: ASE, spectral gain dynamics off | | pedestal |
| instruments off except those requested | skip FROG / contrast unless asked | nothing for the other outputs |

**Rules.** (1) Never silent: `summary.json` records the fidelity and every switch that was changed (`fidelityReport`), and each affected element note says what was dropped. (2) A switch that would change the answer a lot is **estimated, not hidden**:
quick mode computes a cheap dimensionless indicator (B-integral, γP/|β2|-type ratios, P/P_cr, peak intensity vs ionisation threshold) and warns "quick mode result is unreliable: B = 9 rad, Raman matters" so an agent cannot trust a bad number.
(3) `validate` and `describe` report which fidelity a file/run needs ("this system needs `carrier` at least because it has THG"). (4) Physics tests run in `full`; a test per switch compares to `full` within the table's stated tolerance on a reference case.
(5) Schema keys in `BuiltInOptions.cs`, Python builder args, CLI `--fidelity quick`, regen.

**Agents/MCP.** WP-A must expose `fidelity` in `run` / `scan`, teach the skills the workflow "explore in quick, confirm in full" (scan in `quick`, re-run the best 1-3 points in `full`, compare, report both), and make `run` return the `fidelityReport` and warnings.

## C4. FP32 study (backlog B4) and Mac

Runs here and decides on Metal (deferred past 1.0 by the owner). CPU FP32-emulation mode; reference cases: SPM plate, 50-transit MPC, soliton, SHG, contrast floor. Accept FP32 for a step type if energy 1e-6, spectrum L1 1e-4, floor unchanged. Report in `docs/reference/speed.md`; the result also feeds mixed precision for consumer NVIDIA cards (WP-C(B)).
