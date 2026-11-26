"""The sixteen MCP tools: definitions (name, description, JSON Schema) and the preparation of their arguments.

``TOOLS`` maps a tool name to :class:`Tool` ``(description, schema, op, timeout_s)``. ``prepare(tool, args, ws)`` applies the policy
(:mod:`ultrafast.mcp.policy`) and returns the arguments for the worker operation (:mod:`ultrafast.mcp.ops`); ``explain_warning`` has no
worker (``op`` None) and is answered by :mod:`ultrafast.mcp.explain`.

Tools: ``describe``, ``validate``, ``run`` (``fidelity``), ``scan`` (``fidelity``), ``explain_warning``, ``plot``, ``compare_to_measured``,
``fit`` (a ``fit`` section in the run spec, a time budget, the memory pre-flight), ``study`` (scan / optimize / sensitivity over parameters, at most 400
samples), ``backward`` (a ``backward`` section: the measured output field propagated to the planes upstream) and the digital-twin tools ``twin_fit``,
``twin_validate``, ``twin_predict``, ``twin_design`` (the next measurement to take), ``twin_track`` (drift) and ``twin_init`` (``ultrafast twin init``:
a synthetic-lab project) (a project folder inside the workspace). Results of ``run`` are
written to ``.ultrafast-mcp/<name>/`` in the workspace; ``plot`` reads them from there.
"""
from __future__ import annotations

import math
import re
from dataclasses import dataclass
from typing import Any, Dict, Optional

from .policy import MAX_FILE_BYTES, RESULTS_FOLDER, Refused, Workspace, check_name, check_output, check_set_path

MAX_AXES = 4
MAX_SCAN_POINTS = 400
MAX_PARALLEL = 4
MAX_TWIN_SAMPLES = 24
MAX_STUDY_EVALUATIONS = 400
MAX_DESIGN_CANDIDATES = 12      # twin_design: candidate measurements per call (each costs one engine run per fitted parameter, plus one)
MAX_TRACK_MEASUREMENTS = 12     # twin_track: recorded files per call
MAX_TRACK_MEMBERS = 32          # twin_track: ensemble members (one engine run each per measurement)
MAX_TRACK_RUNS = 192            # twin_track: members x measurements
_ID_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_.-]{0,63}$")

_SRC = {"description": "A path of a file inside the workspace (relative or absolute) or the JSON object itself.", "type": ["string", "object"]}
_FIDELITY = {"type": "string", "enum": ["quick", "standard", "full"],
             "description": "quick: explore (Raman, self-steepening and polarisation dropped; fastest); standard: looser tolerance; full (default): the physics as written. "
                            "Explore in quick, then confirm the points you report in full (resource ultrafast://fidelity)."}
_FIGURES = ["overview", "system", "instruments", "measurements", "probes", "elements", "byproducts", "gain", "oscillator", "spectrogram_3d"]


@dataclass(frozen=True)
class Tool:
    description: str
    schema: Dict[str, Any]
    op: Optional[str]
    timeout_s: float


