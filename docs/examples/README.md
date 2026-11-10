# Examples

Each example is a folder with a README, its commands and its expected output, grouped by topic. **Interface** says how it drives the engine: the `ultrafast` command line with `.ufs` and `run.json` files, the Python package, or the C API from C++. Run `python docs/examples/make_all.py` to rebuild the figures (see below), or install the examples with `ultrafast-examples` (`ultrafast-examples list`, `copy <group|name> <folder>`, `run <name>`).

## GettingStarted

First runs: build a system, run it, read the results, bring your own index, element or medium.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [quickstart](GettingStarted/quickstart/README.md) | 00 | Python | build a system in code, run it, read results, scan a parameter | `python quickstart.py` |
| [spm-plate](GettingStarted/spm-plate/README.md) | 01 | CLI | SPM in an AR-coated plate, instruments, provenance, overrides | `ultrafast run plate.ufs run.json` |
| [custom-index-python](GettingStarted/custom-index-python/README.md) | 04 | Python | your own n(t, I) as a NumPy callback (plasma + MPA) | `python custom_response.py` |
| [user-element](GettingStarted/user-element/README.md) | 12 | Python | a pulse shaper written in Python as a user element | `python dazzler.py` |
| [custom-nonlinear-medium](GettingStarted/custom-nonlinear-medium/README.md) | 18 | CLI + Python | your own n(x, y, z, t, I, F): expression and Python function | `ultrafast run medium.ufs run.json`, `python custom_medium.py` |

## LaserSystems

Stretcher, amplifier, compressor and parametric chains, oscillators, and the contrast of a chain.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [stretcher-compressor](LaserSystems/stretcher-compressor/README.md) | 02 | CLI | CPA line from analytic elements; grid sizing for ns stretching; photodiode, residual chirp | `ultrafast run cpa.ufs run.json` |
| [yb-multipass-cpa](LaserSystems/yb-multipass-cpa/README.md) | 13 | CLI | Yb:YAG CPA: cw-pumped 8-pass amplifier, steady state at 10 kHz, gain narrowing, thermal lens | `ultrafast run amp.ufs run.json` |
| [ti-sapphire-regen](LaserSystems/ti-sapphire-regen/README.md) | 14 | CLI | 1 kHz Ti:sapphire regen, pulsed green pump, automatic ejection, recompression to 41 fs | `ultrafast run regen.ufs run.json` |
| [ti-sapphire-klm-haus](LaserSystems/ti-sapphire-klm-haus/README.md) | 19 | CLI | KLM Ti:sapphire oscillator (Haus model) from noise; soliton area theorem | `ultrafast run laser.ufs run.json` |
| [yb-laser-system](LaserSystems/yb-laser-system/README.md) | 20 | CLI | SESAM Yb:YAG oscillator → stretcher → amplifier → compressor | `ultrafast run system.ufs run.json` |
| [four-grating-compressor](LaserSystems/four-grating-compressor/README.md) | 38 | CLI | a compressor grating by grating: spatial chirp opens and closes, GDD against Treacy | `ultrafast run compressor.ufs run.json -o results`, `python report.py` |
| [shg-lbo](LaserSystems/shg-lbo/README.md) | 08 | CLI | SHG in LBO: carrier change, by-products, phase-matching instruments, radial variant | `ultrafast run shg.ufs run.json` |
| [nopcpa-bbo](LaserSystems/nopcpa-bbo/README.md) | 09 | CLI | non-collinear OPCPA (magic angle), pump from example 08's results, delay scan, compressor | `ultrafast run nopcpa.ufs run.json` |
| [noncollinear-opa](LaserSystems/noncollinear-opa/README.md) | 42 | CLI | a NOPA in a Branched `.ufs`: the pump arm's tilt sets the angle, a virtual seed, the idler's direction from transverse momentum | `ultrafast run nopa.ufs run.json -o results`, `python report.py` |
| [contrast-chain](LaserSystems/contrast-chain/README.md) | 22 | CLI | a window's ghost becomes a pre-pulse through the Kerr effect; pedestal, third-order cross-correlator, energy scan with a parameter study | `ultrafast run chain.ufs run.json`, `ultrafast study study.json` |

## Nonlinear

