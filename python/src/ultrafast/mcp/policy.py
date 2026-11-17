"""What the MCP server refuses (decision E8: local, workspace-sandboxed, no code from the agent).

Every refusal is a :class:`Refused` with a message that says what to do instead; the server returns it as a tool result with
``isError: true`` (not as a protocol error), so the agent reads it and corrects the call.

Main entry points (all pure Python, no engine):

* :class:`Workspace` ``(root)``: ``resolve(path)`` (a path inside the workspace, no ``..`` escape, no symlink or junction on the way),
  ``result_dir(name)`` (``<root>/.ultrafast-mcp/<name>``), ``source(value)`` (a system / run argument: a workspace file or an inline dict,
  checked with :func:`check_document`).
* :func:`check_document` ``(obj)``: refuses ``"@name"`` strings (callbacks), ``kind`` / ``type`` of ``callback``, ``python``,
  ``expression`` or ``user``, and file keys that point outside the workspace.
* :func:`check_set_path` ``(path)``: study / scan paths may only start with ``pulse/``, ``element:`` or ``instrument:``.
* :func:`check_arguments` ``(schema, args)``: unknown, missing or wrongly typed arguments.
"""
from __future__ import annotations

import math
import os
import re
from pathlib import Path
from typing import Any, Dict, Mapping, Optional

NAME_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")
REFUSED_KINDS = frozenset({"callback", "python", "expression", "user"})
SET_PREFIXES = ("pulse/", "element:", "instrument:")
OUTPUT_RE = re.compile(r"^[A-Za-z][A-Za-z0-9]*(:[A-Za-z0-9_.-]+)?/[A-Za-z0-9_./-]+$")
_FILE_KEY = re.compile(r"(file|folder|dir|directory)$", re.IGNORECASE)
RESULTS_FOLDER = ".ultrafast-mcp"
_MAX_DEPTH = 40


class Refused(Exception):
    """A request the server will not run; the message is returned to the agent."""


def _is_link(p: Path) -> bool:
    """True for a symbolic link, and on Windows for a junction or any other reparse point."""
    try:
        if p.is_symlink():
            return True
        st = os.lstat(p)
    except OSError:
        return False
    return bool(getattr(st, "st_file_attributes", 0) & 0x400)   # FILE_ATTRIBUTE_REPARSE_POINT


class Workspace:
    """The one folder the server may read from and write to."""

    def __init__(self, root: Any):
        self.root = Path(root).resolve()
        if not self.root.is_dir():
            raise ValueError("workspace is not a folder: %s" % self.root)

    # ------------------------------------------------------------------ paths
    def resolve(self, value: Any, *, must_exist: bool = False) -> Path:
        s = str(value)
        if not s or "\x00" in s:
            raise Refused("empty or invalid path")
        cand = Path(s)
        if not cand.is_absolute():
            cand = self.root / cand
        norm = Path(os.path.normpath(cand))
        try:
            rel = norm.relative_to(self.root)
        except ValueError:
            raise Refused("path %r is outside the workspace %s; copy the file into the workspace first" % (s, self.root)) from None
        cur = self.root
        for part in rel.parts:
            cur = cur / part
            if _is_link(cur):
                raise Refused("path %r goes through a symbolic link or junction (%s); links are not followed" % (s, part))
            if not cur.exists():
                break
        if must_exist and not norm.exists():
            raise Refused("file not found in the workspace: %s" % rel.as_posix())
        return norm

    def rel(self, p: Path) -> str:
        return Path(p).relative_to(self.root).as_posix()

    def result_dir(self, name: str) -> Path:
        check_name(name)
        return self.resolve(Path(RESULTS_FOLDER) / name)

    # ----------------------------------------------------------- system / run
    def source(self, value: Any, what: str) -> Any:
        """A system or run argument: a file in the workspace (returned as an absolute path string) or an inline dict (checked)."""
        if isinstance(value, str):
            if value.startswith("@"):
                raise Refused("%s: strings starting with '@' name callbacks, which this server does not run" % what)
            p = self.resolve(value, must_exist=True)
            if not p.is_file():
                raise Refused("%s: %s is not a file" % (what, self.rel(p)))
            return str(p)
        if isinstance(value, dict):
            check_document(value, self, what)
            return value
        raise Refused("%s must be a path inside the workspace or a JSON object" % what)