TOOLS: Dict[str, Tool] = {
    "describe": Tool(
        "List the elements of a system file (id, type, main parameters), its topology and what the engine reports when it checks it. Use it first to see what a .ufs contains.",
        {"type": "object", "properties": {"system": _SRC}, "required": ["system"], "additionalProperties": False}, "describe", 60),
    "validate": Tool(
        "Check a system file (and optionally a run spec) without propagating: errors, warnings, notes. Cheap; call it after every edit.",
        {"type": "object", "properties": {"system": _SRC, "run": _SRC}, "required": ["system"], "additionalProperties": False}, "validate", 60),
    "run": Tool(
        "Propagate one pulse through a system and write the results to .ultrafast-mcp/<name>/ in the workspace. Returns the output pulse, per-element B-integral and fluence, "
        "instrument values, warnings, the 'validity' list and the fidelity report. Read warnings and validity before quoting a number. Refused above the memory limit; killed at the time budget.",
        {"type": "object", "properties": {"system": _SRC, "run": _SRC, "name": {"type": "string", "description": "Result folder name (letters, digits, '_', '-', '.')."}, "fidelity": _FIDELITY},
         "required": ["system", "run", "name"], "additionalProperties": False}, "run", 300),
    "scan": Tool(
        "Scan one to four parameters on a grid (at most 400 points) and return the output arrays. Axis keys are study paths ('element:plate/lengthM', 'pulse/energyJ', 'instrument:NAME/key'); "
        "outputs are study outputs ('pulse:end/fwhmS'). Explore with fidelity 'quick', then re-run the best points with the run tool in 'full'.",
        {"type": "object", "properties": {"system": _SRC, "run": _SRC,
                                           "axes": {"type": "object", "description": "{path: [values in SI units]}"},
                                           "outputs": {"type": "array", "description": "Output names, e.g. [\"pulse:end/fwhmS\"]."},
                                           "fidelity": _FIDELITY, "parallel": {"type": "integer", "minimum": 1, "maximum": MAX_PARALLEL, "description": "Points computed at once (default 1)."}},
         "required": ["system", "run", "axes", "outputs"], "additionalProperties": False}, "scan", 300),
    "explain_warning": Tool(
        "Explain a warning of the engine (text from summary.warnings) or a validity code (zeroFrequency, criticalPower, kerrLensCollapse, lidt, windowEdge): what it means and what to do.",
        {"type": "object", "properties": {"warning": {"type": "string"}, "code": {"type": "string"}}, "additionalProperties": False}, None, 5),
    "compare_to_measured": Tool(
        "Run a system whose instruments carry recordings ('measured' on an instrument in the run spec) and return only how well each simulated reading matches its recording: chi2, "
        "reduced chi2, rms error, scalar deviations and the notes (units assumed, background removed, points outside the range). Files named in the run spec must be in the workspace.",
        {"type": "object", "properties": {"system": _SRC, "run": _SRC, "fidelity": _FIDELITY,
                                           "name": {"type": "string", "description": "Optional: also write the full results to .ultrafast-mcp/<name>/."}},
         "required": ["system", "run"], "additionalProperties": False}, "compare", 300),
    "fit": Tool(
        "Fit parameters of a system to recordings: the run spec carries a 'fit' section (and 'measured' on the instruments). Runs under a time budget and the memory pre-flight; "
        "returns the fitted values with their uncertainties, chi2 before and after, and writes the fitted system to .ultrafast-mcp/<name>/. A fit killed at the budget returns nothing; "
        "use a coarser fidelity or fewer parameters, then confirm in 'full'.",
        {"type": "object", "properties": {"system": _SRC, "run": _SRC, "name": {"type": "string", "description": "Result folder name."}, "fidelity": _FIDELITY,
                                           "timeBudgetS": {"type": "number", "exclusiveMinimum": 0, "description": "Time budget in seconds (default and upper limit: the server's budget)."}},
         "required": ["system", "run", "name"], "additionalProperties": False}, "fit", 300),
    "study": Tool(
        "Parameter study with a mode: 'scan' (sampling grid, random, lhs or sobol), 'optimize' (an objective: minimise, maximise or hit a target of one output) or 'sensitivity' (local or sobol). "
        "Parameters are {path, min, max, points} with paths like 'element:plate/lengthM'; outputs like 'pulse:end/fwhmS'. At most 400 evaluations. Explore in 'quick', confirm the optimum with the run tool in 'full'.",
        {"type": "object", "properties": {"system": _SRC, "run": _SRC, "parameters": {"type": "array", "description": "[{path, min, max, points?}] in SI units."},
                                           "outputs": {"type": "array", "description": "Output names, e.g. [\"pulse:end/fwhmS\"]."},
                                           "mode": {"type": "string", "enum": ["scan", "optimize", "sensitivity"]},
                                           "sampling": {"type": "string", "enum": ["grid", "random", "lhs", "sobol"], "description": "Scan mode (default grid)."},
                                           "samples": {"type": "integer", "minimum": 1, "maximum": MAX_SCAN_POINTS, "description": "Number of samples (random, lhs, sobol; sobol sensitivity)."},
                                           "objective": {"type": "object", "description": "Optimize mode: {output, goal: min|max|target, target?, maxEvaluations?} (at most 400 evaluations)."},
                                           "sensitivity": {"type": "object", "description": "Sensitivity mode: {method: local|sobol, samples?}."},
                                           "seed": {"type": "integer"}, "fidelity": _FIDELITY,
                                           "parallel": {"type": "integer", "minimum": 1, "maximum": MAX_PARALLEL, "description": "Points computed at once (default 1)."}},
         "required": ["system", "run", "parameters", "outputs"], "additionalProperties": False}, "study", 300),
    "backward": Tool(
        "Backward propagation (fitting plugin): the run spec's pulse is the field measured at a plane ('backward': {from, to, regularization, active}); the engine undoes the elements in between "
        "and the instruments placed 'at' those planes read the reconstructed field. Results go to .ultrafast-mcp/<name>/ (reconstructed-pulse.json is the field at the input). Noise is amplified where the system attenuated: raise 'regularization' for noisy data.",
        {"type": "object", "properties": {"system": _SRC, "run": _SRC, "name": {"type": "string", "description": "Result folder name."}, "fidelity": _FIDELITY},
         "required": ["system", "run", "name"], "additionalProperties": False}, "backward", 300),
    "twin_fit": Tool(
        "Fit the digital twin of a lab project (a folder in the workspace with twin.json, a system, a run and the data of each condition): the multi-condition fit. Writes fits/<name>/ in the project folder. Runs under the time budget.",
        {"type": "object", "properties": {"folder": {"type": "string", "description": "Project folder (workspace-relative)."}, "name": {"type": "string", "description": "Fit name (default: a time stamp)."}},
         "required": ["folder"], "additionalProperties": False}, "twin_fit", 300),
    "twin_validate": Tool(
        "Validate the latest fit of a twin project against its held-out conditions with credible bands: verdict, chi2 per condition, coverage, flags. Read the flags: they say where the twin must not be trusted.",
        {"type": "object", "properties": {"folder": {"type": "string"}, "samples": {"type": "integer", "minimum": 2, "maximum": MAX_TWIN_SAMPLES, "description": "Parameter draws for the bands (default 12)."}},
         "required": ["folder"], "additionalProperties": False}, "twin_validate", 300),
    "twin_predict": Tool(
        "Predict the instrument reading of a condition with a 1-sigma credible band (x, y, y_sigma on at most 200 points) and the output scalars with their uncertainty. 'variables' are the condition's variables; 'set' sets values by path.",
        {"type": "object", "properties": {"folder": {"type": "string"}, "variables": {"type": "object", "description": "{name: number}"}, "set": {"type": "object", "description": "{path: number}, e.g. {\"pulse/energyJ\": 2e-6}"},
                                           "samples": {"type": "integer", "minimum": 2, "maximum": MAX_TWIN_SAMPLES}},
         "required": ["folder"], "additionalProperties": False}, "twin_predict", 300),
    "twin_design": Tool(
        "Rank candidate measurements by the information they would add to the fitted twin of a project (expected information gain, D-optimal; A-optimal with 'weights') and "
        "name the next one to take. Candidates: 'variable' with 'values' (and 'setKey', e.g. pulse/energyJ), 'placement' (the instrument after each element) or a 'candidates' list. "
        "At most %d candidates; each costs one engine run per fitted parameter plus one. A linearised expectation at the fitted point: it says where the twin is blind, not what the data will show. "
        "Writes reports/design.json in the project." % MAX_DESIGN_CANDIDATES,
        {"type": "object", "properties": {"folder": {"type": "string", "description": "Project folder (workspace-relative) with a fit (twin_fit)."},
                                           "variable": {"type": "string", "description": "A condition variable to vary, e.g. energyJ."},
                                           "values": {"type": "array", "description": "Values of 'variable' in SI units (1 to %d)." % MAX_DESIGN_CANDIDATES},
                                           "setKey": {"type": "string", "description": "Path that also receives the value, e.g. pulse/energyJ."},
                                           "placement": {"type": "boolean", "description": "Candidates: the instrument at the input, after each element and at the end."},
                                           "candidates": {"type": "array", "description": "[{name, variables?: {name: number}, set?: {path: number}}] (set 'instrument:NAME/at' takes an element id)."},
                                           "picks": {"type": "integer", "minimum": 1, "maximum": MAX_DESIGN_CANDIDATES, "description": "Measurements chosen greedily (default 1)."},
                                           "weights": {"type": "array", "description": "One weight per fitted parameter: A-optimal score (weighted variance reduction)."},
                                           "noiseSigma": {"type": "number", "exclusiveMinimum": 0, "description": "Noise of the new reading, peak-normalised (default: the project's sigma)."},
                                           "timeBudgetS": {"type": "number", "exclusiveMinimum": 0, "description": "Time budget in seconds (default and upper limit: the server's budget)."}},
         "required": ["folder"], "additionalProperties": False}, "twin_design", 300),
    "twin_track": Tool(
        "Track drift: assimilate new recordings of a project's instrument with an ensemble Kalman filter started from the fit. Returns, after each recording, the mean and sigma of the tracked "
        "parameters, their distance from the fit in fitted sigmas and the parameters that drifted (above 'alarmSigmas'). Give 'process' (the drift you expect per measurement, in the "
        "parameter's unit): the fitted sigma is usually far smaller. At most %d recordings and %d engine runs (members x recordings). Writes reports/track.json." % (MAX_TRACK_MEASUREMENTS, MAX_TRACK_RUNS),
        {"type": "object", "properties": {"folder": {"type": "string", "description": "Project folder (workspace-relative) with a fit (twin_fit)."},
                                           "measurements": {"type": "array", "description": "[{data: file (relative to the project folder), variables?: {name: number}, set?: {path: number}, label?}] in time order."},
                                           "parameters": {"type": "array", "description": "Names of the tracked parameters (default: every shared one)."},
                                           "process": {"type": "object", "description": "{parameter name: expected drift per measurement, in its unit}."},
                                           "members": {"type": "integer", "minimum": 4, "maximum": MAX_TRACK_MEMBERS, "description": "Ensemble members (default 16)."},
                                           "alarmSigmas": {"type": "number", "exclusiveMinimum": 0, "description": "Drift alarm: distance from the fit in fitted sigmas (default 3)."},
                                           "seed": {"type": "integer"},
                                           "timeBudgetS": {"type": "number", "exclusiveMinimum": 0, "description": "Time budget in seconds (default and upper limit: the server's budget)."}},
         "required": ["folder", "measurements"], "additionalProperties": False}, "twin_track", 300),
    "twin_init": Tool(
        "Create a twin project folder in the workspace (`ultrafast twin init`): the synthetic lab, a hidden truth the twin must recover ('synthetic-lab'), or one whose energy law bends "
        "outside the training range so the validation must flag it ('synthetic-lab-saturating'). Writes twin.json, system.ufs and data/. A real lab's twin.json is written by hand (docs/plugins/twin.md).",
        {"type": "object", "properties": {"folder": {"type": "string", "description": "Project folder to create (workspace-relative)."},
                                           "template": {"type": "string", "enum": ["synthetic-lab", "synthetic-lab-saturating"], "description": "Default synthetic-lab."},
                                           "force": {"type": "boolean", "description": "Overwrite an existing twin.json (default false)."}},
         "required": ["folder"], "additionalProperties": False}, "twin_init", 300),
    "plot": Tool(
        "Draw a figure of a finished run (from the run tool's result folder) and return it as a PNG. Styles: ultrafast (default), ultrafast-dark, paper, science.",
        {"type": "object", "properties": {"result": {"type": "string", "description": "The 'name' given to the run tool."}, "figure": {"type": "string", "enum": _FIGURES},
                                           "style": {"type": "string", "enum": ["ultrafast", "ultrafast-dark", "paper", "science"]},
                                           "column": {"type": "string", "enum": ["single", "double"], "description": "Paper styles: figure width."},
                                           "dpi": {"type": "integer", "minimum": 50, "maximum": 200}},
         "required": ["result"], "additionalProperties": False}, "plot", 120),
}


