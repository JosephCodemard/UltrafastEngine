"""The operations the MCP worker runs (inside the child process; they import the engine).

``describe``, ``validate``, ``run``, ``scan``, ``study``, ``backward``, ``compare``, ``fit``, ``plot`` and the twin operations (``twin_fit``,
``twin_validate``, ``twin_predict``, ``twin_design``, ``twin_track``, ``twin_init``; they call the twin plugin's own functions) take the arguments the server has already checked
(:mod:`ultrafast.mcp.policy`) and return plain JSON-able dicts. ``system`` / ``run`` arrive as an absolute path string or a dict;
``outDir`` is an absolute folder inside the workspace. ``preflight`` raises :class:`~ultrafast.mcp.policy.Refused` when the memory
estimate is above the limit.
"""
from __future__ import annotations

import base64
import json
import math
import os
import re
import tempfile
import time
from pathlib import Path
from typing import Any, Dict, List

from .policy import Refused, strip_jsonc

MAX_ELEMENTS = 200
MAX_LIST = 40
MAX_IMAGE_BYTES = 900_000


# ------------------------------------------------------------------------ helpers
def _uf():
    import ultrafast as uf
    return uf


def _clean(x: Any) -> Any:
    """JSON-safe: NaN / inf -> None, numpy -> Python."""
    import numpy as np
    if isinstance(x, np.ndarray):
        return _clean(x.tolist())
    if isinstance(x, (np.floating, np.integer)):
        return _clean(x.item())
    if isinstance(x, float):
        return x if math.isfinite(x) else None
    if isinstance(x, dict):
        return {str(k): _clean(v) for k, v in x.items()}
    if isinstance(x, (list, tuple)):
        return [_clean(v) for v in x]
    return x


def load_json_file(path: str) -> dict:
    return json.loads(strip_jsonc(Path(path).read_text(encoding="utf-8-sig")))


def _brief(summary: dict) -> dict:
    els = []
    for e in (summary.get("elements") or [])[:MAX_ELEMENTS]:
        els.append({k: e[k] for k in ("id", "type", "lengthM", "energyOutJ", "bIntegralRad", "peakFluenceJPerM2", "lidtMargin", "criticalPowerRatio")
                    if k in e})
    ins = [{"name": i.get("name"), "kind": i.get("kind"), "values": i.get("values", {})} for i in (summary.get("instruments") or [])[:MAX_LIST]]
    return {"output": summary.get("output"), "elements": els, "instruments": ins, "unverifiedInputs": (summary.get("unverifiedInputs") or [])[:MAX_LIST]}


def preflight(system: Any, spec: Any, copies: int = 1) -> dict:
    """Refuse a problem whose estimated memory is above the limit (``ULTRAFAST_MCP_MEMORY_LIMIT_BYTES``) or 60 % of the free memory."""
    uf = _uf()
    try:
        est = uf.estimate_memory(system, spec)
    except Exception:     # a problem that does not compile reports its own error when it runs
        return {}
    total = float(est.get("totalBytes", 0)) * max(1, copies)
    limit = float(os.environ.get("ULTRAFAST_MCP_MEMORY_LIMIT_BYTES", "8e9"))
    avail = float(est.get("availableBytes", -1))
    if avail > 0:
        limit = min(limit, 0.6 * avail)
    if est.get("exceedsLimit") or total > limit:
        raise Refused("estimated memory %.2f GB%s is above the limit of %.2f GB (free: %s); lower the grid size, set outputs.keepProfiles to "
                      "\"used\", use fidelity \"quick\" or fewer parallel points (docs/reference/memory.md)"
                      % (total / 1e9, " (%d runs at once)" % copies if copies > 1 else "", limit / 1e9, "%.2f GB" % (avail / 1e9) if avail > 0 else "unknown"))
    return est


