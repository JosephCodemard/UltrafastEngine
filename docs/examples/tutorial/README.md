# Tutorial files

The files used by the step-by-step tutorials in [docs/tutorials](../../tutorials/index.md). Run the commands
from this folder.

| tutorial | files | run |
|---|---|---|
| [1. Your first simulation](../../tutorials/01-first-run.md) | `plate.ufs`, `run.json` | `ultrafast run plate.ufs run.json -o results-1` |
| [2. Diagnostics](../../tutorials/02-diagnostics.md) | `run-diagnostics.json` | `ultrafast run plate.ufs run-diagnostics.json -o results-2` |
| [3. Larger systems](../../tutorials/03-larger-systems.md) | `cpa.ufs`, `run-cpa.json`, `cpa.py` | `ultrafast run cpa.ufs run-cpa.json -o results-3`, `python cpa.py` |
| [4. Your own measured data](../../tutorials/04-measured-data.md) | `make_data.py`, `data/`, `run-measured.json`, `run-fit.json` | `ultrafast run plate.ufs run-measured.json -o results-4a`, `ultrafast run plate.ufs run-fit.json -o results-4b` |

Plot any results folder with `ultrafast-plot results-N`.