def definitions() -> list:
    return [{"name": n, "description": t.description, "inputSchema": t.schema} for n, t in TOOLS.items()]


def prepare(tool: str, args: Dict[str, Any], ws: Workspace) -> Dict[str, Any]:
    """Policy checks for one tool call; the result goes to the worker operation of the same name."""
    out: Dict[str, Any] = {}
    if tool in TWIN_TOOLS:
        return _prepare_twin(tool, args, ws)
    if tool in ("describe", "validate", "run", "scan", "compare_to_measured", "fit", "study", "backward"):
        out["system"] = ws.source(args["system"], "system")
        run_doc: Dict[str, Any] = {}
        if args.get("run") is not None:
            out["run"], run_doc = ws.document(args["run"], "run")
        for k in ("fit", "study", "backward"):
            if k in run_doc and not (tool == k):       # inline dicts and run files alike
                raise Refused("run: the '%s' section is not available through this tool (%s)" % (k, {"fit": "use the fit tool", "backward": "use the backward tool", "study": "use the study tool with its parameters and outputs, or the scan tool"}[k]))
        if tool == "backward" and "backward" not in run_doc:
            raise Refused("backward: the run spec has no 'backward' section (docs/plugins/fitting.md#backward-propagation)")
        if tool == "fit" and "fit" not in run_doc:
            raise Refused("fit: the run spec has no 'fit' section (docs/plugins/fitting.md)")
        if tool == "compare_to_measured" and not any(isinstance(i, dict) and "measured" in i for i in (run_doc.get("instruments") or [])):
            raise Refused("compare_to_measured: no instrument of the run spec has a 'measured' recording (docs/reference/measured-data.md)")
    if tool in ("run", "scan", "compare_to_measured", "fit", "study", "backward"):
        if args.get("fidelity"):
            out["fidelity"] = args["fidelity"]
    if tool in ("fit", "compare_to_measured", "backward") and args.get("name"):
        out["outDir"] = str(ws.result_dir(check_name(args["name"])))
        out["outDirRel"] = RESULTS_FOLDER + "/" + args["name"]
    if tool == "fit":
        if args.get("timeBudgetS") is not None:
            out["timeBudgetS"] = float(args["timeBudgetS"])
    if tool == "study":
        _prepare_study(args, out)
    if tool == "run" or tool == "backward":
        out["outDir"] = str(ws.result_dir(check_name(args["name"])))
        out["outDirRel"] = RESULTS_FOLDER + "/" + args["name"]
    elif tool == "scan":
        axes = args["axes"]
        if not axes or len(axes) > MAX_AXES:
            raise Refused("scan: give 1 to %d axes" % MAX_AXES)
        n = 1
        clean: Dict[str, Any] = {}
        for k, v in axes.items():
            check_set_path(k)
            if not isinstance(v, list) or not v or not all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in v):
                raise Refused("scan: axis '%s' must be a non-empty list of numbers" % k)
            if any(x != x or abs(x) == float("inf") for x in v):
                raise Refused("scan: axis '%s' has a value that is not finite" % k)
            n *= len(v)
            clean[k] = [float(x) for x in v]
        if n > MAX_SCAN_POINTS:
            raise Refused("scan: %d points is above the limit of %d; use fewer values or scan in two steps" % (n, MAX_SCAN_POINTS))
        outs = args["outputs"]
        if not outs or len(outs) > 20:
            raise Refused("scan: give 1 to 20 outputs")
        out["axes"] = clean
        out["outputs"] = [check_output(o) for o in outs]
        out["parallel"] = int(args.get("parallel", 1))
    elif tool == "plot":
        name = check_name(args["result"])
        out["resultDir"] = str(ws.result_dir(name))
        out["resultDirRel"] = RESULTS_FOLDER + "/" + name
        for k in ("figure", "style", "column", "dpi"):
            if k in args:
                out[k] = args[k]
    return out