# --------------------------------------------------------------------- operations
def validate(a: Dict[str, Any]) -> dict:
    uf = _uf()
    sys_ = uf.System.load(a["system"])
    rep = sys_.validate()
    errors = list(rep.get("errors", []))
    warnings = list(rep.get("warnings", []))
    if a.get("run") is not None:
        try:
            uf.Run(sys_, a["run"] if isinstance(a["run"], dict) else a["run"])
        except Exception as e:      # the run spec does not parse or does not fit the system
            errors.append("run spec: %s" % e)
    return {"ok": not errors and bool(rep.get("ok", True)), "errors": errors, "warnings": warnings, "notes": list(rep.get("notes", []))[:MAX_LIST],
            "elementCount": rep.get("elementCount")}


def describe(a: Dict[str, Any]) -> dict:
    uf = _uf()
    sys_ = uf.System.load(a["system"])
    d = sys_.to_dict()
    els = []
    for e in (d.get("elements") or [])[:MAX_ELEMENTS]:
        els.append({k: v for k, v in e.items() if not isinstance(v, (dict, list)) or k in ("next",)})
    rep = sys_.validate()
    return {"name": d.get("name"), "topology": d.get("topology", "sequential"), "wavelengthsM": d.get("wavelengthsM"), "elementCount": len(d.get("elements") or []),
            "elements": els, "ok": rep.get("ok"), "errors": rep.get("errors", []), "warnings": rep.get("warnings", []), "notes": list(rep.get("notes", []))[:MAX_LIST]}


def _with_fidelity(spec: Any, fidelity: Any):
    """(spec to use, temporary file to delete). A run file is copied next to the original so relative data paths still resolve."""
    if not fidelity:
        return spec, None
    if isinstance(spec, dict):
        d = dict(spec); d["fidelity"] = fidelity
        return d, None
    d = load_json_file(spec); d["fidelity"] = fidelity
    tmp = Path(spec).with_name(".mcp-%d-%s" % (os.getpid(), Path(spec).name))
    tmp.write_text(json.dumps(d), encoding="utf-8")
    return str(tmp), tmp


def run(a: Dict[str, Any]) -> dict:
    uf = _uf()
    spec, tmp = _with_fidelity(a["run"], a.get("fidelity"))
    try:
        est = preflight(a["system"], spec)
        t0 = time.time()
        res = uf.run(a["system"], spec, memory="off")
        out = Path(a["outDir"]); out.mkdir(parents=True, exist_ok=True)
        res.write(out)
        s = res.summary
        r = {"resultFolder": a["outDirRel"], "elapsedS": round(time.time() - t0, 3), "fidelityReport": s.get("fidelityReport"),
             "warnings": list(s.get("warnings", []))[:MAX_LIST], "validity": list(s.get("validity", []))[:MAX_LIST], "notes": list(s.get("notes", []))[:MAX_LIST]}
        r.update(_brief(s))
        r["estimatedMemoryBytes"] = est.get("totalBytes")
        fid = (s.get("fidelityReport") or {}).get("level") or a.get("fidelity")
        if fid in ("quick", "standard"):
            r["advice"] = "This ran in '%s'. Re-run the points you will report with fidelity 'full' and say which numbers came from which level." % fid
        if r["validity"]:
            r["advice"] = (r.get("advice", "") + " Validity entries are present: read them (explain_warning) before trusting the result.").strip()
        return _clean(r)
    finally:
        if tmp is not None:
            try:
                tmp.unlink()
            except OSError:
                pass


def scan(a: Dict[str, Any]) -> dict:
    uf = _uf()
    spec = a["run"]
    fid = a.get("fidelity")
    n_par = int(a.get("parallel", 1))
    preflight(a["system"], spec, n_par)
    t0 = time.time()
    sc = uf.scan(a["system"], spec, a["axes"], a["outputs"], fidelity=fid, parallel=n_par, memory="off")
    sc.compute()
    r: Dict[str, Any] = {"axes": {k: list(map(float, v)) for k, v in sc.axes.items()}, "shape": list(sc.shape),
                         "outputs": {o: sc[o] for o in sc.outputs}, "errors": [{k: v for k, v in e.items()} for e in sc.errors[:5]],
                         "errorCount": len(sc.errors), "notes": list(sc.notes)[:MAX_LIST], "elapsedS": round(time.time() - t0, 3)}
    if fid in ("quick", "standard"):
        r["advice"] = ("Scanned in '%s'. Re-run the best one to three points with the run tool in fidelity 'full' and compare; do not trust the "
                       "scan where a quick-level warning fired (see resource ultrafast://fidelity)." % fid)
    return _clean(r)


