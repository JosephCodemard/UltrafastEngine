# Roadmap

What is planned. The current capabilities are described in [Capabilities and limitations](docs/capabilities.md);
how the engine got here is in [Development history](docs/design/history.md). Fitting, Zemax and live data are
*plugins*: installed separately, loaded through public interfaces, never required by the core.

## Position and priorities (review of 2026-10-29)

UltrafastEngine is compared with LightPipes (monochromatic scalar beam optics, a catalogue of textbook cases) and
TorchOptics (differentiable Fourier optics, partial coherence). It is the only one of the three that treats the field as a
pulse with a spectrum in media with real physics: dispersion to all orders, χ(2) / χ(3) / Raman / plasma / gain, gases,
contrast diagnostics, measured-data comparison, validated against analytic and cross-code references. The others do
monochromatic beams through apertures and lenses with more breadth (beam sources, masks, demos), and TorchOptics has partial
coherence and autodiff. We do not chase their breadth for its own sake; we close the gaps that table-top ultrafast optics
needs. **Next: T0 (accuracy and trust), then T1 (core table-top capability).** Zemax moves after T1.

## T0 -- accuracy and trust (now)
- [x] Diffraction through an iris validated against the Fresnel integral (round / square, Gaussian / flat-top / uniform beams; Fresnel zones on the axis, Airy and sinc zeros), an under-resolved-aperture warning, area-averaged hard edges, the axis spike removed (absorbing boundary); [example 30](docs/examples/30-iris-diffraction/README.md), [tutorial 6](docs/tutorials/06-diffraction.md)
- [x] `mkdocs build --strict` passes (owner's machine)
- [x] Roadmap: GPU line corrected, `ROADMAP.md` and `docs/roadmap.md` in step; `PROJECT_STATUS.md` split into current status and history
- [x] Stale statements: test count no longer hard-coded; `docs/capabilities.md` covers delayed light, the hitran and gpu plugins, detector presets
- [x] Example check as a script (`build/example_check.py`): every README and tutorial command in strict mode, quoted output and key tables checked, `--against` diffs every `summary.json` number against a git ref -- *left:* prose and prose tables are checked by hand; example 07 and the C++ example are manual
- [x] Validation matrix generated from the tests (`docs/reference/validation-matrix.md`, checked like the schemas) -- *left:* the rest are behaviour and bookkeeping tests, reviewed 2026-10-30
- [x] Audit of tests that compare the engine only with itself: the matrix counts them apart (references starting with "cross-")
- [ ] Independent oracle: a LightPipes cross-check in a scratch venv, dev-time only and never shipped (iris, Fresnel propagation, lens focus, Talbot self-imaging, Young's slits), opt-in like the HAPI test

## T1 -- core table-top capability (next)
1. **Narrowband and monochromatic fast path**
   - [x] Linear spatial elements propagate only the stored frequencies of the profile; `transverse.spectralSamples`; absorbing boundary for linear elements; Hankel matrix built once per size (2048 points: 85 s → 4 s)
   - [x] A cw / single-frequency source: `shape: "cw"`, one spectral bin end to end
   - [x] Absorbing boundary in nonlinear spatial elements (an absorption in the layer)
   - [x] Faster Hankel matrix (symmetric products) -- *left:* a timing benchmark against LightPipes, with the cross-check
2. **Branches: splitters, arms, coherent recombination** -- the largest architectural limit (Michelson, Mach-Zehnder, pump-probe, OPA with its own pump arm). **Deferred by the owner until the existing engine is settled.**
   - [ ] Design first (`docs/design/`): a graph of elements with named ports; `beamSplitter` / `combiner` elements; per-arm delay and phase; arms recombined on a shared grid; how instruments, probes, studies and the loss budget see branches; reuse of the χ(2) aux beams
   - [ ] Implementation, a Michelson and a Mach-Zehnder example with analytic fringes, a pump-probe example
3. **Spatio-temporal couplings**
   - [x] Kostenbauder couplings with the tracked beam: gratings, tilted interfaces (prisms), plates, single-pass grating pairs; pulse-front tilt (Hebling, Akturk); the GDD they add (Treacy, 4f = 0)
   - [x] Angular dispersion on carried cartesian profiles (`physics.spatioTemporal: "profile"`), checked against the tracker and Treacy
   - [x] `spaceTime` instrument from the tracked beam's couplings; `prism` element
   - [x] Dispersion to fourth order from θ(ω): an exact ray bundle (Treacy's TOD reproduced)
4. **Beam sources and masks**
   - [x] Hermite-Gauss, Laguerre-Gauss, Bessel-Gauss and Airy beams as `initialProfile` kinds, with analytic propagation tests
   - [x] `mask` element: axicon, zone plate, vortex plate, slits, amplitude / phase maps from NPZ 
   - [x] Measured near field and wavefront as input (`initialProfile` kind `measured`: NPZ, background, resampling) 
   - [x] Gerchberg-Saxton phase retrieval from near / far-field images (`retrieve gerchbergSaxton`), PNG input, near-field centring and threshold

## T2 -- after T1
- [x] Thermal lenses of gain media in the Gaussian beam tracking -- *left:* regenerative amplifiers
- [x] Thermal stress birefringence and depolarisation (Koechner, uniformly heated cubic rods) -- *left:* the spatial (four-lobed) pattern
- [x] Partial coherence: Gaussian-Schell beams as coherent modes -- *left:* coupling between modes in nonlinear elements; other source models
- [x] Resonators around a nested sequence: stability, eigenmode, Fox-Li -- *left:* a cycle in the element graph, with branches

## Engineering
- [x] GitHub Actions on pushes and pull requests (`.github/workflows/ci.yml`, Linux): build net10.0 and net48, CLI compiled for every platform, C# tests, Python tests with pyNLO, `mkdocs build --strict`, the example check -- *left:* first run on GitHub
- [ ] Release workflow: a version tag builds the packages per platform (`build.sh`; NativeAOT on its own OS), runs smoke tests and publishes a GitHub release; the ZOS-API assemblies never packaged (checked)
- [ ] Choose the GPU back-end at install (`pip install ultrafast[cuda]` / `[opencl]`, plugin packages), reported by `ultrafast devices` (agreed)
- [ ] GPU coverage: user responses as device kernels, χ(2), gain and carrier-resolved propagation on the device
- [ ] Metal / Vulkan single-precision back-end, only after a study of which propagation steps tolerate FP32 (Apple GPUs have no FP64)
- [ ] Speed: profile the hot paths (Hankel O(M² N), cartesian FFTs, instruments) and keep them allocation-free

## Zemax plugin (OpticStudio depth) -- after T1
- [ ] Zernike aberrations exported from OpticStudio applied to spatial fields
- [ ] Paraxial ABCD matrices between elements from paraxial-ray operands (exact transverse transport)
- [ ] Prism pairs and Martinez stretchers with lens aberrations as analytic elements
- [ ] Crystal surfaces recognised and mapped to `crystal` elements
- [ ] Output basis vectors in results (lab-frame polarisation after folded paths)
- [ ] OpticStudio User Extension (menu-integrated) in addition to the Interactive Extension

## Delayed light and gas (done; what is left)
- [x] 2-D plate at P / P_cr ~ 100: seeded noise / ripple, diagnostics, Bespalov-Talanov check, example 29 -- *left:* a 256 x 256 run at 2^13 time points on a GPU
- [x] Detector response tables, photon counting and presets (user-defined) -- *left:* a catalogue of real instruments (needs their manuals / calibrations)
- [x] Analytic surface scatter for `delayedScatter` -- *left:* non-Gaussian roughness spectra (fractal / PSD tables), bulk scatter from a measured BRDF
- [x] TIPS partition sums for the hitran plugin; H2O, CO2 and O2 vs HAPI -- *left:* a comparison with a measured air transmission; continuum absorption
- [x] Tail: smooth elements skip the exact step; first-order cross-phase -- *left:* second-order tail nonlinearity (the tail's own self-phase)

## Fibres
- [ ] Frequency-dependent effective area and multimode fibres
- [ ] z-dependent dispersion within one fibre element (tapers)
- [ ] Saturated fibre gain (rate equations along the fibre)

## Fitting plugin (after the transforms of 2.1.8)
- [ ] General low-rank and learned (ML) field operators as built-in transform kinds
- [ ] Ensemble Kalman and Bayesian-optimisation estimators
- [ ] Spatio-temporal transform operators (pulse-front tilt, spatial chirp)
- [ ] Backward propagation through saturated gain and frequency conversion beyond linearisation
- [ ] Autodiff, if inverse design becomes a goal: as a plugin, linear parts first

## Plugins and distribution
- [x] GPU compute back-ends as a plugin: CUDA and OpenCL (1-D and spatial envelope); native CPU FFTs (Accelerate, FFTW)
- [ ] Materials and coatings data: vendor R / T curves, temperature-dependent index; a continuum absorption model (MT_CKD-like) for the hitran plugin
- [ ] Wheels for Windows / macOS / Linux on PyPI, NuGet package for the core
- [ ] Live-data plugin (streamed recordings, a digital twin that re-fits as data arrive)
- [ ] ML / PINN estimator and surrogate plugins

Ideas and priorities are tracked in issues; contributions are welcome ([CONTRIBUTING.md](CONTRIBUTING.md)).
