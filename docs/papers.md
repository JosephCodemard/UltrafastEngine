# Paper replications

The repository's root folder `papers/` reproduces the headline results of published papers with the engine, one
folder per paper. Its index and rules are in `papers/README.md` (in the repository, outside this site); each
folder has a `README.md` with the paper's claims, our setup, plots with the paper's formula or numbers on the same
axes, a comparison table and what does not reproduce.

| folder | paper | what is checked | result |
|---|---|---|---|
| `papers/escoto-2022-compression-quality/` | Escoto et al., JOSA B 39, 1694 (2022) (arXiv:2201.05836) | compression factor C ≈ 0.59 B + 1 (Eq. 2), energy and intensity ratios vs C, chirped input | reproduced; Eq. 2 for GDD-only compression holds from B ≈ 4 rad (6 % off at B ≈ 2) |
| `papers/gordon-1986-ssfs/` | Gordon, Opt. Lett. 11, 662 (1986) | soliton self-frequency shift vs T0 for three silica Raman models, against the perturbation integral | reproduced after a fix of the Raman kernel on coarse grids |
| `papers/boyd-kleinman-1968-focused-shg/` | Boyd & Kleinman, J. Appl. Phys. 39, 3597 (1968) | focusing function h(σ, ξ) of the radial crystal element, h_m = 1.068 at ξ = 2.84 | reproduced to 0.1 % |
| `papers/bespalov-talanov-1966-ssf/` | Bespalov & Talanov, JETP Lett. 3, 307 (1966) | small-scale self-focusing gain spectrum g(K) of the cartesian model | reproduced to 2 % |

## Running them

```bash
python3 python/tools/build_engine.py          # the engine for Python
python3 build/paper_check.py                  # every paper, fast tier (CI); exit 1 if a hard check fails
python3 build/paper_check.py gordon --full    # one paper, full tier
cd papers/gordon-1986-ssfs && python run.py --tier full --figures    # rewrite its committed figures
```

The harness runs each folder's `run.py`, reads `results/numbers.json` and evaluates `checks.json`: `number`
(value ± tolerance, absolute or relative), `ordering` (a < b) and `location` (the maximum of a curve within a
window). Checks of numbers that could not be verified in the paper's text are marked `"hard": false`: reported,
never failing. CI runs the fast tier in the `papers` job.

## Engine fixes found by the replications

* **Raman kernel on coarse grids** (Gordon): the delayed response was sampled on the grid's time step; a step
  longer than the 12 fs Raman oscillation aliased it and the self-frequency shift came out up to 43 % too fast.
  The kernel is now band-limited to the grid (sampled on a sub-grid, cut at the grid's Nyquist frequency).
  Test: `FiberTests.SolitonSelfFrequencyShift_OnACoarseGrid_MatchesGordon`.
* **Compressor instrument FWHM** (Escoto): the compressed duration was read on a compact copy of the pulse whose
  time step resolved the spectrum but not the compressed peak (3.8 % short at B = 40 rad). It is now read on the
  pulse's own grid. Test: `InstrumentTests.Compressor_AfterStrongSpm_ReportsTheFwhmOnTheFullGrid`.
