# Testing and validation

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
| system | loss budget closes; aperture clip formula; override priority and provenance; material override beats traced data; system file round trip and validation | exact |
| instruments | AC deconvolution (Gaussian, sech^2), IAC 8:1, spectrometer FWHM, ideal compressor, polarimeter, energy meter, d-scan optimum, FROG marginal | 1 ... 5 % |
| Zemax sampling | Treacy pair through the sampler == Treacy + slab (and the phase-path model has the wrong sign); argon MPC: passes, mirrors, coating rules, GBPS/GBPW waist, per-pass B; tilted slab AOI, AR rule, Fresnel; record/replay; beam profile reconstruction | 0.5 % / exact |
| χ(2) crystals | SHG vs Armstrong tanh² (0.02-85 % conversion); OPA vs cosh² gain and Manley-Rowe; sinc² angle detuning; cascaded-χ(2) phase; QPM averaged vs domains; calculator vs handbook angles (BBO, LBO, KDP, KTP, NC magic angle, PPLN periods); radial vs 1-D overlap; aux beam from saved results; conversion / delayScan / phaseMatchingCurve instruments; C# user element | 2e-4 ... 2 % / 0.15° |
| spatial | Hankel quadrature, Gaussian w(z) radial and cartesian, M² = 1, lens focus, thin Kerr lens, energy of a carried profile after spectral broadening, beamProfile instrument | 1e-12 ... 2 % |
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
| resolution and sampling | `axis` explicit pixels with pixel bins; axis resolution == spectrometer resolution; coarse AC delay steps; dB curves follow their linear curves; input-spectrum deconvolution recovers 2 nm from a 2.83 nm recording; Gaussian tails restore a truncated spectrum; measured-resolution mismatch reported | 1 ... 6 % |
| cross-code | N ~ 2.4 higher-order soliton with TOD and self-steepening vs **pyNLO**: bulk medium and `fiber` element, no Raman, Blow-Wood Raman, Lin-Agrawal Raman (pyNLO's weights), flat gain | < 0.3 % (gain 0.5 %) |
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
