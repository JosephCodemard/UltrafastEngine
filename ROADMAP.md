# Roadmap

What is planned. The current capabilities are described in [Capabilities and limitations](docs/capabilities.md);
how the engine got here is in [Development history](docs/design/history.md). Fitting, Zemax and live data are
*plugins*: installed separately, loaded through public interfaces, never required by the core.

## Next: Zemax plugin (OpticStudio depth)
- [ ] Zernike aberrations exported from OpticStudio applied to spatial fields
- [ ] Paraxial ABCD matrices between elements from paraxial-ray operands (exact transverse transport)
- [ ] Prism pairs and Martinez stretchers with lens aberrations as analytic elements
- [ ] Crystal surfaces recognised and mapped to `crystal` elements
- [ ] Output basis vectors in results (lab-frame polarisation after folded paths)

## Delayed light and gas (done; what is left)
- [x] 2-D plate at P / P_cr ~ 100: seeded noise / ripple, diagnostics, Bespalov-Talanov check, example 29 -- *left:* a 256 x 256 run at 2^13 time points on a GPU
- [x] Detector response tables, photon counting and presets (user-defined) -- *left:* a catalogue of real instruments (needs their manuals / calibrations)
- [x] Analytic surface scatter for `delayedScatter` -- *left:* non-Gaussian roughness spectra (fractal / PSD tables), bulk scatter from a measured BRDF
- [x] TIPS partition sums for the hitran plugin; H2O, CO2 and O2 vs HAPI -- *left:* a comparison with a measured air transmission; continuum absorption
- [x] Tail: smooth elements skip the exact step; first-order cross-phase -- *left:* second-order tail nonlinearity (the tail's own self-phase)

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
- [ ] GPU compute back-ends (Vulkan / OpenCL, CUDA, Metal) as plugins
- [ ] Branches (splitters, parallel paths, coherent recombination)
- [ ] Wheels for Windows / macOS / Linux on PyPI, NuGet package for the core
- [ ] OpticStudio User Extension (menu-integrated) in addition to the Interactive Extension

Ideas and priorities are tracked in issues; contributions are welcome ([CONTRIBUTING.md](CONTRIBUTING.md)).
