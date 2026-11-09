# Assessment: test fixes, an adaptive-optics plugin, and the digital-twin use case

Written 2026-11-09 after an ultra-high-intensity laser conference. **Nothing here is implemented.** It says what the engine
does today, what is missing, what examples to add and how useful each item would be to the wider laser community. Sizes are
focused agent-days (S ≈ 1, M ≈ 2-3, L ≈ 4-6, XL ≥ 7), as in [plan-next.md](plan-next.md).

How sure each statement is: *checked* means I read the docs or code for it; *not checked* means it is an inference that a
spike must confirm before anything is built on it.

## Owner decisions (2026-11-09, answers to the first version of this page)

| # | decision | effect on this page |
|---|---|---|
| 1 | The AO plugin is about **transverse** shaping first; **temporal** shaping is wanted too | B6 rewritten: a temporal-shaping comment and a staged plan (a 1-D spectral shaper, an AOPDF model) |
| 2 | "Zernike" means Zernike **modes** | confirmed; modal correction is a first-class path next to zonal |
| 3 | The fibre array is a **coherent tiled-aperture amplifier array** (separate channels, one phase actuator each) | B3: `fibreBundle` becomes `tiledArray`; multicore and imaging bundles are out of scope |
| 4 | No specific vendor device. Everything is **parametric**: dimensions, grid, pitch, count, response | B3: presets are generic, every dimension is a parameter; no vendor data needed |
| 5 | Scale: up to **what exists physically, a full 1920 × 1080 SLM** | B8 and B9: a hard requirement that sets the first milestone (streaming per frequency, sparse spectral sampling, GPU) |
| 6 | The digital twin becomes **its own plugin on top of fitting**; the **fitting plugin** gets multi-condition transforms (PINN-style or other), for example spectra at different energies | C2 and C3 rewritten; new items F1-F4 |
| 7 | An **MCP** server is of interest; explain the trade-offs | new section C6 |
| 8 | `paper-replicator` runs on `haiku` (its usage was very high) | done in `.claude/agents/paper-replicator.md`; separate from this plan |

The work packages, their order and their acceptance criteria are in
[roadmap-ao-twin-2026-11.md](roadmap-ao-twin-2026-11.md).

## Summary

| part | verdict |
|---|---|
| **A. two failing CI tests** | Both are CI-versus-Mac numerical sensitivity, not new physics bugs. One is a README that quotes round-off noise (easy). One is a retrieval test that finds a worse minimum on Linux (needs a look at the algorithm). |
| **B. adaptive optics** | About 40 % of the physics exists (Zernike wavefronts, file phase masks, spatial propagation, a wavefront sensor, Gerchberg-Saxton, GRIN-style `indexProfile`). Missing are the *device models* (actuators, pixels, voltages, wrapping, fibre cores), a real sensor model, the calibration / control algorithms and a fast loop. Recommended split: a few general primitives in the core, devices and algorithms in an `ao` plugin. |
| **C. digital twin** | Mostly supported today (transform blocks, measured data, fits, backward propagation, `lab-mirror` agent). The gaps are packaging (one clear worked example), joint fitting over many operating conditions (not checked), and a gradient path for neural-network transforms. The strongest differentiator of the three. |

---

## A. Test fixes

Both failed in the CI run of commit `d283409` (467 passed, 1 failed on Linux; the docs job found the second). Both pass on the
owner's Mac, which is the clue.

### A1. `Frog_PcgpaAndEpie_RecoverAChirpedPulse_FromSdAndPgTraces_WithTheSign`

* Failing case: SD-FROG, ePIE, GDD −300 fs². Retrieved duration 34.4 fs, true 41.6 fs (17 % off, tolerance 3 %).
* *Checked:* the test asserts `res.Error < 8e-3` first and that passed, so the retrieved pulse **reproduces the trace**, and
  its duration is still 17 % off. That is a different pulse that fits the trace equally well: the usual FROG ambiguity
  (time reversal, or a near-degenerate minimum), here for the case with negative chirp.
* *Not checked:* why it differs from the Mac. Suspects, in order: (1) the ePIE start or visiting order depends on something
  that round-off changes (FFT provider: Accelerate on the Mac, a managed or FFTW one on Linux); (2) the 300-iteration limit
  is only just enough on this case; (3) a random start with a seed that is not fixed (I did not find `seed` or `Random` in
  `Retrieval/Retrieval.cs`, so check how the initial guess is built).
* Proposed order of work: reproduce on Linux (Docker, `linux/amd64`, or `ULTRAFAST_FFT=managed` on the Mac if that switch
  exists), print duration versus iteration, then decide. If the algorithm is marginal, fix the **algorithm** (more
  iterations are not a fix; a better initial guess or step schedule is). Do not loosen the 3 % tolerance. If the
  degenerate minimum is a real ambiguity of SD-FROG, say so in `docs/reference/retrieval.md` and test the property the
  retrieval can guarantee (trace error and the *sign* of the GDD), keeping duration for the cases where it is unique.
* Size: S to M.

### A2. `32-interferometers`: `4.1e-14` versus `4.0e-14`

