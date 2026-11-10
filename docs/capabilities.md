# Capabilities and limitations

What UltrafastEngine models, how each part is validated, and what it does not do. Every option mentioned here is
listed with its unit and default in the [option reference](reference/options.md).

## Scope

UltrafastEngine is built for **table-top, open-air ultrafast optics**: a pulse that travels through mirrors, gratings,
prisms, lenses, plates, crystals, gain media, gas-filled paths and optical instruments on an optical table, with some
support for single-mode fibre. It is not a general waveguide, multimode-fibre or laser-cavity design tool, and it does
not trace rays (geometry comes from element parameters or from a ray trace exported by the zemax plugin).

The pulse is a field resolved in time and frequency (both polarisations). The beam enters in one of three ways: through
its size (an effective area that follows a beam model or the tracked Gaussian beam), as a carried transverse profile on a
radial or cartesian grid for the elements that need the transverse structure, or as the real carrier-resolved field
instead of an envelope on a carrier.

A system is a **sequence** of elements (the default) or a **branched** graph of elements with named ports
([Branched systems](reference/branches.md)): beam splitters, parallel arms that recombine with the right phase and mode
overlap, delays, by-products of a crystal routed onwards, and loops.

## Supported elements

Every element type below is declared in `src/UltrafastEngine.Core/Schema/BuiltIn*.cs`; all keys, units and defaults are
in the [option reference](reference/options.md#elements). Aliases in brackets.

| Element | What it models | Documented in |
|---|---|---|
| `dispersion` | linear dispersion from sampled group delay, spectral phase or Taylor coefficients | [System file](reference/system-file.md) |
| `bulk` (`medium`), `gap` | propagation in a material or in air (linear, or with Kerr, Raman, plasma and, with a `transverse` model, diffraction and self-focusing) | [System file](reference/system-file.md), [Nonlinear physics](reference/nonlinear.md), [Spatial models](reference/transverse.md) |
| `plate` (`window`) | plane-parallel plate: two surfaces, bulk dispersion, absorption, nonlinearity, ghost replicas | [Components](reference/components.md#plate) |
| `interface` (`surface`), `mirror`, `grating` | Fresnel or coated surface, curved or off-axis mirror, diffraction grating; angle of incidence, s/p phases, clipping, scatter, damage threshold | [System file](reference/system-file.md), [Coatings](reference/coatings.md), [Components](reference/components.md) |
| `gratingPair` | Treacy grating pair (exact dispersion; negative separation is a stretcher) | [System file](reference/system-file.md) |
| `prism` | prism in air: two faces and the glass on the chief ray; angular dispersion of the tracked beam | [Components](reference/components.md#spatio-temporal-couplings) |
| `chirpedMirror` | GDD, TOD, FOD per bounce, with optional GDD ripple | [System file](reference/system-file.md), [Components](reference/components.md#gdd-ripple) |
| `lens` | thin lens, chromatic with a material; acts on a carried profile | [System file](reference/system-file.md) |
| `aperture`, `mask` | hard or soft aperture; axicon, zone plate, vortex, slit and file masks on the carried profile | [Spatial models](reference/transverse.md#diffraction-and-apertures) |
| `wavefront` (`aberration`) | Zernike or random wavefront error and optional focal power | [Components](reference/components.md#aberrations) |
| `filter`, `custom` | any spectral filter model (transmission and phase) | [System file](reference/system-file.md), [Models](reference/models.md) |
| `ndFilter` | absorptive or reflective neutral-density filter, substrate, ghosts | [Components](reference/components.md#ndfilter) |
| `waveplate`, `polarizer`, `rotator`, `thinFilmPolarizer` (`tfp`), `faradayRotator` (`faraday`) | Jones-calculus polarisation optics, multi-order and zero-order waveplates, thin-film polarisers, non-reciprocal rotation | [Components](reference/components.md#waveplates), [Surfaces and polarisation](reference/coatings.md) |
| `beamSplitter`, `polarizingSplitter`, `dichroic`, `leakMirror`, `delay` | splitting, recombination and delay in a Branched system | [Branched systems](reference/branches.md) |
| `crystal` | χ(2): SHG, SFG, DFG, OPA / OPCPA, collinear and non-collinear, birefringent and quasi-phase matching, χ(3) in the crystal | [Crystals](reference/crystals.md) |
| `fiber` (`fibre`, `waveguide`) | single-mode fibre: Taylor betas or a dispersion curve, γ, loss, gain, Raman, noise, soliton and dispersive-wave diagnostics | [Fibres](reference/fibers.md) |
| `gain`, `amplifier` | pumped gain media (rate equations, spectral saturation, thermal lens, optional stress birefringence, ASE); multipass and regenerative amplifiers | [Gain media](reference/gain.md) |
| `oscillator`, `saturableAbsorber` (`sesam`) | mode-locked oscillators (parametric, Haus, cavity model); SESAM, fast and Kerr-lens absorbers | [Oscillators](reference/oscillators.md) |
| `resonator` | passive resonator around nested elements: round-trip matrix, stability, eigenmode, Fox-Li mode and loss | [Oscillators](reference/oscillators.md#resonators) |
| `user` | your own function of the main beam's spectrum (Python, C#, C) | [Active elements](reference/active-elements.md#user-elements) |
| `probe` (`marker`) | a named place for snapshots and instruments; does nothing to the beam | [System file](reference/system-file.md) |
| `deformableMirror`, `slm`, `spectralShaper`, `aopdf` (ao plugin) | parametric adaptive-optics devices: a deformable mirror and an SLM (cells, influence, stroke, grey levels, liquid-crystal dispersion), a pixelated spectral shaper and an AOPDF; holograms, wavefront control and sensorless optimisers in Python | [AO plugin](plugins/ao.md) |
| `transform` (fitting plugin) | fittable spectral phase and amplitude, energy, polarisation, contrast, wavefront | [Fitting plugin](plugins/fitting.md#transform-elements) |

Instruments (energy meter, spectrometer, scope, autocorrelator, FROG, d-scan, cross-correlator, polarimeter, cameras and
more) are listed in [Instruments](reference/instruments.md).

## What is modelled

| Area | Capabilities | Validated by |
|---|---|---|
| **Materials** | Sellmeier, Cauchy, tabulated, expression and callback indices; absolute (vacuum) indices with Ciddor air; the refractiveindex.info database (all formulas, tabulated n / k, pressure and temperature scaling); birefringent crystals; position-dependent Δn(x, y, z) | Ciddor air at 633 nm, N-BK7 and fused-silica nd, fused-silica GDD / TOD, argon refractivity (2·10⁻⁵ … 3 %) |
| **Linear optics** | dispersion to all orders from materials or ray-traced group delay; grating pairs (Treacy), chirped mirrors with GDD ripple, filters, apertures; Gaussian beam tracking through lenses, off-axis mirrors (astigmatic) and thermal lenses; spatio-temporal couplings (angular dispersion, spatial chirp, pulse-front tilt; Kostenbauder) from gratings, prisms and tilted plates | Treacy GDD, sampled stretcher, chirped-mirror GDD, ABCD focusing and off-axis f_t / f_s (10⁻⁴ … 2 %); grating + gap + grating = Treacy GDD (0.1 %), Hebling pulse-front tilt (0.2 %), 4f stretcher GDD = 0 |
| **Surfaces and losses** | Fresnel, thin-film stacks (transfer matrix), AR / HR / GDD / tabulated coatings, clipping, scatter, absorption, ND filters, thin-film polarisers; exact per-mechanism loss budget; peak fluence and LIDT margins | R + T = 1, Brewster, TIR, quarter-wave AR, Bragg mirror (10⁻⁹ … 10⁻¹²); loss budget closes exactly |
| **Polarisation** | Jones calculus with s/p basis transport along folded paths; thin and thick waveplates (multi-order, zero-order, compound, birefringent dispersion), polarisers, rotators, Faraday rotators | waveplates, polariser leakage, periscope basis transport, Faraday double pass |
| **Ghosts** | double reflections inside plates, windows, ND filters, rods, crystals, gain media and thick lenses, with mode overlap from walk-off and wedge; the replicas are part of the field, so nonlinear elements downstream act on them | delay 2 n_g d / c and level R², Kerr-generated pre-pulse ∝ B² (example 22) |
| **Kerr nonlinearity** | SPM / XPM with the full isotropic χ(3) tensor, delayed Raman response (Blow–Wood, Lin–Agrawal, Hollenbeck–Cantrell, user) as a tensor with isotropic and anisotropic parts (nonlinear ellipse rotation, circular vs linear light), self-steepening, user responses n(t, I) in co-moving and lab time; adaptive interaction-picture Runge–Kutta solver | SPM vs analytic, fundamental soliton, Raman / steepening directions; NER rotation and circular/linear phase vs analytic (≤ 2 %); **pyNLO**: higher-order soliton with and without Raman (< 0.5 %) |
| **Liquids and cubic crystals** | built-in CS2 (Kedenburg index, electronic plus orientational n2 with a purely anisotropic delayed response) and BaF2; cubic Kerr anisotropy σ with a cut axis and azimuth for cross-polarised-wave generation; a delayed response of any kind can carry an anisotropic fraction up to 1.5 (purely orientational) | CS2 index and GDD (0.3 %), circular/linear phase and NER against the Hellwarth tensor, XPW ∝ sin²(4β) in a [001] cut; the CS2 n2 and split and the BaF2 n2 and σ are unverified literature values |
| **Fibres** | Taylor coefficients or dispersion curves, γ or γ(z), loss and gain (flat, spectral, scaled to an output), soliton number, fission length, dispersive-wave resonance; one-photon-per-mode noise and a coherence study | fundamental soliton, D(λ) = Taylor, loss / gain, tapered B-integral, dispersive-wave phase matching; **pyNLO** with Lin–Agrawal Raman and gain (< 0.5 %); supercontinuum benchmark (Dudley 2006) |
| **Gases** | density-scaled refractivity and n₂; rotational Raman of N₂, O₂, CO₂, H₂ and air (tensor response); photo-ionisation (PPT, ADK, Keldysh, MPI) and Drude plasma with avalanche, recombination and ionisation losses | impulsive rotational response, PPT multiphoton order and tunnelling limit, electron density = ∫W dt, plasma blue shift |
| **χ(2) crystals** | SHG, SFG, DFG, OPA / OPCPA; birefringent and quasi-phase matching (averaged or domain-resolved, chirped gratings); non-collinear OPA, DFG and SFG, with crossing beams and recorded output angles; walk-off and spatial depletion on grids; phase-matching calculator | Armstrong tanh², OPA cosh² and Manley–Rowe, sinc² detuning, handbook angles; **pyNLO** DFG in PPLN (< 1 %) |
| **Gain and lasers** | Yb:YAG, Ti:sapphire, Nd:YAG and user media; rate equations with pumping, spectral Frantz–Nodvik saturation, pulse-train steady state, thermal lens; multipass and regenerative amplifiers; mode-locked oscillators (Haus master equation, Kerr-lens, SESAM) | small-signal gain, Frantz–Nodvik, soliton area theorem, steady-state energy balance |
| **Spatial models** | radial (Hankel) and cartesian grids per element: diffraction (exact or paraxial) by round and square apertures with an absorbing boundary; Gaussian, flat-top, tophat, square, Hermite-Gauss, Laguerre-Gauss, Bessel-Gauss, Airy and measured near-field beams; axicon, zone-plate, vortex, slit and file masks; self-focusing, Kerr lensing, Zernike and random wavefront errors, vector (Jones) fields, UPPE coupling, ionisation, spatial gain; profiles carried between elements | Gaussian w(z), M² = 1, lens focus, thin Kerr lens, UPPE = envelope for paraxial beams; apertures vs the Fresnel integral (< 1 % of the peak), Fresnel zones on the axis, Airy and sinc zeros; mode radii, Bessel core, Airy trajectory, zone-plate gain, Young's fringes |
| **Pixelated modulators and tiled arrays** | `modulator` element (SLM, deformable-mirror surface, tile mask): cell grid with pitch, offset, rotation, fill factor, gaps, quantisation, wrapping, cross-talk (Gaussian or kernel), zero-order leak; maps as arrays, callbacks (no files; a loop reads the callback each trial) or NPZ; chromatic rules `opd`, `fixedPhase`, `index` (liquid-crystal Δn(λ)); coherent tile source (square, hex, ring, explicit; piston, tilt, delay); wavefront-sensor slope maps; frequency streaming and profile retention for 1920 × 1080 panels ([Modulator](reference/modulator.md)) | tilt f θ, thin-lens focus, N-level blaze [sin(π/N)/(π/N)]², replica orders, grating dispersion x₁ ∝ λ, N × peak of N tiles, \|Σ e^{iφ}\|²/N², delay overlap, streaming = all at once (10⁻¹²) |
| **Delayed light** | a second, long linear grid (`grid.tail`) carrying ghosts, mirror-substrate echoes, coating-ripple pedestals, delayed scatter (analytic surface model) and gas-line ring-down next to the nonlinear grid, energy-exact; window planning that names the satellite that does not fit ([Delayed light](reference/delayed-light.md)) | ledger = one large grid (10⁻³ … 10⁻⁵), echo delay 2 n_g d / c, ripple pedestal statistics |
| **Contrast** | pedestal models of the input (replicas, pre/post-pulses, ASE, spectral phase noise, exponential, or a measured contrast trace); third-order cross-correlator with noise floor, resolution, SHG acceptance | correlator artefacts, pedestal levels, measured pedestal reproduced |
| **Instruments** | energy meter, spectrometer, scope, intensity / interferometric autocorrelator, FROG (SHG, polarisation gate, self-diffraction, XFROG), d-scan, contrast, cross-correlator (with a gate-arm option), single-shot time-to-space cross-correlator, polarimeter, cameras (imaging + sensor), wavefront sensor, ideal compressor, conversion, phase-matching curve, delay scan; virtual Wigner, field and space–time probes. Every instrument has its resolution / range parameters, a spectral `filter`, a `detector` (calibration, noise, floor, bit depth) and near / far-field `sampling` through pinholes, fibres or slits, and reports the settings it used. Detectors have spectral response tables, photon counting, saturation and stitched segments, and reusable presets ([Detector presets](reference/instruments.md#detector-presets)) | AC deconvolution, IAC 8:1, FROG marginals with acceptance and resolution, detector reproducibility, pinhole / slit transmissions |
| **Measured data** | measured input spectra (CSV or arrays; sorted, repeated wavelengths merged, background and threshold, recording resolution deconvolved, Gaussian tails beyond the recorded range); any instrument compared with a recording (CSV / NPZ / JSON curves, 2-D traces, scalar readings; linear or dB) on the recording's own axis and range, at its resolution | digitised spectra with out-of-order and repeated rows; floor restores the transform limit; overlap-only comparison; resolution matching |
| **Branched systems, cavities and coherence** | beam splitters, polarising splitters, dichroics, leaky mirrors, delays; interferometers (Mach-Zehnder, Michelson, f-2f) with the carrier phase and mode overlap of the meeting beams; graph loops (`ringDown`, `steadyState`); `resonator`: round-trip matrix, stability, eigenmode, Fox-Li mode and loss; Gaussian-Schell partial coherence as coherent modes; cw sources | Michelson dark port and fringe period λ/2, Mach-Zehnder fringes vs path difference, Fabry-Perot loop = Airy function, f-2f fringes vs CEP, Kogelnik & Li eigenmode, Fox-Li vs ABCD, N^(−3/2) loss, M² law |
| **Analysis** | scans (grid, random, Latin hypercube, Sobol), Nelder–Mead optimisation, local and Sobol sensitivities with a frozen grid and prefix caching; FROG (PCGPA, ePIE; SHG, PG, SD, XFROG; multi-trace double FROG) and d-scan retrieval; the Kerr-matched mode of a multi-pass cell (`cellMode`); Gerchberg-Saxton wavefront retrieval; fits of transforms and parameters to measured data, backward propagation (fitting plugin) | B ∝ L scans, elasticities, Sobol indices; retrieval of chirped pulses (duration, GDD) |

Test details: [Testing and validation](testing.md). Synthetic measurements with known answers to try the analysis
tools: [Assets](assets/README.md).

## Plugins

Capabilities that depend on outside software or are not needed by everyone are plugins, discovered at start-up:

* **fitting** — `transform` elements (fittable spectral phase and amplitude, energy, polarisation, replicas, phase
  noise, ASE pedestals, wavefront errors, with passivity, causality and smoothness constraints); fits to the
  instruments' recorded data (the core's `measured` comparisons) with
  Levenberg–Marquardt, CMA-ES or MCMC (or your own estimator), priors, instrument-response parameters, three result
  layers (simulated, inferred, believed), an influence map and provenance; a fitted system file for new inputs; and
  backward propagation of a measured field to the input.
* **zemax** — exports a sequential OpticStudio system (group delay along the real ray, angles of incidence, coatings,
  beam sizes) to a system file. Runs on Windows with OpticStudio.
* **gpu** — CUDA and OpenCL compute back-ends for 1-D and spatial envelope propagation (double precision), and native CPU
  FFTs (Apple Accelerate, FFTW) ([GPU](plugins/gpu.md)).
* **hitran** — gas absorption and dispersion from HITRAN line lists (Voigt lines, TIPS partition sums) in air gaps and
  `environment.air`, checked against HAPI for H₂O, CO₂ and O₂ ([HITRAN](plugins/hitran.md)).

Anyone can add element types, instruments, models, run modes, retrieval methods, compute back-ends and CLI commands
the same way ([Plugin system](reference/plugins.md)).

## Customisation

* Every physical ingredient is a **model** — data, an expression or a function in C#, Python or C — and can be
  replaced per material, per element or globally, from the run spec, without editing the system file
  ([Models and overrides](reference/models.md)).
* **User elements** and **user responses** are functions that receive the field
  ([Active elements](reference/active-elements.md#user-elements), [Extending](reference/extending.md)).
* The FFT and the compute back-end (where nonlinear segments run) are replaceable ([Speed](reference/speed.md)).

## Limitations

**Optical layout**

* No ray tracing. Geometry enters through ray-traced data exported from OpticStudio (zemax plugin) or through the
  element parameters; prism pairs and Martinez stretchers need traced group delay.
* [Branched systems](reference/branches.md): mode overlap of meeting beams assumes simple Gaussian beams at one plane and
  ignores M²; a linked crystal port takes the strongest packet only; merged carried profiles share one transverse shape
  per stored frequency; a graph loop has one direction (a linear cavity is written as separate forward and return
  entries); `resonator` is a nested sequence, not a graph.
* A crystal makes by-products (the idler of an OPA, for example). In a Sequential system they are recorded but not
  propagated; in a Branched system each is an output port that can be routed on.
* Partial coherence is one run per coherent mode, with no coupling between modes in nonlinear elements.

**Physics**

* Beam tracking handles simple astigmatism (principal axes x and y). The tracked beam stays Gaussian: a thermal lens is
  applied as the parabola that fits it best over the beam (its aberration needs a spatial model), and the dispersion of
  spatio-temporal couplings is applied to fourth order (GDD, TOD, FOD). A multipass amplifier puts its thermal lens on the
  tracked beam only when given the path between passes; a regenerative amplifier does not.
* One-dimensional elements do not change the beam shape: self-focusing above the critical power and aberration effects
  need a spatial model (the engine warns). Carried profiles take angular dispersion only with
  `physics.spatioTemporal` = `"profile"`, on cartesian grids.
* Thermal stress birefringence of a gain rod (`thermalLens.stress`, cubic hosts such as YAG) is the beam-averaged result
  for a uniformly heated rod: a depolarised fraction moved between x and y, not the four-lobed pattern. It is off by
  default, and the thermal lens itself is radial with no end-face bulging.
* Fibres are single-mode with a frequency-independent effective area; within one `fiber` element the dispersion is
  z-independent (use several elements for a taper's dispersion; γ(z) is supported). Fibre gain is unsaturated. There is
  no hollow-capillary (Marcatili) waveguide model.
* The carrier-resolved field mode is scalar (one polarisation; a y component is propagated linearly with a warning).
* ASE in amplifiers is one-dimensional, in the signal polarisation, and covers the simulated time window only.
* In envelope mode, the Drude plasma term uses the carrier frequency.

**Numerics and models**

* A pulse of about one optical cycle broadened by SPM until its spectrum reaches zero frequency (for example 10 fs at 2 µm, B = 3) is cut at zero frequency (the envelope model has no negative frequencies); the engine warns and books the lost energy as `spectrum at non-positive frequency`. Use the carrier-resolved model there.
* The window frame follows the group delay at the lattice centre, not at the pulse carrier: a dispersive pulse whose carrier sits off the lattice drifts by GDD x offset x L inside the window (73 fs for a 1 ps soliton over 3 dispersion lengths at 515 nm). `snapCarrier: false` removes it.
* A continuous-wave source is one frequency-grid line and sits on the nearest bin (a relative error of about 3e-7 in its frequency on the default grid); only pulses are built at the requested carrier.
* The grid planner adds the memory of Raman and orientational delayed responses to the window; a generic `timeResponse` or a gas rotational revival is not planned for, so give a window that holds them.
* A round aperture ignores `decenterXM` on a one-dimensional beam (a rectangular aperture does not). The tracked-beam overlap of crossing beams in a non-collinear crystal is a Gaussian-overlap estimate (a 2° crossing at 64 / 150 µm radii costs about half of the collinear gain); use a transverse model when it matters.
* The cubic Kerr anisotropy (cross-polarised waves) runs in 1-D envelope propagation only: spatial (`transverse`) and carrier-resolved fields throw, and GPU back-ends do not take it.
* `singleShotCorrelator` maps position to delay and applies the crossing geometry's window and resolution; it does not propagate the beams through the crystal (no conversion efficiency, depletion, beam envelope or walk-off), and air scatter, stripe filters, reflection spikes and fibre-array readout are not modelled.
* `cellMode` handles round beams and peak power; it is a command and Python function, not an element option.
* Built-in values that are not checked against the original sources: CS2 n2, orientational fraction and time constants; BaF2 n2 and σ.

**Data**

* Nonlinear coefficients, Raman dephasing times, gain cross sections and ionisation parameters are literature values
  with typical spreads of 10–30 %; override them for quantitative work. The built-in TGG index and the MgO:LN
  temperature coefficient are approximate; BiBO has no built-in d tensor.
* No fibre database: give the fibre's dispersion and γ.

**Software**

* The Python package and the NativeAOT C library are built from source (no PyPI wheels or NuGet package yet).
* GPU back-ends (`gpu` plugin: CUDA, OpenCL) run 1-D envelope and spatial envelope propagation; user responses,
  rotational Raman, position-dependent properties, carrier-resolved fields, χ(2), amplifiers and ionisation run on the CPU. Apple GPUs
  are not used (no double precision); macOS uses the Accelerate FFT on the CPU ([GPU](plugins/gpu.md)).
* The zemax plugin compiles against the OpticStudio API, but its operand conventions should be checked against a
  running OpticStudio before quantitative use ([Zemax plugin](plugins/zemax.md#known-assumptions-to-confirm-on-your-installation)).