def check_name(name: Any) -> str:
    if not isinstance(name, str) or not NAME_RE.match(name) or name.endswith(".") or ".." in name:
        raise Refused("bad name %r: use 1 to 64 letters, digits, '_', '-' or '.', starting with a letter or digit" % (name,))
    return name


def check_set_path(path: Any) -> str:
    if not isinstance(path, str) or not path.startswith(SET_PREFIXES):
        raise Refused("parameter path %r is not allowed: paths must start with 'pulse/', 'element:' or 'instrument:'" % (path,))
    return path


def check_output(name: Any) -> str:
    if not isinstance(name, str) or not OUTPUT_RE.match(name):
        raise Refused("output name %r is not valid; examples: 'pulse:end/fwhmS', 'record:plate/BIntegralRad'" % (name,))
    return name


def check_document(obj: Any, ws: Optional[Workspace] = None, what: str = "document", _depth: int = 0) -> None:
    """Walk a system or run dict: no callbacks, no user code, file names inside the workspace."""
    if _depth > _MAX_DEPTH:
        raise Refused("%s is nested too deeply" % what)
    if isinstance(obj, dict):
        for key, value in obj.items():
            if key in ("kind", "type") and isinstance(value, str) and value.lower() in REFUSED_KINDS:
                raise Refused("%s: %s '%s' runs user code, which this server does not do" % (what, key, value))
            if key == "callback" or key == "callbacks":
                raise Refused("%s: key '%s' names user code, which this server does not run" % (what, key))
            if isinstance(value, str) and ws is not None and (_FILE_KEY.search(key) or (key == "path" and not value.startswith(SET_PREFIXES))):
                if not value.startswith("@"):
                    ws.resolve(value)       # raises Refused when outside the workspace or through a link
            check_document(value, ws, what, _depth + 1)
    elif isinstance(obj, list):
        for item in obj:
            check_document(item, ws, what, _depth + 1)
    elif isinstance(obj, str):
        if obj.startswith("@"):
            raise Refused("%s: the string %r starts with '@' (a callback or field reference); this server does not run user code" % (what, obj))


_TYPES = {"string": str, "object": dict, "array": list, "boolean": bool}


def check_arguments(tool: str, schema: Mapping[str, Any], args: Any) -> Dict[str, Any]:
    """Unknown, missing and wrongly typed arguments are refused (the schema is the tool's ``inputSchema``)."""
    if args is None:
        args = {}
    if not isinstance(args, dict):
        raise Refused("%s: arguments must be an object" % tool)
    props = schema.get("properties", {})
    for k in args:
        if k not in props:
            raise Refused("%s: unknown argument '%s' (known: %s)" % (tool, k, ", ".join(sorted(props)) or "none"))
    for k in schema.get("required", []):
        if k not in args:
            raise Refused("%s: missing required argument '%s'" % (tool, k))
    for k, v in args.items():
        spec = props[k]
        t = spec.get("type")
        types = t if isinstance(t, list) else [t]
        ok = False
        for one in types:
            if one == "integer":
                ok = ok or (isinstance(v, int) and not isinstance(v, bool))
            elif one == "number":
                ok = ok or (isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v))
            elif one in _TYPES:
                ok = ok or isinstance(v, _TYPES[one])
            else:
                ok = True
        if not ok:
            raise Refused("%s: argument '%s' must be of type %s" % (tool, k, "/".join(str(x) for x in types)))
        if "enum" in spec and v not in spec["enum"]:
            raise Refused("%s: argument '%s' must be one of %s (got %r)" % (tool, k, ", ".join(map(str, spec["enum"])), v))
        if "minimum" in spec and v < spec["minimum"] or "maximum" in spec and v > spec["maximum"]:
            raise Refused("%s: argument '%s' must be between %s and %s" % (tool, k, spec.get("minimum"), spec.get("maximum")))
    return args
