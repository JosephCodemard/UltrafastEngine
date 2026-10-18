# Roadmap

What is planned. The current capabilities are described in [Capabilities and limitations](capabilities.md);
how the engine got here is in [Development history](design/history.md). Fitting, Zemax and live data are
*plugins*: installed separately, loaded through public interfaces, never required by the core.

## Next: fitting plugin with `transform` elements
- [ ] `transform` element: a parameterised operator on the complex field -- spectral amplitude / phase, temporal modulation, Jones matrix per frequency, energy, spatial phase (Zernike), pointing, general (low-rank) matrices, user / ML operators -- so spectrum, pulse, contrast and beam stay mutually consistent
- [ ] Constraints: passivity, causality (Kramers-Kronig), smoothness; identifiability report
- [ ] Replaceable estimators (LM, regularised GN, CMA-ES, ensemble Kalman, MCMC; user plugins); instrument-response parameters (resolution, calibration) as fit parameters
- [ ] Results in three layers: simulated -> inferred -> believed, with provenance and an influence map

## Then: Zemax plugin (OpticStudio depth)
- [ ] Zernike aberrations exported from OpticStudio applied to spatial fields
- [ ] Paraxial ABCD matrices between elements from paraxial-ray operands (exact transverse transport)
- [ ] Prism pairs and Martinez stretchers with lens aberrations as analytic elements
- [ ] Crystal surfaces recognised and mapped to `crystal` elements
- [ ] Output basis vectors in results (lab-frame polarisation after folded paths)

## Physics extensions under consideration
- [ ] Frequency-dependent effective area and multimode fibres
- [ ] z-dependent dispersion within one fibre element (tapers)
- [ ] Spatio-temporal couplings from angular dispersion (pulse-front tilt, spatial chirp; Kostenbauder matrices)
- [ ] Thermal lenses of gain media in the Gaussian beam tracking
- [ ] Saturated fibre gain (rate equations along the fibre)

## Plugins and distribution
- [ ] Live-data plugin (streamed recordings, a digital twin that re-fits as data arrive)
- [ ] ML / PINN estimator and surrogate plugins
- [ ] GPU compute back-ends (Vulkan / OpenCL, CUDA, Metal) as plugins
- [ ] Branches (splitters, parallel paths, coherent recombination)
- [ ] Wheels for Windows / macOS / Linux on PyPI, NuGet package for the core
- [ ] OpticStudio User Extension (menu-integrated) in addition to the Interactive Extension

Ideas and priorities are tracked in issues; contributions are welcome (`CONTRIBUTING.md` in the repository).
