"""Goal files and the benchmark of design tasks with known answers.

A goal file (``goals.json`` in the project) states what a good twin must do, as checks an agent or a CI job can run::

    {"format": "ultrafast-twin-goals", "checks": [
      {"id": "held-out", "type": "validationVerdict", "equals": "ok"},
      {"id": "slope", "type": "parameter", "name": "kerr.bRad.c0", "value": 3.0, "relTol": 0.02},
      {"id": "precision", "type": "parameterSigma", "name": "filter.centerNm", "max": 0.05},
      {"id": "heldChi2", "type": "heldOutChi2", "max": 2.0},
      {"id": "coverage", "type": "coverage", "min": 0.8},
      {"id": "pred", "type": "prediction", "variables": {"energyJ": 2e-6}, "set": {"pulse/energyJ": 2e-6}, "scalar": "energyJ", "value": 8.8e-7, "relTol": 0.02}]}

Check types: ``validationVerdict`` (equals), ``parameter`` (value with relTol/absTol), ``parameterSigma`` (max), ``heldOutChi2`` (max),
``coverage`` (min, on held-out conditions), ``prediction`` (a scalar of a predicted condition, value with relTol/absTol).
:func:`check` returns a result per check and an overall ``passed``.
"""
from __future__ import annotations

import json
import tempfile
from pathlib import Path
from typing import Any, Dict, List

import numpy as np


class GoalError(ValueError):
    pass


TYPES = ("validationVerdict", "parameter", "parameterSigma", "heldOutChi2", "coverage", "prediction")


def load_goals(path) -> Dict[str, Any]:
    g = json.loads(Path(path).read_text(encoding="utf-8"))
    if g.get("format") != "ultrafast-twin-goals":
        raise GoalError(f"{path}: \"format\" must be \"ultrafast-twin-goals\"")
    for c in g.get("checks", []):
        if c.get("type") not in TYPES:
            raise GoalError(f"check {c.get('id')}: type must be one of {', '.join(TYPES)}")
    return g


def _close(value: float, target: float, rel: float = 0.0, abs_: float = 0.0) -> bool:
    return abs(value - target) <= abs_ + rel * abs(target)


def check(twin, goals: Dict[str, Any], samples: int = 8) -> Dict[str, Any]:
    report = twin.validate(n_samples=samples, write=False)
    by_name = {p["name"]: p for p in twin.params}
    out: List[Dict[str, Any]] = []
    for c in goals.get("checks", []):
        t, ok, detail = c["type"], False, ""
        try:
            if t == "validationVerdict":
                ok, detail = report["verdict"] == c["equals"], f"verdict {report['verdict']}"
            elif t == "parameter":
                v = by_name[c["name"]]["value"]
                ok, detail = _close(v, c["value"], c.get("relTol", 0.0), c.get("absTol", 0.0)), f"{v:.6g} vs {c['value']}"
            elif t == "parameterSigma":
                s = by_name[c["name"]].get("sigma")
                ok, detail = s is not None and s <= c["max"], f"sigma {s}"
            elif t == "heldOutChi2":
                held = [r["reducedChi2"] for r in report["conditions"] if r["heldOut"]]
                ok, detail = bool(held) and max(held) <= c["max"], f"max {max(held) if held else 'n/a'}"
            elif t == "coverage":
                held = [r["coverage2sigma"] for r in report["conditions"] if r["heldOut"]]
                ok, detail = bool(held) and min(held) >= c["min"], f"min {min(held) if held else 'n/a'}"
            elif t == "prediction":
                pr = twin.predict(c.get("variables"), c.get("set"), n_samples=samples)
                v = pr["scalars"][c["scalar"]][0]
                ok, detail = _close(v, c["value"], c.get("relTol", 0.0), c.get("absTol", 0.0)), f"{v:.6g} vs {c['value']}"
        except KeyError as e:
            detail = f"missing {e}"
        out.append({"id": c.get("id", t), "type": t, "passed": bool(ok), "detail": detail})
    return {"passed": all(o["passed"] for o in out) and bool(out), "checks": out, "verdict": report["verdict"]}


# ---------------------------------------------------------------------------------------------------- benchmark
def benchmark(workdir=None) -> Dict[str, Any]:
    """Design tasks whose answer is known from the construction of the synthetic lab:

    1. *placement*: an instrument before the filter sees nothing of the parameters (gain 0); one after the nonlinear phase sees all;
    2. *sensitivity*: a phase proportional to the energy cannot be seen at zero energy (the reading does not depend on the slope) and can at
       0.3 uJ. (Not a ranking: measured, a zero-energy shot is the *best* way to shrink the slope's uncertainty because it pins the
       filter parameters the slope is correlated with -- see docs/plugins/twin.md.)
    3. *saturation*: a twin trained on a saturating lab is flagged, one on the linear lab is not."""
    from . import design, project
    tmp = Path(workdir) if workdir else Path(tempfile.mkdtemp(prefix="uf-twin-bench-"))
    tw = project.init(tmp / "linear", "synthetic-lab").fit()
    results = []
    pl = design.rank_candidates(tw, design.placement_candidates(tw))["ranking"]
    results.append({"task": "placement", "expected": "input has zero gain and the last position ranks first",
                    "answer": [r["name"] for r in pl], "passed": abs(pl[-1]["gain"]) < 1e-6 and pl[-1]["name"].endswith("input") and pl[0]["name"].split()[-1] in ("kerr", "end")})
    cands = design.energy_candidates(tw, "energyJ", [1e-12, 0.3e-6], "pulse/energyJ")
    col = [float(np.linalg.norm(design.jacobian(tw, c)[:, 2])) for c in cands]   # d(reading)/d(slope)
    results.append({"task": "sensitivity", "expected": "d(reading)/d(slope) is zero at zero energy and clearly non-zero at 0.3 uJ", "answer": col, "passed": col[0] < 1e-9 and col[1] > 0.05})
    v_good = tw.validate(n_samples=4, write=False)["verdict"]
    v_bad = project.init(tmp / "saturating", "synthetic-lab-saturating").fit().validate(n_samples=4, write=False)["verdict"]
    results.append({"task": "saturation", "expected": "linear lab ok, saturating lab fits-training-only", "answer": [v_good, v_bad], "passed": v_good == "ok" and v_bad == "fits-training-only"})
    return {"passed": all(r["passed"] for r in results), "tasks": results}