* *Checked:* the script prints `np.abs(s_out - s_in * fringes)[band].max() / s_in.max()` with `%.1e`. A relative error of
  4e-14 is double-precision round-off, which depends on the FFT implementation and the CPU. Each platform gives a different
  last digit, so the README can never match on all of them.
* Fix (the number is a *claim of exactness*, not a measurement): print a bound, for example
  `"spectral fringes largest error  < 1e-12 of the peak"` using `max(err, ...)` rounding, and change the README line to match.
  Do the same for any other quoted number that is round-off (`grep` the example READMEs for `e-1[0-9]`).
  `build/example_check.py` could also learn a rule: numbers below 1e-10 are compared as "both below a threshold".
* Size: S.

---

## B. Adaptive optics (AO)

### B1. What the engine already does (all *checked* in the docs)

| need | what exists | limit |
|---|---|---|
| wavefront error | `wavefront` element and `aberration` on mirrors, surfaces, lenses: Zernike (Noll, waves rms) or random figure; OPD applied as phase ωW/c on the carried profile; Maréchal Strehl estimate | static; Zernike or random only; radial grids keep only axisymmetric terms |
| arbitrary phase / amplitude map | `mask` of kind `file`: NPZ with `x_m`, `y_m`, `amplitude`, `phase_rad` (phase scales with frequency like an optical path) | a file, not a device; no pixels, voltages, wrapping rule or fill factor; slow to loop over (file I/O per trial) |
| refractive-index map | `indexProfile`, `absorptionProfile`, `n2Scale` as expressions, tables or Python callbacks of (x, y, z, r, θ) on `bulk` (example 16, a GRIN rod focuses as the thin-lens formula, 0.9 %) | an SLM written as a thin `bulk` with Δn(x, y) should work; *not checked* for 10⁶-pixel maps or for wrapped phase |
| spatial propagation | radial and cartesian grids, exact or paraxial diffraction, Fresnel-integral checks < 1 %, GPU back-end for spatial envelopes | memory and time grow as N_x · N_y · N_ω (the profile is carried at every stored frequency) |
| wavefront sensing | `wavefrontSensor` instrument (lenslets, wavefront map in waves, Zernike fit to 36 terms, rms / PV, defocus, astigmatism, coma, spherical); `camera` with imaging and far field; virtual `field` and `spaceTime` probes | an *ideal* sensor: no spot centroiding, noise, dynamic range or lenslet cross-talk |
| phase retrieval | Gerchberg-Saxton from near- and far-field intensities (`retrieve gerchbergSaxton`, with restarts), output ready for `initialProfile` or a `file` mask | one algorithm, no target-shaping variants |
| optimisation | scans, Nelder-Mead, Sobol sensitivities, fitting-plugin LM, CMA-ES and MCMC; `transform` `wavefront` operator with fittable Zernike terms | tens of parameters, not hundreds of actuators |
| beam splitting and recombination | Branched systems recombine arms with the carrier phase and a Gaussian mode overlap | overlap is simple Gaussian at one plane; no array of N coherent cores |
| temporal side | spectral phase and amplitude `transform` blocks, FROG, d-scan, chirp-sensitive instruments | no spatial light modulator in a Fourier plane (a pulse *shaper*) |

So a user can already say "put this phase map in the beam and look at the focus". They cannot yet say "this DM with 97
actuators, these influence functions, this stroke, drive it so the Strehl is maximal".

### B2. What is missing

1. **Device models** (the heart of the request): deformable mirror, SLM, fibre bundle / multicore array.
2. **A programmable map primitive** that takes the map as an in-memory array (run spec or Python), applies it with sub-pixel
   averaging and a stated *chromatic rule*, and is cheap to call thousands of times.
3. **A physical wavefront-sensor model**: Shack-Hartmann with centroiding and noise, plus a modal/zonal reconstructor;
   optionally a lateral-shearing or phase-diversity sensor.
4. **Calibration and control algorithms**: interaction (poke) matrix, regularised pseudo-inverse, closed loop.
5. **Hologram / inverse-design algorithms**: weighted GS, MRAF, complex-to-phase-only encoding, quantisation-aware
   refinement.
6. **Sensorless optimisers**: SPGD, hill climbing, modal Zernike search (N + 1 measurements per mode).
7. **A fast path**: a monochromatic (cw) spatial run for the AO loop, with pulses used to validate at the end.

### B3. Proposed split: core primitives, plugin devices and algorithms

You asked for the physics in the core and the plugin to supply components and algorithms. This is the split I would make.
It follows how the engine already works (every element is declared in `Schema/BuiltIn*.cs` or by a plugin, and every
physical ingredient is a replaceable *model*).

**Core (physics that is not specific to one device)**