TWIN_TOOLS = ("twin_fit", "twin_validate", "twin_predict", "twin_design", "twin_track", "twin_init")


def _prepare_study(args: Dict[str, Any], out: Dict[str, Any]) -> None:
    params = args["parameters"]
    if not params or len(params) > MAX_AXES:
        raise Refused("study: give 1 to %d parameters" % MAX_AXES)
    clean = []
    n_grid = 1
    for p in params:
        if not isinstance(p, dict) or "path" not in p:
            raise Refused("study: each parameter is an object {path, min, max, points?}")
        check_set_path(p["path"])
        for k in p:
            if k not in ("path", "min", "max", "points", "values", "scale"):
                raise Refused("study: unknown key %r in a parameter (path, min, max, points, values, scale)" % k)
        for k in ("min", "max", "points"):
            if k in p and (not isinstance(p[k], (int, float)) or isinstance(p[k], bool) or p[k] != p[k] or abs(p[k]) == float("inf")):
                raise Refused("study: parameter %r: %r must be a finite number" % (p["path"], k))
        n_grid *= int(p.get("points", len(p["values"]) if isinstance(p.get("values"), list) else 1))
        clean.append(dict(p))
    outs = args["outputs"]
    if not outs or len(outs) > 20:
        raise Refused("study: give 1 to 20 outputs")
    out["parameters"] = clean
    out["outputs"] = [check_output(o) for o in outs]
    mode = args.get("mode", "scan")
    out["mode"] = mode
    samples = int(args.get("samples", 0)) or None
    if mode == "scan":
        sampling = args.get("sampling", "grid")
        out["sampling"] = sampling
        n = n_grid if sampling == "grid" else (samples or 64)
        if n > MAX_STUDY_EVALUATIONS:
            raise Refused("study: %d evaluations is above the limit of %d; use fewer points or samples" % (n, MAX_STUDY_EVALUATIONS))
        if samples:
            out["samples"] = samples
    elif mode == "optimize":
        obj = args.get("objective")
        if not isinstance(obj, dict) or "output" not in obj or obj.get("goal") not in ("min", "max", "target"):
            raise Refused("study: mode 'optimize' needs objective {output, goal: min|max|target, target?, maxEvaluations?}")
        obj = dict(obj)
        check_output(obj["output"])
        obj["maxEvaluations"] = min(int(obj.get("maxEvaluations", 100)), MAX_STUDY_EVALUATIONS)
        out["objective"] = obj
    else:
        sens = dict(args.get("sensitivity") or {"method": "local"})
        if sens.get("method") not in ("local", "sobol"):
            raise Refused("study: sensitivity.method is 'local' or 'sobol'")
        if int(sens.get("samples", 0)) > MAX_STUDY_EVALUATIONS:
            raise Refused("study: sensitivity.samples is above the limit of %d" % MAX_STUDY_EVALUATIONS)
        out["sensitivity"] = sens
    if args.get("seed") is not None:
        out["seed"] = int(args["seed"])
    out["parallel"] = int(args.get("parallel", 1))


