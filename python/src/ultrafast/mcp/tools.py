"""The eight MCP tools: definitions (name, description, JSON Schema) and the preparation of their arguments.

``TOOLS`` maps a tool name to :class:`Tool` ``(description, schema, op, timeout_s)``. ``prepare(tool, args, ws)`` applies the policy
(:mod:`ultrafast.mcp.policy`) and returns the arguments for the worker operation (:mod:`ultrafast.mcp.ops`); ``explain_warning`` has no
worker (``op`` None) and is answered by :mod:`ultrafast.mcp.explain`.

Tools: ``describe``, ``validate``, ``run`` (``fidelity``), ``scan`` (``fidelity``), ``explain_warning``, ``plot``, ``compare_to_measured``,
``fit`` (a ``fit`` section in the run spec, a time budget, the memory pre-flight). Results of ``run`` are
written to ``.ultrafast-mcp/<name>/`` in the workspace; ``plot`` reads them from there.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Optional

from .policy import RESULTS_FOLDER, Refused, Workspace, check_name, check_output, check_set_path

MAX_AXES = 4
MAX_SCAN_POINTS = 400
MAX_PARALLEL = 4

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
    if tool in ("describe", "validate", "run", "scan", "compare_to_measured", "fit"):
        out["system"] = ws.source(args["system"], "system")
        run_doc: Dict[str, Any] = {}
        if args.get("run") is not None:
            out["run"], run_doc = ws.document(args["run"], "run")
        for k in ("fit", "study", "backward"):
            if k in run_doc and not (tool == "fit" and k == "fit"):       # inline dicts and run files alike
                raise Refused("run: the '%s' section is not available through this tool (%s)" % (k, "use the fit tool" if k == "fit" else "use the scan tool, or run it with the CLI"))
        if tool == "fit" and "fit" not in run_doc:
            raise Refused("fit: the run spec has no 'fit' section (docs/plugins/fitting.md)")
        if tool == "compare_to_measured" and not any(isinstance(i, dict) and "measured" in i for i in (run_doc.get("instruments") or [])):
            raise Refused("compare_to_measured: no instrument of the run spec has a 'measured' recording (docs/reference/measured-data.md)")
    if tool in ("run", "scan", "compare_to_measured", "fit"):
        if args.get("fidelity"):
            out["fidelity"] = args["fidelity"]
    if tool in ("fit", "compare_to_measured") and args.get("name"):
        out["outDir"] = str(ws.result_dir(check_name(args["name"])))
        out["outDirRel"] = RESULTS_FOLDER + "/" + args["name"]
    if tool == "fit":
        if args.get("timeBudgetS") is not None:
            out["timeBudgetS"] = float(args["timeBudgetS"])
    if tool == "run":
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
