"""MCP resources: the JSON schemas of the system and run files, the fidelity table and an index of the examples.

``list_resources(ws)`` and ``read_resource(uri, ws)``. Schemas are found in ``$ULTRAFAST_SCHEMA_DIR``, the package (``ultrafast/schemas``,
put there by the release wheel) or a source checkout (``<repo>/schemas``). The examples index reads ``docs/examples`` of the workspace, of a
source checkout or of the ``ultrafast_examples`` package, whichever exists first.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any, Dict, List, Optional

SCHEMAS = {"ultrafast://schema/run": ("run.schema.json", "Run spec schema", "JSON Schema of run.json: pulse, physics, stepper, instruments, fidelity, ..."),
           "ultrafast://schema/system": ("ufs.schema.json", "System file schema", "JSON Schema of a .ufs system file: every element type and option, with units and defaults.")}

FIDELITY_TEXT = """# Fidelity: quick, standard, full

A run-level key `"fidelity"` (or the `fidelity` argument of the run and scan tools) is a named bundle of switches. A key you write in the run spec is never overridden.

| level | what it changes | when |
|---|---|---|
| `full` (default) | nothing | results you will report |
| `standard` | looser solver tolerance (1e-5 instead of 1e-6); about 1.2 to 2 times faster | most work |
| `quick` | Raman off, self-steepening off, scalar polarisation, tolerance 1e-4, spatial models replaced by the 1-D beam model where possible, FROG / XFROG / d-scan instruments skipped | exploring: scans, first looks |

Not touched by any level: carrier-resolved propagation, third harmonic, ionisation and plasma, gain, satellites, ghosts, pedestals, grid size.

Never silent: `summary.fidelityReport` lists every switch changed, those kept as written, indicators and reasons a result may be unreliable. After a quick or
standard run the engine warns when strongly nonlinear (B above 3 rad), self-steepening dropped (B / (omega0 T0) above 0.01), Raman dropped (B above 1 rad and a pulse
shorter than 2 ps), a spatial model collapsed (peak power above 0.3 of the critical power) or the polarisation was made scalar.

Workflow: 1. scan in `quick`. 2. Re-run the best one to three points in `full` and compare. Report both and say which numbers came from which level.
3. If a quick warning fired, do not trust the quick numbers for that region. Full page: docs/reference/fidelity.md.
"""


def _schema_dir() -> Optional[Path]:
    cands = []
    if os.environ.get("ULTRAFAST_SCHEMA_DIR"):
        cands.append(Path(os.environ["ULTRAFAST_SCHEMA_DIR"]))
    here = Path(__file__).resolve()
    cands += [here.parents[1] / "schemas", here.parents[4] / "schemas"] if len(here.parents) > 4 else [here.parents[1] / "schemas"]
    for c in cands:
        if (c / "run.schema.json").is_file():
            return c
    return None


def _examples_root(ws) -> Optional[Path]:
    cands = [ws.root / "docs" / "examples"]
    here = Path(__file__).resolve()
    if len(here.parents) > 4:
        cands.append(here.parents[4] / "docs" / "examples")
    try:
        import ultrafast_examples      # type: ignore
        cands.append(Path(ultrafast_examples.__file__).parent / "examples")
    except Exception:
        pass
    for c in cands:
        if c.is_dir():
            return c
    return None


def _examples_index(ws) -> str:
    root = _examples_root(ws)
    if root is None:
        return "No examples found in the workspace (docs/examples). Install them with `pip install ultrafast-examples` or from the release assets (install.py --include examples)."
    lines = ["# Examples (group/name: first heading of its README)", ""]
    for readme in sorted(root.glob("*/*/README.md")):
        title = next((l.lstrip("# ").strip() for l in readme.read_text(encoding="utf-8", errors="replace").splitlines() if l.startswith("#")), "")
        lines.append("* %s/%s: %s" % (readme.parent.parent.name, readme.parent.name, title))
    return "\n".join(lines) + "\n"


def list_resources(ws) -> List[Dict[str, Any]]:
    out = [{"uri": u, "name": n, "description": d, "mimeType": "application/json"} for u, (_, n, d) in SCHEMAS.items()]
    out.append({"uri": "ultrafast://fidelity", "name": "Fidelity levels", "description": "quick / standard / full: what each changes and the explore-then-confirm workflow.", "mimeType": "text/markdown"})
    out.append({"uri": "ultrafast://examples", "name": "Examples index", "description": "Every example folder with the title of its README.", "mimeType": "text/markdown"})
    return out


def read_resource(uri: str, ws) -> Dict[str, Any]:
    """The ``contents`` entry of ``resources/read``; raises KeyError for an unknown URI, FileNotFoundError when a schema is not installed."""
    if uri in SCHEMAS:
        d = _schema_dir()
        if d is None:
            raise FileNotFoundError("the schema files are not installed with this package; run `ultrafast schema run` / `ultrafast schema system` instead")
        return {"uri": uri, "mimeType": "application/json", "text": (d / SCHEMAS[uri][0]).read_text(encoding="utf-8")}
    if uri == "ultrafast://fidelity":
        return {"uri": uri, "mimeType": "text/markdown", "text": FIDELITY_TEXT}
    if uri == "ultrafast://examples":
        return {"uri": uri, "mimeType": "text/markdown", "text": _examples_index(ws)}
    raise KeyError(uri)