Kerr, plasma, Raman, supercontinuum, chi(2) conversion in gases, fibres and crystals; spatial nonlinear effects.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [mpc-argon](Nonlinear/mpc-argon/README.md) | 03 | Python + CLI | 20-pass argon Herriott cell generated from Python; per-pass B, LIDT, compression | `python make_mpc.py; ultrafast run mpc.ufs run.json` |
| [carrier-resolved](Nonlinear/carrier-resolved/README.md) | 06 | CLI | index modulated on the fs scale in lab time -> real-field propagation | `ultrafast run fast-index.ufs run.json` |
| [spatial-kerr](Nonlinear/spatial-kerr/README.md) | 11 | CLI | self-focusing with diffraction resolved (radial model), profile carried through free space; on-axis vs whole-beam spectra and autocorrelations (pinhole, slit, fibre in the far field) | `ultrafast run kerr.ufs run.json` |
| [filament](Nonlinear/filament/README.md) | 24 | CLI | a gas cell with rotational Raman and ionisation: blue shift, plasma density, retrieval of the output pulse from a simulated FROG | `ultrafast run cell.ufs run.json`, `ultrafast retrieve pcgpa results/result.npz` |
| [supercontinuum](Nonlinear/supercontinuum/README.md) | 25 | CLI | the PCF supercontinuum benchmark (Dudley 2006): `fiber` element with beta2..beta10, soliton fission, dispersive waves, coherence over quantum-noise realisations | `ultrafast run pcf.ufs run.json`, `ultrafast study coherence.json` |
| [ner-measured-input](Nonlinear/ner-measured-input/README.md) | 27 | CLI + Python | nonlinear ellipse rotation in 10 fused-silica plates driven by a digitised spectrum (floor, rows out of order, a repeated wavelength): cleaning and background, the satellite window, rotation vs analytic with the anisotropic Raman response, an OSA recording compared at its resolution and range | `python ner.py` |
| [small-scale-self-focusing](Nonlinear/small-scale-self-focusing/README.md) | 29 | CLI | a 10 mm fused-silica plate at P / P_cr ≈ 100 in a flat-top beam on a 128 x 128 cartesian grid, seeded noise, small-scale gain and the Bespalov-Talanov reference | `ultrafast run plate.ufs run.json` |
| [ppln-dfg](Nonlinear/ppln-dfg/README.md) | 10 | CLI | mid-IR DFG in MgO:PPLN (QPM), idler as main beam, spectral margin; pyNLO comparison | `ultrafast run dfg.ufs run.json` |

## Diffraction

Apertures, tilted beams and structured modes on a carried transverse profile.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [iris-diffraction](Diffraction/iris-diffraction/README.md) | 30 | CLI + Python | Gaussian, round flat-top, uniform and square beams through a 1 mm iris, imaged at Fresnel numbers 3, 2, 1 and in the far zone; absorbing boundary; every pattern checked against the Fresnel integral; an iris scan from Python | `ultrafast run gaussian.ufs run.json -o results-gaussian` (x4), `python compare.py`, `python iris.py` |
| [tilt-fringes](Diffraction/tilt-fringes/README.md) | 35 | CLI | a Michelson with a tilted mirror in a Branched `.ufs`: fringes at lambda / angle from carried profiles | `ultrafast run michelson.ufs run.json -o results`, `python report.py` |
| [structured-beams](Diffraction/structured-beams/README.md) | 36 | Python | Hermite-Gauss and Laguerre-Gauss M², a Bessel core from an axicon, a zone-plate focus | `python structured.py` |

## Wavefronts

Position-dependent index and spatially resolved gain: lenses and aberrations made by the medium.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [position-dependent-index](Wavefronts/position-dependent-index/README.md) | 16 | CLI + Python | Δn(r, z) as an expression, a table and a Python function (GRIN lens, tapered GRIN) | `python grin_variants.py` |
| [spatial-gain-thermal-lens](Wavefronts/spatial-gain-thermal-lens/README.md) | 15 | CLI | spatially resolved gain in a Yb:YAG rod: per-point inversion, thermal lens, cameras; 60 W vs 5 W pump | `ultrafast run rod.ufs run.json` |

## Branched

