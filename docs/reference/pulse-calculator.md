# Pulse calculator: time-bandwidth product

A stand-alone calculator for the **time-bandwidth product** (TBP) of a pulse, its **transform limit**, how far it is from it
(the **stretch factor**) and the **group-delay dispersion** (GDD) that would compress it. It is the same code behind the CLI
(`ultrafast pulse`), Python (`ultrafast.calc`) and the C API (`uf_pulse_calc`), like the [phase-matching calculator](crystals.md#phase-matching-calculator).
Every simulation also reports `timeBandwidthProduct` in `summary.json`; this calculator is for numbers you have at hand.

Durations and bandwidths are **intensity FWHM**. The bandwidth is in Hz, or in metres together with the centre wavelength
(`Δν = c Δλ / λ²`, first order in `Δλ / λ`).

## Shapes (TBP = Δt · Δν, intensity FWHM)

| shape | TBP | exact form |
|---|---|---|
| `gaussian` | 0.4413 | 2 ln 2 / π |
| `sech2` | 0.3148 | (2 ln(1+√2))² / π² |
| `rectangular` | 0.8859 | 2 x / π with sin x / x = 1/√2 |

With the root-mean-square widths of the intensities, a Gaussian has σ_t σ_ν = 1/(4π) = 0.0796 (`timeBandwidthProductRms`).

## Command line

```bash
ultrafast pulse shapes
ultrafast pulse limit --shape sech2 --duration 100e-15                 # transform-limited bandwidth
ultrafast pulse limit --shape gaussian --bandwidth-m 40e-9 --center 1030e-9
ultrafast pulse analyze --duration 300e-15 --bandwidth-m 10e-9 --center 1030e-9
ultrafast pulse measure --request measured.json --json                 # arrays: timeS, intensity, wavelengthM | frequencyHz, spectrum
```

`analyze` prints (300 fs, 10 nm at 1030 nm): TBP 0.848, 1.92 × the Gaussian limit, transform-limited duration 156 fs and a GDD
magnitude of 14 400 fs².

## Python

```python
from ultrafast import calc
calc.tbp("sech2")                                                        # 0.3148
calc.limit("gaussian", duration=100e-15, center=800e-9)                  # bandwidth in Hz and m
r = calc.analyze(duration=300e-15, bandwidth_m=10e-9, center=1030e-9)
r["timeBandwidthProduct"], r["stretchFactor"], r["gddMagnitudeFs2"]
calc.measure(time_s, intensity, spectrum, wavelength_m=wl)               # from measured arrays (FWHM and rms products)
```

## Results

| key | meaning |
|---|---|
| `timeBandwidthProduct` | `durationFwhmS × bandwidthFwhmHz` |
| `transformLimitProduct`, `ratioToLimit` | the product of the shape, and the measured product divided by it |
| `transformLimitedDurationS`, `stretchFactor` | the shortest duration for this bandwidth, and `durationFwhmS` divided by it |
| `gddMagnitudeFs2` | Gaussian only: `\|GDD\| = τ₀² / (4 ln 2) · √(stretch² − 1)` for a linearly chirped Gaussian; the sign needs a phase measurement |
| `timeBandwidthProductRms` | `measure` only: σ_t σ_ν of the intensity curves |

A stretch factor below one means the shape, the widths or the units are wrong; the result says so. A product above the limit does
not distinguish a linear chirp from higher-order phase or a structured spectrum: use the [instruments](instruments.md) or a retrieval for that.

## Tests

`PulseCalculatorTests` (closed-form products, the unit conversion, the chirped-Gaussian GDD, a sampled Gaussian with the rms limit
1/(4π)) and `python/tests/test_setup_and_calc.py`.