def _write_if_named(res: Any, a: Dict[str, Any]) -> None:
    if a.get("outDir"):
        out = Path(a["outDir"]); out.mkdir(parents=True, exist_ok=True)
        res.write(out)


def compare(a: Dict[str, Any]) -> dict:
    """Run and return only the comparisons of the instruments with their recordings."""
    uf = _uf()
    spec, tmp = _with_fidelity(a["run"], a.get("fidelity"))
    try:
        preflight(a["system"], spec)
        t0 = time.time()
        res = uf.run(a["system"], spec, memory="off")
        _write_if_named(res, a)
        s = res.summary
        comps = list(s.get("comparisons") or [])
        if not comps:
            raise ValueError("the run produced no comparison: check that the instruments' 'measured' files exist and overlap the simulated range (see the run notes: %s)" % "; ".join(list(s.get("notes", []))[:3]))
        keep = ("instrument", "kind", "at", "mode", "points", "chi2", "reducedChi2", "rmsError", "file", "normalization", "xAxis", "values", "notes")
        r: Dict[str, Any] = {"comparisons": [{k: c[k] for k in keep if k in c} for c in comps[:MAX_LIST]],
                             "totalChi2": sum(float(c.get("chi2", 0.0)) for c in comps), "warnings": list(s.get("warnings", []))[:MAX_LIST],
                             "elapsedS": round(time.time() - t0, 3)}
        if a.get("outDirRel"):
            r["resultFolder"] = a["outDirRel"]
        if (a.get("fidelity") or "full") != "full":
            r["advice"] = "Compared in '%s': confirm the agreement with fidelity 'full' before reporting it." % a["fidelity"]
        return _clean(r)
    finally:
        if tmp is not None:
            try:
                tmp.unlink()
            except OSError:
                pass


def fit(a: Dict[str, Any]) -> dict:
    """Run a spec with a ``fit`` section (fitting plugin); return the fitted values, their uncertainties and chi2."""
    uf = _uf()
    spec, tmp = _with_fidelity(a["run"], a.get("fidelity"))
    try:
        est = preflight(a["system"], spec)
        t0 = time.time()
        res = uf.run(a["system"], spec, memory="off")
        _write_if_named(res, a)
        s = res.summary
        f = s.get("fit")
        if not f:
            raise ValueError("the run produced no fit result: is the fitting plugin loaded (ultrafast plugins) and does the run spec have a 'fit' section? Notes: %s" % "; ".join(list(s.get("notes", []))[:3]))
        r: Dict[str, Any] = {"fit": f, "warnings": list(s.get("warnings", []))[:MAX_LIST], "resultFolder": a.get("outDirRel"), "elapsedS": round(time.time() - t0, 3),
                             "estimatedMemoryBytes": est.get("totalBytes")}
        r["advice"] = "Read the parameter uncertainties and the chi2 before and after; a parameter at its bound is not determined. Confirm the fitted system with the run tool in fidelity 'full'."
        return _clean(r)
    finally:
        if tmp is not None:
            try:
                tmp.unlink()
            except OSError:
                pass


_FIGURES = ("overview", "system", "instruments", "measurements", "probes", "elements", "byproducts", "gain", "oscillator", "spectrogram_3d")


