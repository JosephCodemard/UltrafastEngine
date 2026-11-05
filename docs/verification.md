# Component verification

The folder `verification/` runs each component of the engine alone, through the Python API, and compares it with an
analytic or published answer. Every check prints its numbers, and most also draw a figure a person can read. The C#
component sheet (`tests/UltrafastEngine.Tests/Components/`) asserts the same numbers in the test suite.

## Running it

```bash
python3 python/tools/build_engine.py                 # after any C# change
python3 verification/run_all.py                      # all families, figures, verification/report/index.html
python3 verification/run_all.py --no-plots           # numbers only (what CI runs)
python3 verification/run_all.py linear crystals      # some families
python3 verification/components/linear.py            # one family; exit code 1 if a check fails
```

The scripts find `python/src` and the hosted engine build (`python/src/ultrafast/bin/hosted`) themselves. They set
`ULTRAFAST_STRICT_OPTIONS=1`, so an unknown key is an error. Figures need matplotlib; `--no-plots` needs only NumPy.

## Families

| script | what is checked | reference |
|---|---|---|
| `linear.py` | `bulk` and `gap`: group delay, GDD, TOD at 515 nm, 1030 nm and 2 µm, for 4 glasses and air; the whole applied φ(ω) over a 10 fs band; no spectral narrowing at 1 ps and 10 fs; `plate`: Fresnel transmission for p and s, tilted-plate GDD; GDD-coated `mirror`, `chirpedMirror`, `dispersion`; both `leakMirror` ports in Sequential and Branched systems; `gratingPair`, 1 and 2 passes | Sellmeier (Malitson, Schott) × Ciddor air; Fresnel; plane-wave k_z d; Taylor phase; Treacy 1969 |
| `splitters_masks.py` | `beamSplitter` port energies from R = 0 to 1 at three carriers; Sequential = Branched (field to 1e-12); Mach-Zehnder (Stokes relations); `polarizingSplitter` against the input angle; `dichroic` port energies and spectra; a 515 nm beam fed into port b beside a 1030 nm beam; hard aperture on a Gaussian beam | energy conservation, (t² − r²)², 4r²t², ∫R S dω, 1 − exp(−2a²/w²) |
| `polarisation.py` | design waveplates (pulse-averaged Stokes parameters); polariser (Malus with extinction); vector Kerr: B of linear light, B(χ)/B_lin = 1 − sin²2χ/3 (circular 2/3), ellipse rotation B sin2χ/3 at the pulse peak, circular light at 1.5× the power has the linear spectrum | Jones calculus; Maker, Terhune & Savage 1964 |
| `crystals.py` | BBO type-I SHG at three carriers: phase-matching angle, absolute conversion efficiency, η ∝ L² with depletion, η(Δk)/η(0) = sinc²(ΔkL/2), energy conservation at high conversion, the carrier on each output port | Eimerl 1987 and Tamošauskas 2018 Sellmeier; Armstrong et al. 1962 |
| `instruments.py` | intensity autocorrelator (FWHM ratio √2 Gaussian, 1.5427 sech², and the whole trace); spectrometer resolution (FWHMs add in quadrature) and its energy; SHG-FROG marginals; third-order cross-correlator (FWHM √(3/2) τ, floor, trace in dB) | Diels & Rudolph; closed-form correlations |

Gain, gases and fibres have no script yet; their analytic checks are in the C# suite (`GainTests`, `GasTests`,
`FiberTests`).

## Reading the report

`verification/report/index.html` (not committed) has one section per family:

* **Figures.** The title states the check. The engine is drawn solid (with markers where it is a set of runs), the
  analytic answer dashed black. The lower panel is the residual: engine − analytic in the unit of the upper axis, or
  the relative residual when the panel says so. A good check has a flat residual near zero, small against the
  quantity above it.
* **Formula.** Under each figure: the analytic expression that was used.
* **Table.** Every check, with the engine value, the analytic value, the relative error, the tolerance and the result:
  `ok`, `FAIL`, or `KNOWN`. `KNOWN` is a reported engine limitation that has not been fixed yet; the note says what it
  is. It does not fail the run. When a `KNOWN` check starts to pass, the table says so and the marker should be removed.

Tolerances are as tight as the reference allows; where a tolerance is looser than the numerics, a comment in the
script gives the reason (for example, group-velocity mismatch at 515 nm in BBO, or the 1 fs delay sampling of a
cross-correlation on a −100 dB flank).

## When a check fails

Decide first whether the engine or the analytic expectation is wrong. The conventions that matter most are in
[Concepts](concepts.md): elements use absolute indices (a plate does not remove the air it replaces), the envelope sits
on a grid centred on the carrier snapped to the frequency step (the reported GDD and TOD are those at the requested
carrier, `grid.reportOmega0` in the summary; a fit to the field gives the values at the grid centre, so it includes
TOD (ω_c − ω)), and pulse-averaged quantities (Stokes parameters of a chromatic waveplate, a pulse's SHG
efficiency) need the average over the spectrum or the pulse, not the value at the carrier. If the engine is wrong, fix
it with a C# test in `tests/UltrafastEngine.Tests/Components/`.

## Adding a check

Add a function to the family script that calls `fam.check(name, engine, analytic, rel=..., abs=..., unit=..., formula=...)`
for each number, and draws its figure inside `with fam.figure(key, title, formula) as f:` with
`overlay(f, x, engine, analytic, ...)`. The harness is `verification/components/_common.py`; independent references
(Sellmeier, Ciddor air, Treacy, Fresnel, tilted plate) are in `_analytic.py`, which does not import the engine. A new
family script also goes into `FAMILIES` in `run_all.py`. In C#, a linear element gets the whole sheet (three carriers,
two bandwidths, energy, reported GDD, Sequential = Branched) by being listed in `LinearComponentTests.Cases`.