Interferometers, cavities, f-2f and pump-probe built as systems with ports, links and loops.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [cavity-and-coherence](Branched/cavity-and-coherence/README.md) | 31 | CLI + Python | a ring resonator's eigenmode against its Fox-Li mode; a Gaussian-Schell beam of M² = 2.5 against the M² law; cw sources | `ultrafast run lens-guide.ufs run-cavity.json`, `python report.py` |
| [interferometers](Branched/interferometers/README.md) | 32 | Python | a Mach-Zehnder and an etalon as branched systems, with their budgets; the Airy function from a loop | `python interferometers.py` |
| [f2f-cep](Branched/f2f-cep/README.md) | 37 | Python | an f-2f interferometer whose fringes follow the carrier-envelope phase | `python f2f.py` |
| [pump-probe](Branched/pump-probe/README.md) | 41 | Python | a pump and a probe of opposite circular polarisation in a Branched system: the probe's spectral shift from cross-phase against the delay, exact reference | `python pump_probe.py` |

## Diagnostics

Every instrument, modelled instruments, delayed light and measured data compared with simulation.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [instrument-tour](Diagnostics/instrument-tour/README.md) | 17 | CLI | every instrument kind once along one beam line, plus spectrometers and an autocorrelator looking through a pinhole, a fibre (far field) and a slit | `ultrafast run tour.ufs run.json` |
| [modelled-autocorrelator](Diagnostics/modelled-autocorrelator/README.md) | 33 | Python | a splitter, a delay stage and a crystal against the `autocorrelator` instrument | `python autocorrelator.py` |
| [modelled-instruments](Diagnostics/modelled-instruments/README.md) | 34 | Python | an interferometric autocorrelator, a Fourier-transform spectrometer and a FROG built from elements | `python instruments.py` |
| [third-order-cross-correlator](Diagnostics/third-order-cross-correlator/README.md) | 39 | Python | SHG, a delay and THG in a Branched system: the layout of a SEQUOIA or TOCC | `python tocc.py` |
| [noncollinear-correlators](Diagnostics/noncollinear-correlators/README.md) | 40 | Python | beams crossing in a crystal: a background-free autocorrelator with its geometric smearing, and crossed THG with the output angles | `python correlators.py` |
| [measured-data](Diagnostics/measured-data/README.md) | 21 | CLI | recorded spectrum (CSV), autocorrelation, d-scan (NPZ) and power reading: compare, calibrate 3 unknowns with error bars, predict unmeasured instruments | `ultrafast run line.ufs run-compare.json`, `ultrafast run line.ufs run-fit.json` |
| [delayed-light](Diagnostics/delayed-light/README.md) | 28 | CLI | a plate ghost with a wavelength-dependent AR table, a mirror-substrate echo and a coating-ripple pedestal on the delayed-light tail grid (`grid.tail`), read by a cross-correlator; window warnings for the same system on a manual grid | `ultrafast run cell.ufs run.json` |

## Components

Coatings, polarisation, waveplates, beam tracking through curved optics.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [polarization-coatings](Components/polarization-coatings/README.md) | 05 | CLI | quarter-wave mirror at 45 deg, Brewster window, waveplate, polariser, polarimeters | `ultrafast run beamline.ufs run.json` |
| [components](Components/components/README.md) | 23 | CLI | Gaussian beam tracking through curved mirrors and a lens; thick waveplate, TFP, Faraday rotator, ND filter, wavefront error, GDD ripple | `ultrafast run components.ufs run.json` |

## DigitalTwin

Fitting a simulated line to measurements and trusting it: transforms, calibration, backward propagation, a twin fitted across conditions, what-if, experiment design, drift tracking. See [DigitalTwin](DigitalTwin/README.md).

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [transforms](DigitalTwin/transforms/README.md) | 26 | CLI | fitting plugin: a `transform` (residual dispersion, gain narrowing, post-pulse, ASE pedestal) fitted to a spectrum, d-scan, cross-correlator and power meter; the fitted line on a new seed; backward propagation of the measured output to the seed | `ultrafast run line.ufs run-fit.json -o results-fit` |
| [build-the-twin](DigitalTwin/build-the-twin/README.md) | - | Python | five energies, one set of shared parameters, held-out predictions with credible bands, a validation report that catches a failing twin | `python build_twin.py` |
| [explore-the-twin](DigitalTwin/explore-the-twin/README.md) | - | Python | what-if with bands, the most informative next measurement, drift tracking (ensemble Kalman filter) | `python explore.py` |

