# Roadmap

What is planned. The current capabilities are described in [Capabilities and limitations](capabilities.md);
how the engine got here is in [Development history](design/history.md). Fitting, Zemax and live data are
*plugins*: installed separately, loaded through public interfaces, never required by the core.

## Next: Zemax plugin (OpticStudio depth)
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

## Fitting plugin (after the transforms of 2.1.8)
- [ ] General low-rank and learned (ML) field operators as built-in transform kinds
- [ ] Ensemble Kalman and Bayesian-optimisation estimators
- [ ] Spatio-temporal transform operators (pulse-front tilt, spatial chirp)
- [ ] Backward propagation through saturated gain and frequency conversion beyond linearisation

## Plugins and distribution
- [ ] Live-data plugin (streamed recordings, a digital twin that re-fits as data arrive)
- [ ] ML / PINN estimator and surrogate plugins
- [x] GPU compute back-ends as a plugin: CUDA and OpenCL (1-D and spatial envelope); native CPU FFTs (Accelerate, FFTW)
- [ ] GPU: user responses as device kernels, χ(2) and carrier-resolved propagation on the device
- [ ] Branches (splitters, parallel paths, coherent recombination)
- [ ] Wheels for Windows / macOS / Linux on PyPI, NuGet package for the core
- [ ] OpticStudio User Extension (menu-integrated) in addition to the Interactive Extension

Ideas and priorities are tracked in issues; contributions are welcome (`CONTRIBUTING.md` in the repository).
