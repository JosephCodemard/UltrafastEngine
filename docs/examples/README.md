# Examples

| | what it shows | run |
|---|---|---|
| [01-spm-plate](01-spm-plate/README.md) | SPM in an AR-coated plate, instruments, provenance, overrides | `ultrafast run plate.ufs run.json` |
| [02-stretcher-compressor](02-stretcher-compressor/README.md) | CPA line from analytic elements; grid sizing for ns stretching; photodiode, residual chirp | `ultrafast run cpa.ufs run.json` |
| [03-mpc-argon](03-mpc-argon/README.md) | 20-pass argon Herriott cell generated from Python; per-pass B, LIDT, compression | `python make_mpc.py; ultrafast run mpc.ufs run.json` |
| [04-custom-index-python](04-custom-index-python/README.md) | your own n(t, I) as a NumPy callback (plasma + MPA) | `python custom_response.py` |
| [05-polarization-coatings](05-polarization-coatings/README.md) | quarter-wave mirror at 45 deg, Brewster window, waveplate, polariser, polarimeters | `ultrafast run beamline.ufs run.json` |
| [06-carrier-resolved](06-carrier-resolved/README.md) | index modulated on the fs scale in lab time -> real-field propagation | `ultrafast run fast-index.ufs run.json` |
| [07-zemax-workflow](07-zemax-workflow/README.md) | OpticStudio export -> system file -> run; try it with the fake MPC | `ultrafast zemax-demo mpc export.json` |
| [08-shg-lbo](08-shg-lbo/README.md) | SHG in LBO: carrier change, by-products, phase-matching instruments, radial variant | `ultrafast run shg.ufs run.json` |
| [09-nopcpa-bbo](09-nopcpa-bbo/README.md) | non-collinear OPCPA (magic angle), pump from example 08's results, delay scan, compressor | `ultrafast run nopcpa.ufs run.json` |
| [10-ppln-dfg](10-ppln-dfg/README.md) | mid-IR DFG in MgO:PPLN (QPM), idler as main beam, spectral margin; pyNLO comparison | `ultrafast run dfg.ufs run.json` |
| [11-spatial-kerr](11-spatial-kerr/README.md) | self-focusing with diffraction resolved (radial model), profile carried through free space; on-axis vs whole-beam spectra and autocorrelations (pinhole, slit, fibre in the far field) | `ultrafast run kerr.ufs run.json` |
| [12-user-element](12-user-element/README.md) | a pulse shaper written in Python as a user element | `python dazzler.py` |
| [13-yb-multipass-cpa](13-yb-multipass-cpa/README.md) | Yb:YAG CPA: cw-pumped 8-pass amplifier, steady state at 10 kHz, gain narrowing, thermal lens | `ultrafast run amp.ufs run.json` |
| [14-ti-sapphire-regen](14-ti-sapphire-regen/README.md) | 1 kHz Ti:sapphire regen, pulsed green pump, automatic ejection, recompression to 42 fs | `ultrafast run regen.ufs run.json` |
| [15-spatial-gain-thermal-lens](15-spatial-gain-thermal-lens/README.md) | spatially resolved gain in a Yb:YAG rod: per-point inversion, thermal lens, cameras; 60 W vs 5 W pump | `ultrafast run rod.ufs run.json` |
| [16-position-dependent-index](16-position-dependent-index/README.md) | Δn(r, z) as an expression, a table and a Python function (GRIN lens, tapered GRIN) | `python grin_variants.py` |
| [17-instrument-tour](17-instrument-tour/README.md) | every instrument kind once along one beam line, plus spectrometers and an autocorrelator looking through a pinhole, a fibre (far field) and a slit | `ultrafast run tour.ufs run.json` |
| [18-custom-nonlinear-medium](18-custom-nonlinear-medium/README.md) | your own n(x, y, z, t, I, F): expression and Python function | `ultrafast run medium.ufs run.json`, `python custom_medium.py` |
| [19-ti-sapphire-klm-haus](19-ti-sapphire-klm-haus/README.md) | KLM Ti:sapphire oscillator (Haus model) from noise; soliton area theorem | `ultrafast run laser.ufs run.json` |
| [20-yb-laser-system](20-yb-laser-system/README.md) | SESAM Yb:YAG oscillator → stretcher → amplifier → compressor | `ultrafast run system.ufs run.json` |
| [21-measured-data](21-measured-data/README.md) | recorded spectrum (CSV), autocorrelation, d-scan (NPZ) and power reading: compare, calibrate 3 unknowns with error bars, predict unmeasured instruments | `ultrafast run line.ufs run-compare.json`, `ultrafast run line.ufs run-fit.json` |
| [22-contrast-chain](22-contrast-chain/README.md) | a window's ghost becomes a pre-pulse through the Kerr effect; pedestal, third-order cross-correlator, energy scan with a parameter study | `ultrafast run chain.ufs run.json`, `ultrafast study study.json` |
| [23-components](23-components/README.md) | Gaussian beam tracking through curved mirrors and a lens; thick waveplate, TFP, Faraday rotator, ND filter, wavefront error, GDD ripple | `ultrafast run components.ufs run.json` |
| [24-filament](24-filament/README.md) | a gas cell with rotational Raman and ionisation: blue shift, plasma density, retrieval of the output pulse from a simulated FROG | `ultrafast run cell.ufs run.json`, `ultrafast retrieve pcgpa results/result.npz` |
| [25-supercontinuum](25-supercontinuum/README.md) | the PCF supercontinuum benchmark (Dudley 2006): `fiber` element with beta2..beta10, soliton fission, dispersive waves, coherence over quantum-noise realisations | `ultrafast run pcf.ufs run.json`, `ultrafast study coherence.json` |
| [26-transforms](26-transforms/README.md) | fitting plugin: a `transform` (residual dispersion, gain narrowing, post-pulse, ASE pedestal) fitted to a spectrum, d-scan, cross-correlator and power meter; the fitted line on a new seed; backward propagation of the measured output to the seed | `ultrafast run line.ufs run-fit.json -o results-fit` |
| [27-ner-measured-input](27-ner-measured-input/README.md) | nonlinear ellipse rotation in 10 fused-silica plates driven by a digitised spectrum (floor, rows out of order, a repeated wavelength): cleaning and background, the satellite window, rotation vs analytic with the anisotropic Raman response, an OSA recording compared at its resolution and range | `python ner.py` |
| [tutorial](tutorial/README.md) | the files of the step-by-step tutorials (docs/tutorials): first run, diagnostics, a CPA + SHG chain, measured data and fitting | see README |
| [python/quickstart.py](python/quickstart.py) | build, run, scan, plot from Python | `python quickstart.py` |
| [cpp](cpp/README.md) | the C API from C++ (CMake / Visual Studio) | see README |

`ultrafast` = `dotnet run --project src/UltrafastEngine.Cli --` (or a published copy on your PATH).
Python examples need the engine built once: `python python/tools/build_engine.py`.

Plot any results folder: `ultrafast-plot results/` (or `python python/src/ultrafast/plot.py results/`) ->
`results/plots/` with system, overview, instruments, measurements, fit, probes, elements, gain and oscillator figures plus `report.pdf`.
