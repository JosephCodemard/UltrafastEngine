# Roadmap

What is planned, in three blocks: **in 1.0.0** (what the first public release holds and still needs), **next** (the first releases after it) and
**later** (wanted, not scheduled). What the engine can do today is in [Capabilities and limitations](docs/capabilities.md); the physics it
reproduces is shown in the [paper replications](docs/papers/README.md) and the [examples](docs/examples/README.md); history is in
the [changelog](https://github.com/JosephCodemard/UltrafastEngine/blob/main/CHANGELOG.md). Fitting, Zemax, hitran, GPU, adaptive optics and the digital twin are *plugins*:
installed separately, loaded through public interfaces, never required by the core. Dates are not promised; order is.

## In 1.0.0

The only open engine that treats the field as a pulse with a spectrum in real media: dispersion to all orders, χ(2) / χ(3) / Raman / plasma / gain, gases,
spatial propagation, branched systems, spatio-temporal couplings, delayed light, instruments, measured-data comparison and fitting. Every component is
checked against an analytic or published value ([test matrix](docs/reference/validation-matrix.md)); published results are reproduced in [papers](docs/papers/README.md).

* **Done:** the core engine and plugins; calculators (`ultrafast.calc`); plot styles and system schematics; memory estimate and limit; fidelity levels
  (quick / standard / full); adaptive optics, temporal shaping and the digital twin; MCP tools for agents (`ultrafast-mcp`: describe, validate, run, scan,
  explain_warning, plot, compare_to_measured, fit; prompts; `ultrafast-agent install`; see [MCP reference](docs/reference/mcp.md)).
* **Still to do before the first upload:** the first run of the release workflow on a release-candidate tag, and a check of the wheel in a fresh environment on
  Linux, Windows and macOS (the macOS NativeAOT wheel is built on the maintainer's Mac). Install is `pip install ultrafast-engine` (the import name is `ultrafast`).
* **Examples:** one structure for every example README (template, a metadata file and a lint check); the twelve adaptive-optics, digital-twin and fibre examples
  follow it, the other groups are converted in waves after 1.0.0.

## Next

Most useful first.

1. **Agents:** a `design-system` skill, scoring the evaluation prompts with live agents (the replays of reference trajectories exist), MCP tools for studies and the digital twin.
   (Done: `compare_to_measured`, `fit`, prompts and the shipped agent pack `ultrafast-agent install`.)
2. **Design plugins** (separate plugins; each emits ordinary `.ufs` objects that the core re-checks): *coating design* (R, T, group delay and GDD over band, angle
   and polarisation; then design against a goal; then tolerance Monte Carlo) and *crystal design* (custom crystals from index data; quasi-phase-matching domain
   engineering). Both use the fitting plugin's estimators.
3. **Engine accuracy:** carrier frame (a dispersive pulse off the lattice drifts), pulses near one optical cycle (a carrier-resolved path), the Kerr-lensed beam carried
   to later elements, field snapshots along z inside an element, built-in metal mirrors from a published table.
4. **More paper replications** (`docs/papers/`): polarisation, diffraction, supercontinuum, hollow-fibre compression, OPCPA design, SPIDER, d-scan.
5. **Speed and GPU (v1.1.0):** for rapid calculations on the device: the plasma term of χ(2) crystals (ionisation in BBO, LBO, KTP), spatial χ(2) and mixed precision, rate-equation gain with ASE, carrier-resolved propagation, instrument traces (FROG, d-scan), several GPUs for studies, and a CPU OpenCL device in CI so the GPU tests run there. Apple GPUs (Metal, single precision) only for quick-fidelity scans.

## Later

* **Waveguides and gain:** frequency-dependent effective area, hollow-capillary gas waveguides, tapers, saturated rate-equation gain in fibres with spectrally resolved ASE.
* **Cross-polarised-wave generation** in the spatial and carrier-resolved models and on the GPU (the 1-D version exists).
* **Fitting:** ensemble Kalman and Bayesian-optimisation estimators, learned field operators, spatio-temporal transform operators.
* **Zemax depth** (needs the user's OpticStudio installation): Zernike aberrations, paraxial ABCD matrices from operands, prism pairs and Martinez stretchers, crystal surfaces,
  a menu-integrated extension.
* **A catalogue** of materials, coatings, crystals and substrates checked against published sources, with the user's own entries beside them. Users bring vendor data; none is shipped.
* **Examples:** the remaining example groups in the new structure, the plugin pages' "what is checked" sections.
* **Known limits that are planned work:** resonators as graphs, thermal-stress birefringence patterns, very large delayed-light grids, detector presets, live-data plugins.

Ideas and priorities are tracked in issues; contributions are welcome (`CONTRIBUTING.md` in the repository).