| primitive | what | why in the core |
|---|---|---|
| `modulator` element | a cartesian grid of cells (pitch, count, offset, rotation), each with a complex transmission or an OPD / phase, given as an array; options: `fillFactor`, `crosstalkKernel`, `zeroOrderLeak`, `phaseWrapRad`, `quantisationLevels`, and `chromatic`: `"opd"` (non-dispersive, a mirror), `"phaseAtDesign"` (phase fixed at a design wavelength and scaled ∝ ω with optional wrapping, an LC cell), `"index"` (Δn(x, y) times a thickness with the material's dispersion) | any plugin or user needs it; a mirror and an LC cell differ only in the chromatic rule, and the rule decides what happens to a femtosecond pulse |
| sub-pixel resampling | average the map over each simulation cell (the `mask` code already does this for edges) | physical pixel edges and gaps; avoids aliasing |
| in-memory map input | arrays through the Python API / C API / run spec without a file; a `map` callback `f(state) -> array` | the AO loop calls this thousands of times |
| coherent source array | an input beam made of N sub-beams: positions, amplitudes, phases, delays, per-beam waist | the fibre bundle and tiled-aperture combining, and any laser array |
| monochromatic spatial fast path | document and test `cw` source + spatial model as the AO inner loop; cache the compiled system between trials (the study machinery already caches prefixes) | AO is spatial first; running 100 frequencies per trial would make the loop unusable |
| zonal wavefront output | `wavefrontSensor` already gives a map; add the slope maps and a stable array output | the control matrix is built from these |

**`ao` plugin (devices and algorithms)**

| item | what |
|---|---|
| `deformableMirror` | **fully parametric** (no vendor data needed): actuator layout (square, hex, ring, or an explicit list of positions), count and pitch, clear aperture, **influence function** (Gaussian with a coupling coefficient, or a measured map), stroke limit and optional hysteresis / creep, quantisation of the drive (bits), surface-to-wavefront factor 2 cos(aoi), print-through, optional segmented piston-tip-tilt kind and a bimorph kind. Compiles to a `modulator` with `chromatic: "opd"`. |
| `slm` | **fully parametric**: pixel count (up to 1920 × 1080 and beyond), pitch, active-area size, fill factor, grey levels, the **voltage-to-retardance curve** (tabulated or tanh-like), liquid-crystal thickness, birefringence Δn(λ) (so phase = 2π Δn(λ) d / λ and wrapping at the design wavelength), fringing-field cross-talk kernel, zero-order leakage. Input as a phase target, a Δn(x, y) map, or grey levels / voltages. Compiles to a `modulator` with `chromatic: "phaseAtDesign"` or `"index"`. A 1-D variant for a Fourier-plane pulse shaper. |
| `tiledArray` | a **coherent tiled-aperture amplifier array**: N channels on a square, hexagonal or ring layout (pitch, count, fill factor, tile shape: Gaussian, flat-top or truncated). Per channel: amplitude (power imbalance), **piston phase** (the actuator), tip-tilt, delay and dispersion difference, and optionally its own nonlinear phase (B-integral) or a nested channel system. Output: the coherent sum of the tiles in the near field, propagated to the far field or a focus. Reports the **combining efficiency** (power in the central lobe / total), the far-field Strehl and the grating-lobe fraction. Phase-locking algorithms (SPGD, LOCSET, hill climbing) come from `ao optimise`. Multicore fibre with coupled cores and imaging bundles are **out of scope** for now. |
| `shackHartmann` model | a physical sensor for the `wavefrontSensor`: lenslet pitch and focal length, spot centroiding (CoG, thresholded), photon and read noise, dynamic range, and a reconstructor (modal Zernike, zonal Southwell). |
| `ao calibrate` | poke each actuator (or each SLM pixel group), record the sensor, build the interaction matrix; for a *linear* phase device this is exact, so one run per actuator. |
| `ao correct` | one-shot: command = −G · measurement with G a Tikhonov or truncated-SVD pseudo-inverse; reports residual rms, Strehl before / after, actuators near their stroke limit, null-space and waffle modes. |
| `ao loop` | closed-loop integrator with gain and delay; for the case of a time-varying aberration. |
| `ao hologram` | GS, weighted GS, MRAF, double-phase and Bolduc-type complex encoding, with the SLM's quantisation and wrapping applied in the loop. Output: grey levels and voltages. |
| `ao optimise` | sensorless: SPGD, hill climbing, modal search with a metric you choose (peak intensity, Strehl, encircled energy, SHG signal from a `conversion` instrument). |
| `ao transmissionMatrix` | measure the linear map input-modes → output-modes of a scattering or multimode element by phase stepping; invert for focusing. |

The plugin declares `"requires": ["fitting"]` if it reuses the estimators (decision 7 of [plan-next.md](plan-next.md)).

**Why not all in the core?** Device catalogues, vendor-specific curves and control algorithms change fastest and are not
needed by most users. **Why not all in the plugin?** The chromatic rule, sub-pixel averaging, array sources and the fast
path touch the compiler and the propagation, and any user (or another plugin) benefits.

### B4. The physics that has to be right (and its reference value)

Every item needs an analytic or published reference in a test (project rule).

| topic | reference value |
|---|---|
| tilt / piston / defocus from a DM | a pure tilt moves the focus by f·θ; defocus gives the thin-lens focal shift |
| Strehl from rms wavefront error | Maréchal, S = exp(−σ²) (σ in rad), for σ < ~0.3 waves; the engine already reports the estimate, so compare it with the *simulated* focal peak |
| influence function and fitting error | Gaussian influence with coupling c: stroke at a neighbour = c; residual of a Zernike mode fitted by N actuators falls with N, and with c |
| Shack-Hartmann | spot displacement = f · ∂W/∂x; Zernike fit recovers a known mode exactly in the noiseless case |
| SLM quantisation | a blazed grating with N levels has first-order efficiency [sin(π/N) / (π/N)]² (N = 8: 95 %; N = 256: 99.99 %) |
| SLM pixelation | the replica orders are separated by λ / pitch; the envelope is sinc²(fill · m · …) |
| SLM and a pulse | wrapping at 2π at the design wavelength is exact only there; for bandwidth Δω the residual is a spatially dependent spectral phase. A test: a linear ramp (a blazed grating) angularly disperses a pulse as a grating of the same period |
| fibre array | N equal coherent beams give peak intensity N² × a single beam (N × the power in the combined peak); phase error σ costs exp(−σ²); the fill factor sets the side-lobe energy of the far field |
| GS | the error decreases monotonically (Fienup 1982); a known phase is recovered up to the stated ambiguities |
| transmission-matrix focusing | with N controlled phase-only modes, the focus enhancement is (π/4)(N − 1) + 1 on average (Vellekoop & Mosk, Opt. Lett. 32, 2309 (2007)) |
| core coupling | two identical coupled cores exchange power as cos²(κz) |

### B5. Examples to add (with plots)

| # | example | shows | key plot |
|---|---|---|---|
| AO-1 | **Component gallery**: a DM (with an influence-function map and a poke), an SLM (grey levels → phase → far field, with 2π wrapping and 8-bit steps), a 19-core bundle (near field, far field) | the three components, as you asked | 3 × 3 panel: device map, wavefront, focus |
| AO-2 | **DM correction of a Zernike-aberrated beam**: calibrate, correct in one shot, show the focus and Strehl before / after, the residual per actuator and the stroke used | one-shot algorithm | before / after focus; Strehl versus number of actuators |
| AO-3 | **Sensorless correction**: SPGD and modal search from a metric only, convergence curves, compared with the one-shot result | iterative algorithm | metric versus iteration, three methods |
| AO-4 | **Aberrated PW-class beam with a DM before the focus and a chirped, nonlinear pulse**: the aberration raises local intensity and the B-integral; the corrected beam does not | what the engine adds beyond other AO tools | peak-fluence map and B-integral map, with and without correction |
| AO-5 | **Beam shaping with an SLM**: GS / WGS for a target intensity in the focal plane. **Smiley face**, **square**, **ring**, **a donut (vortex)**, with the SLM's 8-bit quantisation applied | the requested shaping | target, hologram, simulated result, error |
| AO-6 | **Complex shaping with a phase-only device** (double-phase encoding): amplitude and phase target at once | an important limit of phase-only devices | amplitude / phase error versus method |
| AO-7 | **Coherent tiled-aperture combining**: random phases, then a phase-locking loop (SPGD); far-field peak N², and the effect of a delay mismatch for a 30 fs pulse | tiled-aperture laser array (hex layout, 7 to 61 channels) | far-field pattern before / after, peak versus iteration, and versus delay |
| AO-8 | **Temporal shaping** (see B6): flat-top, double pulse and pulse train from a programmed phase; pixel-gap side-pulses; a Dazzler-like device restoring a gain-narrowed pulse; closed-loop shaping to a target with a simulated FROG | temporal shaping, SLM 4f line and AOPDF | pulse in time for each shape, with the spectrum and phase beside it |
| AO-9 | **Transmission matrix through a scatterer** and focusing | the (π/4)N enhancement | enhancement versus N, with the formula |

### B6. Transverse and temporal shaping (decision 1)

**Transverse** shaping (a smiley face, a square, a donut in the focal plane; wavefront correction) is the main target and
is what B1-B5 describe.

**Temporal shaping**, a comment, since you said you know it less well:

* *What it is.* A pulse of bandwidth Δω is shaped by changing its spectral phase and amplitude: spectral phase φ(ω) gives
  the time profile (a linear φ delays, a quadratic φ chirps, a sinusoidal φ makes a pulse train, a π step makes a
  double pulse); spectral amplitude A(ω) sets the envelope. The engine already does this ideally: the `transform`
  `spectralPhase` / `spectralAmplitude` operators, the `custom` filter and `chirpedMirror`.
* *How it is done in a lab.* Three standard devices. (1) A **4f line**: a grating splits the colours, a lens maps colour to
  position, a **1-D SLM** in the Fourier plane sets phase (and amplitude) per colour, a mirror image of the optics puts the
  beam back together (Weiner's pulse shaper). (2) An **acousto-optic programmable dispersive filter** (AOPDF; the Dazzler
  is the common product): an acoustic wave in a crystal sets a programmable spectral phase and amplitude. It is the standard
  tool in high-intensity chirped-pulse amplifiers, to pre-compensate gain narrowing and high-order dispersion. (3) A **DM or
  SLM in the Fourier plane of a stretcher**, which shapes the spectral phase of a stretched pulse.
* *Why a real device is not the ideal transform.* The pixelated Fourier plane has finite spectral resolution (the focal
  spot is not a point), pixel gaps and fringing fields, a phase that wraps at 2π and is exact only at one wavelength, and any
  misalignment leaves space-time coupling (spatial chirp). These produce pre- and post-pulses at multiples of
  1/(pixel bandwidth), reduced efficiency, and a pulse that is not the one you programmed. The AOPDF has its own limits
  (diffraction efficiency, a finite temporal window, a small amplitude ripple).
* *Why it matters to this project.* In a high-intensity laser the shaper sits in front of a gain chain and the nonlinear
  elements the engine already models, so the real question is "what does the pixelation of my shaper do to the
  contrast and to the compressed pulse at the target?". That needs the device model, the nonlinear chain and the
  instruments in one run. This is where the engine adds value over a stand-alone shaper calculator.
* *Staged plan.* (a) **Stage T1, cheap and valuable**: a 1-D `spectralShaper` element: pixels mapped to wavelength through
  the dispersion of the grating and the focal length, a Gaussian resolution kernel from the spot size, fill factor,
  grey levels, wrapping, optional gaps. It acts on the spectrum directly (no spatial grid), so it is fast and testable
  against Weiner's analytic pre-pulse spacing. (b) **Stage T2**: an `aopdf` element (programmable spectral phase and
  amplitude with a finite window, efficiency and ripple), and the closed-loop shaping of a pulse with FROG or d-scan in the
  loop: the algorithms in B3 apply, with a pulse metric instead of a focal-spot metric. (c) **Stage T3, later**: the full 4f
  line with the spatial model (spatial chirp and space-time coupling), which needs the spatio-temporal "profile" mode.
