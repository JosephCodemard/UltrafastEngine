# Roadmap

What is planned. What the engine can do today is in [Capabilities and limitations](capabilities.md); the physics it
reproduces is shown in the [paper replications](papers/README.md) and the [examples](examples/README.md); history is in
the [changelog](https://github.com/JosephCodemard/UltrafastEngine/blob/main/CHANGELOG.md). Fitting, Zemax, hitran and GPU are *plugins*: installed separately, loaded
through public interfaces, never required by the core.

## Where the engine is

The only open engine that treats the field as a pulse with a spectrum in real media: dispersion to all orders,
χ(2) / χ(3) / Raman / plasma / gain, gases, spatial propagation, branched systems (interferometers, pump arms),
spatio-temporal couplings, delayed light, instruments (FROG, autocorrelators, third-order cross-correlators, contrast), measured-data
comparison and fitting. Every component is checked against an analytic or published value
([test matrix](reference/validation-matrix.md), [component verification](component-tests.md)), and published results are
reproduced in [papers](papers/README.md).

## Next

Priorities, most useful first.

**Done (November 2026, merged 2026-11-10):** adaptive optics, temporal shaping, conditioned transforms and a digital twin, in five sessions ([record](design/history.md); the next iteration is [planned](design/next-steps.md)). S1 (Python package structure, plotting, Zernike plot, animation, grouped examples, optical-computing examples, gallery), S2 (core primitives: the `modulator` element, coherent tiles, in-memory maps, frequency streaming), S3 (the `ao` plugin), S4 (fitting across conditions and the `twin` plugin) and S5 (AdaptiveOptics, DigitalTwin and Fibres examples, documentation, verification) are done. **Still to do from that plan:** the MCP server for agents, a component gallery and a complex-field-shaping example for the `ao` plugin, a spline-against-network twin example, a wavefront twin, `UF_WITH_AO` in the native library, a grid-planner hook for shaper side pulses, and the same figure-and-story treatment for the older examples.

1. **Design plugins** (separate plugins; each emits ordinary `.ufs` objects that the core re-checks)
   - *Coating design*: analysis of R, T, group delay and GDD over band, angle and polarisation, and the field inside a
     stack; then design (needle and gradual evolution, chirped, double-chirped and Gires-Tournois mirrors) against a goal; then
     manufacturing-tolerance Monte Carlo. Useful first for multipass-cell mirrors.
   - *Crystal design*: custom crystals from index data and point-group tensors (Sellmeier fit, Miller's rule); then
     quasi-phase-matching domain engineering (chirped, apodised, aperiodic poling, cascaded processes, adiabatic
     conversion); then 1-D photonic lattices. First-principles prediction of a new material's χ(2) is out of scope.
   - Both use the fitting plugin's estimators (`requires: ["fitting"]`); gradients are written per problem (thin-film
     matrices, undepleted QPM) before any general automatic differentiation.
2. **More paper replications** (`docs/papers/`, `/replicate`): twelve are done (see the [index](papers/README.md)); each README says how well the
   science matched. Next, to cover polarisation, diffraction, nonlinear effects, OPCPA and diagnostics (PDFs to be supplied):
   Maker, Terhune & Savage, PRL 12, 507 (1964) (intensity-dependent index, ellipse rotation); Dudley, Genty & Coen, Rev. Mod. Phys. 78, 1135 (2006)
   (supercontinuum); Nisoli et al., Opt. Lett. 22, 522 (1997) (hollow-fibre compression); Durnin, PRL 58, 1499 (1987) (Bessel beams); a classic
   Fresnel edge or circular-aperture diffraction paper; Ross et al., Opt. Commun. 144, 125 (1997) (OPCPA design); Iaconis & Walmsley, Opt. Lett. 23, 792 (1998)
   (SPIDER); Miranda et al., Opt. Express 20, 18732 (2012) (d-scan); and Michelson's 1892 paper (the PDF supplied as such was Perot & Fabry 1899).
3. **Waveguides and gain** (open-air focus)
   - [ ] Frequency-dependent effective area
   - [ ] Hollow-capillary (Marcatili) gas waveguide with gas dispersion, loss and ionisation
   - [ ] z-dependent dispersion within one fibre element (tapers)
   - [ ] Saturated rate-equation gain in fibres, and forward / backward, spectrally resolved ASE shared with the bulk gain elements. Today the bulk `gain` / `amplifier` elements have rate equations and one-dimensional ASE seeded in the signal polarisation, and fibre gain is unsaturated
   - Not planned: multimode fibres beyond a graded-index example
4. **Cross-polarised-wave generation** (anisotropic cubic χ(3)) for contrast cleaning: the 1-D tensor and BaF2 are in; spatial and carrier-resolved versions are open (see below).
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

## Open items found by the November 2026 consolidation

- **Carrier frame:** move the window frame to the pulse carrier (today it follows the lattice centre; a dispersive pulse off the lattice drifts by GDD x offset x L); then a 10 fs soliton on the coarse default grid is exact without `snapCarrier: false`.
- **Pulses near one cycle:** SPM that reaches zero frequency (10 fs at 2 µm, B = 3) is cut and reported; a carrier-resolved path for it.
- **XPW and cubic Kerr:** spatial and carrier-resolved implementations, GPU support, and verified literature values for BaF2 n2 and σ and for CS2 n2, split and time constants.
- **Single-shot cross-correlator:** conversion efficiency and beam envelope in the crystal, air scatter with a stripe filter, crystal-face reflection spikes, fibre-array readout; an option to put elements in the probe arm only.
- **Retrieval:** multi-trace retrieval from the CLI; XFROG with a measured reference file.
- **`cellMode`:** an element option or run mode, astigmatic injection, pulse (not only peak-power) Kerr lens.
- **Delayed responses:** window planning for generic `timeResponse` and gas rotational revivals (only Raman and orientational are planned today); round-aperture decentre.
- **Carrier snap in a nonlinear cell:** `grid.snapCarrier` still changes the multi-pass-cell result of Schulte et al. by 1.4 % in bandwidth and 0.4 % in B (13.27 vs 13.46 nm) although the input pulse is built at the requested carrier; find which linear or nonlinear term still uses the lattice carrier.
- **Multi-trace retrieval:** the two-trace case (reference SHG plus XFROG) of `multiFrog` ends in the time-reversed twin in most starts; check the initial guess and step order.
- **`cellMode`:** a plate touching a mirror is treated as immersing it (power 2n/R), which should be documented; the helper does not check that a matched mode survives a perturbation (a 514 mm cell reading is matched but unstable in the radial model).
- **Crossed-beam overlap:** test the gain loss of a non-collinear OPA (beams separated by αL) against an analytic Gaussian-overlap average (a 2° crossing roughly halves the gain at 64 / 150 µm radii).
- **Phase-matching calculator:** accept `noncollinearDeg` for `sfg` as the element does.

## Zemax plugin (OpticStudio depth)

Planned after the items above; needs the user's OpticStudio installation.
- [ ] Zernike aberrations exported from OpticStudio applied to spatial fields
- [ ] Paraxial ABCD matrices between elements from paraxial-ray operands
- [ ] Prism pairs and Martinez stretchers with lens aberrations as analytic elements
- [ ] Crystal surfaces recognised and mapped to `crystal` elements
- [ ] Output basis vectors in results (lab-frame polarisation after folded paths)
- [ ] OpticStudio User Extension (menu-integrated) in addition to the Interactive Extension

## Known limits that are planned work

- Branches: a `resonator` is a nested sequence and not a graph (graph loops exist: `ringDown`, `steadyState`); mode overlap beyond simple Gaussian beams (M² is ignored); coupling between coherent modes in nonlinear elements.
- Thermal stress birefringence: the spatial (four-lobed) pattern (the beam-averaged depolarisation with `thermalLens.stress` exists); end-face bulging in the thermal lens; thermal lenses in regenerative amplifiers.
- Delayed light and gas: a 256 × 256 plate at 2^13 time points (needs a GPU); non-Gaussian roughness spectra and bulk scatter from a measured BRDF; continuum absorption for the hitran plugin; the tail's own self-phase.
- Detector presets: a catalogue of real instruments (needs their manuals and calibrations).
- Live-data and machine-learning surrogate plugins: after the stable release.

Ideas and priorities are tracked in issues; contributions are welcome (`CONTRIBUTING.md` in the repository).
