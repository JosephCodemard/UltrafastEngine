# Capabilities and limitations

What UltrafastEngine models, how each part is validated, and what it does not do. Every option mentioned here is
listed with its unit and default in the [option reference](reference/options.md).

## Scope

The engine propagates an ultrashort pulse along **one beam path** through a sequence of elements. By default the
field is resolved in time and frequency (both polarisations) and the beam enters through its size — an effective
area that follows a beam model or the tracked Gaussian beam. Where the transverse structure matters, an element can
resolve it on a radial or cartesian grid. The pulse can be an envelope on a carrier (default) or the real
carrier-resolved field.

## What is modelled

| Area | Capabilities | Validated by |
|---|---|---|
| **Materials** | Sellmeier, Cauchy, tabulated, expression and callback indices; absolute (vacuum) indices with Ciddor air; the refractiveindex.info database (all formulas, tabulated n / k, pressure and temperature scaling); birefringent crystals; position-dependent Δn(x, y, z) | Ciddor air at 633 nm, N-BK7 and fused-silica nd, fused-silica GDD / TOD, argon refractivity (2·10⁻⁵ … 3 %) |
| **Linear optics** | dispersion to all orders from materials or ray-traced group delay; grating pairs (Treacy), chirped mirrors with GDD ripple, filters, apertures; Gaussian beam tracking through lenses, off-axis mirrors (astigmatic) and thermal lenses; spatio-temporal couplings (angular dispersion, spatial chirp, pulse-front tilt; Kostenbauder) from gratings, prisms and tilted plates | Treacy GDD, sampled stretcher, chirped-mirror GDD, ABCD focusing and off-axis f_t / f_s (10⁻⁴ … 2 %); grating + gap + grating = Treacy GDD (0.1 %), Hebling pulse-front tilt (0.2 %), 4f stretcher GDD = 0 |
| **Surfaces and losses** | Fresnel, thin-film stacks (transfer matrix), AR / HR / GDD / tabulated coatings, clipping, scatter, absorption, ND filters, thin-film polarisers; exact per-mechanism loss budget; peak fluence and LIDT margins | R + T = 1, Brewster, TIR, quarter-wave AR, Bragg mirror (10⁻⁹ … 10⁻¹²); loss budget closes exactly |
| **Polarisation** | Jones calculus with s/p basis transport along folded paths; thin and thick waveplates (multi-order, zero-order, compound, birefringent dispersion), polarisers, rotators, Faraday rotators | waveplates, polariser leakage, periscope basis transport, Faraday double pass |
| **Ghosts** | double reflections inside plates, windows, ND filters, rods, crystals, gain media and thick lenses, with mode overlap from walk-off and wedge; the replicas are part of the field, so nonlinear elements downstream act on them | delay 2 n_g d / c and level R², Kerr-generated pre-pulse ∝ B² (example 22) |
| **Kerr nonlinearity** | SPM / XPM with the full isotropic χ(3) tensor, delayed Raman response (Blow–Wood, Lin–Agrawal, Hollenbeck–Cantrell, user) as a tensor with isotropic and anisotropic parts (nonlinear ellipse rotation, circular vs linear light), self-steepening, user responses n(t, I) in co-moving and lab time; adaptive interaction-picture Runge–Kutta solver | SPM vs analytic, fundamental soliton, Raman / steepening directions; NER rotation and circular/linear phase vs analytic (≤ 2 %); **pyNLO**: higher-order soliton with and without Raman (< 0.5 %) |
| **Fibres** | Taylor coefficients or dispersion curves, γ or γ(z), loss and gain (flat, spectral, scaled to an output), soliton number, fission length, dispersive-wave resonance; one-photon-per-mode noise and a coherence study | fundamental soliton, D(λ) = Taylor, loss / gain, tapered B-integral, dispersive-wave phase matching; **pyNLO** with Lin–Agrawal Raman and gain (< 0.5 %); supercontinuum benchmark (Dudley 2006) |
| **Gases** | density-scaled refractivity and n₂; rotational Raman of N₂, O₂, CO₂, H₂ and air (tensor response); photo-ionisation (PPT, ADK, Keldysh, MPI) and Drude plasma with avalanche, recombination and ionisation losses | impulsive rotational response, PPT multiphoton order and tunnelling limit, electron density = ∫W dt, plasma blue shift |
| **χ(2) crystals** | SHG, SFG, DFG, OPA / OPCPA; birefringent and quasi-phase matching (averaged or domain-resolved, chirped gratings); non-collinear OPA, DFG and SFG, with crossing beams and recorded output angles; walk-off and spatial depletion on grids; phase-matching calculator | Armstrong tanh², OPA cosh² and Manley–Rowe, sinc² detuning, handbook angles; **pyNLO** DFG in PPLN (< 1 %) |
| **Gain and lasers** | Yb:YAG, Ti:sapphire, Nd:YAG and user media; rate equations with pumping, spectral Frantz–Nodvik saturation, pulse-train steady state, thermal lens; multipass and regenerative amplifiers; mode-locked oscillators (Haus master equation, Kerr-lens, SESAM) | small-signal gain, Frantz–Nodvik, soliton area theorem, steady-state energy balance |
| **Spatial models** | radial (Hankel) and cartesian grids per element: diffraction (exact or paraxial) by round and square apertures with an absorbing boundary; Gaussian, flat-top, tophat, square, Hermite-Gauss, Laguerre-Gauss, Bessel-Gauss, Airy and measured near-field beams; axicon, zone-plate, vortex, slit and file masks; self-focusing, Kerr lensing, Zernike and random wavefront errors, vector (Jones) fields, UPPE coupling, ionisation, spatial gain; profiles carried between elements | Gaussian w(z), M² = 1, lens focus, thin Kerr lens, UPPE = envelope for paraxial beams; apertures vs the Fresnel integral (< 1 % of the peak), Fresnel zones on the axis, Airy and sinc zeros; mode radii, Bessel core, Airy trajectory, zone-plate gain, Young's fringes |
| **Delayed light** | a second, long linear grid (`grid.tail`) carrying ghosts, mirror-substrate echoes, coating-ripple pedestals, delayed scatter (analytic surface model) and gas-line ring-down next to the nonlinear grid, energy-exact; window planning that names the satellite that does not fit ([Delayed light](reference/delayed-light.md)) | ledger = one large grid (10⁻³ … 10⁻⁵), echo delay 2 n_g d / c, ripple pedestal statistics |
| **Contrast** | pedestal models of the input (replicas, pre/post-pulses, ASE, spectral phase noise, exponential); third-order cross-correlator with noise floor, resolution, SHG acceptance | correlator artefacts, pedestal levels |
| **Instruments** | energy meter, spectrometer, scope, intensity / interferometric autocorrelator, FROG, d-scan, contrast, cross-correlator, polarimeter, cameras (imaging + sensor), wavefront sensor, ideal compressor, conversion, phase-matching curve, delay scan; virtual Wigner, field and space–time probes. Every instrument has its resolution / range parameters, a spectral `filter`, a `detector` (calibration, noise, floor, bit depth) and near / far-field `sampling` through pinholes, fibres or slits, and reports the settings it used. Detectors have spectral response tables, photon counting, saturation and stitched segments, and reusable presets ([Detector presets](reference/instruments.md#detector-presets)) | AC deconvolution, IAC 8:1, FROG marginals with acceptance and resolution, detector reproducibility, pinhole / slit transmissions |
| **Measured data** | measured input spectra (CSV or arrays; sorted, repeated wavelengths merged, background and threshold, recording resolution deconvolved, Gaussian tails beyond the recorded range); any instrument compared with a recording (CSV / NPZ / JSON curves, 2-D traces, scalar readings; linear or dB) on the recording's own axis and range, at its resolution | digitised spectra with out-of-order and repeated rows; floor restores the transform limit; overlap-only comparison; resolution matching |
| **Cavities and coherence** | `resonator`: round-trip matrix, stability, eigenmode, Fox-Li mode and loss; Gaussian-Schell partial coherence as coherent modes; cw sources | Kogelnik & Li eigenmode, Fox-Li vs ABCD, N^(−3/2) loss, M² law |
| **Analysis** | scans (grid, random, Latin hypercube, Sobol), Nelder–Mead optimisation, local and Sobol sensitivities with a frozen grid and prefix caching; FROG (PCGPA, ePIE) and d-scan retrieval; Gerchberg-Saxton wavefront retrieval; fits of transforms and parameters to measured data, backward propagation (fitting plugin) | B ∝ L scans, elasticities, Sobol indices; retrieval of chirped pulses (duration, GDD) |

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

* One beam path. Beam splitters, parallel arms and coherent recombination are not modelled (an element can produce
  by-products, such as the idler of an OPA, which are recorded but not propagated further).
* No ray tracing. Geometry enters through ray-traced data exported from OpticStudio (zemax plugin) or through the
  element parameters; prism pairs and Martinez stretchers need traced group delay.
* Cavities appear only inside the `oscillator` element (and regenerative amplifiers); a general resonator between
  arbitrary elements is not.

**Physics**

* Beam tracking handles simple astigmatism (principal axes x and y). The tracked beam stays Gaussian: a thermal lens is
  applied as the parabola that fits it best over the beam (its aberration needs a spatial model), and the dispersion of
  spatio-temporal couplings is applied to fourth order. A multipass amplifier puts its thermal lens on the tracked beam only when given the path between passes;
  a regenerative amplifier does not.
* One-dimensional elements do not change the beam shape: self-focusing above the critical power and aberration effects
  need a spatial model (the engine warns). Carried profiles take angular dispersion only with `physics.spatioTemporal` = `"profile"`, on cartesian grids.
* [Branched systems](reference/branches.md) cover splitters, arms, interference and loops, with mode overlap of tracked beams and carried profiles meeting. Beams crossing in a crystal remain collinear in the model. A `resonator` is a nested sequence; partial coherence is one run per mode, with no coupling between modes in nonlinear elements.
  Thermal stress birefringence is the beam-averaged result for a uniformly heated rod of a cubic host.
* Fibres are single-mode with a frequency-independent effective area; within one `fiber` element the dispersion is
  z-independent (use several elements for a taper's dispersion; γ(z) is supported). Fibre gain is unsaturated.
* The carrier-resolved field mode is scalar (one polarisation).
* ASE in amplifiers is one-dimensional; there is no thermal stress birefringence.
* In envelope mode, the Drude plasma term uses the carrier frequency.

**Data**

* Nonlinear coefficients, Raman dephasing times, gain cross sections and ionisation parameters are literature values
  with typical spreads of 10–30 %; override them for quantitative work. The built-in TGG index and the MgO:LN
  temperature coefficient are approximate; BiBO has no built-in d tensor.
* No fibre database: give the fibre's dispersion and γ.

**Software**

* The Python package and the NativeAOT C library are built from source (no PyPI wheels or NuGet package yet).
* GPU back-ends (`gpu` plugin: CUDA, OpenCL) run 1-D envelope and spatial envelope propagation; user responses,
  rotational Raman, position-dependent properties, carrier-resolved fields, χ(2) and gain run on the CPU. Apple GPUs
  are not used (no double precision); macOS uses the Accelerate FFT on the CPU ([GPU](plugins/gpu.md)).
* The zemax plugin compiles against the OpticStudio API, but its operand conventions should be checked against a
  running OpticStudio before quantitative use ([Zemax plugin](plugins/zemax.md#known-assumptions-to-confirm-on-your-installation)).