def plot(a: Dict[str, Any]) -> dict:
    import matplotlib
    matplotlib.use("Agg")
    from .. import plotting as up
    folder = Path(a["resultDir"])
    if not (folder / "summary.json").is_file():
        raise ValueError("no results in %s (run the 'run' tool first, or give the folder of a finished run)" % a["resultDirRel"])
    res = up.load(folder)
    name = a.get("figure", "overview")
    if name not in _FIGURES:
        raise ValueError("unknown figure %r (%s)" % (name, ", ".join(_FIGURES)))
    with up.use_style(a.get("style", "ultrafast"), column=a.get("column")):
        fig = getattr(up, name)(res)
        if fig is None:
            raise ValueError("this result has no data for the '%s' figure" % name)
        out = folder / "plots"; out.mkdir(exist_ok=True)
        path = out / ("%s-%s.png" % (name, a.get("style", "ultrafast")))
        dpi = min(int(a.get("dpi", 110)), 200)
        fig.savefig(path, dpi=dpi)
    import matplotlib.pyplot as plt
    plt.close("all")
    data = path.read_bytes()
    r: Dict[str, Any] = {"file": a["resultDirRel"] + "/plots/" + path.name, "figure": name, "style": a.get("style", "ultrafast"), "bytes": len(data)}
    if len(data) <= MAX_IMAGE_BYTES:
        r["_imageBase64"] = base64.b64encode(data).decode("ascii")
    else:
        r["note"] = "image too large to return inline (%.1f MB); open the file" % (len(data) / 1e6)
    return r


MAX_STUDY_ROWS = 400
MAX_TWIN_POINTS = 200


def study(a: Dict[str, Any]) -> dict:
    """A parameter study (scan / optimize / sensitivity) through ``uf.study``; the arguments were limited by the policy (at most 400 evaluations)."""
    uf = _uf()
    spec, tmp = _with_fidelity(a["run"], a.get("fidelity"))
    try:
        n_par = int(a.get("parallel", 1))
        preflight(a["system"], spec, n_par)
        opts: Dict[str, Any] = {k: a[k] for k in ("sampling", "samples", "objective", "sensitivity", "seed") if k in a}
        if n_par > 1:
            opts["parallel"] = n_par
        t0 = time.time()
        res = uf.study(a["system"], spec, parameters=a["parameters"], outputs=a["outputs"], mode=a["mode"], **opts)
        rows = list(res.get("rows") or [])
        r: Dict[str, Any] = {"mode": a["mode"], "rowCount": len(rows), "rows": rows[:MAX_STUDY_ROWS], "analysis": res.get("analysis"), "notes": list(res.get("notes") or [])[:MAX_LIST],
                             "errors": list(res.get("errors") or [])[:5], "elapsedS": round(time.time() - t0, 3)}
        if len(rows) > MAX_STUDY_ROWS:
            r["truncated"] = "%d of %d rows returned; narrow the study or read the analysis" % (MAX_STUDY_ROWS, len(rows))
        if (a.get("fidelity") or "full") != "full":
            r["advice"] = "Studied in '%s'. Re-run the best point with the run tool in fidelity 'full' and say which numbers came from which level." % a["fidelity"]
        return _clean(r)
    finally:
        if tmp is not None:
            try:
                tmp.unlink()
            except OSError:
                pass


def backward(a: Dict[str, Any]) -> dict:
    """Backward propagation (the run spec's ``backward`` section): write the results and return the instrument values and the reconstruction."""
    uf = _uf()
    spec, tmp = _with_fidelity(a["run"], a.get("fidelity"))
    try:
        preflight(a["system"], spec)
        t0 = time.time()
        res = uf.run(a["system"], spec, memory="off")
        out = Path(a["outDir"]); out.mkdir(parents=True, exist_ok=True)
        res.write(out)
        s = res.summary
        r = {"resultFolder": a["outDirRel"], "elapsedS": round(time.time() - t0, 3), "backward": s.get("backward"), "warnings": list(s.get("warnings", []))[:MAX_LIST],
             "notes": list(s.get("notes", []))[:MAX_LIST]}
        r.update(_brief(s))
        if (out / "reconstructed-pulse.json").is_file():
            r["reconstructedPulseFile"] = a["outDirRel"] + "/reconstructed-pulse.json"
        else:
            r["warnings"].append("no reconstructed-pulse.json was written: is the fitting plugin loaded (ultrafast plugins) and does the 'backward' section end at the input?")
        r["advice"] = "Backward propagation amplifies noise wherever the system attenuated the light: compare the reconstruction at two 'regularization' values before quoting it."
        return _clean(r)
    finally:
        if tmp is not None:
            try:
                tmp.unlink()
            except OSError:
                pass


