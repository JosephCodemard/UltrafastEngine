# Progress: consolidation of tests, papers and docs (branch `dev/2026-11-consolidate`)

Started 2026-11-07. Owner rules: no push to GitHub (local commits only); PDFs never committed (`docs/papers/*.pdf` is gitignored);
agents must not edit C# or rebuild the engine while others run; engine bugs are reported with a minimal repro, not tuned away.

## Owner decisions
* Replicate all 12 PDFs, including Michelson-Morley 1887 and Michelson 1892.
* Old papers (Escoto, Gordon, Boyd-Kleinman, Bespalov-Talanov, lab-mpc-ner) are deleted (superseded). Kept: `docs/papers/README.md`
  (to rewrite as the index of the new set), `CLAUDE.md`, `paperlib.py`.
* Component Python scripts move to `tests/components/`; the C# sheet stays in `tests/UltrafastEngine.Tests/Components/`.
* Owner will download extra papers I recommended (Maker 1964, Dudley 2006, Nisoli 1997, Ross 1997, SPIDER, d-scan).

## Done
* `verification/` -> `tests/components/` (flat), `docs/verification.md` -> `docs/component-tests.md`; links, CI job (`components`),
  .gitignore updated; `run_all.py --no-plots` = 378/378.
* Old paper folders removed; stale code-comment references removed (FiberTests, SystemTests, Instruments.cs, Nonlinear.cs).
* Docs audit against code: capabilities, concepts, roadmap (+ROADMAP.md), index, README, agents, architecture, plugins, llms.txt.

## In progress (background agents; check the folders for output)
* Paper replications, each in `docs/papers/<author>-<year>-<topic>/` (paper.json, run.py, checks.json, figures/, README.md):
  FROG pair; Michelson 1887 + 1892 + Treacy 1969; Shimizu 1967 + CS2 NER cleaner; multi-pass cell pair.
* Component families in `tests/components/`: `optics_misc.py`, `media.py`, `crystals_chi2.py`, `spatial.py`, `instruments_more.py`
  (repros of engine bugs in `tests/components/repro/`).

## To do
1. Not started papers: Bjorkholm-type "dispersion and focusing in harmonic production" (BBO/SHG), "Powerful fs pulse generation by
   chirped parametric amplification in BBO" (OPCPA), "Single-shot cross-correlator for contrast".
2. Register new families in `FAMILIES` in `tests/components/run_all.py`; run `run_all.py --no-plots`; add them to
   `docs/component-tests.md`, `docs/testing.md`, `PROJECT_STATUS.md` (counts), CHANGELOG (Unreleased); add C# sheet entries where cheap.
3. Rewrite `docs/papers/README.md` (index, ours vs paper, what does not reproduce); run `.venv/bin/python build/paper_check.py`
   (currently exits 1 when no folders exist); `mkdocs build --strict` must be 0 warnings.
4. Collect agent reports; list every engine disagreement for the owner (they want these flagged, not fixed silently).
5. Known findings not yet fixed: generated options.md "More:" links point to nonexistent `reference/elements.md` (C# in
   `BuiltInElements.cs`, then `/regen`); `docs/CLAUDE.md` claims broken anchors fail the build (no `validation:` in mkdocs.yml).
6. Run `/verify` before the final commit.

## Environment
`export ULTRAFAST_LIBRARY=$PWD/python/src/ultrafast/bin/hosted/UltrafastEngine.Native.dll PYTHONPATH=python/src ULTRAFAST_STRICT_OPTIONS=1 MPLBACKEND=Agg`
and use `.venv/bin/python`. Rebuild the engine only with no agents running: `.venv/bin/python python/tools/build_engine.py`.