* *Examples.* Flat-top, double pulse and pulse train from a programmed phase; the pixelation artefacts and how the grey-level
  and gap settings change them; **a pulse shaped by a Dazzler-like device before a stretcher and compressor so that the
  gain-narrowed output is restored**; closed-loop shaping to a target pulse with a simulated FROG.
* *Usefulness.* High in the ultra-high-intensity community (spectral phase control and gain-narrowing compensation are daily
  tasks), and the same loop algorithms serve both domains, which is a good reason to build them once.

### B7. Usefulness to the community

An honest assessment, split by device:

| item | usefulness | reasoning |
|---|---|---|
| DM + wavefront sensing + correction in a *pulse and nonlinear* model | **High** for the ultra-high-intensity community | Focal-spot quality sets the peak intensity at every PW-class facility, and DMs are standard there. What is rare is a tool that puts the wavefront in the same model as chirp, B-integral and nonlinear cleaning, so one can ask "what does this aberration do to the B-integral and to the damage margin of the next optic?" |
| Coherent tiled-aperture combining | **High, topical** | Tiled-aperture coherent combining (the ICAN / XCAN line of work) is a main route to high repetition rate at high energy. A model that includes the **pulse** (delay mismatch, dispersion per channel, B-integral per channel) is less common than a pure Fourier-optics one. |
| SLM beam shaping | **Medium** | Widely used (cold atoms, microscopy, laser machining), and well served by existing tools. For ultrafast lasers its value is the *chromatic* behaviour of wrapped phase, which many tools ignore. |
| Temporal pulse shaping (SLM 4f, AOPDF) | **High for high intensity, high for spectroscopy** | Standard in the ultrafast community, and the Dazzler-type device is in nearly every high-intensity chirped-pulse amplifier; pixelation artefacts are a classic source of error. See B6. |
| Sensorless optimisation and transmission matrices | **Medium** | Useful to teach and to test control code before it touches a real mirror. |