# ---------------------------------------------------------------- digital twin
MAX_DESIGN_RUNS = 240     # twin_design: candidates x (fitted parameters + 1) engine runs


def _twin_module():
    uf = _uf()
    uf.plugins.load_python_plugins()
    try:
        import ultrafast_twin as tw
    except ImportError:
        raise ValueError("the twin plugin is not available (ultrafast plugins list; install it with `ultrafast plugins install`)")
    return tw


def _project(a: Dict[str, Any]):
    return _twin_module().TwinProject(a["folder"])


def twin_fit(a: Dict[str, Any]) -> dict:
    t0 = time.time()
    proj = _project(a)
    twin = proj.fit(a.get("name"))
    folder = Path(a["folder"])
    latest = (folder / "fits" / "latest.txt").read_text(encoding="utf-8").strip() if (folder / "fits" / "latest.txt").is_file() else None
    return _clean({"fit": latest, "parameters": list(twin.names()), "elapsedS": round(time.time() - t0, 3),
                   "advice": "Run twin_validate next: a fit is only a model until it predicts held-out conditions."})


def twin_validate(a: Dict[str, Any]) -> dict:
    t0 = time.time()
    rep = _project(a).twin().validate(n_samples=int(a.get("samples", 12)))
    keep = ("project", "fit", "verdict", "trainingReducedChi2", "conditions", "flags", "parameters", "fitWarnings", "samples")
    r = {k: rep[k] for k in keep if k in rep}
    r["elapsedS"] = round(time.time() - t0, 3)
    r["advice"] = "Do not quote a prediction outside the conditions the verdict covers; every flag says where the twin fails."
    return _clean(r)


def _thin(x, n: int = MAX_TWIN_POINTS):
    import numpy as np
    x = np.asarray(x, dtype=float)
    if x.size <= n:
        return x
    return x[np.linspace(0, x.size - 1, n).astype(int)]


def twin_predict(a: Dict[str, Any]) -> dict:
    t0 = time.time()
    p = _project(a).twin().predict(variables=a.get("variables") or None, set=a.get("set") or None, n_samples=int(a.get("samples", 12)))
    r = {"x": _thin(p["x"]), "y": _thin(p["y"]), "ySigma": _thin(p["y_sigma"]), "pointsReturned": min(len(p["x"]), MAX_TWIN_POINTS), "pointsTotal": len(p["x"]),
         "scalars": {k: {"mean": v[0], "sigma": v[1]} for k, v in p["scalars"].items()}, "samples": p["n_samples"], "elapsedS": round(time.time() - t0, 3),
         "advice": "y is peak-normalised; the band is 1 sigma over parameter draws. A prediction outside the fitted conditions is an extrapolation: say so."}
    return _clean(r)


def twin_design(a: Dict[str, Any]) -> dict:
    """Rank candidate measurements with ``ultrafast_twin.design`` (expected information gain at the fitted point); writes reports/design.json."""
    t0 = time.time()
    from ultrafast_twin import design as D
    proj = _project(a)
    twin = proj.twin()
    if a.get("placement"):
        cands = D.placement_candidates(twin)
    elif a.get("variable"):
        cands = D.energy_candidates(twin, a["variable"], a["values"], a.get("setKey"))
    else:
        cands = a["candidates"]
    if a.get("variable"):
        missing = [d.variable for d in twin.deps if d.variable != a["variable"]]
        if missing:
            raise Refused("twin_design: the fit's dependences also need the condition variable(s) %s; give 'candidates' with every variable" % ", ".join(sorted(set(missing))))
    runs = len(cands) * (len(twin.values) + 1)
    if runs > MAX_DESIGN_RUNS:
        raise Refused("twin_design: %d candidates x %d runs each = %d engine runs is above the limit of %d; rank fewer candidates"
                      % (len(cands), len(twin.values) + 1, runs, MAX_DESIGN_RUNS))
    if a.get("weights") is not None and len(a["weights"]) != len(twin.values):
        raise Refused("twin_design: 'weights' has %d entries; the fit has %d parameters (%s)" % (len(a["weights"]), len(twin.values), ", ".join(twin.names())))
    r = twin.design(cands, picks=int(a.get("picks", 1)), weights=a.get("weights"), sigma=a.get("noiseSigma"))
    out = Path(a["folder"]) / "reports"
    out.mkdir(exist_ok=True)
    (out / "design.json").write_text(json.dumps(_clean(r), indent=2), encoding="utf-8")
    r = dict(r, candidates=len(cands), engineRuns=runs, fit=twin.dir.name, report="reports/design.json", elapsedS=round(time.time() - t0, 3),
             advice="The gain is a linearised expectation at the fitted point: it says where the twin is blind, not what the data will show. Take the chosen "
                    "measurement, add it as a condition to twin.json, run twin_fit and twin_validate again.")
    return _clean(r)


