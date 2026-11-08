# Published results reproduced with the engine

Each folder reproduces the headline results of one paper (or models one real beamline) with UltrafastEngine, and says
plainly what agrees, what does not and why. Every folder has the paper's claims, our setup, plots with the published
formula or numbers drawn on the same axes, a short commentary and a comparison table.

| folder | paper | topic | result | headline numbers: published vs ours |
|---|---|---|---|---|
| [kane-1993-frog](kane-1993-frog/README.md) | Kane & Trebino, IEEE JQE 29, 571 (1993) | FROG: SD / PG / SHG traces | reproduced with the engine's self-diffraction gate and retrieval | chirp-rate slopes 4/3 (SD) and 2/3 (PG): 1.3333, 0.6667; SD retrieval sign right on 6 of 6 seeds (GDD within 4.2 % of +6247 fs²); SHG trace symmetric: 1e-15 |
| [kida-2014-double-frog](kida-2014-double-frog/README.md) | Kida et al., Opt. Express 22, 2014 | double FROG: removing the time-direction ambiguity | partly reproduced (engine XFROG and multi-trace retrieval; the two-trace case differs) | double FROG finds the right time direction in 16 of 16 starts (paper 6 of 6; partly by construction); XFROG error ratio wrong vs right reference 2.06 (paper 1.8-2.5); two-trace retrieval lands in the twin in 13 of 16 starts; substrate GDD 18.2 vs 25.6 fs² (paper's path unspecified) |
| [michelson-1887-ether-null](michelson-1887-ether-null/README.md) | Michelson & Morley, Am. J. Sci. 34, 333 (1887) | Michelson interferometer readout | readout reproduced (the null is built in, so the check is circular) | 0.4 fringe for D = 11 m: 0.4000; equal arms: 0; white-light envelope to 2e-5 |
| [perot-1899-multiple-beam-interference](perot-1899-multiple-beam-interference/README.md) | Perot & Fabry, Astrophys. J. 9, 87 (1899) (the PDF supplied as 'Michelson 1892') | Fabry-Perot etalon fringes | reproduced | Airy coefficient 80: 80; coincidence period 311.9 / 14.56515 / 4.759901: 311.914 / 14.56515 / 4.759902 |
| [treacy-1969-grating-pulse-compression](treacy-1969-grating-pulse-compression/README.md) | Treacy, IEEE JQE 5, 454 (1969) | grating-pair compressor | engine matches the paper's equations; four printed numbers do not | GDD, delay vs eqs. 7, 8: 1e-5; delay 0.58 ps/cm printed, eq. 5a and engine 0.610; optimum 10 cm printed, 9.51 cm; eq. 17 (TOD) 1.76x the exact value; angular dispersion per 100 Å 1.31e-2 vs ~2e-2 rad (scan reading) |
| [shimizu-1967-cs2-broadening](shimizu-1967-cs2-broadening/README.md) | Shimizu, Phys. Rev. Lett. 19, 1097 (1967) | SPM in liquid CS2 with delayed response | qualitatively reproduced (n2, intensity, τ not given: assumed); built-in CS2 cross-checked | fringe spacing grows to the extremity (1.02 of the stationary-phase formula); red side 0.72-0.95 (paper ~0.9); mean period 6.8 vs 10 ± 3 cm⁻¹; engine equals the paper's model to 6e-4 |
| [liu-2013-ner-cs2-pulse-cleaner](liu-2013-ner-cs2-pulse-cleaner/README.md) | Liu et al., 2013 | NER pulse cleaner in CS2 | reproduced for the model; cleaned width not | Eq. (1) maximum transmissivity 0.667 (paper ~0.66); contrast gain 3.9 vs 3.7 orders; optimum angle 32.5°; width 158 vs ~60 fs (CS2 GDD in the 1-D model); built-in CS2 gives 171 fs and a higher optimum (response values unverified) |
| [schulte-2016-mpc-fused-silica](schulte-2016-mpc-fused-silica/README.md) | Schulte et al., Opt. Lett. 41, 4511 (2016) | multi-pass cell, fused-silica plate | reproduced (cell geometry assumed; launch beam from `cellMode`) | bandwidth 13.6 / 14.2 / 17.2 nm vs 13.4 / 13.5 / 16.1 nm; autocorrelation 207 vs 200 fs; main peak 79 vs ~75 %; the 514 mm air-gap reading is unstable (50 nm, B = 135 rad) |
| [kaur-2024-mpc-ner-xpw](kaur-2024-mpc-ner-xpw/README.md) | Kaur et al., J. Phys. Photonics 6, 015001 (2024) | NER in a multi-pass cell (XPW not modelled) | partly reproduced (NER ceilings and XPW angle law; cell and B of the optimum do not match) | efficiency ceiling 57 / 68 % vs 58 / 69 %; pressure and B of the optimum differ (paper ~350 mbar, B ≈ 6); XPW angle law with built-in BaF2 (peak at 25.5° from [100] in a [011] cut = 64.5° from [01-1]); XPW efficiency 26-53 % vs 57-65 % depending on the assumed beam radius |
| [ma-2018-single-shot-cross-correlator](ma-2018-single-shot-cross-correlator/README.md) | Ma et al., 2018 | single-shot cross-correlator for contrast | instrument window and resolution reproduced; crystal physics and artefacts not simulated | window 32.4 / 37.6 ps/cm and resolution 0.53 / 0.79 / 1.05 ps (1 / 1.5 / 2 mm) equal eqs. 1-2 to 1e-6; BK7 ghost spacing 11.63 ps; replica II 3.7e-6 vs 2.7e-6, replicas I and III 0.5 and 0.95 decades above the paper (plate angle and coating unspecified) |
| [maker-1962-dispersion-focusing-harmonics](maker-1962-dispersion-focusing-harmonics/README.md) | Maker et al., Phys. Rev. Lett. 8, 21 (1962) | Maker fringes, focused SHG | partly reproduced | quartz fringe 13.71 vs 13.9 µm; KDP θ0 50.55° vs 52 ± 2°; d_eff follows sin 2φ at all 19 azimuths (ratio to the textbook form 1.0238 = sin(θ+ρ)/sinθ); focusing gain 0.54 of the 1/area law vs the paper's ×30 |
| [dubietis-1992-chirped-pulse-parametric-amplification](dubietis-1992-chirped-pulse-parametric-amplification/README.md) | Dubietis et al., Opt. Commun. 88, 437 (1992) | chirped-pulse parametric amplification in BBO | partly reproduced | 67 fs limit, 24x compression, 0.9 GW agree; OPA stage gain 6.9 at the 2° crossing (15.8 collinear); fibre width 264 vs 155 cm⁻¹, grating 202 vs 170 mm, compressed 129 vs 70 fs do not |

Each README says first whether the PDF could be read. Several are scans (Michelson-Morley, Perot-Fabry, Treacy, Shimizu): numbers
that could not be read are marked UNVERIFIED in `paper.json` and are soft checks. Where a paper does not give its inputs, the
README lists the assumption. The papers' own figures are never reproduced here; our plots carry the published numbers or formula.

Each README ends its comparison with a section *How well the science matched*: per quantity, whether it agrees and the most likely
reason (inputs the paper does not give, a paper erratum, an engine limitation or an engine bug).

## Run them

```bash
python3 python/tools/build_engine.py          # the engine for Python
python3 build/paper_check.py                  # every folder, fast tier (minutes); exit 1 if a hard check fails
python3 build/paper_check.py kane --full    # one folder, full tier
cd docs/papers/kane-1993-frog && python run.py --tier full --figures    # rewrite its figures
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
