# Testing and validation

The complete list of tests, with the analytic result, published value or other code each one is checked against, is the
generated [validation matrix](reference/validation-matrix.md). This page is the overview by subject.

Continuous integration (`.github/workflows/ci.yml`) runs on Linux: it builds net10.0 and net48, compiles the CLI for every platform, and runs the C#
tests on both runtimes, the Python tests with pyNLO, the strict docs build and `python build/example_check.py`, which reruns every example and tutorial and
checks the numbers they quote. OpticStudio cannot run there: the Zemax binding stays a manual check.

## Running

```bash
dotnet run --project tests/UltrafastEngine.Tests -f net10.0             # all (slow tests included only when filtered or with --slow)
dotnet run --project tests/UltrafastEngine.Tests -f net10.0 -- Soliton  # name filter
dotnet run --project tests/UltrafastEngine.Tests -f net10.0 -- --slow --verbose
mono tests/UltrafastEngine.Tests/bin/Debug/net48/UltrafastEngine.Tests.exe   # .NET Framework build (macOS/Linux)
python -m pytest python/tests                                            # Python bindings + pyNLO cross-checks
ULTRAFAST_SLOW=1 python -m pytest python/tests                           # + slow Python tests (high-gain DFG vs pyNLO)
UltrafastEngine.Zemax.exe selftest                                       # binding on fake systems
```

The C# runner is a console app with its own `[Test]` attribute (no NuGet packages, runs on .NET 10 and .NET
Framework 4.8/Mono). In Visual Studio set it as start-up project and press Ctrl+F5.

## What is checked

