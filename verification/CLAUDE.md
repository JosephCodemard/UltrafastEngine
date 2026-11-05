# verification/ -- every component checked alone, with plots a person can read

* `components/<family>.py`: runs each component of the family alone (Python API), overlays the analytic answer,
  prints the numbers, and exits non-zero if a check fails. Families: linear, splitters_masks, polarisation, crystals,
  instruments (gain, gases, fibres still to write). Harness `_common.py` (`Family.check`, `figure`, `overlay`;
  `known="..."` marks a reported, unfixed engine bug: shown as KNOWN, does not fail), references `_analytic.py`
  (no engine import). Read docs/verification.md for the conventions that trip analytic expectations.
* The checks follow the component sheet: energy, dispersion, carrier (515 nm,
  1030 nm, 2 µm), bandwidth (1 ps and 10 fs), topology (Sequential = Branched), ports, off cases, reversal,
  polarisation (circular / linear SPM = 2/3, NER rotation B sin2χ / 3).
* `run_all.py` runs them all and writes `report/index.html` (git-ignored): figure, formula, numbers, pass / fail.
  `run_all.py --no-plots` is the CI mode.
* The C# component tests (`tests/UltrafastEngine.Tests/Components/`) assert the same numbers; keep the two in step.
* Environment: the repo `.venv`, `PYTHONPATH=python/src`, `ULTRAFAST_LIBRARY` pointing at the hosted build
  (`python/src/ultrafast/bin/hosted/UltrafastEngine.Native.dll`) after `python3 python/tools/build_engine.py`.
