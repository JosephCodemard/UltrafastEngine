"""MCP prompts: two ready-made workflows an agent can start from (``prompts/list``, ``prompts/get``).

``explore-in-quick`` is the explore-then-confirm workflow (scan or run in fidelity ``quick``, confirm the points that are reported in ``full``);
``compare-to-measurement`` builds a comparison with a recording, then fits only if the comparison says the model is close enough to fit.
The text is plain instructions that name the tools of :mod:`ultrafast.mcp.tools`; nothing here runs code.
"""
from __future__ import annotations

from typing import Any, Dict, List

PROMPTS: Dict[str, Dict[str, Any]] = {
    "explore-in-quick": {
        "description": "Explore a parameter in fidelity 'quick', then confirm the best points in 'full' and report which numbers came from which level.",
        "arguments": [{"name": "system", "description": "The system file in the workspace.", "required": True},
                      {"name": "goal", "description": "What to find, e.g. 'the shortest output pulse as the plate thickness changes'.", "required": True}],
        "text": ("Goal: {goal}\n\nSystem: {system}\n\n1. Call describe, then validate; fix every error and read every warning.\n"
                 "2. Read the resources ultrafast://fidelity and ultrafast://schema/run before writing a run spec.\n"
                 "3. Explore with the scan tool (or run) in fidelity 'quick'. Keep to one to three axes and at most a few dozen points.\n"
                 "4. Re-run the best one to three points with the run tool in fidelity 'full'. Compare the full and quick numbers; if they differ by more than the "
                 "quick-level warnings allow, say so and continue in 'full'.\n"
                 "5. Read warnings and 'validity' of the full runs (explain_warning for any code), then plot the result.\n"
                 "6. Report the answer with the fidelity level of every number, the warnings that remain and what you did not check."),
    },
    "compare-to-measurement": {
        "description": "Compare a model with a recording, judge the agreement, and fit only when the model is close enough for a fit to mean something.",
        "arguments": [{"name": "system", "description": "The system file in the workspace.", "required": True},
                      {"name": "recording", "description": "The recording file in the workspace (CSV) and which instrument it belongs to.", "required": True}],
        "text": ("System: {system}\nRecording: {recording}\n\n1. describe and validate the system. Add an instrument of the right kind with a 'measured' entry for the recording "
                 "(resource ultrafast://schema/run; docs/reference/measured-data.md): state the unit, background and resolution of the recording as the file's author gave them; do not guess.\n"
                 "2. Call compare_to_measured in fidelity 'quick'. Read the notes: units assumed, background removed, points outside the simulated range. A large share left out "
                 "means the comparison says little.\n"
                 "3. If the rms error is large, look for a missing physical effect before fitting (plot the instrument; explain_warning for the warnings). Do not fit a model that is "
                 "qualitatively wrong.\n"
                 "4. Only when the shapes agree: add a 'fit' section naming a few parameters with bounds from the lab (not the data), call fit with a time budget, and read the "
                 "uncertainties; a parameter at its bound is undetermined.\n"
                 "5. Confirm the fitted system with run in fidelity 'full' and compare_to_measured again. Report chi2 before and after, the fitted values with their uncertainties "
                 "and what the fit cannot determine."),
    },
}


def list_prompts() -> List[Dict[str, Any]]:
    return [{"name": n, "description": p["description"], "arguments": p["arguments"]} for n, p in PROMPTS.items()]


def get_prompt(name: Any, arguments: Dict[str, Any]) -> Dict[str, Any]:
    if not isinstance(name, str) or name not in PROMPTS:
        raise KeyError(name)
    p = PROMPTS[name]
    missing = [a["name"] for a in p["arguments"] if a.get("required") and not str(arguments.get(a["name"], "")).strip()]
    if missing:
        raise ValueError("prompt '%s' needs the argument(s): %s" % (name, ", ".join(missing)))
    text = p["text"].format(**{a["name"]: str(arguments.get(a["name"], "")) for a in p["arguments"]})
    return {"description": p["description"], "messages": [{"role": "user", "content": {"type": "text", "text": text}}]}