def _number(v: Any) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def _numbers(tool: str, what: str, d: Any, *, positive: bool = False) -> Dict[str, float]:
    """``{name: number}`` with finite numbers (and > 0 when ``positive``)."""
    if d is None:
        return {}
    if not isinstance(d, dict):
        raise Refused("%s: '%s' must be an object {name: number}" % (tool, what))
    out: Dict[str, float] = {}
    for k, v in d.items():
        if not _number(v) or (positive and v <= 0):
            raise Refused("%s: %s %r must be a %snumber" % (tool, what, k, "positive " if positive else "finite "))
        out[str(k)] = float(v)
    return out


def _condition_set(tool: str, d: Any) -> Dict[str, Any]:
    """The ``set`` of a condition: allowed paths only, numbers; a string only for an instrument position (``.../at``: an element id, input or end)."""
    if d is None:
        return {}
    if not isinstance(d, dict):
        raise Refused("%s: 'set' must be an object {path: number}" % tool)
    out: Dict[str, Any] = {}
    for k, v in d.items():
        check_set_path(k)
        if isinstance(v, str) and k.endswith("/at") and _ID_RE.match(v):
            out[k] = v
        elif _number(v):
            out[k] = float(v)
        else:
            raise Refused("%s: 'set' value of %r must be a finite number%s" % (tool, k, " or an element id" if k.endswith("/at") else ""))
    return out