Caveats that should be stated in the docs: other open packages already cover single-frequency AO well (to my knowledge,
HCIPy and aotools for astronomy-style AO; LightPipes for diffraction). The engine should not claim to beat them at that. Its
value is the combination: pulse + nonlinearity + chromatic devices + the digital twin below. Models of actuators are
idealised unless the user supplies measured influence functions; the docs must say so.

### B8. Risks and open points

* **Scale is a requirement (decision 5): a full 1920 × 1080 SLM.** One complex double-precision slice is 33 MB at one cell
  per pixel (1920 × 1080), 133 MB at 2 × 2 cells per pixel (3840 × 2160, enough to show fill factor and cross-talk), and
  268 MB padded to 4096 × 4096 for the FFT. The engine carries the profile at every stored frequency, so 100 frequencies
  would need about 27 GB at the padded size. Three measures, each to be confirmed by the stage-0 measurement before it is
  built: (1) **stream over frequency**: for a chain of *linear* spatial elements the frequencies are independent, so run
  one frequency at a time and keep only what the instruments need (memory = one slice); (2) **sparse spectral sampling**:
  a mask, SLM or DM changes smoothly with ω (apart from diffraction angles ∝ λ), so use K ≈ 8-16 frequencies across the band and
  interpolate, with a built-in error estimate against a dense run; (3) the **GPU spatial back-end** (CUDA / OpenCL, where
  available) and a cw / narrow-band mode for the AO loop. Apple silicon runs the CPU path (Accelerate FFT); the
  full-size SLM examples must therefore fit a laptop CPU in minutes, and the roadmap carries an explicit time and memory
  budget for them.
* **Chromatic SLM physics** is the part most likely to be wrong in a first version. Start with a monochromatic test and
  add the pulse case against the blazed-grating-as-a-grating reference.
* **Vendor numbers** (stroke, hysteresis, voltage curves) are not in the repository. Ship generic presets labelled as such
  and a loader for measured data (the pattern used for detector presets).
