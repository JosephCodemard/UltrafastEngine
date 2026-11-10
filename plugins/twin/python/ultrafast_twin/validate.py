"""Validation of a twin: does it predict what it was not fitted to?

Rules (stated, not tuned): ``rt`` = max(1, reduced chi^2 of the training conditions). A held-out condition is *flagged* when its
chi^2 per point exceeds 3 rt, *marginal* above 1.5 rt, or when fewer than 80 % of its points lie within 2 sigma of the credible
band combined with the stated noise. Verdict: ``unvalidated`` (no held-out condition), ``fits-training-only`` (a held-out condition is
flagged), ``marginal`` or ``ok``. Credible bands come from parameter draws of the fit covariance (:meth:`Twin.predict`).
"""
from __future__ import annotations

import json
from typing import Any, Dict, List

import numpy as np

FLAG_FACTOR, MARGINAL_FACTOR, MIN_COVERAGE = 3.0, 1.5, 0.8


def validate(twin, n_samples: int = 12, write: bool = True) -> Dict[str, Any]:
    proj = twin.project
    conds = twin.fit.get("conditions", [])
    if not conds:
        raise ValueError("this fit has no conditions: validation needs a multi-condition fit (twin.json conditions)")
    train = [c for c in conds if not c["heldOut"]]
    tp = sum(c["points"] for c in train)
    rt = max(1.0, sum(c["chi2"] for c in train) / tp) if tp else 1.0
    rows: List[Dict[str, Any]] = []
    flags: List[str] = []
    for c in conds:
        pr = twin.predict(name=c["name"], n_samples=n_samples, data=proj.data_path(proj.condition(c["name"])))
        row = {"name": c["name"], "heldOut": c["heldOut"], "variables": c.get("variables", {}), "points": c["points"],
               "reducedChi2": c["reducedChi2"], "coverage2sigma": pr["coverage2sigma"], "rmsNormalisedResidual": pr["rmsNormalisedResidual"],
               "meanBand": float(np.mean(pr["y_sigma"])), "status": "training"}
        if c["heldOut"]:
            red = c["reducedChi2"] if c["reducedChi2"] is not None else float("nan")
            if red > FLAG_FACTOR * rt or pr["coverage2sigma"] < MIN_COVERAGE:
                row["status"] = "flagged"
                flags.append(f"held-out condition {c['name']}: chi^2 per point {red:.3g} (training {rt:.3g}), {100 * pr['coverage2sigma']:.0f} % of points inside the 2-sigma band -- "
                             "the twin does not predict conditions it was not fitted to")
            elif red > MARGINAL_FACTOR * rt:
                row["status"] = "marginal"
            else:
                row["status"] = "ok"
        rows.append(row)
    held = [r for r in rows if r["heldOut"]]
    if not held:
        verdict = "unvalidated"
        flags.append("no held-out condition: nothing tests the twin's predictions (mark at least one condition heldOut)")
    elif any(r["status"] == "flagged" for r in held):
        verdict = "fits-training-only"
    elif any(r["status"] == "marginal" for r in held):
        verdict = "marginal"
    else:
        verdict = "ok"
    report = {"project": proj.name, "fit": twin.dir.name, "verdict": verdict, "trainingReducedChi2": rt, "conditions": rows, "flags": flags,
              "parameters": [{"name": p["name"], "value": p["value"], "sigma": p.get("sigma"), "atBound": p.get("atBound", False)} for p in twin.params],
              "fitWarnings": twin.fit.get("warnings", []), "samples": n_samples}
    if write:
        out = proj.folder / "reports"
        out.mkdir(exist_ok=True)
        (out / "validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        (out / "validation.md").write_text(to_markdown(report), encoding="utf-8")
    return report


def to_markdown(r: Dict[str, Any]) -> str:
    lines = [f"# Validation of {r['project']} ({r['fit']})", "", f"**Verdict: {r['verdict']}**  (training reduced chi^2 {r['trainingReducedChi2']:.3g})", ""]
    lines += ["| condition | held out | points | chi^2 / point | inside 2-sigma band | status |", "|---|---|---|---|---|---|"]
    for c in r["conditions"]:
        lines.append(f"| {c['name']} | {'yes' if c['heldOut'] else 'no'} | {c['points']} | {c['reducedChi2']:.3g} | {100 * c['coverage2sigma']:.0f} % | {c['status']} |")
    if r["flags"]:
        lines += ["", "## Flags", ""] + [f"* {f}" for f in r["flags"]]
    lines += ["", "## Parameters", "", "| parameter | value | sigma |", "|---|---|---|"]
    for p in r["parameters"]:
        s = p["sigma"]
        lines.append(f"| {p['name']} | {p['value']:.6g} | {'-' if s is None else format(s, '.3g')} |" + ("  at bound" if p["atBound"] else ""))
    return "\n".join(lines) + "\n"