def _prepare_twin(tool: str, args: Dict[str, Any], ws: Workspace) -> Dict[str, Any]:
    """A twin project is a folder of the workspace; its twin.json, system and run are read here so the policy sees what the plugin will read."""
    if tool == "twin_init":
        return _prepare_twin_init(args, ws)
    folder = ws.resolve(args["folder"], must_exist=True)
    if not folder.is_dir() or not (folder / "twin.json").is_file():
        raise Refused("%s: %s is not a twin project folder (it needs twin.json; create one with the twin_init tool or `ultrafast twin init`)" % (tool, args["folder"]))
    rel = ws.rel(folder)
    _, doc = ws.document(rel + "/twin.json", "twin")
    docs: Dict[str, Any] = {}
    for k in ("system", "run"):
        v = doc.get(k)
        if isinstance(v, str):
            docs[k] = ws.document(rel + "/" + v, k)[1]
    out: Dict[str, Any] = {"folder": str(folder)}
    if tool == "twin_fit":
        if args.get("name") is not None:
            out["name"] = check_name(args["name"])
    elif tool in ("twin_validate", "twin_predict"):
        out["samples"] = int(args.get("samples", 12))
    if tool == "twin_predict":
        out["variables"] = _numbers(tool, "variable", args.get("variables"))
        out["set"] = _condition_set(tool, args.get("set"))
    elif tool == "twin_design":
        _prepare_design(args, out, docs.get("system") or {})
    elif tool == "twin_track":
        _prepare_track(args, out, ws, folder)
    if tool in ("twin_design", "twin_track") and args.get("timeBudgetS") is not None:
        out["timeBudgetS"] = float(args["timeBudgetS"])
    return out


