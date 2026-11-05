# Published results reproduced with the engine

Each folder reproduces the headline results of one paper (or models one real beamline) with UltrafastEngine, and says
plainly what agrees, what does not and why. Every folder has the paper's claims, our setup, plots with the published
formula or numbers drawn on the same axes, a short commentary and a comparison table.

| paper | topic | result | headline numbers: published vs ours |
|---|---|---|---|
| [Escoto et al., JOSA B 39, 1694 (2022)](escoto-2022-compression-quality/README.md) | post-compression: temporal quality vs compression factor | reproduced | C ≈ 0.59 B + 1 within 5 % to B = 40: 3.6 % (transform limit), 4.4 % (GDD only, B ≥ 4); energy ratio ~70 % at C = 100: 69.4 %; intensity ratio → 10 %: 10.0 % |
| [Gordon, Opt. Lett. 11, 662 (1986)](gordon-1986-ssfs/README.md) | fibres: soliton self-frequency shift | reproduced against the perturbation theory | shift rate ∝ T0⁻⁴: exponent −4.0 to −4.1 (Blow-Wood, Hollenbeck); rate vs Gordon's integral within 1.3 % (Blow-Wood); about one spectral width in ~100 m: 107 m |
| [Boyd & Kleinman, JAP 39, 3597 (1968)](boyd-kleinman-1968-focused-shg/README.md) | χ(2): focused Gaussian-beam SHG | reproduced | h_m = 1.068 at ξ = 2.84: 1.068 at ξ = 2.85; σ_m = 0.573: 0.574 |
| [Bespalov & Talanov, JETP Lett. 3, 307 (1966)](bespalov-talanov-1966-ssf/README.md) | beams: small-scale self-focusing | reproduced | g_max = k0 n2 I: 0.9997; K_opt = k0 √(2 n2 I / n0): 0.992 K_opt; ripple growth vs cosh(gL) within 2 % |
| [Escoto et al., CLEO 2024, SF2O.8](escoto-2024-ner-mpc/README.md) | nonlinear ellipse rotation in a multi-pass cell: efficiency and contrast vs two waveplate angles | partly reproduced (the cell is not given and is assumed) | dark band and contrast gain along θ1 = θ2, maxima in the (−,+) and (+,−) quadrants, exact symmetry; diagonal maximum at 10°: 15° (B = 10 rad), 11° (B = 15 rad); contrast improvement 8–9 orders: ~6 |
| [Lab case: Pharos + multi-pass cell + SEQUOIA](lab-mpc-ner-contrast/README.md) | a real beamline: spectra fitted with the fitting plugin, measured contrast as the input pedestal, water lines of the air | fitted, partly reproduced (recordings, not a paper) | seed spectrum rms 4 %; cell spectrum rms 12 % with an energy scale 3.8× nominal; contrast within 5 dB at +4…30 ps |

Papers behind a paywall that could not be read are marked UNVERIFIED in their `paper.json`; for those the reference values
are computed from the published formula, not copied from the paper.

## Run them

```bash
python3 python/tools/build_engine.py          # the engine for Python
python3 build/paper_check.py                  # every folder, fast tier (minutes); exit 1 if a hard check fails
python3 build/paper_check.py gordon --full    # one folder, full tier
cd docs/papers/gordon-1986-ssfs && python run.py --tier full --figures    # rewrite its figures
```

The harness runs each folder's `run.py`, reads `results/numbers.json` and evaluates `checks.json`: `number` (a value within
a tolerance), `ordering` (a < b) and `location` (the maximum of a curve within a window). Checks on numbers that could not
be verified in the paper are *soft*: reported, never failing.

## Add a paper

In Claude Code, `/replicate <paper URL, DOI or title>` runs the `paper-replicator` agent: it reads the paper, extracts the
inputs and headline numbers with their locations, builds the smallest faithful system, compares and writes the folder. By
hand, copy a folder and keep the layout:

```
docs/papers/<author>-<year>-<topic>/
  paper.json   citation, DOI, whether the full text was read, every reference number with its location
  run.py       builds and runs the system (Python API); writes results/numbers.json; figures with --figures
  checks.json  the comparison with the published numbers
  figures/     our results with the published curve or numbers on the same axes (each under 300 kB)
  README.md    claims, setup, plots, commentary, comparison table, what does not reproduce
```

`paperlib.py` provides the command line (`--tier`, `--figures`), `write_numbers` and the figure style. Quote numbers with
their location; never copy a paper's text, figures or PDF.
