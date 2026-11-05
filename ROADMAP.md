# Roadmap

What is planned. What the engine can do today is in [Capabilities and limitations](docs/capabilities.md); the physics it
reproduces is shown in the [paper replications](docs/papers/README.md) and the [examples](docs/examples/README.md); history is in
the [changelog](CHANGELOG.md). Fitting, Zemax, hitran and GPU are *plugins*: installed separately, loaded
through public interfaces, never required by the core.

## Where the engine is

The only open engine that treats the field as a pulse with a spectrum in real media: dispersion to all orders,
χ(2) / χ(3) / Raman / plasma / gain, gases, spatial propagation, branched systems (interferometers, pump arms),
spatio-temporal couplings, instruments (FROG, autocorrelators, third-order cross-correlators, contrast), measured-data
comparison and fitting. Every component is checked against an analytic or published value
([test matrix](docs/reference/validation-matrix.md), [component verification](docs/verification.md)), and published results are
reproduced in [papers](docs/papers/README.md).

## Next

Priorities, most useful first.

1. **Design plugins** (separate plugins; each emits ordinary `.ufs` objects that the core re-checks)
   - *Coating design*: analysis of R, T, group delay and GDD over band, angle and polarisation, and the field inside a
     stack; then design (needle and gradual evolution, chirped, double-chirped and Gires-Tournois mirrors) against a goal; then
     manufacturing-tolerance Monte Carlo. Useful first for multipass-cell mirrors.
   - *Crystal design*: custom crystals from index data and point-group tensors (Sellmeier fit, Miller's rule); then
     quasi-phase-matching domain engineering (chirped, apodised, aperiodic poling, cascaded processes, adiabatic
     conversion); then 1-D photonic lattices. First-principles prediction of a new material's χ(2) is out of scope.
   - Both use the fitting plugin's estimators (`requires: ["fitting"]`); gradients are written per problem (thin-film
     matrices, undepleted QPM) before any general automatic differentiation.
2. **More paper replications** (`docs/papers/`, `/replicate`): Yb and Ti:sapphire amplifiers and oscillators, OPCPA chains,
   gas-filled capillaries, filaments, multipass cells. Each states what reproduces and what does not.
3. **Waveguides and gain** (open-air focus)
   - [ ] Frequency-dependent effective area
   - [ ] Hollow-capillary (Marcatili) gas waveguide with gas dispersion, loss and ionisation
   - [ ] z-dependent dispersion within one fibre element (tapers)
   - [ ] Rate-equation gain with ASE, shared by fibres and the bulk gain elements (ASE as a spectrum and as a contrast pedestal)
   - Not planned: multimode fibres beyond a graded-index example
4. **Cross-polarised-wave generation** (anisotropic cubic χ(3)) for contrast cleaning, with the BaF2 tensor.
5. **Fitting plugin**
   - [ ] Ensemble Kalman and Bayesian-optimisation estimators
   - [ ] General low-rank and learned field operators as transform kinds
   - [ ] Spatio-temporal transform operators (pulse-front tilt, spatial chirp)
   - [ ] Backward propagation through saturated gain and frequency conversion beyond linearisation
6. **Distribution and speed**
   - [ ] Release workflow: a version tag builds the packages per platform, runs smoke tests and attaches them (and the Python wheels) to a GitHub release; the ZOS-API assemblies are never packaged
   - [ ] Plugins as Python extras: `pip install "ultrafast[hitran,cuda,fitting]"`
   - [ ] GPU coverage: rotational Raman of gases, user responses as device kernels, χ(2), carrier-resolved propagation
   - [ ] Mixed-precision (FP32) mode for the CUDA and OpenCL back-ends after a study of which steps tolerate it; Metal only if that study is favourable (Apple GPUs have no FP64)
   - [ ] Speed: profile the hot paths (Hankel, Cartesian FFTs, instruments) and keep them allocation-free
7. **A catalogue**: the materials, coatings, crystals and substrates the engine knows in one place, checked against published
   sources, with the user's own entries beside them. Users bring vendor data; none is shipped.

## Zemax plugin (OpticStudio depth)

Planned after the items above; needs the user's OpticStudio installation.
- [ ] Zernike aberrations exported from OpticStudio applied to spatial fields
- [ ] Paraxial ABCD matrices between elements from paraxial-ray operands
- [ ] Prism pairs and Martinez stretchers with lens aberrations as analytic elements
- [ ] Crystal surfaces recognised and mapped to `crystal` elements
- [ ] Output basis vectors in results (lab-frame polarisation after folded paths)
- [ ] OpticStudio User Extension (menu-integrated) in addition to the Interactive Extension

## Known limits that are planned work

- Branches: a cycle in the element graph together with a resonator's nested sequence; coupling between coherent modes in nonlinear elements.
- Thermal stress birefringence: the spatial (four-lobed) pattern; thermal lenses in regenerative amplifiers.
- Delayed light and gas: a 256 × 256 plate at 2^13 time points (needs a GPU); non-Gaussian roughness spectra and bulk scatter from a measured BRDF; continuum absorption for the hitran plugin; the tail's own self-phase.
- Detector presets: a catalogue of real instruments (needs their manuals and calibrations).
- Live-data and machine-learning surrogate plugins: after the stable release.

Ideas and priorities are tracked in issues; contributions are welcome ([CONTRIBUTING.md](CONTRIBUTING.md)).