def _prepare_design(args: Dict[str, Any], out: Dict[str, Any], system: Dict[str, Any]) -> None:
    tool = "twin_design"
    given = [k for k in ("variable", "placement", "candidates") if args.get(k) not in (None, False)]
    if len(given) != 1:
        raise Refused("%s: give exactly one of 'variable' (with 'values'), 'placement' or 'candidates' (got %s)" % (tool, ", ".join(given) or "none"))
    n = 0
    if given[0] == "variable":
        if not _ID_RE.match(args["variable"]):
            raise Refused("%s: variable %r is not a valid name" % (tool, args["variable"]))
        vals = args.get("values")
        if not isinstance(vals, list) or not vals or not all(_number(v) for v in vals):
            raise Refused("%s: 'values' must be a non-empty list of finite numbers" % tool)
        n = len(vals)
        out.update(variable=args["variable"], values=[float(v) for v in vals])
        if args.get("setKey") is not None:
            out["setKey"] = check_set_path(args["setKey"])
    elif given[0] == "placement":
        n = 2 + len([e for e in (system.get("elements") or []) if isinstance(e, dict) and "id" in e])
        out["placement"] = True
    else:
        cands = args["candidates"]
        if not isinstance(cands, list) or not cands:
            raise Refused("%s: 'candidates' must be a non-empty list of {name, variables?, set?}" % tool)
        clean, names = [], set()
        for c in cands:
            if not isinstance(c, dict) or not isinstance(c.get("name"), str) or not c["name"]:
                raise Refused("%s: each candidate is an object with a 'name' (and 'variables', 'set')" % tool)
            extra = set(c) - {"name", "variables", "set"}
            if extra:
                raise Refused("%s: unknown key %r in a candidate (name, variables, set)" % (tool, sorted(extra)[0]))
            if c["name"] in names:
                raise Refused("%s: two candidates are named %r; names must be unique" % (tool, c["name"]))
            names.add(c["name"])
            clean.append({"name": c["name"][:120], "variables": _numbers(tool, "variable", c.get("variables")), "set": _condition_set(tool, c.get("set"))})
        n = len(clean)
        out["candidates"] = clean
    if given[0] != "variable" and (args.get("setKey") is not None or args.get("values") is not None):
        raise Refused("%s: 'values' and 'setKey' go with 'variable'" % tool)
    if n > MAX_DESIGN_CANDIDATES:
        raise Refused("%s: %d candidates is above the limit of %d; rank them in smaller groups" % (tool, n, MAX_DESIGN_CANDIDATES))
    picks = int(args.get("picks", 1))
    if picks > n:
        raise Refused("%s: picks (%d) is more than the %d candidates" % (tool, picks, n))
    out["picks"] = picks
    if args.get("weights") is not None:
        w = args["weights"]
        if not isinstance(w, list) or not w or not all(_number(x) and x >= 0 for x in w):
            raise Refused("%s: 'weights' must be a list of non-negative numbers, one per fitted parameter" % tool)
        out["weights"] = [float(x) for x in w]
    if args.get("noiseSigma") is not None:
        out["noiseSigma"] = float(args["noiseSigma"])