* **Apple silicon**: nothing here needs CUDA, but a large spatial run is faster on the GPU plugin, which cannot use Apple
  GPUs (no FP64). Keep the AO examples small enough for the CPU.
* Zernike-only wavefront correction cannot represent a DM's mid-spatial-frequency errors; the zonal path matters.

### B9. Staging and sizes

The work packages with ownership, tests, example deliverables and acceptance criteria are in
[roadmap-ao-twin-2026-11.md](roadmap-ao-twin-2026-11.md). Summary:

| stage | content | size |
|---|---|---|
| 0 | measurements and spikes (scale, speed, chromatic SLM, SPGD with CMA-ES, tiled array in a Branched system) that decide the primitives | S-M |
| 1 | core: `modulator` (chromatic rules, sub-pixel averaging, in-memory map), streaming and sparse spectral sampling for linear spatial chains, cw fast path, coherent tile source, tests | L-XL |
| 2 | `ao` plugin: `deformableMirror`, `slm`, `tiledArray`, plugin docs, device tests | L |
| 3 | sensing and control: `shackHartmann` model, Zernike modal and zonal reconstructors, `ao calibrate`, `ao correct`, `ao loop` | M |
| 4 | holograms (GS family, complex encoding) and sensorless optimisers (SPGD, modal, phase locking) | M |
| 5 | temporal shaping: `spectralShaper` (T1), `aopdf` and closed-loop pulse shaping (T2) | M |
| 6 | examples and visual deliverables (plots and short animations for every iterative algorithm) | M-L |

Total roughly 6-8 weeks of focused work for the AO plugin with the temporal part; the digital twin (part C) is a separate,
partly parallel stream.

---

## C. Digital twin and a backend for AI-assisted laser design

### C1. What already supports it (*checked* unless stated)

| piece | what exists |
|---|---|
| model of the known system | every element in [capabilities](../capabilities.md), plus ghosts, delayed light, measured input spectra, pedestal from a measured contrast trace |
| "unknown" blocks | fitting-plugin `transform` elements with physical meaning: spectral phase (GDD, TOD, FOD, spline knots with a smoothness penalty), spectral amplitude, energy, Jones matrices, replicas, phase noise, ASE pedestal, Zernike wavefront |
| lab data | the core's `measured` data type: any instrument compared with a recording on its own axis, at its resolution (spectra, FROG, d-scan, contrast, cameras, polarimeter, energy) |
| fitting | Levenberg-Marquardt, CMA-ES, MCMC, priors, instrument-response parameters; three result layers (simulated, inferred, believed); influence map (what the data can and cannot constrain); provenance; a fitted system file for new inputs |
| backward propagation | a measured field is propagated back through the fitted system to the input (example 26) |
| agents | `lab-mirror` agent and `mirror-lab-setup` skill (build, calibrate and check a model of a lab), `setup-optimiser`, `debug-setup`, `AGENTS.md`, `llms.txt`, JSON schemas, strict option checking, typed Python builders |

### C2. What is missing or not checked

| gap | detail | size |
|---|---|---|
| **One clear worked example** | The pieces exist, but there is no single end-to-end story: "build the twin, place transforms, collect data at several settings, fit, validate on a setting you did not fit, then explore". This is the main deliverable. | M |
| **Joint fit over many operating conditions** (*not checked*) | Is a single transform set fitted to several recordings, each with its own settings (pump energy, gas pressure, grating separation) in one cost function? Example 26 shows a single fit. If it is not supported: a `conditions` list in the `fit` block, each with parameter overrides and its own measured data. | M |
| **Held-out validation** | A convention and a helper: fit on some conditions, report the error on the others. Without it a twin only reproduces its training data. | S |
| **Where to put transforms** | A "twin planner": from the layout and the instruments available, list the places where a transform is identifiable (the influence map answers it after the fact; a planner would answer it before). | M |
| **Which data to collect next** | Optimal experimental design: rank candidate operating conditions by how much they reduce the posterior width (the MCMC posterior is already there). | M |
| **Black-box (neural-network) transforms** | A transform whose parameters are network weights. Two points of honesty: (1) the repository rule is *no NuGet packages*, so a network would be a small hand-written MLP in the engine or a Python callback (user elements and user responses already take Python callbacks); (2) training through nonlinear propagation needs gradients, and the engine has none. Options below. | see below |
| **Drift in time** | Laser state changes over hours. Roadmap already lists ensemble Kalman and Bayesian estimators for this. | L |
| **Uncertainty on predictions** | MCMC samples exist; a ready function "predict instrument X at new settings with a credible band" does not (*not checked*). | S to M |
| **Wavefront twin** | Today the `wavefront` transform fits Zernike terms. With stage 1-3 of B it could fit a DM / SLM *map* or a measured wavefront, so the twin includes the beam quality. | M, after B |

**Neural-network transforms, options:** (a) **do not use a network first**: spline-knot transforms with a smoothness prior
are a flexible black box with a physical meaning (a spectral phase is a function of ω), already supported, and are
identifiable with a few tens of parameters; (b) a Python-callback network trained with CMA-ES for up to ~100 weights, no
engine change; (c) an adjoint (backward) gradient through the split-step solver, giving gradients for thousands of
weights: XL and a real research item; (d) export a surrogate of the engine to a differentiable framework: separate project.
Recommendation: build the example with (a) and (b), say plainly that (c) is a possible later step, and describe where a
network is *not* needed.