| area | test | criterion |
|---|---|---|
| numerics | JSON round trip, expression engine, NPZ, FFT round trip / Parseval / delay sign, dispersion extraction | exact / 1e-12 |
| materials | Ciddor air at 633 nm, N-BK7 and fused silica n_d, fused-silica GDD/TOD at 800 nm, argon refractivity | 2e-5 ... 3 % |
| surfaces | Fresnel R+T = 1, normal-incidence R, Brewster r_p = 0, total internal reflection, quarter-wave AR, Bragg-mirror reflectance (analytic) and its group-delay sign | 1e-9 ... 1e-12 |
| linear optics | Treacy grating pair GDD, sampled stretcher (FWHM, arrival time, GDD, chirp fit, auto window), chirped-mirror GDD per bounce, Beer-Lambert | 1e-4 ... 2 % |
| nonlinear | SPM broadening of a Gaussian vs analytic (Agrawal 4.1.15) and B-integral; fundamental soliton over a period; adaptive vs fixed stepper; self-steepening and Raman directions; envelope vs carrier-resolved; callback/expression == built-in Kerr; Bessel sidebands of lab-time modulation | 1e-9 ... 4 % |
| polarisation | waveplates, polariser leakage, Brewster window s/p, periscope basis transport | 1e-3 ... 1e-12 |
| vector Kerr and Raman (NER) | circular / elliptical B-integral (2/3, 1 − sin²2χ/3 of linear); circular at 1.5× energy has the linear spectrum; ellipse rotation B sin(2χ)/3 (electronic) and B sin(2χ)[(1 − fR)/3 + fR b/2] with the anisotropic silica Raman; circular/linear phase (1 − fR)2/3 + fR(1 − b/2); linear light unchanged by the split; Lin-Agrawal anisotropic share | 0.5 ... 2 % |
| system | loss budget closes; aperture clip formula; override priority and provenance; material override beats traced data; system file round trip and validation | exact |
| instruments | AC deconvolution (Gaussian, sech^2), IAC 8:1, spectrometer FWHM, ideal compressor, polarimeter, energy meter, d-scan optimum, FROG marginal | 1 ... 5 % |
| Zemax sampling | Treacy pair through the sampler == Treacy + slab (and the phase-path model has the wrong sign); argon MPC: passes, mirrors, coating rules, GBPS/GBPW waist, per-pass B; tilted slab AOI, AR rule, Fresnel; record/replay; beam profile reconstruction | 0.5 % / exact |
| χ(2) crystals | SHG vs Armstrong tanh² (0.02-85 % conversion); OPA vs cosh² gain and Manley-Rowe; sinc² angle detuning; cascaded-χ(2) phase; QPM averaged vs domains; calculator vs handbook angles (BBO, LBO, KDP, KTP, NC magic angle, PPLN periods); radial vs 1-D overlap; aux beam from saved results; conversion / delayScan / phaseMatchingCurve instruments; C# user element | 2e-4 ... 2 % / 0.15° |
| spatial | Hankel quadrature, Gaussian w(z) radial and cartesian, M² = 1, lens focus, thin Kerr lens, energy of a carried profile after spectral broadening, beamProfile instrument | 1e-12 ... 2 % |
| measured input spectra | repeated wavelength no longer NaN; 29 out-of-order rows sorted and reported; non-finite rows dropped; a 0.66 % floor shortens the transform limit and `background` (auto / number) restores it; auto background from the darker edge of a cut-off spectrum; threshold; CSV in descending order; bad background is an error | 0.2 ... 2 % |
| comparisons, satellite window | recording wider than the simulated instrument compared on the overlap only; coarser recording matched by blurring; 20 ghosted plates keep a modest window; satellite room sums coincident delays coherently and honours `grid.satelliteLevel` | exact / 1 % |
| sampling, measured data, fit | near/far-field pinhole and slit transmissions vs Gaussian analytics, off-axis pinhole, far-field fibre colour shift on a 1-D beam; CSV/NPZ loading and units; zero residual when data = simulation (curve, d-scan trace, values); normalisations and background; missing files become warnings; pseudo-inverse null space; fit paths (lists, overrides); fit recovers energy and chirp from a spectrum and autocorrelation; unidentifiable parameter flagged; Python comparison, fit and figures | 2 % / 1 % / exact |
| grids | exact resampling round trip between derived grids; carrier change SHG → glass; chirped input fits the auto window | 1e-9 |
| components | Gaussian beam tracking (lens, off-axis mirrors), Zernike / Strehl, plate and end-face ghosts (delay, level, Kerr pre-pulse), ND OD(λ), thick and compound waveplates, TFP Tp / Ts, Faraday rotation and dispersion, GDD ripple, pedestals | 10⁻⁴ ... 2 % |
| gases, ionisation | rotational impulse response and revivals, molecular gas data, electronic + rotational n2, PPT / ADK / Keldysh rates and limits, Dawson and elliptic functions, electron density, plasma blue shift, filament clamping (UPPE vs envelope) | 10⁻⁹ ... 5 % |
| fibres | fundamental soliton over 5 L_D, D(λ) = Taylor, loss / gain / gain goal, tapered γ(z) B-integral, Raman kernels (unit integral, causality, Hollenbeck peak 13.2 THz), dispersive-wave phase matching, quantum-noise energy, coherence of a soliton and of a long-pulse supercontinuum | 10⁻³ ... 3 % |
| instruments (device) | spectrometer normalisation modes, settings echo, FROG acceptance (product) and resolution (convolution), AC resolution and acceptance, detector calibration / noise / bits reproducibility, filter in front, polarimeter extinction, every enum value accepted | 10⁻¹² ... 5 % |
| speed, studies, retrieval | mixed-radix / Bluestein FFT vs DFT, SIMD = scalar, scans with and without the prefix cache, optimisation, elasticities, Sobol indices; PCGPA, ePIE and d-scan retrieval of chirped pulses | 10⁻¹² ... 5 % |
| transforms, fitting | transform phase == dispersion element; filter, energy and loss budget; replica level and grid room; Jones rotation / quarter wave; minimum phase is causal; not-a-knot spline exact for parabolas; every operator inverts; wavefront on a radial profile and 1-D note; ASE pedestal level; schema accepts fit objects | 10⁻¹² ... 2 % |
| backward propagation | linear chain (glass, transform, dispersion) recovers a 60 fs input; nonlinear plate (B > 2) un-broadens to the input bandwidth and peak power; active elements refused / identity / linearised | 1 % |
| fits | marked transform parameters from a spectrum (layers, influence, SHA-256 provenance, fitted system on a new input); GDD and TOD from a d-scan; prior-only parameter (σ = prior, influence 100 % priors); instrument resolution as a parameter; CMA-ES from a far start; MCMC 95 % intervals contain the truth; smooth spline phase from a FROG | 0.01 ... 5 % |
| resolution and sampling | `axis` explicit pixels with pixel bins; axis resolution == spectrometer resolution; coarse AC delay steps; dB curves follow their linear curves; input-spectrum deconvolution recovers 2 nm from a 2.83 nm recording; Gaussian tails restore a truncated spectrum; measured-resolution mismatch reported (coarser simulated instrument) or matched (coarser recording) | 1 ... 6 % |
| CPU FFT and compute (`ComputeTests`) | radix-4 Stockham = radix-2 = DFT for every power of two to 2^17; FFTW / Accelerate pass the self-check or say why; `compute.fft` selects the provider with equal results; allocation-free envelope operator = general operator (scalar, Raman, vector Raman; same steps); `auto` without a device = CPU | 10⁻¹³ ... 10⁻¹¹ |
| GPU (`GpuTests`, Python `test_compute.py`) | device FFT (contiguous and strided) = managed FFT; 1-D scalar / vector Kerr + Raman + steepening, adaptive and fixed steppers, radial and cartesian spatial and UPPE coupling on the device = CPU (field, B-integral, step count); small / unsupported problems stay on the CPU with a note; 6 concurrent runs on one device = CPU and release their slots; `devices` self-test passes. Needs an OpenCL or CUDA device: PoCL provides one on any CPU ([GPU](plugins/gpu.md#how-it-works-for-developers-and-ai-agents)); skipped otherwise | 10⁻⁹ (observed ~10⁻¹⁵) |
| delayed light (`DelayedLightTests`) | **ledger vs brute force**: a plate ghost, a mirror echo and a ledger run reproduce one large grid (delay 1e-3, relative peak power 1e-3, total energy 1e-5); the energy budget closes with the tail booked as `delayed light (tail)`; light that leaves the main window returns; the cross-correlator probe sees the ghost, its gate comes from the main field; ghosts follow a table coating's R(lambda)^2; mirror echo delay = 2 n_g d / c (6.35 / 9.5 / 12.7 mm), echo intensity (T r_b)^2 / R, echo = t^2 r_b exp(i psi) exactly (real and imaginary part), leakage follows T(lambda), AR back face, wedge; window warnings name the satellite (delay, level, energy, wrapped position), `extendForSatellites`, `strict`, pedestal replicas; coating ripple pedestal sigma^2 within the max delay, amplitude ripple energy 1 - sqrt(2/pi) sigma + sigma^2/2, seed reproducibility; delayed scatter fraction, delay support, mean delay, lambda^-n weight, plate default delay; detector saturation clips the peak and stitching restores the contrast, the floor of an attenuated segment rises as 1/attenuation | 1e-3 .. 1e-5 (ledger vs brute), 1 % .. 25 % (random ripple / scatter statistics) |
| HITRAN plugin (`HitranTests`, Python `test_delayed_light.py`) | Faddeeva: w(0) = 1, erfcx(1), erfcx(0.1), Re/Im w(1) = e^-1 and 2/sqrt(pi) Dawson(1), continuity at the asymptotic switch; `.par` and CSV round trips; Doppler width = kinetic formula (0.0141 cm-1); Lorentz line: peak n_d S / (pi gamma_L), dispersion extremum n_d S / (8 pi^2 nu gamma_L) with the right signs; **Kramers-Kronig** of a Voigt line by a numerical Hilbert transform; temperature and density scaling; `environment.air` off by default and plain Ciddor reproduces the built-in Air; a gap's own `air`; energy lost = sum S (1 - exp(-alpha L)); **three water lines in a 10 m gap: tail grid (N = 1024 + 262 144) vs one 2 ns window (N = 524 288)**; Python: builders, the downloader with a fake HITRAN API, a hitran run and a ghost run through the hosted engine | 1 % .. 1e-9 |
| small-scale self-focusing (`SmallScaleTests`) | seeded ripple on a flat-top beam in a Kerr plate against **Bespalov-Talanov** cosh(g z) integrated over the pulse (q = Delta and 3 Delta); diffraction-only ripple = Talbot `2 eps |cos(q L)|`; seeded noise reproducible and linear in its amplitude; P / P_cr | 6 % (observed < 1 %) |
| diffraction by apertures (`DiffractionTests`) | plane wave through a round iris: on-axis **4 F sin²(πN/2)** (Born & Wolf 8.8) at N = 3, 2, 1; Gaussian and flat-top through an iris vs the **Fresnel integral** over the whole profile and transmission 1 − exp(−2a²/w²); square flat-top through a square aperture vs the separable Fresnel integral (512²); far zone: first Airy ring 0.61 λz/a, first sinc² zero λz/2a; area-averaged hard edges on coarse and fine grids, warning under 10 samples; absorbing boundary (wrapped light removed, loss budget closes); linear profile-only path = full-field path; square shapes need a cartesian grid | 1 ... 2 % of the peak (observed 0.02 ... 0.7 %); 10⁻⁴ (paths) |
| beam sources and masks (`BeamSourceTests`) | Airy function and polynomials vs tables; Hermite-Gauss and Laguerre-Gauss radii, dark vortex axis, LG node; **Durnin** Bessel core over 6 Rayleigh ranges; **Siviloglou & Christodoulides** Airy trajectory; measured near field; zone plate 4 N² / 16 N² on the axis; axicon core; **Young's** fringes; vortex plate; file mask | 1e-12 ... 5 % |
| cw, nonlinear boundary, retrieval (`FieldToolsTests`) | cw through an iris: Fresnel intensity; Kerr phase of a cw beam; absorbing layer in the stepper vs the linear path; **Gerchberg-Saxton** on a known phase; phase map round trip; PNG near field with background, centre, threshold | 1e-3 ... 3 % |
| cavities and coherence (`CavityAndCoherenceTests`) | **Kogelnik & Li** eigenmode; two-mirror formulas; Fox-Li vs ABCD; plane-mirror loss **N^(−3/2)** (Fox & Li); **Gaussian-Schell** M² law; **Koechner** depolarisation and bifocal lens; amplifier lens per pass | 1e-3 ... 25 % |
| branched systems (`BranchingTests`) | carrier phase of glass; **Mach-Zehnder** fringes, spectral fringes pinning the sign; separate packets; **Airy function**; ring pulse train; steady-state loop; every graph check, port form and alert; virtual beams; branch-fed crystal against `aux`; modelled autocorrelator **√2 τ** | 1e-9 ... 1 % |
| physics review (`PhysicsReviewTests`) | dispersed beam in glass; reversed return pass in a linear cavity; Gaussian stress depolarisation; Fox-Li that must not claim a mode; mode truncation; profile tilt of a prism; curvature sign | 1e-6 ... 2 % |
| splitter family and space (`BranchedElementsTests`) | **Gouy phase**; **Michelson** fringes; **Malus**; leak, dichroic; **Gaussian overlap** in tilt and offset; tilt fringes **λ/θ** of carried profiles; harmonic phase; crystal ports; tail through a splitter; **f-2f** following the CEP | 1e-9 ... 3 % |
| modelled instruments (`ModelledInstrumentsTests`) | **IAC 8:1**; **FTS** (Wiener-Khinchin) against the input; FROG marginal **√2 τ**; study node cache and array outputs | 0.2 ... 2 % |
| review regressions (`ReviewRegressionTests`) | **M²** of tilted, decentred, Hermite-Gauss and Laguerre-Gauss beams (2m + 1, 2p + \|l\| + 1) on each axis; centroid radius; packets at `end`; packets at crystal ports; passive `steadyState`; all-pass ring |
| beam tracking (`BeamTrackingTests`) | thermal lens on the tracked beam: small beam = **Innocenzi** πKw_p²/(P_h dn/dT); a measured focal length focuses like a thin lens (Gaussian lens law); tracked radius vs the radial spatial gain model (7 %, aberrated lens). Spatio-temporal couplings (**Kostenbauder**): grating angular dispersion and **Hebling** pulse-front tilt; grating + gap + grating = `gratingPair` (**Treacy** GDD, spatial chirp); pulse-front tilt from spatial chirp and GDD vs the Gaussian result (Akturk 2005); prism vs Snell at both faces; 4f zero-dispersion stretcher GDD = 0; `physics.spatioTemporal` modes | 0.1 ... 7 % |
| delayed light, follow-ups (`DelayedLightTests`) | smooth elements skip the exact long-grid step with the same result (energy 1e-9, power 1e-6) and are counted; first-order cross-phase of the tail vs the full nonlinear difference field (B = 0.11: 7.5 %); analytic surface scatter: in-mode share = numerical integral of the roughness spectrum, lost power TIS (1 - g), lambda^-2, returned power g TIS, plate has 2 surfaces; detector spectral response = table, photon counting = x lambda, presets (run spec name, file, overrides, unknown name) | 1e-12 .. 15 % |
| HITRAN, partition sums and real lines (`HitranTests`) | partition table interpolation and range check; strength follows the table (T^-3 vs T^-1.5); `<NAME>.partition.csv` found next to the lines; opt-in **real H2O, CO2, O2 lines at 250 / 296 / 350 K vs HAPI** (`ULTRAFAST_HITRAN_VALIDATE_DIR`, `plugins/hitran/tools/make_hapi_reference.py`) | 1 % integrated, 4 % worst point |
| release packaging | `build/package.py`: platform aliases, plugin selection, ZOS-API assemblies never copied, the wheel writer (metadata, extras, entry point, RECORD hashes), dry run (Python `test_packaging.py`) | exact |
| cross-code | N ~ 2.4 higher-order soliton with TOD and self-steepening vs **pyNLO**: bulk medium and `fiber` element, no Raman, Blow-Wood Raman, Lin-Agrawal Raman (pyNLO's weights), flat gain | < 0.3 % (gain 0.5 %) |
| cross-code | spatial engine vs **LightPipes** (`test_lightpipes.py`, skipped without it): fluence maps through free space, lenses, cylinder, curved mirror, tilt, decentre, HG, eight Zernike terms, vortex handedness, axicon, apertures, double slit, Talbot, glass; branched Mach-Zehnder vs `BeamMix` with tilt and phase; tracked Gaussian vs wave optics | 0.1 % smooth beams; 3 to 6 % hard edges, limited by LightPipes' 0/1 masks at N = 256 |
| cross-code | branched arms vs **pyNLO** (`test_pynlo_branched.py`): a fibre after a splitter, before one, and in both arms of a Mach-Zehnder | 0.5 % |
| cross-code | DFG 1064 + 1550 → 3.39 µm in MgO:PPLN vs **pyNLO** three-wave mixing (energies, idler spectrum) | 0.1-0.7 % small signal, 0.5 % at gain 50 |

## pyNLO

`python/tests/test_pynlo.py` runs the same fibre in pyNLO and UltrafastEngine (identical grid, dispersion, gamma,
self-steepening, Raman model, gain) and compares temporal profiles, spectra, energy and peak power -- once as a bulk
medium with a constant beam, and once through the `fiber` element. Agreement is better than 0.3 % (L1 distance of the
profiles) with and without Raman. Two details matter at that level: the engine normalises the sampled Raman kernel on
the time grid (as pyNLO does), and pyNLO's "full" (Lin-Agrawal) model carries a unit-dependent normalisation of its
oscillator part, which the test reproduces with explicit weights ([Fibres](reference/fibers.md#compared-with-pynlo)). pyNLO is optional: set `PYNLO_SRC` to a checkout's `src` (fetch the pinned commit with
`python python/tools/get_pynlo.py`); the test applies small shims for current NumPy/SciPy.

`python/tests/test_pynlo_dfg.py` compares the crystal element with pyNLO's `dfg_problem` (Boyd's coupled-amplitude
equations, domain-resolved poling, plane-wave coupling at the pump waist). The engine is set up identically:
`MgO:LiNbO3` at 24.5 °C (same Gayer Sellmeier), `deffPmPerV` 14.9, pyNLO's period (designed with ΔkL = 3.2),
`qpm.model: "domains"` with `phaseDeg: 90`, `overlap: {"model": "planeWave", "radiusM": 100e-6}`, `chi3: false`.
pyNLO's three-wave code needs a few more shims (the Python-2 `exceptions` module, a NumPy stand-in for two
pyfftw calls), applied by `conftest.import_pynlo()`; the high-gain case is slow (`ULTRAFAST_SLOW=1`).

## Adding tests

Every physics feature comes with a test against an analytic result, a conservation law, or another code.
Prefer small grids (N <= 4096) and mark anything over ~10 s as `[Test(Slow = true)]`.