## AdaptiveOptics

Deformable mirror, SLM, tiled apertures, temporal shaping (the `ao` plugin). See [AdaptiveOptics](AdaptiveOptics/README.md).

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [dm-correction](AdaptiveOptics/dm-correction/README.md) | - | Python | calibrate, correct and close the loop on a Zernike aberration; Zernike plots | `python dm_correction.py` |
| [sensorless-correction](AdaptiveOptics/sensorless-correction/README.md) | - | Python | modal search and SPGD from the focal peak alone | `python sensorless.py` |
| [aberrated-high-power](AdaptiveOptics/aberrated-high-power/README.md) | - | Python | a DM removes hot spots and nonlinear phase of a 2 GW beam in a Kerr plate | `python high_power.py` |
| [slm-beam-shaping](AdaptiveOptics/slm-beam-shaping/README.md) | - | Python | Gerchberg-Saxton holograms with 8-bit grey levels; full 1920 x 1080 panel | `python slm_shaping.py` |
| [tiled-array](AdaptiveOptics/tiled-array/README.md) | - | Python | coherent combining of 7 and 61 beams; piston, delay, SPGD locking | `python tiled_array.py` |
| [temporal-shaping](AdaptiveOptics/temporal-shaping/README.md) | - | Python | spectral shaper and AOPDF: pulses on demand, side pulses, closed-loop chirp search | `python temporal_shaping.py` |
| [scatterer-focusing](AdaptiveOptics/scatterer-focusing/README.md) | - | Python | transmission matrix from intensities, focusing through a diffuser | `python scatterer.py` |

## Fibres

Single-mode fibres: solitons, Raman, self-phase modulation. See [Fibres](Fibres/README.md).

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [soliton-breathing](Fibres/soliton-breathing/README.md) | - | Python | N = 1 and N = 3 solitons; the soliton period | `python solitons.py` |
| [raman-soliton-shift](Fibres/raman-soliton-shift/README.md) | - | Python | self-frequency shift against pulse width (Gordon) | `python raman_shift.py` |
| [spm-compression](Fibres/spm-compression/README.md) | - | Python | SPM broadening and compression in a fibre | `python spm_compression.py` |

## Interop

OpticStudio (Zemax) export and the C API from C++.

| example | old no. | interface | what it shows | run |
|---|---|---|---|---|
| [zemax-workflow](Interop/zemax-workflow/README.md) | 07 | CLI | OpticStudio export -> system file -> run; try it with the fake MPC | `ultrafast zemax-demo mpc export.json` |
| [cpp](Interop/cpp/README.md) | - | C++ | the C API from C++: run a system with callbacks, list compute devices (CMake / Visual Studio) | see README |

## Figures

Every example folder has a `make_figures.py`; `python docs/examples/make_all.py` runs them (`--group`, `--only`, `--no-animations`, `--jobs`). The
overview of each run is written to `figures/` (git-ignored). An example whose README showed no figure has a committed `hero.png`, embedded in
the README; the Computing, AdaptiveOptics, DigitalTwin (new examples) and Fibres examples commit their `hero.png` and `animation.gif`. The [gallery](../gallery.md) collects them. Size budget: PNG 300 kB, GIF 2.5 MB.

## Other groups

`Computing/` (optical computing: 4f correlator, optical convolution, matrix-vector, parallelism and time) is described in [Computing](Computing/README.md).

## Old numbers

The folders were numbered 00-42 before the regrouping. Prose in the READMEs that says "example 08" means the folder with old number 08 in the table above (`LaserSystems/shg-lbo`). Dependencies that cross folders: `LaserSystems/nopcpa-bbo` reads the results of `LaserSystems/shg-lbo` (run that first); `DigitalTwin/transforms` uses `results-fit/fitted-system.ufs` written by its own first run.

The files of the step-by-step [tutorials](../tutorials/index.md) are in `docs/tutorials/files/`.

`ultrafast` = `dotnet run --project src/UltrafastEngine.Cli --` (or a published copy on your PATH).
Python examples need the engine built once: `python python/tools/build_engine.py`.

Plot any results folder: `ultrafast-plot results/` (or `python python/src/ultrafast/plot.py results/`) ->
`results/plots/` with system, overview, instruments, measurements, fit, probes, elements, gain and oscillator figures plus `report.pdf`.
