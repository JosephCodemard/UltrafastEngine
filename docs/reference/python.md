# Python

```python
import ultrafast as uf
from ultrafast import build as b
```

## Installing

```bash
pip install -e "python[hosted,plot,test]"     # from the repository
python python/tools/build_engine.py           # hosted engine -> python/src/ultrafast/bin/hosted (needs .NET 10)
python python/tools/build_engine.py --aot     # native library -> python/src/ultrafast/bin/native (no .NET at run time)
```

The package looks for the engine in `$ULTRAFAST_LIBRARY`, then `ultrafast/bin/native`, `ultrafast/bin/hosted`,
then a repository build. `uf._native.library().mode` says which back-end is in use.

## API

| | |
|---|---|
| `uf.System.load(path | dict | json)` | load a system; `.to_dict()`, `.validate()` |
| `uf.Run(system, spec)` | spec = path, dict or JSON text |
| `run.time_response(name, fn, carrier_resolved=False)` | `fn(ctx)` writes `ctx.dn_re` / `ctx.dn_im` or returns `dn_re` or `(dn_re, dn_im)` |
| `run.index(name, fn)` | `fn(omega) -> n` |
| `run.filter(name, fn)` | `fn(omega) -> T` or `(T, phase)` |
| `run.beam(name, fn)` | `fn(z) -> w` or `(wx, wy)` |
| `run.array_function(name, fn)` | `fn(x) -> y` (e.g. a Raman h(t)) |
| `run.element(name, fn)` | user element: `fn(ctx: ElementContext)` returns the new spectrum `ax` or `(ax, ay)` ([Active elements](active-elements.md#user-elements)) |
| `run.execute(progress=None)` | `progress(element_id, i, n, fraction)`; return True to cancel |
| `uf.run(system, spec, callbacks={name: fn}, elements={name: fn})` | one call; with `"fit"` in the spec it runs the calibration and returns the best run (`result.summary["fit"]`, [Fitting plugin](../plugins/fitting.md#calibration-fit)) |
| `uf.crystal.phase_match(crystal, process, type=None, fundamental=, pump=, signal=, idler=, noncollinear=, qpm=, length=, temperature_c=, ...)` | phase-matching calculator ([Crystals](crystals.md#phase-matching-calculator)) |
| `uf.crystal.crystals()` | built-in crystal names |
| `result.summary` | the `summary.json` dict |
| `result["output_spectrum"]`, `result.arrays`, `result.names()` | NumPy arrays (copied, cached) |
| `result.instrument("spectrometer")` | values + arrays of one instrument |
| `result.provenance`, `result.write(dir)` | |
| `uf.load("results")` | open a CLI results folder (same interface as a Result; no engine needed) |
| `uf.plot(result, "overview" | "temporal" | "spectrum" | "phase" | "losses" | "elements")` | quick matplotlib plots |
| `ultrafast.plotting.report(result_or_folder, out_dir)` | all figures as PNG/SVG + `report.pdf` (see [Plotting](#plotting-results)) |
| `uf.study(system, spec, parameters=[...], outputs=[...], mode="scan" \| "optimize" \| "sensitivity", **options)` | parameter studies ([Parameter studies](studies.md)) |
| `uf.retrieve("pcgpa" \| "epie" \| "dscan", trace, instrument=, fundamental=, material=, output=)` | pulse retrieval; returns a ready-to-use `"pulse"` ([Pulse retrieval](retrieval.md)) |
| `uf.materials.available()`, `.show(name)`, `.import_page(path, name=)`, `.refractiveindex(page, pressure_bar=)` | material library and refractiveindex.info pages ([Materials](materials.md#refractiveindexinfo-pages)) |
| `uf.command({"command": ...})` | any engine command as JSON (the same as the C function `uf_command`) |
| `ultrafast.types` | TypedDicts of every element and section (`PlateElement`, `RunSpec`, ...): editor completion and type checking |
| `uf.version()`, `uf.capabilities()` | |
| `uf.plugins.installed()`, `uf.plugins.module(id)`, `uf.plugins.search_paths()` | installed plugins and their Python modules ([Plugin system](plugins.md)) |
| `ultrafast.plotting.register_figure(name, maker, after=None)` | add a figure to reports and `ultrafast-plot` (plugins) |

`ResponseContext` fields: `t`, `t_lab`, `intensity`, `intensity_x`, `intensity_y`, `instant_intensity`,
`field`, `t_arrival`, `z`, `length`, `omega0`, `area`, `carrier_resolved`, `element_id`, `dn_re`, `dn_im`,
`x`, `y`, `r`, `theta`, `spatial` (position of the grid point on spatial models; 0 / False in 1-D)
(NumPy views valid during the call).

Position-dependent properties (`indexProfile`, `absorptionProfile`, `n2Scale`, gain `dopingProfile`) can be Python
functions: `uf.run(..., fields={"name": lambda x, y, z: ...})` or `Run.field("name", fn)`, referenced as
`"@name"` ([Position-dependent properties](spatial-properties.md)). Exceptions raised in callbacks abort the run and are re-raised by
`execute()`.

`ElementContext` fields: `omega` (absolute, ascending), `t`, `omega0`, `dt`, `arrival`, `element_id`, `params`
(the element's `params` dict), `ax`, `ay` (complex spectra, copies), `set(ax, ay)`, `time_x()`.

## Building systems in Python

Gain media: `b.pump(...)`, `b.gain(...)`, `b.multipass_amplifier(...)`, `b.regenerative_amplifier(...)`
([Gain media](gain.md)). Lasers: `ultrafast.laser.Oscillator` (fluent builder), `b.saturable_absorber(...)`,
`b.soft_aperture(...)` ([Oscillators](oscillators.md)).

`ultrafast.build` returns plain dicts (save them with `b.save(d, "x.ufs")`):

```python
sysd = b.system("mpc", [
    b.interface("win_in", "Air", "FusedSilica", coating="ar"),
    *[b.bulk(f"pass{k}", "Ar2bar", 0.55, beam=b.gaussian_beam(150e-6, 0.275, 1.1)) for k in range(20)],
], materials={"Ar2bar": b.material(gas=b.gas("Ar", 2.0))}, coatings={"ar": {"kind": "ar", "R": 0.002}})
spec = b.run_spec(b.pulse(1.03e-6, 220e-15, energy=200e-6),
                  instruments=[b.instrument("spectrometer"), b.instrument("compressor")],
                  overrides=[b.override("element:pass*", "timeResponse", "@mymodel", mode="add")])
```

Helpers: `system, bulk, gap, interface, mirror, dispersion, grating_pair, chirped_mirror, aperture, waveplate,
polarizer, filter_element, probe, gaussian_beam, constant_beam, sampled_beam, material, sellmeier, gas,
coating_gdd, coating_quarter_wave, pulse, instrument, override, run_spec, save`, and for active elements:
`crystal` (χ(2) element: `process`, `type`, `cut`, `noncollinear`, `qpm`, `aux`, `output`, `main_role`, `record`,
`overlap`, `chi3`, `deff_pm_per_v`, `temperature_c`, `transverse`, ...), `aux_beam(role, pulse | from_results=,
radius_m=, delay_s="auto")`, `transverse(model, points=, radius_m=, ...)`, `lens(id, focal_length_m, material=)`,
`user(id, callback, params=)`; components and fibres: `fiber`, `plate`, `nd_filter`, `thin_film_polarizer`, `faraday_rotator`,
`wavefront`, `pedestal`, `waveplate(..., material=, order=, construction=)` ([Optical components](components.md)).

**Types.** Every builder returns a TypedDict from `ultrafast.types` (`b.plate(...)` is a `PlateElement`), and
dictionaries you write by hand can be annotated the same way, so editors complete keys and type checkers (mypy,
pyright) flag misspelt ones:

```python
from ultrafast.types import PlateElement, RunSpec
plate: PlateElement = {"id": "w", "type": "plate", "material": "FusedSilica", "thicknessM": 5e-3}
plate_typo: PlateElement = {"id": "w", "type": "plate", "thicknesM": 5e-3}   # mypy: Extra key "thicknesM"
```

The full list of functions with signatures: [Python API (generated)](python-api.md).

**Sampling and measured data** (core; [Measured data](measured-data.md)):
`sampling(plane, aperture, radius_m=, width_m=, focal_length_m=, center_x_m=, na=)`,
`measured(file, values=, sigma=, normalize=, background=, columns=, range_x=, resolution_m=, resolution_s=, match_resolution=, scale=, ...)`
for an instrument's recording, and `measured_spectrum(...)` / `measured_spectrum_csv(...)` (with `background=`,
`threshold=`, `resolution_m=`, `deconvolve=`, `extrapolate=`) for a measured input pulse. The comparisons come back
typed: `res.comparisons` (a list of `ultrafast.measured.Comparison`), `res.comparison(name)`, and
`uf.measured.curve(res, name)` / `trace(res, name)` for the compared arrays.

```python
spec = b.run_spec(b.measured_spectrum_csv("data/input.csv", 200e-9, background="auto"),
                  instruments=[b.instrument("spectrometer", name="spec", resolutionM=0.1e-9,
                                            sampling=b.sampling("far", "fibre", radius_m=25e-6, focal_length_m=0.1),
                                            measured=b.measured("data/spectrum.csv", background="auto", sigma=0.005,
                                                                resolution_m=0.5e-9))])
res = uf.run(system, spec)
c = res.comparison("spec")                     # chi2, reducedChi2, rmsError, notes (points outside the range, resolution)
curve = uf.measured.curve(res, "spec")         # measured_x, measured_y, simulated_on_measured (NaN outside the range)
```

Calibration fits against these comparisons (`fit=`, `fit_parameter(...)`, transforms) come from the fitting plugin's
module `ultrafast_fitting` ([Fitting plugin](../plugins/fitting.md)).

```python
pump = b.aux_beam("pump", b.pulse(515e-9, 0.8e-12, energy=200e-6), radius_m=0.6e-3)
sysd = b.system("nopa", [b.crystal("nopa", "BBO", 2e-3, process="opa", noncollinear="magic", aux=[pump],
                                   output="signal", beam=b.gaussian_beam(0.55e-3))])
res = uf.run(sysd, b.run_spec(b.pulse(800e-9, 15e-15, energy=50e-9)))
res.elements()[0]["values"]["conversionEfficiency"]
```

Examples: `docs/examples/python/quickstart.py`, `docs/examples/04-custom-index-python`, `docs/examples/03-mpc-argon/make_mpc.py`,
`docs/examples/12-user-element/dazzler.py`, `docs/examples/10-ppln-dfg/compare_pynlo.py`.

## Performance notes

Callbacks are called once per solver stage with whole arrays: keep them vectorised (NumPy), avoid Python loops
over samples. The ctypes round trip costs microseconds; a typical nonlinear element calls a time response a few
thousand times.

## Plotting results

`ultrafast-plot` (installed with the package; or `python -m ultrafast.plot`, or simply
`python python/src/ultrafast/plot.py` from a checkout without installing) turns a results folder written by the
CLI into figures. It needs only NumPy and matplotlib, not the engine:

```bash
ultrafast run docs/examples/03-mpc-argon/mpc.ufs docs/examples/03-mpc-argon/run.json -o results
ultrafast-plot results/                         # -> results/plots/{system,overview,instruments,measurements,fit,probes,elements,byproducts,...}.png + report.pdf
ultrafast-plot results/ --show --db             # open windows too; spectra in dB
ultrafast-plot results/ --only overview --format svg,pdf --dark
ultrafast-plot runA/ runB/ --labels "no CM" "with CM" --out compare/   # each run + compare.png
```

| figure | contents |
|---|---|
| `system` | schematic of the system: every element (repeated units such as multipass-cell passes collapsed to "× N"), the beam line coloured by its carrier wavelength, aux beams coming in, by-products leaving, probes, and numbered instrument locations (○ instrument, □ virtual); a numbered list of all instruments and where they sit |
| `overview` | the system schematic, input/output pulse (+ ideally compressed pulse), spectrum, spectral phase with offset and group delay removed (where the spectrum is above 1 %), energy / FWHM / cumulative B through the system, loss budget by mechanism, key numbers and warnings |
| `instruments` | one panel per instrument, in system order, titled with its number and location: cameras and wavefront maps (images), Wigner / spectrogram, field probe, space-time maps, spectrometer (dB), autocorrelator, compressor / scope, d-scan trace and FWHM vs GDD, contrast, polarimeter (Stokes vs wavelength); unknown or user instruments are drawn generically (2-D arrays as images against their matching 1-D axes) |
| `measurements` | runs with measured data (core): per instrument the measured points, the simulated reading (with its ±1σ band after a fit) and the residual; measured / simulated / difference images for FROG, d-scan and camera traces; scalar readings in units of σ |
| `fit` | calibration fits (fitting plugin): fitted parameters ± σ (and start values), χ² per iteration, the parameter correlation matrix, χ² per instrument before and after |
| `probes` | spectrum (dB) and power heat maps, one row per probe, input at the bottom |
| `elements` | small multiples per element: energy, FWHM, peak power, cumulative B, system GDD, peak intensity, LIDT margin, pulse GDD |
| `oscillator` | oscillators: build-up over the round trips, evolution of the pulse and the spectrum (heat maps), the final pulse with soliton / QML diagnostics |
| `gain` | gain and amplifier elements: small-signal gain spectrum with the output spectrum, inversion along z (or across the beam), build-up per pass / round trip, pulse-train convergence |
| `compare` | output pulse and spectrum of several runs overlaid (with more than one folder) |

From Python, every panel is also a function taking a live `Result` or a folder:

```python
from ultrafast import plotting as up
res = up.load("results")
fig = up.overview(res); fig.savefig("overview.png", dpi=200)
up.spectrum(res, ax=my_axes, db=True)          # single panels: temporal, spectrum, spectral_phase, evolution, losses, key_numbers
up.report(res, "results/plots", formats=("png", "svg"))
up.compare([up.load("runA"), up.load("runB")], labels=["A", "B"])
up.measurements(res)                           # measured vs simulated (runs with "measured" data)
up.fit(res)                                    # calibration report (runs with "fit")
up.set_theme(dark=True)
```
