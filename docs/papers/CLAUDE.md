# papers/ -- published results reproduced with the engine

Each folder reproduces the **headline results** of one paper (or one lab case) and says plainly what agrees, what
does not, and why (a missing parameter, missing physics, or a bug -- bugs are fixed in the engine with a test first).

* Layout per paper (`papers/<firstauthor>-<year>-<topic>/`):
  `paper.json` (citation, DOI, open access, reference numbers each with page / figure / table, assumptions),
  `system.ufs` / `build.py`, `run.py` (writes `results/`), `checks.json` ({key, expected, tolerance, kind:
  number | ordering | location}), `README.md` (setup, our plots, commentary, the comparison table, what is missing).
* Our plots go in `<paper>/figures/*.png` (committed, small, ≤ 300 kB each); raw results in `results/` (git-ignored).
* `fast` tier (minutes, 1-D) runs in CI through `python3 build/paper_check.py`; `full` tier by hand (`--full`).
* Quote numbers from the paper with their location; never copy the paper's text, figures or PDF (copyright).
  Numbers read off a figure are marked "read off Fig. N" with a tolerance that reflects it.
* Data: only openly published data, or the owner's recordings copied from `../Experiments/data/` (never anything
  else from `../Experiments/`).
* A check that fails is a finding, not something to tune away: triage with `/bug-report`, record the outcome in the
  README's "What does not reproduce" section.
* Index: `papers/README.md` (one row per paper: topic, status, headline numbers ours vs theirs).
