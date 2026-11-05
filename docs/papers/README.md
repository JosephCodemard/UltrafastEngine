# Published results reproduced with the engine

Each folder reproduces the headline results of one paper and says plainly what agrees, what does not, and why
(a missing parameter, missing physics, or a bug -- bugs are fixed in the engine, with a test, before the replication
is adjusted).

## Index

| paper | topic | status | headline numbers: theirs vs ours |
|---|---|---|---|
| [Escoto et al., JOSA B 39, 1694 (2022)](escoto-2022-compression-quality/README.md) | post-compression: temporal quality vs compression factor | reproduced (one claim only from B ≈ 4) | C ≈ 0.59 B + 1 within 5 % to B = 40: FTL 3.6 %, GDD only 4.4 % for B ≥ 4 but 6.1 % at B ≈ 2; FTL energy ratio ~70 % at C = 100: 69.4 %; intensity ratio → 10 %: 10.0 % |
| [Gordon, Opt. Lett. 11, 662 (1986)](gordon-1986-ssfs/README.md) | fibres: soliton self-frequency shift | reproduced against the perturbation theory (engine bug fixed) | rate ∝ T0⁻⁴: exponent −4.02 (Blow-Wood), −4.13 (Hollenbeck); rate vs Gordon's integral 1.3 % (Blow-Wood); ~100 m to shift one width (UNVERIFIED): 107 m (Hollenbeck) |
| [Boyd & Kleinman, JAP 39, 3597 (1968)](boyd-kleinman-1968-focused-shg/README.md) | χ(2): focused Gaussian-beam SHG | reproduced (paper not read; numbers from the BK integral) | h_m = 1.068 at ξ = 2.84: 1.0680 at ξ = 2.84; σ_m 0.573: 0.574; engine vs integral < 0.1 % |
| [Bespalov & Talanov, JETP Lett. 3, 307 (1966)](bespalov-talanov-1966-ssf/README.md) | beams: small-scale self-focusing gain spectrum | reproduced (paper not read; analytic reference) | g_max = k0 n2 I: 0.9997; K_opt = k0 sqrt(2 n0 n2 I): 0.992 K_opt; ripple vs cosh(gL) < 2 % |
| [Escoto et al., CLEO 2024, SF2O.8](escoto-2024-ner-mpc/README.md) | NER in a multi-pass cell: efficiency and contrast vs two QWP angles | partly reproduced (cell assumed; map structure yes, Fig. 2a diagonal and contrast size no) | dark band along theta1 = theta2, maxima in the (-,+)/(+,-) quadrants, symmetry exact; diagonal maximum 10 deg: 19 deg (0.28 of map peak, not 1); contrast improvement ~8-9: 4.2 |
| [Lab case: Pharos + MPC + SEQUOIA](lab-mpc-ner-contrast/README.md) | own recordings: cell spectra and third-order contrast | fitted, partly reproduced (not a paper) | seed transform rms 4 %; 20 % cell spectrum rms 12 % needs B 3.8 x nominal; contrast at +4..30 ps within 5 dB; +49 ps post-pulse not constrained |

"Paper not read" means the full text was not available (paywalled); its numbers are then marked UNVERIFIED in
`paper.json` and only analytic references, computed in `run.py`, are hard checks.

## Rules

* **One folder per paper**, `papers/<firstauthor>-<year>-<topic>/`, with:
  * `paper.json`: citation, DOI, open access, whether the full text was read, and every reference number with its
    location in the paper (page, section, equation, figure or table) and a status: verified in the text,
    analytic, read off a figure, or UNVERIFIED;
  * `run.py` (Python API; or a `system.ufs` / `build.py`): `python run.py --tier fast|full [--figures]`, writes
    `results/numbers.json` (and `results/figures/`; with `--figures` the committed `figures/`);
  * `checks.json`: the checks, each `{id, kind, key, expected, tolerance, source}` (below);
  * `figures/*.png`: our results with the paper's formula or numbers drawn on the same axes, ≤ 300 kB each;
  * `README.md`: what the paper claims, our setup, the plots, a commentary, the comparison table, and
    "What does not reproduce".
* **Tiers.** `fast` (minutes, mostly 1-D) runs in CI through `python3 build/paper_check.py`; `full` (the complete
  figures) runs by hand: `python3 build/paper_check.py --full`, and `python run.py --tier full --figures` in a
  folder to rewrite its committed figures. The harness never rewrites `figures/`.
* **Numbers from the paper** are quoted with their location. Never copy the paper's text, figures or PDF.
  A number read off a figure is marked "read off Fig. N" and gets a tolerance that reflects it. A number that could
  not be checked in a source we read is UNVERIFIED and is never a hard check.
* **Data**: only openly published data, or the owner's own recordings.
* **A failing check is a finding**, not something to tune away: first make sure the setup matches the paper, then
  treat it as a possible engine bug (triage with `/bug-report`), and record the outcome in "What does not
  reproduce". Raw results (`results/`) are git-ignored.

## checks.json

```json
{"checks": [
  {"id": "h_m at xi = 2.84", "kind": "number", "key": "hmAt2p84Engine", "expected": 1.068, "tolerance": 0.005, "relative": true, "source": "..."},
  {"id": "GDD only below FTL", "kind": "ordering", "a": "energyRatioAtB20.gdd", "b": "energyRatioAtB20.ftl", "margin": 0.0},
  {"id": "gain peak", "kind": "location", "key": "gainSpectrum", "expected": 1.0, "tolerance": 0.1},
  {"id": "unverified claim", "kind": "number", "key": "x", "expected": 100, "tolerance": 30, "hard": false, "tiers": ["full"]}
]}
```

* `number`: `key` within `expected` ± `tolerance` (absolute, or a fraction of `expected` with `"relative": true`).
* `ordering`: `a` + `margin` < `b`.
* `location`: the x of the maximum (`"min": true`: minimum) of a curve `{"x": [...], "y": [...]}` lies in
  `expected` ± `tolerance` or in `window` [lo, hi].
* `"hard": false` checks are reported but do not fail the run; `tiers` (default both) limits a check to a tier.
  Keys address `results/numbers.json`, nested objects with dots.

## Shared code

`paperlib.py`: the command line (`--tier`, `--figures`), `write_numbers`, and the figure style (`figure`, `save`
with the 300 kB limit). The harness, `build/paper_check.py`, is tested by `python/tests/test_paper_check.py` on
synthetic paper folders.
