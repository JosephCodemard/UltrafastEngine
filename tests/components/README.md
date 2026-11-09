# verification/ -- every component checked alone, with plots

Each script in `components/` runs one family of components through the Python API, compares them with analytic or
published answers, prints a table of numbers, draws figures (engine solid, analytic dashed, residual below) and exits
with code 1 if a check fails.

```bash
python3 python/tools/build_engine.py          # after any C# change
python3 tests/components/run_all.py               # all families -> tests/components/report/index.html
python3 tests/components/run_all.py --no-plots    # numbers only (CI)
python3 tests/components/crystals.py   # one family
```

| file | contents |
|---|---|
| `components/linear.py` | bulk, gap, plate, mirrors, chirped mirror, dispersion, leakMirror ports, grating pair |
| `components/splitters_masks.py` | beamSplitter, polarizingSplitter, dichroic, a fed beam of another colour, Mach-Zehnder, aperture |
| `components/polarisation.py` | waveplates, polariser, vector Kerr (circular / linear 2/3, ellipse rotation B sin2χ/3) |
| `components/crystals.py` | BBO SHG: angle, efficiency, L², sinc², energy, port carriers |
| `components/instruments.py` | autocorrelator, spectrometer, FROG marginals, third-order cross-correlator |
| `components/_common.py` | harness: engine discovery, checks, figures, the JSON record per family |
| `components/_analytic.py` | independent references (Sellmeier, Ciddor air, Treacy, Fresnel, tilted plate); no engine import |
| `run_all.py` | runs every family and writes `report/index.html` (git-ignored) |

How to read the report, the conventions behind the checks and how to add one: [docs/component-tests.md](../docs/component-tests.md).
The C# component sheet in `tests/UltrafastEngine.Tests/Components/` asserts the same numbers.

<p align="center">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../../assets/logo/ultrafast-logo-long-dark.svg">
  <img alt="UltrafastEngine" src="../../assets/logo/ultrafast-logo-long.svg" height="44">
</picture>
</p>
