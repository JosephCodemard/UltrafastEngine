# Pulse calculator: time-bandwidth product

A stand-alone calculator for the time-bandwidth product (TBP) of a pulse, its transform limit, how far the pulse is from it (the
stretch factor) and the group-delay dispersion (GDD) that would compress it. It is available as the CLI command `ultrafast pulse`, as
Python (`ultrafast.calc`) and as the C API function `uf_pulse_calc`, like the [phase-matching calculator](crystals.md#phase-matching-calculator).
`limit`, `analyze` and `measure` call the same engine code (`PulseCalculator`) in all three. `shapes()` and `tbp()` in Python are a
separate copy of the closed forms that works without the engine; a test compares the two. Every simulation also reports
`timeBandwidthProduct` in `summary.json`; this calculator is for numbers you have at hand.

Durations and bandwidths are intensity FWHM. The bandwidth is in Hz, or in metres together with the centre wavelength
(`Δν = c Δλ / λ²`, first order in `Δλ / λ`).

## Keys

Python: `from ultrafast import calc` gives the package `ultrafast.calc`, which re-exports `analyze`, `limit`, `measure`, `shapes`, `tbp`
from the module `ultrafast.calc.pulse`. Other calculators: [Calculators](calculators.md).

```python
from ultrafast import calc
calc.tbp("sech2")                                                        # 0.3148
calc.limit("gaussian", duration_s=100e-15, center_m=800e-9)              # bandwidth in Hz and m
r = calc.analyze(duration_s=300e-15, bandwidth_m=10e-9, center_m=1030e-9)
r["timeBandwidthProduct"], r["stretchFactor"], r["gddMagnitudeFs2"]
calc.measure(time_s, intensity, spectrum, wavelength_m=wl)               # from measured arrays (FWHM and rms products)
```

| call | arguments and rules |
|---|---|
| `shapes()`, `tbp(shape)` | the table below |
| `limit(shape="gaussian", *, duration_s, bandwidth_hz, bandwidth_m, center_m)` | exactly one of the duration or a bandwidth, not both; `bandwidth_m` needs `center_m` |
| `analyze(*, duration_s, bandwidth_hz, bandwidth_m, center_m, shape="gaussian")` | the duration and a bandwidth (`bandwidth_m` needs `center_m`) |
| `measure(time_s, intensity, spectrum, *, wavelength_m, frequency_hz, shape="gaussian")` | at least 5 points; `time_s` and `intensity` of equal length; exactly one of `wavelength_m` or `frequency_hz` for the spectrum axis. A spectrum per unit wavelength is converted with `S(ν) = S(λ) λ² / c`. The pulse and the spectrum must fall below half their maximum inside the data, otherwise it is an error |

Command line (`--json` prints the full result; `--request FILE` takes a JSON file with the engine's key names):

```bash
ultrafast pulse shapes
ultrafast pulse limit --shape sech2 --duration 100e-15                 # transform-limited bandwidth
ultrafast pulse limit --shape gaussian --bandwidth-m 40e-9 --center 1030e-9
ultrafast pulse limit --shape gaussian --bandwidth-hz 10e12
ultrafast pulse analyze --duration 300e-15 --bandwidth-m 10e-9 --center 1030e-9 --shape gaussian
ultrafast pulse measure --request measured.json --json                 # arrays: timeS, intensity, wavelengthM | frequencyHz, spectrum
```

`analyze` prints (300 fs, 10 nm at 1030 nm): TBP 0.848, 1.92 × the Gaussian limit, transform-limited duration 156 fs and a GDD
magnitude of 14 400 fs².

### Results

| key | meaning |
|---|---|
| `shape`, `durationFwhmS`, `bandwidthFwhmHz` | the inputs, as used |
| `bandwidthFwhmM`, `centerWavelengthM` | present when a centre wavelength is known |
| `timeBandwidthProduct` | `durationFwhmS × bandwidthFwhmHz` |
| `transformLimitProduct`, `ratioToLimit` | the product of the shape, and the measured product divided by it |
| `transformLimitedDurationS`, `stretchFactor` | the shortest duration for this bandwidth, and `durationFwhmS` divided by it |
| `gddMagnitudeFs2` | Gaussian shape only: `\|GDD\| = τ₀² / (4 ln 2) · √(stretch² − 1)` for a linearly chirped Gaussian, 0 when the stretch is 1; the sign needs a phase measurement |
| `timeBandwidthProductRms`, `rmsLimitProduct` | `measure` only: σ_t σ_ν of the intensity curves, and the Gaussian limit 1/(4π) = 0.0796 |
| `note` | stretch below 0.98: the duration is shorter than the limit of this shape, so the shape, the widths or the units are probably wrong; 0.98 to 1.05: close to transform-limited; above 1.05: chirped or structured |

## Model

TBP = Δt · Δν, both intensity FWHM, of a transform-limited pulse (Diels and Rudolph, *Ultrashort Laser Pulse Phenomena*, table 2.1):

| shape | TBP | exact form |
|---|---|---|
| `gaussian` | 0.4413 | 2 ln 2 / π |
| `sech2` | 0.3148 | (2 ln(1+√2))² / π² |
| `lorentzian` | 0.1420 | ln 2 √(√2 − 1) / π; field 1/(1 + (t/τ)²), intensity its square (table: 0.142) |
| `asymmetric-exponential` | 0.1103 | ln 2 / (2π); intensity exp(−t/τ) for t ≥ 0 (table: 0.110) |
| `rectangular` | 0.8859 | 2 x / π with sin x / x = 1/√2 |

Aliases: `gauss`; `sech`, `sech^2`; `lorentz`; `asym-exp`, `asymmetric_exponential`, `exponential`; `square`, `rect`.

The stretch factor is `durationFwhmS / (TBP_shape / bandwidthFwhmHz)`. A linearly chirped Gaussian of limit duration τ₀ has
`τ = τ₀ √(1 + (4 ln 2 · GDD / τ₀²)²)`, which gives the `gddMagnitudeFs2` formula above. A product above the limit does not
distinguish a linear chirp from higher-order phase or a structured spectrum: use the [instruments](instruments.md) or a retrieval for that.

## Validation

`PulseCalculatorTests` check the closed-form products, the unit conversion, the chirped-Gaussian GDD and a sampled Gaussian against the rms
limit 1/(4π). `python/tests/test_setup_and_calc.py` (`test_calc_tbp_of_shapes_matches_closed_forms`,
`test_calc_analyze_and_limit_round_trip`) and `python/tests/test_calc.py` (`test_tbp_table_matches_diels_rudolph`,
`test_python_tbp_table_equals_the_engine_table`) check the Python side.

## Limits

* The bandwidth conversion `Δν = c Δλ / λ²` is first order in `Δλ / λ`.
* The GDD estimate assumes a linear chirp of a Gaussian pulse; for other shapes `gddMagnitudeFs2` is not given.
* The GDD sign is not known from a duration and a bandwidth.
