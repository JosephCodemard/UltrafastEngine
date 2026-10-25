# docs/ -- MkDocs site (also read by AI agents: llms.txt indexes it)

* Plain, precise English; short sentences; SI units; say what the engine does, not how hard it was.
* Every new page goes into `mkdocs.yml` nav and `llms.txt`. `python3 -m mkdocs build --strict` must give 0 warnings
  (broken links and anchors fail).
* `reference/options.md` and `reference/python-api.md` are generated (`/regen`); never edit them.
* Numbers quoted from runs (example READMEs, tutorials) must match a fresh run: re-run with `/example-check` when
  physics, defaults or comparisons change.
* Examples (`examples/NN-name/`): a README with the commands and the expected output, a `.ufs`, `run*.json`; they
  run with `ULTRAFAST_STRICT_OPTIONS=1`. Example 09 needs example 08's results in 08's folder; example 26's
  `run-new-input.json` / `run-backward.json` use `results-fit/fitted-system.ufs`; tutorial specs use `plate.ufs`
  except `run-cpa.json` (`cpa.ufs`).
