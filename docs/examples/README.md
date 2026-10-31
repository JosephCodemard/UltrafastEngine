# Examples

Each example is a folder with a README, its commands and its expected output. **Interface** says how it drives the engine: the `ultrafast` command line with `.ufs` and `run.json` files, the Python package, or the C API from C++.

| | interface | what it shows | run |
|---|---|---|---|
| [00-quickstart](00-quickstart/README.md) | Python | build a system in code, run it, read results, scan a parameter | `python quickstart.py` |
| [01-spm-plate](01-spm-plate/README.md) | CLI | SPM in an AR-coated plate, instruments, provenance, overrides | `ultrafast run plate.ufs run.json` |
| [02-stretcher-compressor](02-stretcher-compressor/README.md) | CLI | CPA line from analytic elements; grid sizing for ns stretching; photodiode, residual chirp | `ultrafast run cpa.ufs run.json` |
| [03-mpc-argon](03-mpc-argon/README.md) | Python + CLI | 20-pass argon Herriott cell generated from Python; per-pass B, LIDT, compression | `python make_mpc.py; ultrafast run mpc.ufs run.json` |
| [04-custom-index-python](04-custom-index-python/README.md) | Python | your own n(t, I) as a NumPy callback (plasma + MPA) | `python custom_response.py` |
| [05-polarization-coatings](05-polarization-coatings/README.md) | CLI | quarter-wave mirror at 45 deg, Brewster window, waveplate, polariser, polarimeters | `ultrafast run beamline.ufs run.json` |
| [06-carrier-resolved](06-carrier-resolved/README.md) | CLI | index modulated on the fs scale in lab time -> real-field propagation | `ultrafast run fast-index.ufs run.json` |
| [07-zemax-workflow](07-zemax-workflow/README.md) | CLI | OpticStudio export -> system file -> run; try it with the fake MPC | `ultrafast zemax-demo mpc export.json` |
| [08-shg-lbo](08-shg-lbo/README.md) | CLI | SHG in LBO: carrier change, by-products, phase-matching instruments, radial variant | `ultrafast run shg.ufs run.json` |
| [09-nopcpa-bbo](09-nopcpa-bbo/README.md) | CLI | non-collinear OPCPA (magic angle), pump from example 08's results, delay scan, compressor | `ultrafast run nopcpa.ufs run.json` |
| [10-ppln-dfg](10-ppln-dfg/README.md) | CLI | mid-IR DFG in MgO:PPLN (QPM), idler as main beam, spectral margin; pyNLO comparison | `ultrafast run dfg.ufs run.json` |
| [11-spatial-kerr](11-spatial-kerr/README.md) | CLI | self-focusing with diffraction resolved (radial model), profile carried through free space; on-axis vs whole-beam spectra and autocorrelations (pinhole, slit, fibre in the far field) | `ultrafast run kerr.ufs run.json` |
| [12-user-element](12-user-element/README.md) | Python | a pulse shaper written in Python as a user element | `python dazzler.py` |
| [13-yb-multipass-cpa](13-yb-multipass-cpa/README.md) | CLI | Yb:YAG CPA: cw-pumped 8-pass amplifier, steady state at 10 kHz, gain narrowing, thermal lens | `ultrafast run amp.ufs run.json` |
| [14-ti-sapphire-regen](14-ti-sapphire-regen/README.md) | CLI | 1 kHz Ti:sapphire regen, pulsed green pump, automatic ejection, recompression to 42 fs | `ultrafast run regen.ufs run.json` |
| [15-spatial-gain-thermal-lens](15-spatial-gain-thermal-lens/README.md) | CLI | spatially resolved gain in a Yb:YAG rod: per-point inversion, thermal lens, cameras; 60 W vs 5 W pump | `ultrafast run rod.ufs run.json` |
| [16-position-dependent-index](16-position-dependent-index/README.md) | CLI + Python | Δn(r, z) as an expression, a table and a Python function (GRIN lens, tapered GRIN) | `python grin_variants.py` |
| [17-instrument-tour](17-instrument-tour/README.md) | CLI | every instrument kind once along one beam line, plus spectrometers and an autocorrelator looking through a pinhole, a fibre (far field) and a slit | `ultrafast run tour.ufs run.json` |
| [18-custom-nonlinear-medium](18-custom-nonlinear-medium/README.md) | CLI + Python | your own n(x, y, z, t, I, F): expression and Python function | `ultrafast run medium.ufs run.json`, `python custom_medium.py` |
| [19-ti-sapphire-klm-haus](19-ti-sapphire-klm-haus/README.md) | CLI | KLM Ti:sapphire oscillator (Haus model) from noise; soliton area theorem | `ultrafast run laser.ufs run.json` |
| [20-yb-laser-system](20-yb-laser-system/README.md) | CLI | SESAM Yb:YAG oscillator → stretcher → amplifier → compressor | `ultrafast run system.ufs run.json` |
| [21-measured-data](21-measured-data/README.md) | CLI | recorded spectrum (CSV), autocorrelation, d-scan (NPZ) and power reading: compare, calibrate 3 unknowns with error bars, predict unmeasured instruments | `ultrafast run line.ufs run-compare.json`, `ultrafast run line.ufs run-fit.json` |
| [22-contrast-chain](22-contrast-chain/README.md) | CLI | a window's ghost becomes a pre-pulse through the Kerr effect; pedestal, third-order cross-correlator, energy scan with a parameter study | `ultrafast run chain.ufs run.json`, `ultrafast study study.json` |
| [23-components](23-components/README.md) | CLI | Gaussian beam tracking through curved mirrors and a lens; thick waveplate, TFP, Faraday rotator, ND filter, wavefront error, GDD ripple | `ultrafast run components.ufs run.json` |
| [24-filament](24-filament/README.md) | CLI | a gas cell with rotational Raman and ionisation: blue shift, plasma density, retrieval of the output pulse from a simulated FROG | `ultrafast run cell.ufs run.json`, `ultrafast retrieve pcgpa results/result.npz` |
| [25-supercontinuum](25-supercontinuum/README.md) | CLI | the PCF supercontinuum benchmark (Dudley 2006): `fiber` element with beta2..beta10, soliton fission, dispersive waves, coherence over quantum-noise realisations | `ultrafast run pcf.ufs run.json`, `ultrafast study coherence.json` |
| [26-transforms](26-transforms/README.md) | CLI | fitting plugin: a `transform` (residual dispersion, gain narrowing, post-pulse, ASE pedestal) fitted to a spectrum, d-scan, cross-correlator and power meter; the fitted line on a new seed; backward propagation of the measured output to the seed | `ultrafast run line.ufs run-fit.json -o results-fit` |
| [27-ner-measured-input](27-ner-measured-input/README.md) | CLI + Python | nonlinear ellipse rotation in 10 fused-silica plates driven by a digitised spectrum (floor, rows out of order, a repeated wavelength): cleaning and background, the satellite window, rotation vs analytic with the anisotropic Raman response, an OSA recording compared at its resolution and range | `python ner.py` |
| [28-delayed-light](28-delayed-light/README.md) | CLI | a plate ghost with a wavelength-dependent AR table, a mirror-substrate echo and a coating-ripple pedestal on the delayed-light tail grid (`grid.tail`), read by a cross-correlator; window warnings for the same system on a manual grid | `ultrafast run cell.ufs run.json` |
| [29-small-scale-self-focusing](29-small-scale-self-focusing/README.md) | CLI | a 10 mm fused-silica plate at P / P_cr ≈ 100 in a flat-top beam on a 128 x 128 cartesian grid, seeded noise, small-scale gain and the Bespalov-Talanov reference | `ultrafast run plate.ufs run.json` |
| [30-iris-diffraction](30-iris-diffraction/README.md) | CLI + Python | Gaussian, round flat-top, uniform and square beams through a 1 mm iris, imaged at Fresnel numbers 3, 2, 1 and in the far zone; absorbing boundary; every pattern checked against the Fresnel integral; an iris scan from Python | `ultrafast run gaussian.ufs run.json -o results-gaussian` (x4), `python compare.py`, `python iris.py` |
| [31-cavity-and-coherence](31-cavity-and-coherence/README.md) | CLI + Python | a ring resonator's eigenmode against its Fox-Li mode; a Gaussian-Schell beam of M² = 2.5 against the M² law; cw sources | `ultrafast run lens-guide.ufs run-cavity.json`, `python report.py` |
| [32-interferometers](32-interferometers/README.md) | Python | a Mach-Zehnder and an etalon as branched systems, with their budgets; the Airy function from a loop | `python interferometers.py` |
| [33-modelled-autocorrelator](33-modelled-autocorrelator/README.md) | Python | a splitter, a delay stage and a crystal against the `autocorrelator` instrument | `python autocorrelator.py` |
| [34-modelled-instruments](34-modelled-instruments/README.md) | Python | an interferometric autocorrelator, a Fourier-transform spectrometer and a FROG built from elements | `python instruments.py` |
| [35-tilt-fringes](35-tilt-fringes/README.md) | CLI | a Michelson with a tilted mirror in a Branched `.ufs`: fringes at lambda / angle from carried profiles | `ultrafast run michelson.ufs run.json -o results`, `python report.py` |
| [36-structured-beams](36-structured-beams/README.md) | Python | Hermite-Gauss and Laguerre-Gauss M², a Bessel core from an axicon, a zone-plate focus | `python structured.py` |
| [37-f2f-cep](37-f2f-cep/README.md) | Python | an f-2f interferometer whose fringes follow the carrier-envelope phase | `python f2f.py` |
| [38-four-grating-compressor](38-four-grating-compressor/README.md) | CLI | a compressor grating by grating: spatial chirp opens and closes, GDD against Treacy | `ultrafast run compressor.ufs run.json -o results`, `python report.py` |
| [cpp](cpp/README.md) | C++ | the C API from C++: run a system with callbacks, list compute devices (CMake / Visual Studio) | see README |

The files of the step-by-step [tutorials](../tutorials/index.md) are in `docs/tutorials/files/`.

`ultrafast` = `dotnet run --project src/UltrafastEngine.Cli --` (or a published copy on your PATH).
Python examples need the engine built once: `python python/tools/build_engine.py`.

Plot any results folder: `ultrafast-plot results/` (or `python python/src/ultrafast/plot.py results/`) ->
`results/plots/` with system, overview, instruments, measurements, fit, probes, elements, gain and oscillator figures plus `report.pdf`.
