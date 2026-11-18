"""The thirteen MCP tools: definitions (name, description, JSON Schema) and the preparation of their arguments.

``TOOLS`` maps a tool name to :class:`Tool` ``(description, schema, op, timeout_s)``. ``prepare(tool, args, ws)`` applies the policy
(:mod:`ultrafast.mcp.policy`) and returns the arguments for the worker operation (:mod:`ultrafast.mcp.ops`); ``explain_warning`` has no
worker (``op`` None) and is answered by :mod:`ultrafast.mcp.explain`.

Tools: ``describe``, ``validate``, ``run`` (``fidelity``), ``scan`` (``fidelity``), ``explain_warning``, ``plot``, ``compare_to_measured``,
``fit`` (a ``fit`` section in the run spec, a time budget, the memory pre-flight), ``study`` (scan / optimize / sensitivity over parameters, at most 400
samples), ``backward`` (a ``backward`` section: the measured output field propagated to the planes upstream) and the digital-twin tools ``twin_fit``,
``twin_validate`` and ``twin_predict`` (a project folder inside the workspace). Results of ``run`` are
written to ``.ultrafast-mcp/<name>/`` in the workspace; ``plot`` reads them from there.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Optional

from .policy import RESULTS_FOLDER, Refused, Workspace, check_name, check_output, check_set_path

MAX_AXES = 4
MAX_SCAN_POINTS = 400
MAX_PARALLEL = 4
MAX_TWIN_SAMPLES = 24
MAX_STUDY_EVALUATIONS = 400

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


TWIN_TOOLS = ("twin_fit", "twin_validate", "twin_predict")


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


def _prepare_twin(tool: str, args: Dict[str, Any], ws: Workspace) -> Dict[str, Any]:
    """A twin project is a folder of the workspace; its twin.json, system and run are read here so the policy sees what the plugin will read."""
    folder = ws.resolve(args["folder"], must_exist=True)
    if not folder.is_dir() or not (folder / "twin.json").is_file():
        raise Refused("%s: %s is not a twin project folder (it needs twin.json; create one with `ultrafast twin init`)" % (tool, args["folder"]))
    rel = ws.rel(folder)
    _, doc = ws.document(rel + "/twin.json", "twin")
    for k in ("system", "run"):
        v = doc.get(k)
        if isinstance(v, str):
            ws.document(rel + "/" + v, k)
    out: Dict[str, Any] = {"folder": str(folder)}
    if tool == "twin_fit":
        if args.get("name") is not None:
            out["name"] = check_name(args["name"])
    else:
        out["samples"] = int(args.get("samples", 12))
    if tool == "twin_predict":
        out["variables"] = {}
        for k, v in (args.get("variables") or {}).items():
            if not isinstance(v, (int, float)) or isinstance(v, bool) or v != v:
                raise Refused("twin_predict: variable %r must be a number" % k)
            out["variables"][str(k)] = float(v)
        out["set"] = {}
        for k, v in (args.get("set") or {}).items():
            check_set_path(k)
            if not isinstance(v, (int, float)) or isinstance(v, bool) or v != v:
                raise Refused("twin_predict: 'set' value of %r must be a number" % k)
            out["set"][k] = float(v)
    return out
