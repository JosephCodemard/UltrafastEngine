"""The operations the MCP worker runs (inside the child process; they import the engine).

``describe``, ``validate``, ``run``, ``scan`` and ``plot`` take the arguments the server has already checked
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


OPS = {"validate": validate, "describe": describe, "run": run, "scan": scan, "plot": plot}