def twin_track(a: Dict[str, Any]) -> dict:
    """Assimilate recordings with ``ultrafast_twin.track.DriftTracker`` (ensemble Kalman filter); writes reports/track.json."""
    t0 = time.time()
    twin = _project(a).twin()
    kw: Dict[str, Any] = {"members": int(a.get("members", 16)), "process_sigma": a.get("process") or None}
    for k, key in (("parameters", "parameters"), ("alarmSigmas", "alarm"), ("seed", "seed")):
        if a.get(k) is not None:
            kw[key] = a[k]
    try:
        trk = twin.tracker(**kw)
    except KeyError as e:
        raise Refused("twin_track: %s" % (e.args[0] if e.args else e))
    unknown = sorted(set(a.get("process") or {}) - set(trk.names))
    if unknown:
        raise Refused("twin_track: 'process' names %s, which are not tracked (%s)" % (", ".join(unknown), ", ".join(trk.names)))
    for m in a["measurements"]:
        trk.update(m["data"], m.get("variables") or None, m.get("set") or None, label=m.get("label"))
    out = Path(a["folder"]) / "reports"
    out.mkdir(exist_ok=True)
    trk.write(out / "track.json")
    last = trk.history[-1]
    r = {"parameters": trk.names, "alarmSigmas": trk.alarm, "members": trk.members, "history": trk.history, "drifted": last["drifted"], "fit": twin.dir.name,
         "report": "reports/track.json", "elapsedS": round(time.time() - t0, 3),
         "advice": "zFromFit is the distance of the tracked mean from the fit in fitted sigmas; a parameter in 'drifted' moved more than alarmSigmas. "
                   "Without 'process' the filter assumes the fitted sigma as the drift per measurement, which is usually far too small to follow a real drift."}
    return _clean(r)


def twin_init(a: Dict[str, Any]) -> dict:
    """``ultrafast twin init``: a synthetic-lab project folder (``ultrafast_twin.init``)."""
    t0 = time.time()
    proj = _twin_module().init(a["folder"], a.get("template", "synthetic-lab"), bool(a.get("force")))
    conds = [{"name": c["name"], "heldOut": bool(c.get("heldOut")), "variables": c.get("variables", {})} for c in proj.conditions()]
    names = [p.get("name") or p.get("path") for p in proj.spec.get("parameters", [])]
    return _clean({"project": a["folderRel"], "name": proj.name, "template": a.get("template", "synthetic-lab"), "instrument": proj.instrument, "conditions": conds,
                   "parameters": names, "files": ["twin.json", proj.spec.get("system"), "data/"], "elapsedS": round(time.time() - t0, 3),
                   "advice": "Next: twin_fit, then twin_validate. The data were generated from a hidden truth (ultrafast_twin.synthetic): compare the fitted values with it."})


OPS = {"validate": validate, "describe": describe, "run": run, "scan": scan, "compare": compare, "fit": fit, "plot": plot, "study": study, "backward": backward,
       "twin_fit": twin_fit, "twin_validate": twin_validate, "twin_predict": twin_predict, "twin_design": twin_design, "twin_track": twin_track,
       "twin_init": twin_init}
