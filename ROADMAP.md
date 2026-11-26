# Roadmap

What is planned, in three blocks: **in 1.0.0** (the steps left before the first upload), **next** (the first releases after it) and
**later** (wanted, not scheduled). What the engine can do today is in [Capabilities and limitations](docs/capabilities.md); the physics it
reproduces is shown in the [paper replications](docs/papers/README.md) and the [examples](docs/examples/README.md); history is in
the [changelog](https://github.com/JosephCodemard/UltrafastEngine/blob/main/CHANGELOG.md). Fitting, Zemax, hitran, GPU, adaptive optics and the digital twin are *plugins*:
installed separately, loaded through public interfaces, never required by the core. Dates are not promised; order is.

## In 1.0.0

**The alpha `1.0.0a1` is published on PyPI** (`pip install --pre ultrafast-engine`; from `1.0.0a2` the plain install brings everything needed to simulate and plot). Everything described in
[capabilities](docs/capabilities.md) is built and tested; use in real labs will show what still needs fixing before 1.0.0, and alphas (`1.0.0a2`, ...) carry those fixes. Still to do:

1. **`1.0.0a2`:** the default install with `clr_loader` and Matplotlib, the new package page on PyPI (a package page can only change with a new version), the extras `all` and `paper` documented.
2. **macOS NativeAOT wheel:** built on the maintainer's Mac with `./build.sh --python --python-engine native` and attached to the release (there is no macOS runner for this repository). Linux wheels follow when they are built in a manylinux container.
3. **Release safety:** required reviewers on the `pypi` and `testpypi` environments, so that an upload waits for a maintainer's click.
4. **Settle:** fix what the alphas show, then tag `v1.0.0`. Install is then `pip install ultrafast-engine` (the import name is `ultrafast`).

## Next

Version 1.1.0 and after, most useful first. Each item says what is done when it is done.

1. **Agents (extensions of the shipped pack and server):** the 16 MCP tools, six skills and seven evals are in the alpha. To do: score all seven evals with a live agent (only the mandatory one has been scored:
   Sonnet and Haiku passed) and with a third agent family (Codex, Cursor), with an adapter that reads their transcripts; MCP tools `twin_design` (the next measurement to take) and `twin_track` (drift);
   a user pack that mentions nothing of the repository's development rules.
2. **Speed and GPU:** for rapid calculations on the device. In this order: (a) the plasma term of χ(2) crystals (ionisation in BBO, LBO, KTP) in the coupled-wave kernel, 1-D first and then spatial, parity 1e-9 with the CPU;
   (b) rate-equation gain with spectrally resolved ASE for amplifier chains, so that a regenerative-amplifier scan runs on the device; (c) FROG, d-scan and cross-correlator traces as batched 2-D transforms on the device;
   (d) spatial χ(2) and spatial mixed precision; (e) multi-GPU studies and Apple Metal single precision for quick-fidelity scans. Benchmarks on a data-centre GPU (full-rate FP64) are missing; the current ones are an RTX 3060 Ti and an Apple M3 CPU ([speed](docs/reference/speed.md#benchmarks)).
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
