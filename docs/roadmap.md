# Roadmap

What is planned, in three blocks: **in 1.0.0** (the steps left before the first upload), **next** (the first releases after it) and
**later** (wanted, not scheduled). What the engine can do today is in [Capabilities and limitations](capabilities.md); the physics it
reproduces is shown in the [paper replications](papers/README.md) and the [examples](examples/README.md); history is in
the [changelog](https://github.com/JosephCodemard/UltrafastEngine/blob/main/CHANGELOG.md). Fitting, Zemax, hitran, GPU, adaptive optics and the digital twin are *plugins*:
installed separately, loaded through public interfaces, never required by the core. Dates are not promised; order is.

## In 1.0.0

Everything of 1.0.0 is built and tested; what remains are steps that only the maintainer can take (accounts, tags, a Mac build):

1. **Publishers and environments:** create the pending trusted publishers for `ultrafast-engine` on PyPI and TestPyPI, and the protected environments `testpypi` and `pypi` in the
   repository settings; switch GitHub Pages to "GitHub Actions".
2. **Release candidate:** tag `v1.0.0rc1`; the workflow uploads to TestPyPI. Then check the install in a fresh environment on Linux, Windows and macOS: `pip install` from TestPyPI, the quick start,
   one strict example, `ultrafast-mcp` and `ultrafast agent list`.
3. **Release:** tag `v1.0.0`; approve the `pypi` environment. Install is `pip install "ultrafast-engine[hosted]"` (the import name is `ultrafast`).
4. **macOS NativeAOT wheel:** built on the maintainer's Mac with `./build.sh --python --python-engine native` and attached to the release (there is no macOS runner for this repository).

## Next

Version 1.1.0 and after, most useful first. Each item says what is done when it is done.

1. **Agents (extensions of the shipped pack and server):**
   * *Live evaluation:* score the five evaluation prompts (`tests/agent-evals/`) with at least two different agents; the criteria and the replays of reference trajectories exist, no live transcript has been scored yet. The quick-then-confirm prompt must pass for both.
   * *MCP tools for studies and for the digital twin:* the `study` and `backward` sections of a run spec, and the `ultrafast twin` commands, each with its refusals, a time budget and a test that the budget kills the call.
   * *One source for the skills:* the development `.claude/skills` and the shipped pack are two copies kept equal by a test; make the pack the only copy and generate the other.
2. **Speed and GPU:** for rapid calculations on the device. In this order: (a) the plasma term of χ(2) crystals (ionisation in BBO, LBO, KTP) in the coupled-wave kernel, 1-D first and then spatial, parity 1e-9 with the CPU;
   (b) rate-equation gain with spectrally resolved ASE for amplifier chains, so that a regenerative-amplifier scan runs on the device; (c) FROG, d-scan and cross-correlator traces as batched 2-D transforms on the device;
   (d) spatial χ(2) and spatial mixed precision; (e) multi-GPU studies and Apple Metal single precision for quick-fidelity scans. Benchmarks on a data-centre GPU (full-rate FP64) are missing; the current ones are an RTX 3060 Ti and an Apple M3 CPU ([speed](reference/speed.md#benchmarks)).
3. **Design plugins** (separate plugins; each emits ordinary `.ufs` objects that the core re-checks): *coating design* (R, T, group delay and GDD over band, angle and polarisation; then design against a goal; then tolerance Monte Carlo) and
   *crystal design* (custom crystals from index data; quasi-phase-matching domain engineering). Both use the fitting plugin's estimators.
4. **Engine accuracy and elements:**
   * a carrier frame (a dispersive pulse off the lattice drifts by GDD × offset × L), and a carrier-resolved path for pulses near one optical cycle;
   * the Kerr-lensed beam carried to later elements, and field snapshots along z inside an element (evolution maps within one fibre or crystal);
   * glass in beam splitters and polarising splitters; delayed-response window planning for generic time responses and rotational revivals;
   * built-in metal mirrors (Ag, Au, Al) from a published table; frequency-dependent effective area; a schema file for study specs.
5. **Examples and documentation:** convert the remaining example groups to the template (README structure, `example.json`, lint): `Wavefronts`, `LaserSystems`, `Nonlinear`, then the others, in waves; a complex-field shaping example with a phase-only SLM; the convergence history of the Fox-Li resonator example.
6. **More paper replications** (`docs/papers/`): polarisation, diffraction, supercontinuum, hollow-fibre compression, OPCPA design, SPIDER, d-scan.
7. **Quality gates in CI:** test coverage and static typing (mypy) as checks on every pull request; net48 tests on a Mono runner.

## Later

* **Waveguides and gain:** hollow-capillary gas waveguides, tapers, saturated rate-equation gain in fibres with spectrally resolved ASE.
* **Cross-polarised-wave generation** in the spatial and carrier-resolved models and on the GPU (the 1-D version exists).
* **Fitting:** ensemble Kalman and Bayesian-optimisation estimators, learned field operators, spatio-temporal transform operators.
* **Zemax depth** (needs the user's OpticStudio installation): Zernike aberrations, paraxial ABCD matrices from operands, prism pairs and Martinez stretchers, crystal surfaces, a menu-integrated extension.
* **A catalogue** of materials, coatings, crystals and substrates checked against published sources, with the user's own entries beside them. Users bring vendor data; none is shipped.
* **Single-shot cross-correlator** as a component (crystal propagation, air scatter, stripe filter, face reflections, fibre array); the `multiFrog` two-trace case; the birth phase of tunnelling electrons.
* **Known limits that are planned work:** resonators as graphs, thermal-stress birefringence patterns, very large delayed-light grids, detector presets, live-data plugins.

Ideas and priorities are tracked in issues; contributions are welcome (`CONTRIBUTING.md` in the repository).