### C2b. Where it lives (decision 6): fitting gets conditioned transforms, a new `twin` plugin sits on top

**Fitting plugin: transforms that know the operating condition (F1-F4).** Today a `transform` is one fixed operator. Real
unknowns change with the condition: a spectral phase that grows with pulse energy (nonlinear phase), a thermal lens
that grows with average power, a gain narrowing that depends on pump. The proposal is a *conditioned transform*: the
operator depends on a condition vector θ (pulse energy, pressure, pump current, grating separation, temperature, ...),
with **parameters shared across all conditions**, and a fit that uses **all recordings at once** (the `conditions` list: for
each, the settings that override the system file and the measured data).

| item | what | notes |
|---|---|---|
| F1 | **Multi-condition fit**: a `conditions` list in the `fit` block; one cost function over all recordings; per-condition nuisance parameters (a pointing offset, a detector gain) kept separate from the shared ones; **held-out conditions** for validation | needed by everything below. *Not checked* whether any of this exists; the docs show a single dataset. |
| F2 | **Parametric conditioning**: each transform parameter is a low-order function of θ (constant, linear, power law, polynomial), with physical priors (for example the nonlinear phase is linear in energy) | the most interpretable and the best-conditioned; ship first |
| F3 | **Smooth bases in (ω, θ)**: tensor-product splines or Chebyshev for spectral phase and log-amplitude, with smoothness penalties in both directions, plus the passivity, causality and Kramers-Kronig constraints the plugin already has | the "black box with a physical meaning" |
| F4 | **Network transforms** (`kind: "network"`): a small multilayer perceptron g(ω, θ; w) giving spectral phase and log-amplitude, with the same hard constraints (|T| ≤ 1, causality, smoothness) built into the output layer, trained with the fitting plugin's estimators | see the training note below |

**On "PINN".** A physics-informed neural network, strictly, puts the residual of a governing equation in the loss. Here the
forward model is the engine itself, so the closer description is a **grey-box (hybrid) model**: the known physics is the
engine, the unknown part is a constrained network inside it. The constraints (energy conservation, passivity, causality,
smoothness, and optionally a Kramers-Kronig relation between amplitude and phase) are the "physics-informed" part. I would
describe F4 as "physics-constrained network transforms" in the docs, and keep the word PINN for a possible later
approach that actually uses an equation residual.

**Training note (the honest limit).** The engine has no automatic differentiation. The fitting plugin's estimators are
Levenberg-Marquardt (a finite-difference Jacobian costs one run per parameter per condition), CMA-ES and MCMC. That is
fine for tens to a few hundred parameters (F2, F3, a small F4 network) and not for thousands of weights. A real path to
gradients exists for a transform followed only by *linear* elements, where the chain rule is exact and cheap through
backward propagation; the nonlinear segments need an adjoint of the split-step solver (a research item, XL). The roadmap
treats gradients as a stage-gated option, not a promise.

**`twin` plugin (requires `fitting`).** The project-level workflow that sits on top; it adds no physics.

| item | what |
|---|---|
| T1 | a **twin project** layout: nominal system, where the transforms are, a catalogue of recordings by condition, fit and validation splits, provenance, a one-command rebuild |
| T2 | **placement planner**: from the layout and the available instruments, which places are identifiable (a prior to the influence map) |
| T3 | **experiment design**: rank candidate operating conditions by the reduction of the posterior width, so beam time is spent where it informs most |
| T4 | **validation report**: prediction error on held-out conditions with credible bands; a model that only reproduces its training data is flagged |
| T5 | **what-if explorer**: scan or optimise on the fitted twin (the `setup-optimiser` route), with the believed-layer uncertainty on every prediction |
| T6 | **drift tracking**: re-fit a few parameters as new recordings arrive (ensemble Kalman, roadmap item) |
| T7 | the **MCP tool server** (C6), because it is the twin that an agent should talk to |
| T8 | an HTML **twin report** (fit quality, parameters with uncertainty, held-out validation, plots) for the lab book |

### C3. The AI-assisted design backend

What makes the engine a good backend for an AI agent: structured JSON in and out; a schema; strict mode that turns unknown
keys into errors; warnings that name problems (grid too small, SPM at zero frequency); a loss budget that closes; damage
margins; scans and optimisers; an `AGENTS.md` and `llms.txt` written for agents.

What would make it better (*not checked* means I did not find it):

| gap | detail | size |
|---|---|---|
| **A tool server for agents** | An MCP server. Trade-offs and a recommendation in C6. | M |
| **A design-goal format** | One file saying "compress to < 30 fs, B < 1, contrast > 10¹⁰ at −10 ps, fluence below the damage threshold", checked automatically, returned as pass / fail per goal. The engine already computes each quantity. | M |
| **Run budget and caching** | An agent may call the engine hundreds of times; cached prefixes exist for studies. Document the cost per run for the example systems. | S |
| **A benchmark of design tasks with known answers** | Gives a way to measure whether an agent design is good, and to catch hallucinated physics. | M |
| **Honest scope statement** | The engine is not a cavity or ray-trace designer (see Limitations). An agent must be told this. | S |