def _prepare_track(args: Dict[str, Any], out: Dict[str, Any], ws: Workspace, folder: Any) -> None:
    tool = "twin_track"
    ms = args["measurements"]
    if not isinstance(ms, list) or not ms:
        raise Refused("%s: 'measurements' must be a non-empty list of {data, variables?, set?, label?}" % tool)
    if len(ms) > MAX_TRACK_MEASUREMENTS:
        raise Refused("%s: %d recordings is above the limit of %d; track them in several calls" % (tool, len(ms), MAX_TRACK_MEASUREMENTS))
    members = int(args.get("members", 16))
    if members * len(ms) > MAX_TRACK_RUNS:
        raise Refused("%s: %d members x %d recordings = %d engine runs is above the limit of %d; use fewer members or recordings"
                      % (tool, members, len(ms), members * len(ms), MAX_TRACK_RUNS))
    clean = []
    for m in ms:
        if not isinstance(m, dict) or not isinstance(m.get("data"), str):
            raise Refused("%s: each measurement is an object with 'data' (a recorded file in the workspace)" % tool)
        extra = set(m) - {"data", "variables", "set", "label"}
        if extra:
            raise Refused("%s: unknown key %r in a measurement (data, variables, set, label)" % (tool, sorted(extra)[0]))
        p = ws.resolve(m["data"], must_exist=True, base=folder)
        if not p.is_file():
            raise Refused("%s: %s is not a file" % (tool, ws.rel(p)))
        if p.stat().st_size > MAX_FILE_BYTES:
            raise Refused("%s: %s is larger than %d MB" % (tool, ws.rel(p), MAX_FILE_BYTES // 1_000_000))
        label = m.get("label")
        if label is not None and not isinstance(label, str):
            raise Refused("%s: 'label' must be a string" % tool)
        clean.append({"data": str(p), "variables": _numbers(tool, "variable", m.get("variables")), "set": _condition_set(tool, m.get("set")),
                      "label": (label or p.name)[:120]})
    out["measurements"] = clean
    out["members"] = members
    if args.get("parameters") is not None:
        ps = args["parameters"]
        if not isinstance(ps, list) or not ps or not all(isinstance(x, str) and x for x in ps):
            raise Refused("%s: 'parameters' must be a non-empty list of parameter names" % tool)
        out["parameters"] = list(ps)
    out["process"] = _numbers(tool, "process sigma of", args.get("process"), positive=True)
    if args.get("alarmSigmas") is not None:
        out["alarmSigmas"] = float(args["alarmSigmas"])
    if args.get("seed") is not None:
        out["seed"] = int(args["seed"])


def _prepare_twin_init(args: Dict[str, Any], ws: Workspace) -> Dict[str, Any]:
    folder = ws.resolve(args["folder"])
    if folder.exists() and not folder.is_dir():
        raise Refused("twin_init: %s exists and is not a folder" % args["folder"])
    if folder == ws.root / RESULTS_FOLDER or (ws.root / RESULTS_FOLDER) in folder.parents:
        raise Refused("twin_init: %s is inside the server's results folder %s; choose another folder" % (args["folder"], RESULTS_FOLDER))
    if (folder / "twin.json").exists() and not args.get("force"):
        raise Refused("twin_init: %s already holds a twin project (twin.json); pass force true to overwrite it, or choose another folder" % args["folder"])
    return {"folder": str(folder), "folderRel": ws.rel(folder) if folder != ws.root else ".", "template": args.get("template", "synthetic-lab"),
            "force": bool(args.get("force", False))}
