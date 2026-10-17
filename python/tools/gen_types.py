"""Generate python/src/ultrafast/types.py (TypedDicts for editor autocompletion) from the JSON Schemas.

    python3 python/tools/gen_types.py            # regenerate after `ultrafast schema system/run -o schemas/...`
    python3 python/tools/gen_types.py --check    # exit 1 if types.py is out of date

Every element type becomes a TypedDict (``PlateElement``, ``MirrorElement``, ...), every nested object of the
schema too (``Pulse``, ``Transverse``, ...); ``System`` and ``RunSpec`` are the two file formats. All keys are optional
(``total=False``), because the engine supplies defaults; the schema descriptions become the docstrings shown on hover.
"""
from __future__ import annotations

import json
import keyword
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SYS = ROOT / "schemas" / "ufs.schema.json"
RUN = ROOT / "schemas" / "run.schema.json"
OUT = ROOT / "python" / "src" / "ultrafast" / "types.py"


def cls_name(defname: str) -> str:
    kind, _, name = defname.partition(".")
    base = name[:1].upper() + name[1:]
    base = re.sub(r"[^0-9A-Za-z_]", "_", base)
    if kind == "element":
        return base + "Element"
    if kind == "instrument":
        return base + "Instrument"
    if kind == "model":
        return base + "Model"
    return base


class Gen:
    def __init__(self, defs: dict, prefix: str):
        self.defs = defs
        self.prefix = prefix
        self.names: dict[str, str] = {}
        self.emitted: list[str] = []

    def name(self, d: str) -> str:
        if d not in self.names:
            n = cls_name(d)
            if self.prefix and not d.startswith(("element.",)):
                n = n if n not in self.taken() else self.prefix + n
            self.names[d] = n
        return self.names[d]

    def taken(self):
        return set(self.names.values())

    def ann(self, s: dict | None) -> str:
        if not isinstance(s, dict) or not s:
            return "Any"
        if "$ref" in s:
            d = s["$ref"].split("/")[-1]
            if d == "element":
                return '"Element"'
            if d == "_any" or d not in self.defs:
                return "Any"
            return f'"{self.name(d)}"'
        if "const" in s:
            return f"Literal[{json.dumps(s['const'])}]"
        if "enum" in s and all(isinstance(e, str) for e in s["enum"]) and s["enum"]:
            return "Literal[" + ", ".join(json.dumps(e) for e in s["enum"]) + "]"
        for key in ("anyOf", "oneOf"):
            if key in s:
                parts = []
                for sub in s[key]:
                    a = self.ann(sub)
                    if a not in parts:
                        parts.append(a)
                if "Any" in parts:
                    return "Any"
                return parts[0] if len(parts) == 1 else "Union[" + ", ".join(parts) + "]"
        t = s.get("type")
        if isinstance(t, list):
            return "Any"
        if t == "string":
            return "str"
        if t == "integer":
            return "int"
        if t == "number":
            return "float"
        if t == "boolean":
            return "bool"
        if t == "array":
            return "List[" + self.ann(s.get("items")) + "]"
        if t == "object":
            return "Dict[str, Any]"
        return "Any"

    def typed_dict(self, name: str, schema: dict, extra: dict | None = None, doc: str | None = None) -> str:
        props = dict(extra or {})
        props.update(schema.get("properties") or {})
        lines = []
        description = doc or schema.get("description") or ""
        fields = []
        for k, v in props.items():
            fields.append((k, self.ann(v), (v or {}).get("description", "") if isinstance(v, dict) else ""))
        ident = all(k.isidentifier() and not keyword.iskeyword(k) for k, _, _ in fields)
        if ident:
            lines.append(f"class {name}(TypedDict, total=False):")
            if description:
                lines.append(f'    """{_doc(description)}"""')
            for k, a, d in fields:
                lines.append(f"    {k}: {a}")
                if d:
                    lines.append(f'    """{_doc(d)}"""')
            if not fields and not description:
                lines.append("    pass")
            elif not fields:
                lines.append("    pass")
        else:
            lines.append(f"{name} = TypedDict({name!r}, {{")
            for k, a, d in fields:
                lines.append(f"    {k!r}: {a},{'  # ' + _doc(d)[:100] if d else ''}")
            lines.append("}, total=False)")
            if description:
                lines.append(f'"""{_doc(description)}"""')
        return "\n".join(lines)


def _doc(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"""', "'''").replace("\n", " ").strip()


def generate() -> str:
    sysd = json.loads(SYS.read_text(encoding="utf-8"))
    rund = json.loads(RUN.read_text(encoding="utf-8"))
    defs = dict(sysd.get("definitions", {}))
    for k, v in rund.get("definitions", {}).items():
        defs.setdefault(k, v)
    g = Gen(defs, "")
    out = [
        '"""TypedDicts for system files and run specs -- editor autocompletion and type checking.',
        "",
        "Generated by python/tools/gen_types.py from schemas/*.json; do not edit by hand.",
        "",
        "    from ultrafast.types import System, RunSpec, PlateElement",
        '    plate: PlateElement = {"id": "w", "type": "plate", "material": "FusedSilica", "thicknessM": 5e-3}',
        "",
        "All keys are optional (the engine fills defaults); ``id`` and ``type`` are required on elements.",
        '"""',
        "from __future__ import annotations",
        "",
        "from typing import Any, Dict, List, Literal, TypedDict, Union",
        "",
    ]
    element_defs = sorted(k for k in defs if k.startswith("element.") and k != "element")
    base_elem = defs.get("element", {}).get("properties", {})
    blocks = []
    for d in sorted(defs):
        if d in ("_any", "element"):
            continue
        sch = defs[d]
        if d.startswith("element."):
            et = d.split(".", 1)[1]
            extra = {"id": base_elem.get("id", {"type": "string"}), "type": {"const": et}}
            blocks.append(g.typed_dict(g.name(d), sch, extra))
        elif sch.get("type") == "object" or "properties" in sch:
            blocks.append(g.typed_dict(g.name(d), sch))
        else:
            blocks.append(f"{g.name(d)} = {g.ann(sch)}\n\"\"\"{_doc(sch.get('description', ''))}\"\"\"")
    out += ["", "\n\n\n".join(blocks), "", ""]
    union = ", ".join(f'"{g.name(d)}"' for d in element_defs)
    out.append(f"Element = Union[{union}, Dict[str, Any]]" if union else "Element = Dict[str, Any]")
    out.append('"""Any element dictionary (the typed forms first; a plain dict for plugin elements)."""')
    out += ["", ""]
    sp = dict(sysd.get("properties", {}))
    sp["elements"] = {"type": "array", "items": {"$ref": "#/definitions/element"}, "description": sp.get("elements", {}).get("description", "Elements in beam order")}
    out.append(g.typed_dict("System", {"properties": sp}, doc=sysd.get("description") or "An ultrafast-system file (.ufs)."))
    out += ["", ""]
    out.append(g.typed_dict("RunSpec", {"properties": rund.get("properties", {})}, doc=rund.get("description") or "A run spec."))
    out += ["", "", "__all__ = [" + ", ".join(repr(n) for n in sorted(set(g.names.values()) | {"System", "RunSpec", "Element"})) + "]", ""]
    return "\n".join(out)


def main() -> int:
    text = generate()
    if "--check" in sys.argv:
        if not OUT.exists() or OUT.read_text(encoding="utf-8") != text:
            print(f"{OUT} is out of date: run python3 python/tools/gen_types.py")
            return 1
        return 0
    OUT.write_text(text, encoding="utf-8")
    print(f"wrote {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