### C4. Examples to add

| # | example | shows |
|---|---|---|
| DT-1 | **"Build the twin"**: a synthetic lab (a known system with hidden aberrations, so the answer is known; the repository already uses synthetic data with answers in `docs/assets`). The twin starts from the nominal parameters, transform blocks are placed at three places, data from five operating conditions are fitted, the hidden values are recovered and a sixth condition is predicted with an error band. |
| DT-2 | **Using the twin without beam time**: scan the compressor, the pump energy and the gas pressure on the fitted model and read the predicted FROG, spectrum and contrast. |
| DT-3 | **Spline-knot and network transforms side by side** on the same data, with the identifiability map: when does the black box help, and when does it only fit noise. |
| DT-4 | **Agent-driven design**: an AI agent (through the tool server) designs a compressor for a stated goal, with the engine's warnings visible, and the result checked against the goal file. |
| DT-5 | **The wavefront twin** (after B): fit a DM / SLM map from a measured wavefront and a focal spot. |

### C5. Usefulness to the community

* **Digital twin: high.** Time on a high-intensity laser is scarce and expensive, and nearly every facility has partial
  models and many diagnostics. The distinctive part is the honest treatment: three result layers (simulated, inferred,
  believed), an influence map that says what the data cannot constrain, and held-out validation. This is not a feature
  most simulation codes have, and it uses what is already built. It also connects every other piece of the engine
  (instruments, measured data, nonlinear cleaning, the AO plugin).
* **AI-assisted design: medium to high, but riskier.** The value depends on the physics being right and on the agent being
  told where the engine's scope ends. The benchmark and the goal file are what make this credible rather than a demo.
* **Neural-network transforms: medium.** Interesting, but a spline or physical-parameter transform is easier to trust.
  Do not lead with it.

### C6. An MCP server: trade-offs

MCP (Model Context Protocol) lets an AI client call typed tools exposed by a program. Today an agent such as Claude Code
already drives the engine through the CLI with `AGENTS.md`, `llms.txt` and the skills, so the question is what a server adds.

| | with an MCP server | CLI + skills (today) |
|---|---|---|
| **Reach** | works in any MCP client (desktop chat apps, IDEs, other agents), including clients with no shell | needs a client that can run shell commands |
| **Structure** | typed inputs and outputs; the model cannot mistype a flag; results can be compact summaries instead of file dumps | JSON files and text; the agent must know the conventions (the docs and `AGENTS.md` teach them) |
| **State and speed** | a long-lived process can keep the engine loaded and a compiled system hot: faster loops of many runs, progress for long jobs | each call starts a process; fine for single runs, slower for hundreds |
| **Context cost** | every tool description is in the model's context on every turn; many tools cost real tokens (keep it to ~8 coarse tools, or load them on demand) | zero until the agent reads a doc or skill |
| **Safety** | can be scoped: a workspace folder, read-only tools versus run tools, a run-time budget, no network | the agent has the whole shell; safety is the client's permission system |
| **Danger** | engine inputs can contain **user expressions and Python callbacks**, which is code execution; a server must refuse them by default or run in a sandbox. Connecting a twin to real lab hardware would be a safety issue: keep the server **read-only with respect to the lab** | the same code-execution risk exists, but it is the user's own session |
| **Maintenance** | one more component to version in step with the engine, test on three operating systems and document | none |
| **Debugging** | an extra layer between the model and an error | the agent sees raw stderr |

*Recommendation.* Build it, but small and optional: a Python server (stdio) in the `twin` plugin, installed with an extra
(`pip install "ultrafast[mcp]"`), exposing about eight coarse tools:
`describe` (capabilities and limits, the content of `llms.txt`), `validate`, `run` (returns a compact summary plus
artefact paths), `compare_to_measured`, `scan`, `fit`, `explain_warning`, `plot` (returns an image). Resources are the
reference pages and the schemas. Defaults: workspace-scoped, user expressions and callbacks refused unless enabled, a
time and memory budget per call. Build it after the twin basics (T1-T5), because an agent is only as useful as the
model it talks to. If few users need it, the CLI route remains.

---

## Suggested order

See [roadmap-ao-twin-2026-11.md](roadmap-ao-twin-2026-11.md) for the full, ordered work packages. In one line: CI fixes,
then measurements and spikes, then the core primitives, then the AO and fitting streams in parallel, then the twin plugin and
the MCP server, then examples and visuals throughout.

Examples AO-1 … AO-9 and DT-1 … DT-5 are scheduled, with plots and animations, as E1-E15 in the roadmap.

## Open questions

1. **Spectral sampling for the full-size SLM** (B8): is a sparse set of 8-16 frequencies acceptable for your pulses
   (bandwidth and the aberrations you care about)? The stage-0 measurement will give an error estimate to decide.
2. **Condition variables** (F1): which operating conditions do you actually vary in the lab (pulse energy, gas pressure,
   compressor separation, pump current)? They set the first conditioned-transform examples.
3. **Tile model** (`tiledArray`): is a per-channel *amplitude, phase, delay* model enough for the first version, with a
   nested per-channel amplifier system as a second step?
4. **MCP**: build it as the last item of the twin stream, as recommended, or earlier?
